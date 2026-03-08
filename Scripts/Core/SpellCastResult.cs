namespace ChroniclesUnbound.Core;

/// <summary>
/// Result of a spell casting attempt, containing all relevant outcome data.
/// </summary>
public class SpellCastResult
{
    /// <summary>Whether the spell was successfully cast.</summary>
    public bool Success { get; init; }

    /// <summary>Reason the cast failed, or empty if successful.</summary>
    public string FailureReason { get; init; } = string.Empty;

    /// <summary>Name of the spell that was cast (or attempted).</summary>
    public string SpellName { get; init; } = string.Empty;

    /// <summary>
    /// Effective spell level used for this cast.
    /// For cantrips this is 0. For upcasted spells this is the slot level used.
    /// </summary>
    public int CastLevel { get; init; }

    /// <summary>Whether a spell slot was expended (false for cantrips).</summary>
    public bool SlotExpended { get; init; }

    /// <summary>Damage roll result, or null if the spell deals no damage.</summary>
    public DiceResult? DamageRoll { get; init; }

    /// <summary>
    /// Spell save DC for this cast (8 + proficiency + casting ability modifier).
    /// Zero if the spell has no saving throw.
    /// </summary>
    public int SaveDC { get; init; }

    /// <summary>
    /// Spell attack bonus (proficiency + casting ability modifier).
    /// Zero if the spell doesn't use an attack roll.
    /// </summary>
    public int SpellAttackBonus { get; init; }

    /// <summary>Whether concentration was started for this spell.</summary>
    public bool ConcentrationStarted { get; init; }

    /// <summary>
    /// Whether a previous concentration spell was ended to start this one.
    /// </summary>
    public bool PreviousConcentrationEnded { get; init; }

    /// <summary>
    /// Creates a failed cast result with the given reason.
    /// </summary>
    public static SpellCastResult Failure(string spellName, string reason)
    {
        return new SpellCastResult
        {
            Success = false,
            SpellName = spellName,
            FailureReason = reason
        };
    }
}
