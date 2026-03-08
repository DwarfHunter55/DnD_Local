using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChroniclesUnbound.LLM;

/// <summary>
/// Async service for communicating with Ollama's REST API.
/// Owns a single HttpClient for connection pooling. Thread-safe for concurrent calls.
/// </summary>
public sealed class LLMService : IDisposable
{
    private readonly LLMConfig _config;
    private readonly System.Net.Http.HttpClient _http;

    public LLMService(LLMConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = new System.Net.Http.HttpClient
        {
            BaseAddress = new Uri(_config.BaseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds)
        };
    }

    /// <summary>
    /// Send a chat completion request to Ollama.
    /// </summary>
    /// <param name="messages">Conversation history (system + user + assistant turns).</param>
    /// <param name="jsonMode">When true, instructs the model to respond in JSON format.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public async Task<LLMResponse> GenerateAsync(
        List<LLMMessage> messages,
        bool jsonMode = false,
        CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count == 0)
            return LLMResponse.Failure("No messages provided.");

        var sw = Stopwatch.StartNew();

        for (int attempt = 0; attempt <= _config.MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                // Exponential backoff: 500ms, 1s, 2s ...
                int delayMs = 500 * (1 << (attempt - 1));
                GD.Print($"[LLMService] Retry {attempt}/{_config.MaxRetries} in {delayMs}ms...");
                await Task.Delay(delayMs, cancellationToken);
            }

            try
            {
                var result = await SendChatRequestAsync(messages, jsonMode, sw, cancellationToken);
                if (result.IsSuccess)
                    return result;

                // Non-retryable error (e.g. model not found) — bail immediately.
                if (result.ErrorMessage != null && result.ErrorMessage.Contains("model"))
                    return result;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                GD.PrintErr($"[LLMService] Request timed out (attempt {attempt + 1}).");
                if (attempt == _config.MaxRetries)
                    return LLMResponse.Failure("Request timed out after all retries.", sw.ElapsedMilliseconds);
            }
            catch (HttpRequestException ex) when (IsConnectionRefused(ex))
            {
                GD.PrintErr("[LLMService] Ollama is not running or not reachable.");
                return LLMResponse.Failure(
                    "Cannot connect to Ollama. Ensure 'ollama serve' is running.", sw.ElapsedMilliseconds);
            }
            catch (HttpRequestException ex)
            {
                GD.PrintErr($"[LLMService] HTTP error: {ex.Message}");
                if (attempt == _config.MaxRetries)
                    return LLMResponse.Failure($"HTTP error: {ex.Message}", sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                return LLMResponse.Failure("Request was cancelled.", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[LLMService] Unexpected error: {ex.Message}");
                if (attempt == _config.MaxRetries)
                    return LLMResponse.Failure($"Unexpected error: {ex.Message}", sw.ElapsedMilliseconds);
            }
        }

        return LLMResponse.Failure("All retries exhausted.", sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Check whether Ollama is reachable and responding.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var response = await _http.GetAsync("/api/tags", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Retrieve the list of models currently available in Ollama.
    /// </summary>
    public async Task<List<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/tags", cancellationToken);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            var root = JObject.Parse(json);
            var models = root["models"];
            if (models == null)
                return new List<string>();

            return models
                .Select(m => m["name"]?.ToString() ?? string.Empty)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LLMService] Failed to fetch models: {ex.Message}");
            return new List<string>();
        }
    }

    // ----------------------------------------------------------------
    // Internals
    // ----------------------------------------------------------------

    private async Task<LLMResponse> SendChatRequestAsync(
        List<LLMMessage> messages,
        bool jsonMode,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        var requestBody = new JObject
        {
            ["model"] = _config.ModelName,
            ["messages"] = new JArray(
                messages.Select(m => new JObject
                {
                    ["role"] = m.Role,
                    ["content"] = m.Content
                })
            ),
            ["stream"] = false,
            ["options"] = new JObject
            {
                ["temperature"] = _config.Temperature,
                ["num_predict"] = _config.MaxTokens
            }
        };

        if (jsonMode)
            requestBody["format"] = "json";

        var content = new StringContent(requestBody.ToString(Formatting.None), Encoding.UTF8, "application/json");
        var httpResponse = await _http.PostAsync("/api/chat", content, cancellationToken);

        string responseJson = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
        {
            string errorDetail = TryExtractError(responseJson) ?? $"HTTP {(int)httpResponse.StatusCode}";
            GD.PrintErr($"[LLMService] API error: {errorDetail}");
            return LLMResponse.Failure(errorDetail, sw.ElapsedMilliseconds);
        }

        var responseRoot = JObject.Parse(responseJson);
        string? rawText = responseRoot["message"]?["content"]?.ToString();

        if (string.IsNullOrEmpty(rawText))
            return LLMResponse.Failure("Empty response from model.", sw.ElapsedMilliseconds);

        JObject? parsedJson = null;
        if (jsonMode)
        {
            try
            {
                parsedJson = JObject.Parse(rawText);
            }
            catch (JsonReaderException)
            {
                GD.PrintErr("[LLMService] JSON mode requested but response is not valid JSON. " +
                            "Raw text preserved in RawText for fallback parsing.");
            }
        }

        return LLMResponse.Success(rawText, sw.ElapsedMilliseconds, parsedJson);
    }

    private static string? TryExtractError(string json)
    {
        try
        {
            var obj = JObject.Parse(json);
            return obj["error"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsConnectionRefused(HttpRequestException ex)
    {
        // .NET 6 wraps SocketException inside HttpRequestException for connection failures.
        return ex.InnerException is System.Net.Sockets.SocketException socketEx &&
               (socketEx.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused ||
                socketEx.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
