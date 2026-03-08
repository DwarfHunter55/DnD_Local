using ChroniclesUnbound.Core;

namespace ChroniclesUnbound.Combat;

/// <summary>
/// Result of executing a combat action, containing all outcome data
/// for the combat log, UI display, and downstream processing.
/// </summary>
public class CombatActionResult
{
    /// <summary>The type of action that was executed.</summary>
    public ActionType ActionType { get; set; }

    /// <summary>Whether the action succeeded (attack hit, spell cast, move completed, etc.).</summary>
    public bool Success { get; set; }

    /// <summary>Human-readable description for the combat log.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Attack roll result, if this was an attack action.</summary>
    public AttackResult AttackResult { get; set; }

    /// <summary>Spell cast result, if this was a spell action.</summary>
    public SpellCastResult SpellCastResult { get; set; }

    /// <summary>Total damage dealt to the target.</summary>
    public int DamageDealt { get; set; }

    /// <summary>Total healing done to the target.</summary>
    public int HealingDone { get; set; }

    /// <summary>Whether this action triggered an opportunity attack from an enemy.</summary>
    public bool TriggeredOpportunityAttack { get; set; }

    /// <summary>Result of the opportunity attack, if one was triggered.</summary>
    public CombatActionResult OpportunityAttackResult { get; set; }

    /// <summary>Whether the target was reduced to 0 HP by this action.</summary>
    public bool TargetDowned { get; set; }

    /// <summary>Whether the target was killed by this action (monster at 0 HP or 3 death save failures).</summary>
    public bool TargetKilled { get; set; }

    /// <summary>
    /// Creates a simple success result with a description.
    /// </summary>
    public static CombatActionResult SimpleSuccess(ActionType type, string description)
    {
        return new CombatActionResult
        {
            ActionType = type,
            Success = true,
            Description = description
        };
    }

    /// <summary>
    /// Creates a failure result with a description.
    /// </summary>
    public static CombatActionResult Failure(ActionType type, string description)
    {
        return new CombatActionResult
        {
            ActionType = type,
            Success = false,
            Description = description
        };
    }
}
