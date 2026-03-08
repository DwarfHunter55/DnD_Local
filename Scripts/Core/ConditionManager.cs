using System.Collections.Generic;
using System.Linq;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Tracks active conditions for a single character and provides query methods
/// used by the combat system, movement system, and saving throw logic.
/// Each character should own one ConditionManager instance.
/// </summary>
public class ConditionManager
{
    /// <summary>Maximum exhaustion level before death.</summary>
    public const int MaxExhaustionLevel = 6;

    /// <summary>
    /// Static lookup of every SRD condition's mechanical effects.
    /// Initialized once and shared across all instances.
    /// </summary>
    private static readonly Dictionary<Condition, ConditionEffect> _conditionEffects = InitializeEffects();

    /// <summary>
    /// Active conditions on this character. Value is stack count (meaningful for Exhaustion; 1 for all others).
    /// </summary>
    private readonly Dictionary<Condition, int> _activeConditions = new();

    // ── Apply / Remove ──────────────────────────────────────────────────

    /// <summary>
    /// Applies a condition to this character. For Exhaustion, stacks are additive
    /// up to <see cref="MaxExhaustionLevel"/>. For all other conditions, stacks are clamped to 1.
    /// </summary>
    public void ApplyCondition(Condition condition, int stacks = 1)
    {
        if (stacks <= 0)
            return;

        if (condition == Condition.Exhaustion)
        {
            _activeConditions.TryGetValue(Condition.Exhaustion, out int current);
            int newLevel = current + stacks;
            _activeConditions[Condition.Exhaustion] = newLevel > MaxExhaustionLevel
                ? MaxExhaustionLevel
                : newLevel;
        }
        else
        {
            _activeConditions[condition] = 1;
        }
    }

    /// <summary>
    /// Removes a condition entirely. For Exhaustion, removes all stacks.
    /// Use <see cref="ReduceExhaustion"/> to remove individual levels.
    /// </summary>
    public void RemoveCondition(Condition condition)
    {
        _activeConditions.Remove(condition);
    }

    /// <summary>
    /// Reduces exhaustion by the specified number of levels (minimum 0).
    /// If the level reaches 0, the Exhaustion condition is removed entirely.
    /// </summary>
    public void ReduceExhaustion(int levels = 1)
    {
        if (!_activeConditions.TryGetValue(Condition.Exhaustion, out int current))
            return;

        int newLevel = current - levels;
        if (newLevel <= 0)
            _activeConditions.Remove(Condition.Exhaustion);
        else
            _activeConditions[Condition.Exhaustion] = newLevel;
    }

    /// <summary>Removes all active conditions.</summary>
    public void ClearAll()
    {
        _activeConditions.Clear();
    }

    // ── Queries ─────────────────────────────────────────────────────────

    /// <summary>Returns true if the character currently has the specified condition.</summary>
    public bool HasCondition(Condition condition)
    {
        return _activeConditions.ContainsKey(condition);
    }

    /// <summary>Returns a list of all currently active conditions.</summary>
    public List<Condition> GetActiveConditions()
    {
        return _activeConditions.Keys.ToList();
    }

    /// <summary>
    /// Returns the current exhaustion level (0-6). Returns 0 if not exhausted.
    /// Level 6 means death.
    /// </summary>
    public int GetExhaustionLevel()
    {
        _activeConditions.TryGetValue(Condition.Exhaustion, out int level);
        return level;
    }

    // ── Combat Query Methods ────────────────────────────────────────────

    /// <summary>
    /// Do attackers get advantage on attack rolls against this character?
    /// True if any active condition grants it (Blinded, Paralyzed, Restrained, Stunned, Unconscious, Prone for melee).
    /// Note: Prone grants advantage only to melee attackers within 5 ft — the caller
    /// should check <see cref="HasCondition"/> for Prone separately for ranged attacks.
    /// </summary>
    public bool HasAdvantageOnAttacksAgainst()
    {
        foreach (var kvp in _activeConditions)
        {
            if (_conditionEffects.TryGetValue(kvp.Key, out var effect) && effect.GrantsAdvantageOnAttacksAgainst)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Does this character have disadvantage on its own attack rolls?
    /// True if any active condition imposes it, or Exhaustion level 3+.
    /// </summary>
    public bool HasDisadvantageOnAttacks()
    {
        if (GetExhaustionLevel() >= 3)
            return true;

        foreach (var kvp in _activeConditions)
        {
            if (_conditionEffects.TryGetValue(kvp.Key, out var effect) && effect.GrantsDisadvantageOnAttacks)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Does this character have disadvantage on ability checks?
    /// True if any active condition imposes it, or Exhaustion level 1+.
    /// </summary>
    public bool HasDisadvantageOnAbilityChecks()
    {
        if (GetExhaustionLevel() >= 1)
            return true;

        foreach (var kvp in _activeConditions)
        {
            if (_conditionEffects.TryGetValue(kvp.Key, out var effect) && effect.GrantsDisadvantageOnAbilityChecks)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Does this character have disadvantage on Dexterity saving throws?
    /// True if any active condition imposes it.
    /// </summary>
    public bool HasDisadvantageOnDexSaves()
    {
        foreach (var kvp in _activeConditions)
        {
            if (_conditionEffects.TryGetValue(kvp.Key, out var effect) && effect.GrantsDisadvantageOnDexSaves)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Does this character automatically fail Strength and Dexterity saving throws?
    /// </summary>
    public bool AutoFailsStrDexSaves()
    {
        foreach (var kvp in _activeConditions)
        {
            if (_conditionEffects.TryGetValue(kvp.Key, out var effect) && effect.AutoFailStrDexSaves)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Can this character take actions this turn?
    /// False if any active condition prevents it (Incapacitated, Paralyzed, Petrified, Stunned, Unconscious).
    /// </summary>
    public bool CanTakeActions()
    {
        foreach (var kvp in _activeConditions)
        {
            if (_conditionEffects.TryGetValue(kvp.Key, out var effect) && effect.CannotTakeActions)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Can this character take reactions?
    /// False if any active condition prevents it.
    /// </summary>
    public bool CanTakeReactions()
    {
        foreach (var kvp in _activeConditions)
        {
            if (_conditionEffects.TryGetValue(kvp.Key, out var effect) && effect.CannotTakeReactions)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Can this character move?
    /// False if any active condition prevents movement, or Exhaustion level 5+.
    /// </summary>
    public bool CanMove()
    {
        if (GetExhaustionLevel() >= 5)
            return false;

        foreach (var kvp in _activeConditions)
        {
            if (_conditionEffects.TryGetValue(kvp.Key, out var effect) && effect.CannotMove)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Is this character incapacitated? True if they cannot take actions
    /// (covers Incapacitated, Paralyzed, Petrified, Stunned, Unconscious).
    /// </summary>
    public bool IsIncapacitated()
    {
        return !CanTakeActions();
    }

    /// <summary>
    /// Do melee hits against this character automatically become critical hits?
    /// True if Paralyzed or Unconscious (attacker must be within 5 ft — caller's responsibility).
    /// </summary>
    public bool MeleeHitsAutoCrit()
    {
        foreach (var kvp in _activeConditions)
        {
            if (_conditionEffects.TryGetValue(kvp.Key, out var effect) && effect.AutoCritOnMeleeHit)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Is this character invisible / hidden from normal sight?
    /// </summary>
    public bool IsInvisible()
    {
        foreach (var kvp in _activeConditions)
        {
            if (_conditionEffects.TryGetValue(kvp.Key, out var effect) && effect.IsHidden)
                return true;
        }
        return false;
    }

    // ── Exhaustion-Specific Queries ─────────────────────────────────────

    /// <summary>
    /// Does this character have disadvantage on all saving throws?
    /// True at Exhaustion level 3+.
    /// </summary>
    public bool HasDisadvantageOnSavingThrows()
    {
        return GetExhaustionLevel() >= 3;
    }

    /// <summary>
    /// Returns the effective speed multiplier accounting for all active conditions
    /// and exhaustion. Multipliers stack multiplicatively.
    /// Exhaustion 2: speed halved (0.5). Exhaustion 5+: speed 0 (handled by <see cref="CanMove"/>).
    /// </summary>
    public float GetSpeedMultiplier()
    {
        float multiplier = 1.0f;

        // Exhaustion level 2+ halves speed
        int exhaustion = GetExhaustionLevel();
        if (exhaustion >= 2)
            multiplier *= 0.5f;

        // Apply condition-specific speed multipliers (e.g., if any condition modifies speed)
        foreach (var kvp in _activeConditions)
        {
            if (_conditionEffects.TryGetValue(kvp.Key, out var effect) &&
                effect.SpeedMultiplier < 1.0f)
            {
                multiplier *= effect.SpeedMultiplier;
            }
        }

        return multiplier;
    }

    /// <summary>
    /// Returns the percentage of max HP this character should have.
    /// 100 normally, 50 at Exhaustion level 4+.
    /// </summary>
    public int GetMaxHPMultiplierPercent()
    {
        return GetExhaustionLevel() >= 4 ? 50 : 100;
    }

    /// <summary>
    /// Returns true if this character is dead due to Exhaustion level 6.
    /// </summary>
    public bool IsDeadFromExhaustion()
    {
        return GetExhaustionLevel() >= MaxExhaustionLevel;
    }

    // ── Static Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the static <see cref="ConditionEffect"/> definition for a given condition.
    /// Returns null if the condition is not recognized (should not happen with the SRD set).
    /// </summary>
    public static ConditionEffect GetConditionEffect(Condition condition)
    {
        _conditionEffects.TryGetValue(condition, out var effect);
        return effect;
    }

    // ── Initialization ──────────────────────────────────────────────────

    /// <summary>
    /// Builds the complete lookup table of all 15 SRD 5.1 conditions
    /// and their mechanical effects. Called once at static init time.
    /// </summary>
    private static Dictionary<Condition, ConditionEffect> InitializeEffects()
    {
        return new Dictionary<Condition, ConditionEffect>
        {
            [Condition.Blinded] = new ConditionEffect
            {
                Condition = Condition.Blinded,
                GrantsAdvantageOnAttacksAgainst = true,
                GrantsDisadvantageOnAttacks = true,
                Description = "A blinded creature can't see and automatically fails any ability check that " +
                    "requires sight. Attack rolls against the creature have advantage, and the creature's " +
                    "attack rolls have disadvantage."
            },

            [Condition.Charmed] = new ConditionEffect
            {
                Condition = Condition.Charmed,
                // Charmed has no generic combat flags — its effects are contextual:
                // - Can't attack the charmer or target them with harmful abilities/magic
                // - Charmer has advantage on social ability checks against the creature
                // These are handled by combat targeting logic, not boolean flags.
                Description = "A charmed creature can't attack the charmer or target the charmer with harmful " +
                    "abilities or magical effects. The charmer has advantage on any ability check to interact " +
                    "socially with the creature."
            },

            [Condition.Deafened] = new ConditionEffect
            {
                Condition = Condition.Deafened,
                // Deafened has no generic combat flags — auto-fails hearing checks
                // handled contextually by the skill check system.
                Description = "A deafened creature can't hear and automatically fails any ability check that " +
                    "requires hearing."
            },

            [Condition.Exhaustion] = new ConditionEffect
            {
                Condition = Condition.Exhaustion,
                // Exhaustion effects are level-dependent and handled by dedicated methods:
                // GetExhaustionLevel(), HasDisadvantageOnAbilityChecks(), GetSpeedMultiplier(),
                // HasDisadvantageOnAttacks(), HasDisadvantageOnSavingThrows(),
                // GetMaxHPMultiplierPercent(), IsDeadFromExhaustion().
                Description = "Exhaustion has 6 cumulative levels. " +
                    "1: Disadvantage on ability checks. " +
                    "2: Speed halved. " +
                    "3: Disadvantage on attack rolls and saving throws. " +
                    "4: Hit point maximum halved. " +
                    "5: Speed reduced to 0. " +
                    "6: Death."
            },

            [Condition.Frightened] = new ConditionEffect
            {
                Condition = Condition.Frightened,
                GrantsDisadvantageOnAttacks = true,
                GrantsDisadvantageOnAbilityChecks = true,
                // "Can't willingly move closer to the source of fear" is handled
                // by movement validation, not a boolean flag.
                Description = "A frightened creature has disadvantage on ability checks and attack rolls while " +
                    "the source of its fear is within line of sight. The creature can't willingly move closer " +
                    "to the source of its fear."
            },

            [Condition.Grappled] = new ConditionEffect
            {
                Condition = Condition.Grappled,
                CannotMove = true,
                // "Ends if the grappler is incapacitated" and "ends if an effect removes
                // the grappled creature from the grappler's reach" are handled by combat logic.
                Description = "A grappled creature's speed becomes 0, and it can't benefit from any bonus to " +
                    "its speed. The condition ends if the grappler is incapacitated or if an effect removes " +
                    "the grappled creature from the reach of the grappler."
            },

            [Condition.Incapacitated] = new ConditionEffect
            {
                Condition = Condition.Incapacitated,
                CannotTakeActions = true,
                CannotTakeReactions = true,
                Description = "An incapacitated creature can't take actions or reactions."
            },

            [Condition.Invisible] = new ConditionEffect
            {
                Condition = Condition.Invisible,
                IsHidden = true,
                // Invisible creatures have advantage on attacks and attacks against them
                // have disadvantage. However, these are the INVERSE of the normal flags:
                // - The invisible creature's attacks have advantage (not tracked here —
                //   the attacker checks if they are invisible).
                // - Attacks AGAINST the invisible creature have disadvantage (opposite of
                //   GrantsAdvantageOnAttacksAgainst).
                // We do NOT set GrantsAdvantageOnAttacksAgainst (that would be wrong).
                // Combat resolver should check IsInvisible() on the target to apply disadvantage
                // to incoming attacks, and check IsInvisible() on the attacker for advantage.
                Description = "An invisible creature is impossible to see without the aid of magic or a " +
                    "special sense. The creature's location can be detected by noise or tracks. Attack " +
                    "rolls against the creature have disadvantage, and the creature's attack rolls have advantage."
            },

            [Condition.Paralyzed] = new ConditionEffect
            {
                Condition = Condition.Paralyzed,
                CannotTakeActions = true,    // Incapacitated
                CannotTakeReactions = true,  // Incapacitated
                CannotMove = true,           // Can't move or speak
                AutoFailStrDexSaves = true,
                GrantsAdvantageOnAttacksAgainst = true,
                AutoCritOnMeleeHit = true,   // Within 5 feet
                Description = "A paralyzed creature is incapacitated and can't move or speak. The creature " +
                    "automatically fails Strength and Dexterity saving throws. Attack rolls against the " +
                    "creature have advantage. Any attack that hits the creature is a critical hit if the " +
                    "attacker is within 5 feet."
            },

            [Condition.Petrified] = new ConditionEffect
            {
                Condition = Condition.Petrified,
                CannotTakeActions = true,    // Incapacitated
                CannotTakeReactions = true,  // Incapacitated
                CannotMove = true,           // Transformed to inanimate substance
                AutoFailStrDexSaves = true,
                // Resistance to all damage, immunity to poison and disease are
                // handled by damage resolution and condition application logic.
                Description = "A petrified creature is transformed into a solid inanimate substance. It is " +
                    "incapacitated, can't move or speak, and is unaware of its surroundings. Attack rolls " +
                    "against the creature have neither advantage nor disadvantage (weight and substance " +
                    "offset incapacitation). The creature automatically fails Strength and Dexterity saving " +
                    "throws. The creature has resistance to all damage and is immune to poison and disease."
            },

            [Condition.Poisoned] = new ConditionEffect
            {
                Condition = Condition.Poisoned,
                GrantsDisadvantageOnAttacks = true,
                GrantsDisadvantageOnAbilityChecks = true,
                Description = "A poisoned creature has disadvantage on attack rolls and ability checks."
            },

            [Condition.Prone] = new ConditionEffect
            {
                Condition = Condition.Prone,
                GrantsDisadvantageOnAttacks = true,
                // Prone grants advantage to melee attackers within 5 ft but disadvantage
                // to ranged attackers. We set the melee advantage flag here; the combat
                // resolver checks Prone specifically for ranged vs melee distinction.
                GrantsAdvantageOnAttacksAgainst = true,
                // Standing from prone costs half movement — handled by movement system.
                Description = "A prone creature's only movement option is to crawl, unless it stands up. " +
                    "The creature has disadvantage on attack rolls. An attack roll against the creature has " +
                    "advantage if the attacker is within 5 feet. Otherwise, the attack roll has disadvantage."
            },

            [Condition.Restrained] = new ConditionEffect
            {
                Condition = Condition.Restrained,
                CannotMove = true,
                GrantsDisadvantageOnAttacks = true,
                GrantsAdvantageOnAttacksAgainst = true,
                GrantsDisadvantageOnDexSaves = true,
                Description = "A restrained creature's speed becomes 0. Attack rolls against the creature " +
                    "have advantage, and the creature's attack rolls have disadvantage. The creature has " +
                    "disadvantage on Dexterity saving throws."
            },

            [Condition.Stunned] = new ConditionEffect
            {
                Condition = Condition.Stunned,
                CannotTakeActions = true,    // Incapacitated
                CannotTakeReactions = true,  // Incapacitated
                CannotMove = true,           // Can't move
                AutoFailStrDexSaves = true,
                GrantsAdvantageOnAttacksAgainst = true,
                // "Speak only falteringly" — flavor, no mechanical flag needed.
                Description = "A stunned creature is incapacitated, can't move, and can speak only " +
                    "falteringly. The creature automatically fails Strength and Dexterity saving throws. " +
                    "Attack rolls against the creature have advantage."
            },

            [Condition.Unconscious] = new ConditionEffect
            {
                Condition = Condition.Unconscious,
                CannotTakeActions = true,    // Incapacitated
                CannotTakeReactions = true,  // Incapacitated
                CannotMove = true,           // Can't move or speak
                AutoFailStrDexSaves = true,
                GrantsAdvantageOnAttacksAgainst = true,
                AutoCritOnMeleeHit = true,   // Within 5 feet
                // "Drops whatever it's holding and falls prone" — handled by combat
                // event logic (auto-apply Prone when Unconscious is applied).
                Description = "An unconscious creature is incapacitated, can't move or speak, and is " +
                    "unaware of its surroundings. The creature drops whatever it's holding and falls prone. " +
                    "The creature automatically fails Strength and Dexterity saving throws. Attack rolls " +
                    "against the creature have advantage. Any attack that hits the creature is a critical " +
                    "hit if the attacker is within 5 feet."
            },
        };
    }
}
