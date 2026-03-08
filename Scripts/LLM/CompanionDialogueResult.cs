namespace ChroniclesUnbound.LLM;

/// <summary>
/// Parsed output from a companion dialogue prompt.
/// </summary>
public sealed class CompanionDialogueResult
{
    /// <summary>The spoken dialogue text.</summary>
    public string Dialogue { get; init; } = string.Empty;

    /// <summary>Emotional state during this dialogue (e.g. "amused", "worried", "angry").</summary>
    public string Emotion { get; init; } = "neutral";

    /// <summary>The companion's internal opinion or attitude about the situation.</summary>
    public string Opinion { get; init; } = string.Empty;
}
