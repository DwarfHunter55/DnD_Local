using System;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Resolves D&D 5e combat actions: attacks, skill checks, saving throws, and death saves.
/// Pure deterministic logic (given dice results) with no Godot dependencies.
/// </summary>
public static class CombatResolver
{
    /// <summary>
    /// Resolve a full attack: d20 roll, hit determination, and damage if applicable.
    /// </summary>
    /// <param name="attackBonus">Total attack bonus (ability mod + proficiency + misc).</param>
    /// <param name="targetAC">Target's armor class.</param>
    /// <param name="damageNotation">Dice notation for base damage (e.g. "1d8").</param>
    /// <param name="damageBonus">Flat bonus added to damage (ability mod, etc.).</param>
    /// <param name="damageType">Damage type string (e.g. "Slashing").</param>
    /// <param name="hasAdvantage">Roll with advantage.</param>
    /// <param name="hasDisadvantage">Roll with disadvantage.</param>
    public static AttackResult ResolveAttack(
        int attackBonus,
        int targetAC,
        string damageNotation,
        int damageBonus,
        string damageType,
        bool hasAdvantage = false,
        bool hasDisadvantage = false)
    {
        int naturalRoll = DiceRoller.RollD20(hasAdvantage, hasDisadvantage);
        int totalAttack = naturalRoll + attackBonus;

        bool isCritical = naturalRoll == 20;
        bool isFumble = naturalRoll == 1;

        // Nat 20 always hits, nat 1 always misses, otherwise compare vs AC.
        bool hits = isCritical || (!isFumble && totalAttack >= targetAC);

        int damage = 0;
        DiceResult? damageRoll = null;

        if (hits)
        {
            // On a critical hit, double the number of damage dice.
            string effectiveNotation = isCritical
                ? DoubleDamageDice(damageNotation)
                : damageNotation;

            damageRoll = DiceRoller.Roll(effectiveNotation);
            damage = Math.Max(0, damageRoll.Total + damageBonus);
        }

        return new AttackResult(
            naturalRoll,
            totalAttack,
            hits,
            isCritical,
            isFumble,
            damage,
            damageType,
            damageRoll);
    }

    /// <summary>
    /// Resolve a skill check against a DC.
    /// </summary>
    public static SkillCheckResult ResolveSkillCheck(
        string skill,
        int modifier,
        int dc,
        bool hasAdvantage = false,
        bool hasDisadvantage = false)
    {
        int roll = DiceRoller.RollD20(hasAdvantage, hasDisadvantage);
        return new SkillCheckResult(skill, roll, modifier, dc, hasAdvantage, hasDisadvantage);
    }

    /// <summary>
    /// Resolve a saving throw against a DC.
    /// </summary>
    public static SavingThrowResult ResolveSavingThrow(
        string ability,
        int modifier,
        int dc,
        bool hasAdvantage = false,
        bool hasDisadvantage = false)
    {
        int roll = DiceRoller.RollD20(hasAdvantage, hasDisadvantage);
        return new SavingThrowResult(ability, roll, modifier, dc, hasAdvantage, hasDisadvantage);
    }

    /// <summary>
    /// Roll a death saving throw per 5e rules.
    /// </summary>
    /// <returns>
    /// success: true if 10+ on d20.
    /// critSuccess: true if natural 20 (regain 1 HP, conscious).
    /// critFail: true if natural 1 (counts as 2 death save failures).
    /// </returns>
    public static (bool success, bool critSuccess, bool critFail) RollDeathSave()
    {
        int roll = DiceRoller.RollSingle(20);

        bool critSuccess = roll == 20;
        bool critFail = roll == 1;
        bool success = roll >= 10;

        return (success, critSuccess, critFail);
    }

    /// <summary>
    /// Double the die count in a notation string for critical hits.
    /// "1d8" -> "2d8", "2d6+3" -> "4d6+3", "d12" -> "2d12".
    /// Only the dice are doubled; modifiers are unchanged.
    /// </summary>
    private static string DoubleDamageDice(string notation)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            notation.Trim(), @"^(\d*)d(\d+)([+-]\d+)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
            return notation; // Fallback: return as-is if we can't parse.

        int count = match.Groups[1].Value.Length > 0
            ? int.Parse(match.Groups[1].Value)
            : 1;

        int sides = int.Parse(match.Groups[2].Value);
        string modPart = match.Groups[3].Value;

        return $"{count * 2}d{sides}{modPart}";
    }
}
