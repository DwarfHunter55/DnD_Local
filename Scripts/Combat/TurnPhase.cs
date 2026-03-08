namespace ChroniclesUnbound.Combat;

/// <summary>
/// Snapshot of what resources a combatant has remaining this turn.
/// Used by the UI and AI to determine available options.
/// </summary>
public class TurnPhase
{
    /// <summary>Remaining movement in feet.</summary>
    public int RemainingMovement { get; set; }

    /// <summary>Whether the action has been used this turn.</summary>
    public bool ActionUsed { get; set; }

    /// <summary>Whether the bonus action has been used this turn.</summary>
    public bool BonusActionUsed { get; set; }

    /// <summary>Whether the reaction has been used (resets at start of own turn).</summary>
    public bool ReactionUsed { get; set; }

    /// <summary>
    /// Creates a TurnPhase snapshot from a combatant's current state.
    /// </summary>
    public static TurnPhase FromCombatant(Combatant combatant)
    {
        return new TurnPhase
        {
            RemainingMovement = combatant.RemainingMovement,
            ActionUsed = combatant.ActionUsed,
            BonusActionUsed = combatant.BonusActionUsed,
            ReactionUsed = combatant.ReactionUsed
        };
    }
}
