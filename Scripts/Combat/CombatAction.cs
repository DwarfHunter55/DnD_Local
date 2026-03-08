using Godot;

namespace ChroniclesUnbound.Combat;

/// <summary>
/// Types of actions available during combat, including standard actions,
/// bonus actions, reactions, and movement.
/// </summary>
public enum ActionType
{
    // ── Standard Actions ────────────────────────────────────────────────
    Attack,
    CastSpell,
    Dash,
    Dodge,
    Disengage,
    Help,
    Hide,
    Ready,
    UseObject,

    // ── Bonus Actions ───────────────────────────────────────────────────
    OffhandAttack,
    BonusActionSpell,
    SecondWind,
    ActionSurge,

    // ── Reactions ────────────────────────────────────────────────────────
    OpportunityAttack,
    ShieldSpell,
    Counterspell,

    // ── Movement ─────────────────────────────────────────────────────────
    Move,
    Stand // Stand from prone — costs half movement
}

/// <summary>
/// Represents a combat action to be validated and executed by the CombatManager.
/// Serves as the command object in the action execution pipeline.
/// </summary>
public class CombatAction
{
    /// <summary>The type of action being performed.</summary>
    public ActionType Type { get; set; }

    /// <summary>The combatant performing the action.</summary>
    public Combatant Actor { get; set; }

    /// <summary>The target combatant, if applicable (null for self-only or movement actions).</summary>
    public Combatant Target { get; set; }

    /// <summary>Target grid position for movement actions.</summary>
    public Vector2I TargetPosition { get; set; }

    /// <summary>Spell name for CastSpell / BonusActionSpell actions.</summary>
    public string SpellName { get; set; }

    /// <summary>Spell slot level to use (0 for cantrips, or higher for upcasting).</summary>
    public int SpellLevel { get; set; }

    /// <summary>
    /// Returns true if this action type consumes the standard action.
    /// </summary>
    public bool IsStandardAction()
    {
        return Type switch
        {
            ActionType.Attack or ActionType.CastSpell or ActionType.Dash or
            ActionType.Dodge or ActionType.Disengage or ActionType.Help or
            ActionType.Hide or ActionType.Ready or ActionType.UseObject => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns true if this action type consumes the bonus action.
    /// </summary>
    public bool IsBonusAction()
    {
        return Type switch
        {
            ActionType.OffhandAttack or ActionType.BonusActionSpell or
            ActionType.SecondWind or ActionType.ActionSurge => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns true if this action type consumes the reaction.
    /// </summary>
    public bool IsReaction()
    {
        return Type switch
        {
            ActionType.OpportunityAttack or ActionType.ShieldSpell or
            ActionType.Counterspell => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns true if this is a movement action (Move or Stand).
    /// </summary>
    public bool IsMovement()
    {
        return Type == ActionType.Move || Type == ActionType.Stand;
    }
}
