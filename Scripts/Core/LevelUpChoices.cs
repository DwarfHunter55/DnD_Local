using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// How the player chooses to resolve hit points on level up.
/// </summary>
public enum HPRollChoice
{
    /// <summary>Roll the hit die for HP.</summary>
    RollHP,

    /// <summary>Take the fixed average (rounded up): (die/2) + 1.</summary>
    TakeAverage
}

/// <summary>
/// Captures all player decisions when leveling up a character.
/// Passed to LevelUpManager.ApplyLevelUp() to finalize the level-up.
/// </summary>
public class LevelUpChoices
{
    /// <summary>
    /// Whether to roll for HP or take the fixed average.
    /// </summary>
    public HPRollChoice HPChoice { get; set; } = HPRollChoice.TakeAverage;

    /// <summary>
    /// Ability Score Improvement allocations. Values must total exactly 2.
    /// Can be +2 to one ability or +1 to two abilities (each score capped at 20).
    /// Ignored if a feat is chosen instead, or if the level has no ASI.
    /// </summary>
    public Dictionary<Ability, int> ASIAllocations { get; set; } = new();

    /// <summary>
    /// If set, the character takes this feat instead of the ASI.
    /// Must match a feat name from FeatData.GetSRDFeats() or other registered feats.
    /// Null or empty means the character takes the ASI instead.
    /// </summary>
    public string? FeatName { get; set; }

    /// <summary>
    /// Spells chosen to learn at this level (for known-spell casters).
    /// </summary>
    public List<string> NewSpells { get; set; } = new();
}
