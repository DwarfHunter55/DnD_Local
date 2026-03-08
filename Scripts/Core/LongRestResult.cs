using System.Collections.Generic;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Result data from a long rest, detailing all resources restored.
/// </summary>
public class LongRestResult
{
    /// <summary>Amount of HP healed (difference between max and pre-rest HP).</summary>
    public int HPRestored { get; init; }

    /// <summary>Number of hit dice recovered (up to half total, minimum 1).</summary>
    public int HitDiceRecovered { get; init; }

    /// <summary>True if spell slots were restored to maximum.</summary>
    public bool SpellSlotsRestored { get; init; }

    /// <summary>True if exhaustion was reduced by 1 level.</summary>
    public bool ExhaustionReduced { get; init; }

    /// <summary>Exhaustion level after the long rest.</summary>
    public int NewExhaustionLevel { get; init; }

    /// <summary>Names of class features that were reset to full uses.</summary>
    public List<string> FeaturesRecovered { get; init; } = new();

    public override string ToString()
    {
        var parts = new List<string>();
        if (HPRestored > 0)
            parts.Add($"Healed {HPRestored} HP to full");
        if (HitDiceRecovered > 0)
            parts.Add($"Recovered {HitDiceRecovered} hit dice");
        if (SpellSlotsRestored)
            parts.Add("All spell slots restored");
        if (ExhaustionReduced)
            parts.Add($"Exhaustion reduced to level {NewExhaustionLevel}");
        if (FeaturesRecovered.Count > 0)
            parts.Add($"Recovered: {string.Join(", ", FeaturesRecovered)}");
        return parts.Count > 0 ? string.Join("; ", parts) : "Fully rested, no changes needed.";
    }
}
