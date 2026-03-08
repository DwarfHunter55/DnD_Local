using System;
using System.Collections.Generic;
using System.Linq;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Handles short and long rest mechanics per D&D 5e SRD rules.
/// Coordinates with SpellSlotTracker, ConcentrationTracker, and ConditionManager
/// for full rest resolution.
/// </summary>
public class RestManager
{
    // ── Hit Die Sizes by Class ──────────────────────────────────────────

    /// <summary>
    /// Maps class names (lowercase) to their hit die size per the 5e SRD.
    /// </summary>
    private static readonly Dictionary<string, int> HitDieSizeByClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["barbarian"] = 12,
        ["fighter"]   = 10,
        ["paladin"]   = 10,
        ["ranger"]    = 10,
        ["bard"]      = 8,
        ["cleric"]    = 8,
        ["druid"]     = 8,
        ["monk"]      = 8,
        ["rogue"]     = 8,
        ["warlock"]   = 8,
        ["sorcerer"]  = 6,
        ["wizard"]    = 6,
    };

    /// <summary>
    /// Features that recover on a short rest, mapped by class (lowercase).
    /// Each entry is (featureName, minLevelRequired).
    /// </summary>
    private static readonly Dictionary<string, List<(string Feature, int MinLevel)>> ShortRestFeatures =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["fighter"] = new List<(string, int)>
        {
            ("Second Wind", 1),
            ("Action Surge", 2),
        },
        ["monk"] = new List<(string, int)>
        {
            ("Ki Points", 1),
        },
        ["bard"] = new List<(string, int)>
        {
            ("Bardic Inspiration", 5),  // Song of Rest at level 5 enables short rest recovery
        },
    };

    /// <summary>
    /// All features that reset on a long rest (superset of short rest features plus others).
    /// Class name (lowercase) -> list of (featureName, minLevel).
    /// </summary>
    private static readonly Dictionary<string, List<(string Feature, int MinLevel)>> LongRestFeatures =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["fighter"] = new List<(string, int)>
        {
            ("Second Wind", 1),
            ("Action Surge", 2),
            ("Indomitable", 9),
        },
        ["monk"] = new List<(string, int)>
        {
            ("Ki Points", 1),
        },
        ["bard"] = new List<(string, int)>
        {
            ("Bardic Inspiration", 1),
        },
        ["paladin"] = new List<(string, int)>
        {
            ("Lay on Hands", 1),
            ("Divine Sense", 1),
            ("Cleansing Touch", 14),
        },
        ["cleric"] = new List<(string, int)>
        {
            ("Channel Divinity", 2),
        },
        ["druid"] = new List<(string, int)>
        {
            ("Wild Shape", 2),
        },
        ["wizard"] = new List<(string, int)>
        {
            ("Arcane Recovery", 1),
        },
        ["sorcerer"] = new List<(string, int)>
        {
            ("Sorcery Points", 2),
        },
        ["barbarian"] = new List<(string, int)>
        {
            ("Rage", 1),
        },
    };

    // ── Short Rest ──────────────────────────────────────────────────────

    /// <summary>
    /// Performs a short rest (1 hour in-game) for the given character.
    /// The player chooses how many hit dice to spend for healing.
    /// </summary>
    /// <param name="character">The character taking the rest.</param>
    /// <param name="hitDiceToSpend">
    /// Number of hit dice the player chooses to spend. Each die is rolled
    /// as 1d(hitDieSize) + CON modifier, healing that amount (minimum 0).
    /// </param>
    /// <returns>A result summarizing what happened during the short rest.</returns>
    /// <exception cref="ArgumentNullException">If character is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If hitDiceToSpend is negative or exceeds available hit dice.
    /// </exception>
    public ShortRestResult ShortRest(Character character, int hitDiceToSpend)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));
        if (hitDiceToSpend < 0)
            throw new ArgumentOutOfRangeException(nameof(hitDiceToSpend), "Cannot spend negative hit dice.");

        int available = GetAvailableHitDice(character);
        if (hitDiceToSpend > available)
            throw new ArgumentOutOfRangeException(nameof(hitDiceToSpend),
                $"Cannot spend {hitDiceToSpend} hit dice; only {available} available.");

        int hitDieSize = GetHitDieSize(character);
        int conMod = character.AbilityScores.GetModifier(Ability.Constitution);

        var healRolls = new List<DiceResult>();
        int totalHealed = 0;

        for (int i = 0; i < hitDiceToSpend; i++)
        {
            // Roll 1d(hitDieSize) + CON modifier
            string notation = conMod >= 0 ? $"1d{hitDieSize}+{conMod}" : $"1d{hitDieSize}{conMod}";
            var roll = DiceRoller.Roll(notation);

            // Healing cannot be negative (min 0 per die)
            int healing = Math.Max(0, roll.Total);
            totalHealed += healing;
            healRolls.Add(roll);
        }

        // Apply healing (cap at max HP)
        int preRestHP = character.HitPoints;
        character.HitPoints = Math.Min(character.HitPoints + totalHealed, character.MaxHitPoints);
        int actualHealed = character.HitPoints - preRestHP;

        // Spend the hit dice
        character.HitDiceSpent += hitDiceToSpend;

        // Recover short rest features
        var recoveredFeatures = RecoverShortRestFeatures(character);

        // Warlock pact magic: restore all spell slots on short rest
        bool slotsRecovered = IsWarlock(character);
        if (slotsRecovered)
        {
            RestoreAllSpellSlots(character);
        }

        return new ShortRestResult
        {
            HitDiceSpent = hitDiceToSpend,
            HPHealed = actualHealed,
            IndividualHealRolls = healRolls,
            FeaturesRecovered = recoveredFeatures,
            SpellSlotsRecovered = slotsRecovered,
        };
    }

    // ── Long Rest ───────────────────────────────────────────────────────

    /// <summary>
    /// Performs a long rest (8 hours in-game) for the given character.
    /// Restores HP to max, recovers half of total hit dice (min 1),
    /// restores all spell slots, reduces exhaustion by 1, and resets
    /// all daily-use class features.
    /// </summary>
    /// <param name="character">The character taking the rest.</param>
    /// <param name="conditionManager">
    /// Optional condition manager for this character, used to reduce exhaustion.
    /// Pass null if exhaustion tracking is not active.
    /// </param>
    /// <param name="concentrationTracker">
    /// Optional concentration tracker for this character, used to end concentration.
    /// Pass null if concentration tracking is not active.
    /// </param>
    /// <returns>A result summarizing what happened during the long rest.</returns>
    /// <exception cref="ArgumentNullException">If character is null.</exception>
    public LongRestResult LongRest(
        Character character,
        ConditionManager? conditionManager = null,
        ConcentrationTracker? concentrationTracker = null)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        // Restore HP to max
        int preRestHP = character.HitPoints;
        character.HitPoints = character.MaxHitPoints;
        int hpRestored = character.HitPoints - preRestHP;

        // Recover hit dice: regain up to half of total (minimum 1)
        int maxHitDice = GetMaxHitDice(character);
        int halfTotal = Math.Max(1, maxHitDice / 2);
        int diceToRecover = Math.Min(halfTotal, character.HitDiceSpent);
        character.HitDiceSpent -= diceToRecover;

        // Restore all spell slots
        bool hadSpellSlots = RestoreAllSpellSlots(character);

        // End concentration
        concentrationTracker?.EndConcentration();

        // Reduce exhaustion by 1 level
        bool exhaustionReduced = false;
        int newExhaustionLevel = 0;
        if (conditionManager != null)
        {
            int priorLevel = conditionManager.GetExhaustionLevel();
            if (priorLevel > 0)
            {
                conditionManager.ReduceExhaustion(1);
                exhaustionReduced = true;
            }
            newExhaustionLevel = conditionManager.GetExhaustionLevel();
        }

        // Reset all daily-use features (long rest features are a superset of short rest)
        var recoveredFeatures = RecoverLongRestFeatures(character);

        return new LongRestResult
        {
            HPRestored = hpRestored,
            HitDiceRecovered = diceToRecover,
            SpellSlotsRestored = hadSpellSlots,
            ExhaustionReduced = exhaustionReduced,
            NewExhaustionLevel = newExhaustionLevel,
            FeaturesRecovered = recoveredFeatures,
        };
    }

    // ── Hit Dice Queries ────────────────────────────────────────────────

    /// <summary>
    /// Returns the number of hit dice currently available to spend.
    /// </summary>
    public int GetAvailableHitDice(Character character)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        return Math.Max(0, GetMaxHitDice(character) - character.HitDiceSpent);
    }

    /// <summary>
    /// Returns the maximum number of hit dice for this character (equal to level).
    /// </summary>
    public int GetMaxHitDice(Character character)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        return Math.Max(1, character.Level);
    }

    /// <summary>
    /// Returns the hit die size for the character's class (e.g. 10 for Fighter, 8 for Cleric).
    /// Defaults to d8 if the class is not recognized.
    /// </summary>
    public int GetHitDieSize(Character character)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        if (HitDieSizeByClass.TryGetValue(character.CharacterClassName, out int size))
            return size;

        // Default to d8 for unrecognized classes (safe middle ground)
        return 8;
    }

    // ── Private Helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the character is a Warlock (pact magic caster).
    /// </summary>
    private static bool IsWarlock(Character character)
    {
        return string.Equals(character.CharacterClassName, "Warlock", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Restores all spell slots to maximum values on the character.
    /// Returns true if the character had any spell slot capacity.
    /// </summary>
    private static bool RestoreAllSpellSlots(Character character)
    {
        bool hasSlots = false;
        for (int i = 0; i < character.SpellSlotsMax.Length; i++)
        {
            if (character.SpellSlotsMax[i] > 0)
                hasSlots = true;
            character.SpellSlotsCurrent[i] = character.SpellSlotsMax[i];
        }
        return hasSlots;
    }

    /// <summary>
    /// Recovers class features that reset on a short rest.
    /// Returns the list of feature names that were restored.
    /// </summary>
    private static List<string> RecoverShortRestFeatures(Character character)
    {
        var recovered = new List<string>();
        string className = character.CharacterClassName;

        if (!ShortRestFeatures.TryGetValue(className, out var features))
            return recovered;

        foreach (var (featureName, minLevel) in features)
        {
            if (character.Level < minLevel)
                continue;

            if (TryRecoverFeature(character, featureName))
                recovered.Add(featureName);
        }

        return recovered;
    }

    /// <summary>
    /// Recovers all class features that reset on a long rest (superset of short rest features).
    /// Returns the list of feature names that were restored.
    /// </summary>
    private static List<string> RecoverLongRestFeatures(Character character)
    {
        var recovered = new List<string>();
        string className = character.CharacterClassName;

        // Recover short rest features too (they also reset on long rest)
        if (ShortRestFeatures.TryGetValue(className, out var shortFeatures))
        {
            foreach (var (featureName, minLevel) in shortFeatures)
            {
                if (character.Level >= minLevel && TryRecoverFeature(character, featureName))
                    recovered.Add(featureName);
            }
        }

        // Recover long-rest-only features
        if (LongRestFeatures.TryGetValue(className, out var longFeatures))
        {
            foreach (var (featureName, minLevel) in longFeatures)
            {
                if (character.Level < minLevel)
                    continue;

                // Skip if already recovered via short rest features above
                if (recovered.Contains(featureName))
                    continue;

                if (TryRecoverFeature(character, featureName))
                    recovered.Add(featureName);
            }
        }

        return recovered;
    }

    /// <summary>
    /// Attempts to restore a single feature to its maximum uses.
    /// Returns true if the feature was found and actually restored (was not already full).
    /// </summary>
    private static bool TryRecoverFeature(Character character, string featureName)
    {
        if (!character.FeatureUsesMax.TryGetValue(featureName, out int maxUses))
            return false;

        character.FeatureUsesRemaining.TryGetValue(featureName, out int current);
        if (current >= maxUses)
            return false;

        character.FeatureUsesRemaining[featureName] = maxUses;
        return true;
    }
}
