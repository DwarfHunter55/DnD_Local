using Godot;
using ChroniclesUnbound.Core;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Combat;

/// <summary>
/// Wraps a Character for combat tracking. Holds per-encounter and per-turn state
/// such as initiative, remaining actions, death saves, and grid position.
/// </summary>
public class Combatant
{
    /// <summary>The underlying character data.</summary>
    public Character Character { get; set; }

    /// <summary>Unique identifier for this combatant in the encounter (e.g. "goblin_1").</summary>
    public string Id { get; set; }

    /// <summary>True if this combatant is controlled by the player (not AI).</summary>
    public bool IsPlayerControlled { get; set; }

    /// <summary>True for the player character and allied companions.</summary>
    public bool IsAlly { get; set; }

    /// <summary>Initiative roll result used for turn order sorting.</summary>
    public int Initiative { get; set; }

    // ── Per-Turn Resources ──────────────────────────────────────────────

    /// <summary>Remaining movement in feet for this turn.</summary>
    public int RemainingMovement { get; set; }

    /// <summary>Whether the standard action has been used this turn.</summary>
    public bool ActionUsed { get; set; }

    /// <summary>Whether the bonus action has been used this turn.</summary>
    public bool BonusActionUsed { get; set; }

    /// <summary>Whether the reaction has been used (resets at start of own turn).</summary>
    public bool ReactionUsed { get; set; }

    /// <summary>Whether this combatant has the Extra Attack feature.</summary>
    public bool HasExtraAttack { get; set; }

    /// <summary>Number of attacks remaining this turn (resets each turn).</summary>
    public int AttacksRemaining { get; set; }

    // ── Combat State ────────────────────────────────────────────────────

    /// <summary>Condition tracker for this combatant.</summary>
    public ConditionManager Conditions { get; set; } = new();

    /// <summary>Current position on the combat grid.</summary>
    public Vector2I GridPosition { get; set; }

    /// <summary>True if the combatant has HP above 0.</summary>
    public bool IsConscious => Character.HitPoints > 0;

    /// <summary>Number of successful death saving throws (3 = stabilized).</summary>
    public int DeathSaveSuccesses { get; set; }

    /// <summary>Number of failed death saving throws (3 = dead).</summary>
    public int DeathSaveFailures { get; set; }

    // ── Tactical Flags ──────────────────────────────────────────────────

    /// <summary>True if this combatant took the Dodge action (attacks against have disadvantage).</summary>
    public bool IsDodging { get; set; }

    /// <summary>True if this combatant took the Disengage action (no opportunity attacks).</summary>
    public bool IsDisengaging { get; set; }

    /// <summary>True if this combatant used the Help action on a target this turn.</summary>
    public bool IsHelping { get; set; }

    /// <summary>The combatant ID that Help was directed at, if any.</summary>
    public string HelpTargetId { get; set; }

    /// <summary>True if this combatant is hidden (successful Stealth check).</summary>
    public bool IsHidden { get; set; }

    // ── Turn Lifecycle ──────────────────────────────────────────────────

    /// <summary>
    /// Resets per-turn resources at the start of this combatant's turn.
    /// Movement, action, bonus action, and attacks are refreshed.
    /// Reaction is also reset here (per 5e: resets at start of your turn).
    /// Tactical flags from the previous turn are cleared.
    /// </summary>
    public void StartTurn()
    {
        // Calculate effective speed from base speed and condition modifiers
        float speedMultiplier = Conditions.GetSpeedMultiplier();
        int baseSpeed = Character.Speed;
        RemainingMovement = Conditions.CanMove()
            ? (int)(baseSpeed * speedMultiplier)
            : 0;

        ActionUsed = false;
        BonusActionUsed = false;
        ReactionUsed = false;

        AttacksRemaining = HasExtraAttack ? 2 : 1;

        // Clear previous turn's tactical flags
        IsDodging = false;
        IsDisengaging = false;
        IsHelping = false;
        HelpTargetId = null;
        IsHidden = false;
    }

    /// <summary>
    /// Processes end-of-turn effects such as ongoing damage from conditions or hazards.
    /// Called when the combatant's turn ends, before the next combatant acts.
    /// </summary>
    public void EndTurn()
    {
        // End-of-turn condition processing (saving throws for ongoing effects, etc.)
        // is handled by the CombatManager, which has context about the full encounter.
        // This method exists as a hook for combatant-local cleanup.
    }

    /// <summary>
    /// Returns true if this combatant is dead (3 death save failures, or a monster at 0 HP).
    /// </summary>
    public bool IsDead()
    {
        if (DeathSaveFailures >= 3)
            return true;

        // Monsters die immediately at 0 HP (no death saves)
        if (!IsAlly && Character.HitPoints <= 0)
            return true;

        return false;
    }

    /// <summary>
    /// Returns true if this combatant is stabilized (3 death save successes while at 0 HP).
    /// </summary>
    public bool IsStabilized()
    {
        return !IsConscious && DeathSaveSuccesses >= 3 && DeathSaveFailures < 3;
    }

    /// <summary>
    /// Resets death save counters (e.g., when healed back to consciousness).
    /// </summary>
    public void ResetDeathSaves()
    {
        DeathSaveSuccesses = 0;
        DeathSaveFailures = 0;
    }
}
