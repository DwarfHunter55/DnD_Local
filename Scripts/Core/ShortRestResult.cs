using System.Collections.Generic;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Result data from a short rest, detailing hit dice healing and recovered features.
/// </summary>
public class ShortRestResult
{
    /// <summary>Number of hit dice spent during this rest.</summary>
    public int HitDiceSpent { get; init; }

    /// <summary>Total HP healed from hit dice rolls.</summary>
    public int HPHealed { get; init; }

    /// <summary>Individual roll results for each hit die spent (die + CON modifier).</summary>
    public List<DiceResult> IndividualHealRolls { get; init; } = new();

    /// <summary>Names of class features that were recovered (e.g. "Second Wind", "Action Surge").</summary>
    public List<string> FeaturesRecovered { get; init; } = new();

    /// <summary>True if Warlock pact magic spell slots were restored.</summary>
    public bool SpellSlotsRecovered { get; init; }

    public override string ToString()
    {
        var parts = new List<string>();
        if (HPHealed > 0)
            parts.Add($"Healed {HPHealed} HP ({HitDiceSpent} hit dice)");
        if (SpellSlotsRecovered)
            parts.Add("Spell slots restored (Pact Magic)");
        if (FeaturesRecovered.Count > 0)
            parts.Add($"Recovered: {string.Join(", ", FeaturesRecovered)}");
        return parts.Count > 0 ? string.Join("; ", parts) : "No changes from short rest.";
    }
}
