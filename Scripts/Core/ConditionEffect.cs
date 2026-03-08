using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Defines the mechanical effects of a single D&D 5e condition.
/// Each property maps to a concrete rule from the SRD 5.1.
/// Instances are created once at startup and shared (read-only after init).
/// </summary>
public class ConditionEffect
{
    /// <summary>The condition this effect describes.</summary>
    public Condition Condition { get; set; }

    /// <summary>Attackers have advantage on attack rolls against this creature (Blinded, Paralyzed, Restrained, Stunned, Unconscious).</summary>
    public bool GrantsAdvantageOnAttacksAgainst { get; set; }

    /// <summary>This creature has disadvantage on its own attack rolls (Blinded, Frightened, Poisoned, Restrained).</summary>
    public bool GrantsDisadvantageOnAttacks { get; set; }

    /// <summary>This creature has disadvantage on ability checks (Frightened, Poisoned).</summary>
    public bool GrantsDisadvantageOnAbilityChecks { get; set; }

    /// <summary>This creature has disadvantage on Dexterity saving throws (Restrained).</summary>
    public bool GrantsDisadvantageOnDexSaves { get; set; }

    /// <summary>This creature automatically fails Strength and Dexterity saving throws (Paralyzed, Petrified, Stunned, Unconscious).</summary>
    public bool AutoFailStrDexSaves { get; set; }

    /// <summary>This creature's speed becomes 0 and it cannot benefit from bonuses to speed (Grappled, Restrained, Paralyzed, Petrified, Stunned, Unconscious).</summary>
    public bool CannotMove { get; set; }

    /// <summary>This creature cannot take actions (Incapacitated, Paralyzed, Petrified, Stunned, Unconscious).</summary>
    public bool CannotTakeActions { get; set; }

    /// <summary>This creature cannot take reactions (Incapacitated, Paralyzed, Petrified, Stunned, Unconscious).</summary>
    public bool CannotTakeReactions { get; set; }

    /// <summary>Melee attacks that hit this creature are automatic critical hits if the attacker is within 5 feet (Paralyzed, Unconscious).</summary>
    public bool AutoCritOnMeleeHit { get; set; }

    /// <summary>This creature is effectively hidden from normal sight (Invisible).</summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Multiplier applied to movement speed. 1.0 = normal.
    /// Prone creatures spend half movement to stand (handled separately in movement logic).
    /// </summary>
    public float SpeedMultiplier { get; set; } = 1.0f;

    /// <summary>SRD description of the condition's full rules text.</summary>
    public string Description { get; set; } = string.Empty;
}
