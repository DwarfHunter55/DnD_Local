namespace ChroniclesUnbound.Core;

/// <summary>
/// Result of a skill check against a difficulty class.
/// </summary>
public sealed class SkillCheckResult
{
    /// <summary>Skill name (e.g. "Perception", "Stealth").</summary>
    public string Skill { get; }

    /// <summary>Natural d20 roll.</summary>
    public int Roll { get; }

    /// <summary>Total modifier applied (ability + proficiency + misc).</summary>
    public int Modifier { get; }

    /// <summary>Final total (Roll + Modifier).</summary>
    public int Total { get; }

    /// <summary>Difficulty class the check was made against.</summary>
    public int DC { get; }

    /// <summary>Whether total meets or exceeds the DC.</summary>
    public bool Success { get; }

    /// <summary>True if the roll was made with advantage.</summary>
    public bool HasAdvantage { get; }

    /// <summary>True if the roll was made with disadvantage.</summary>
    public bool HasDisadvantage { get; }

    public SkillCheckResult(
        string skill, int roll, int modifier, int dc,
        bool hasAdvantage = false, bool hasDisadvantage = false)
    {
        Skill = skill;
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
        return $"{Skill} Check{advText}: d20({Roll}) {modText} = {Total} vs DC {DC} — {passText}";
    }
}
