using Newtonsoft.Json.Linq;

namespace ChroniclesUnbound.LLM;

/// <summary>
/// Parsed result from an Ollama chat completion request.
/// Always check <see cref="IsSuccess"/> before consuming <see cref="RawText"/>.
/// </summary>
public sealed class LLMResponse
{
    /// <summary>Raw text content returned by the model.</summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>Whether the request completed without error.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Error description, or null on success.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Parsed JSON object when json mode was requested and parsing succeeded.</summary>
    public JObject? ParsedJson { get; init; }

    /// <summary>Wall-clock generation time in milliseconds.</summary>
    public long GenerationTimeMs { get; init; }

    /// <summary>Create a successful response.</summary>
    public static LLMResponse Success(string rawText, long generationTimeMs, JObject? parsedJson = null)
    {
        return new LLMResponse
        {
            RawText = rawText,
            IsSuccess = true,
            ErrorMessage = null,
            ParsedJson = parsedJson,
            GenerationTimeMs = generationTimeMs
        };
    }

    /// <summary>Create a failed response with an error message.</summary>
    public static LLMResponse Failure(string errorMessage, long generationTimeMs = 0)
    {
        return new LLMResponse
        {
            RawText = string.Empty,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ParsedJson = null,
            GenerationTimeMs = generationTimeMs
        };
    }
}
