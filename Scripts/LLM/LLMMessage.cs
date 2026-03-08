namespace ChroniclesUnbound.LLM;

/// <summary>
/// A single message in the chat completion conversation history.
/// </summary>
public sealed class LLMMessage
{
    /// <summary>Role: "system", "user", or "assistant".</summary>
    public string Role { get; }

    /// <summary>Text content of the message.</summary>
    public string Content { get; }

    public LLMMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }
}
