namespace ChroniclesUnbound.Companions;

/// <summary>
/// Drives the companion AI's decision-making by categorizing the urgency
/// and intent of the current combat situation.
/// </summary>
public enum CombatPriority
{
    /// <summary>An ally is dying (unconscious, making death saves) or self is critically low HP.</summary>
    Emergency,

    /// <summary>A target can be eliminated or severely wounded this turn.</summary>
    Offensive,

    /// <summary>An ally needs healing, buffing, or condition removal.</summary>
    Support,

    /// <summary>Area denial, crowd control, or battlefield manipulation is the best play.</summary>
    Control,

    /// <summary>No strong priority -- take the best available action.</summary>
    Default
}
