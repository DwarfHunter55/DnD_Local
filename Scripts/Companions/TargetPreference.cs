namespace ChroniclesUnbound.Companions;

/// <summary>
/// Determines which enemies a companion prioritizes during combat.
/// Used by the utility-based combat AI scoring system.
/// </summary>
public enum TargetPreference
{
    /// <summary>
    /// Attack the closest reachable enemy. Simple and predictable.
    /// </summary>
    Nearest,

    /// <summary>
    /// Attack the enemy with the lowest current HP. Good for finishing off targets.
    /// </summary>
    Weakest,

    /// <summary>
    /// Attack the enemy with the highest threat score. Focuses dangerous foes first.
    /// </summary>
    Strongest,

    /// <summary>
    /// Prioritize enemy spellcasters. Useful for disrupting enemy support.
    /// </summary>
    Spellcasters,

    /// <summary>
    /// Pick targets unpredictably. Adds flavor to chaotic or unhinged companions.
    /// </summary>
    Random
}
