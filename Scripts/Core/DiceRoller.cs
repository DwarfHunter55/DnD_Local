using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Thread-safe static dice roller supporting standard dice notation.
/// All rolls are transparent — individual die results are always preserved.
/// </summary>
public static class DiceRoller
{
    // Pattern: optional count, 'd', sides, optional +/- modifier
    // Examples: "2d6+3", "d20", "4d6", "1d8-1", "3d10+5"
    private static readonly Regex DicePattern = new(
        @"^(\d*)d(\d+)([+-]\d+)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Thread-safe RNG — one instance per thread avoids locking overhead.
    [ThreadStatic]
    private static Random? _rng;

    private static Random Rng => _rng ??= new Random(
        Environment.TickCount ^ System.Threading.Thread.CurrentThread.ManagedThreadId);

    /// <summary>
    /// Parse standard dice notation and roll.
    /// Supports: "2d6+3", "d20", "4d6", "1d8-1", "3d10+5".
    /// </summary>
    /// <exception cref="ArgumentException">If the notation cannot be parsed.</exception>
    public static DiceResult Roll(string notation)
    {
        if (string.IsNullOrWhiteSpace(notation))
            throw new ArgumentException("Dice notation cannot be null or empty.", nameof(notation));

        string cleaned = notation.Trim().Replace(" ", "");
        var match = DicePattern.Match(cleaned);

        if (!match.Success)
            throw new ArgumentException($"Invalid dice notation: \"{notation}\"", nameof(notation));

        int count = match.Groups[1].Value.Length > 0
            ? int.Parse(match.Groups[1].Value)
            : 1;

        int sides = int.Parse(match.Groups[2].Value);

        int modifier = match.Groups[3].Value.Length > 0
            ? int.Parse(match.Groups[3].Value)
            : 0;

        if (count <= 0)
            throw new ArgumentException($"Die count must be positive, got {count}.", nameof(notation));
        if (sides <= 0)
            throw new ArgumentException($"Die sides must be positive, got {sides}.", nameof(notation));

        var rolls = RollDice(count, sides);
        return new DiceResult(cleaned, rolls, modifier);
    }

    /// <summary>
    /// Roll a specified number of dice with the given number of sides (no modifier).
    /// </summary>
    public static DiceResult Roll(int count, int sides)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Die count must be positive.");
        if (sides <= 0)
            throw new ArgumentOutOfRangeException(nameof(sides), "Die sides must be positive.");

        var rolls = RollDice(count, sides);
        return new DiceResult($"{count}d{sides}", rolls, 0);
    }

    /// <summary>
    /// Roll 2d20 and take the higher result (advantage).
    /// Both rolls are preserved in IndividualRolls for transparency;
    /// Total reflects only the chosen (higher) die.
    /// </summary>
    public static DiceResult RollWithAdvantage()
    {
        int roll1 = RollSingle(20);
        int roll2 = RollSingle(20);
        int chosen = Math.Max(roll1, roll2);
        return new AdvantageResult("2d20kh1", roll1, roll2, chosen);
    }

    /// <summary>
    /// Roll 2d20 and take the lower result (disadvantage).
    /// Both rolls are preserved in IndividualRolls for transparency;
    /// Total reflects only the chosen (lower) die.
    /// </summary>
    public static DiceResult RollWithDisadvantage()
    {
        int roll1 = RollSingle(20);
        int roll2 = RollSingle(20);
        int chosen = Math.Min(roll1, roll2);

        return new AdvantageResult("2d20kl1", roll1, roll2, chosen);
    }

    /// <summary>
    /// Roll 4d6, drop the lowest die. Standard ability score generation method.
    /// Returns the total (range 3-18).
    /// </summary>
    public static int RollAbilityScore()
    {
        var rolls = RollDice(4, 6);
        return rolls.OrderByDescending(r => r).Take(3).Sum();
    }

    /// <summary>
    /// Roll 1d20 + dexterity modifier for initiative order.
    /// </summary>
    public static int RollInitiative(int dexMod)
    {
        return RollSingle(20) + dexMod;
    }

    /// <summary>
    /// Roll a single die with the given number of sides. Internal convenience method.
    /// </summary>
    internal static int RollSingle(int sides)
    {
        return Rng.Next(1, sides + 1);
    }

    /// <summary>
    /// Roll a d20 respecting advantage/disadvantage rules.
    /// If both advantage and disadvantage apply, they cancel out (straight roll).
    /// Returns the effective natural d20 result.
    /// </summary>
    internal static int RollD20(bool hasAdvantage, bool hasDisadvantage)
    {
        // Per 5e rules: if you have both, they cancel regardless of quantity.
        if (hasAdvantage == hasDisadvantage)
            return RollSingle(20);

        int roll1 = RollSingle(20);
        int roll2 = RollSingle(20);

        return hasAdvantage ? Math.Max(roll1, roll2) : Math.Min(roll1, roll2);
    }

    private static List<int> RollDice(int count, int sides)
    {
        var results = new List<int>(count);
        for (int i = 0; i < count; i++)
            results.Add(Rng.Next(1, sides + 1));
        return results;
    }
}
