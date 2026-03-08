namespace ChroniclesUnbound.LLM;

/// <summary>
/// Configuration for the Ollama LLM connection. Adjust defaults per-model
/// or override at runtime via settings UI.
/// </summary>
public sealed class LLMConfig
{
    /// <summary>Base URL of the Ollama REST API.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Model identifier (must be pulled in Ollama first).</summary>
    public string ModelName { get; set; } = "llama3.1:8b";

    /// <summary>Maximum tokens to generate per response (num_predict).</summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>Sampling temperature. Lower = more deterministic.</summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Number of retry attempts on transient failures.</summary>
    public int MaxRetries { get; set; } = 3;
}
