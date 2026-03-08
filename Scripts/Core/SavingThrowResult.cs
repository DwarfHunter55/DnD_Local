namespace ChroniclesUnbound.Core;

/// <summary>
/// Result of a saving throw against a difficulty class.
/// </summary>
public sealed class SavingThrowResult
{
    /// <summary>Ability name (e.g. "Dexterity", "Wisdom").</summary>
    public string Ability { get; }

    /// <summary>Natural d20 roll.</summary>
    public int Roll { get; }

    /// <summary>Total modifier applied (ability + proficiency + misc).</summary>
    public int Modifier { get; }

    /// <summary>Final total (Roll + Modifier).</summary>
    public int Total { get; }

    /// <summary>Difficulty class the save was made against.</summary>
    public int DC { get; }

    /// <summary>Whether total meets or exceeds the DC.</summary>
    public bool Success { get; }

    /// <summary>True if the roll was made with advantage.</summary>
    public bool HasAdvantage { get; }

    /// <summary>True if the roll was made with disadvantage.</summary>
    public bool HasDisadvantage { get; }

    public SavingThrowResult(
        string ability, int roll, int modifier, int dc,
        bool hasAdvantage = false, bool hasDisadvantage = false)
    {
        Ability = ability;
        Roll = roll;
        Modifier = modifier;
        Total = roll + modifier;
        DC = dc;
        Success = Total >= dc;
        HasAdvantage = hasAdvantage;
        HasDisadvantage = hasDisadvantage;
    }

    public override string ToString()
    {
        string advText = HasAdvantage ? " (Advantage)" : HasDisadvantage ? " (Disadvantage)" : "";
        string modText = Modifier >= 0 ? $"+{Modifier}" : Modifier.ToString();
        string passText = Success ? "SUCCESS" : "FAILURE";
        return $"{Ability} Save{advText}: d20({Roll}) {modText} = {Total} vs DC {DC} — {passText}";
    }
}
