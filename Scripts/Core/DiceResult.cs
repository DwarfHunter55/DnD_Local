using System.Collections.Generic;
using System.Linq;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Immutable result of a dice roll, preserving individual die values for transparency.
/// </summary>
public class DiceResult
{
    /// <summary>Standard dice notation that produced this result (e.g. "2d6+3").</summary>
    public string Notation { get; }

    /// <summary>Each individual die result before any modifier.</summary>
    public IReadOnlyList<int> IndividualRolls { get; }

    /// <summary>Flat modifier added to (or subtracted from) the roll total.</summary>
    public int Modifier { get; }

    /// <summary>Final total: sum of all individual rolls plus the modifier.</summary>
    public virtual int Total { get; }

    public DiceResult(string notation, IEnumerable<int> rolls, int modifier)
    {
        Notation = notation;
        IndividualRolls = rolls.ToList().AsReadOnly();
        Modifier = modifier;
        Total = IndividualRolls.Sum() + modifier;
    }

    /// <summary>
    /// Human-readable breakdown, e.g. "[3, 5] + 3 = 11" or "[14] = 14".
    /// </summary>
    public override string ToString()
    {
        string rollsPart = $"[{string.Join(", ", IndividualRolls)}]";

        if (Modifier > 0)
            return $"{rollsPart} + {Modifier} = {Total}";
        if (Modifier < 0)
            return $"{rollsPart} - {-Modifier} = {Total}";

        return $"{rollsPart} = {Total}";
    }
}

/// <summary>
/// Dice result for advantage/disadvantage rolls where two d20s are rolled
/// but only one is used. Both dice are visible in IndividualRolls,
/// but Total reflects only the chosen die.
/// </summary>
public sealed class AdvantageResult : DiceResult
{
    /// <summary>The effective die value used (higher for advantage, lower for disadvantage).</summary>
    public int ChosenRoll { get; }

    public override int Total => ChosenRoll;

    public AdvantageResult(string notation, int roll1, int roll2, int chosenRoll)
        : base(notation, new[] { roll1, roll2 }, 0)
    {
        ChosenRoll = chosenRoll;
    }

    public override string ToString()
    {
        return $"[{string.Join(", ", IndividualRolls)}] keep {ChosenRoll} = {Total}";
    }
}
