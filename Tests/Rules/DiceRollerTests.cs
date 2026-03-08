using NUnit.Framework;
using ChroniclesUnbound.Core;

namespace ChroniclesUnbound.Tests.Rules;

/// <summary>
/// Tests for the DiceRoller static class covering notation parsing,
/// roll ranges, advantage/disadvantage, and ability score generation.
/// </summary>
[TestFixture]
public class DiceRollerTests
{
    // ── Notation Parsing ────────────────────────────────────────────

    [Test]
    public void Roll_StandardNotation_ReturnsValidResult()
    {
        var result = DiceRoller.Roll("2d6+3");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Notation, Is.EqualTo("2d6+3"));
        Assert.That(result.IndividualRolls.Count, Is.EqualTo(2));
        Assert.That(result.Modifier, Is.EqualTo(3));
        // 2d6+3 range: min 5 (1+1+3), max 15 (6+6+3)
        Assert.That(result.Total, Is.InRange(5, 15));
    }

    [Test]
    public void Roll_SingleDieNoModifier_Works()
    {
        var result = DiceRoller.Roll("d20");

        Assert.That(result.IndividualRolls.Count, Is.EqualTo(1));
        Assert.That(result.Modifier, Is.EqualTo(0));
        Assert.That(result.Total, Is.InRange(1, 20));
    }

    [Test]
    public void Roll_NegativeModifier_SubtractsCorrectly()
    {
        var result = DiceRoller.Roll("1d8-1");

        Assert.That(result.Modifier, Is.EqualTo(-1));
        // 1d8-1 range: min 0 (1-1), max 7 (8-1)
        Assert.That(result.Total, Is.InRange(0, 7));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("not_dice")]
    [TestCase("2x6")]
    public void Roll_InvalidNotation_ThrowsArgumentException(string notation)
    {
        Assert.Throws<ArgumentException>(() => DiceRoller.Roll(notation));
    }

    [Test]
    public void Roll_ZeroDice_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => DiceRoller.Roll("0d6"));
    }

    // ── Numeric Overload ────────────────────────────────────────────

    [Test]
    public void Roll_CountAndSides_ReturnsCorrectDieCount()
    {
        var result = DiceRoller.Roll(4, 6);

        Assert.That(result.IndividualRolls.Count, Is.EqualTo(4));
        Assert.That(result.Modifier, Is.EqualTo(0));
        // 4d6 range: 4-24
        Assert.That(result.Total, Is.InRange(4, 24));
    }

    [Test]
    public void Roll_InvalidCount_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiceRoller.Roll(0, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiceRoller.Roll(-1, 6));
    }

    [Test]
    public void Roll_InvalidSides_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiceRoller.Roll(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiceRoller.Roll(1, -1));
    }

    // ── Advantage / Disadvantage ────────────────────────────────────

    [Test]
    public void RollWithAdvantage_ReturnsTwoDice_TakesHigher()
    {
        // Run multiple times to verify the constraint statistically.
        for (int i = 0; i < 100; i++)
        {
            var result = DiceRoller.RollWithAdvantage();

            Assert.That(result.IndividualRolls.Count, Is.EqualTo(2));
            int higher = Math.Max(result.IndividualRolls[0], result.IndividualRolls[1]);
            Assert.That(result.Total, Is.EqualTo(higher));
        }
    }

    [Test]
    public void RollWithDisadvantage_ReturnsTwoDice_TakesLower()
    {
        for (int i = 0; i < 100; i++)
        {
            var result = DiceRoller.RollWithDisadvantage();

            Assert.That(result.IndividualRolls.Count, Is.EqualTo(2));
            int lower = Math.Min(result.IndividualRolls[0], result.IndividualRolls[1]);
            Assert.That(result.Total, Is.EqualTo(lower));
        }
    }

    // ── Ability Score Generation ────────────────────────────────────

    [Test]
    public void RollAbilityScore_ReturnsInValidRange()
    {
        // 4d6 drop lowest: min 3 (all 1s), max 18 (all 6s)
        for (int i = 0; i < 100; i++)
        {
            int score = DiceRoller.RollAbilityScore();
            Assert.That(score, Is.InRange(3, 18));
        }
    }

    // ── Initiative ──────────────────────────────────────────────────

    [Test]
    public void RollInitiative_AddsDexModifier()
    {
        for (int i = 0; i < 50; i++)
        {
            int result = DiceRoller.RollInitiative(3);
            // 1d20+3 range: 4-23
            Assert.That(result, Is.InRange(4, 23));
        }
    }

    [Test]
    public void RollInitiative_NegativeModifier_CanGoBelow1()
    {
        for (int i = 0; i < 50; i++)
        {
            int result = DiceRoller.RollInitiative(-2);
            // 1d20-2 range: -1 to 18
            Assert.That(result, Is.InRange(-1, 18));
        }
    }

    // ── DiceResult.ToString ─────────────────────────────────────────

    [Test]
    public void DiceResult_ToString_FormatsCorrectly()
    {
        var result = new DiceResult("2d6+3", new[] { 4, 5 }, 3);
        Assert.That(result.ToString(), Is.EqualTo("[4, 5] + 3 = 12"));
    }

    [Test]
    public void DiceResult_ToString_NegativeModifier_FormatsCorrectly()
    {
        var result = new DiceResult("1d8-2", new[] { 6 }, -2);
        Assert.That(result.ToString(), Is.EqualTo("[6] - 2 = 4"));
    }

    [Test]
    public void DiceResult_ToString_NoModifier_FormatsCorrectly()
    {
        var result = new DiceResult("1d20", new[] { 15 }, 0);
        Assert.That(result.ToString(), Is.EqualTo("[15] = 15"));
    }

    // ── Edge Cases ──────────────────────────────────────────────────

    [Test]
    public void Roll_VeryLargeRoll_Works()
    {
        var result = DiceRoller.Roll("10d10+50");

        Assert.That(result.IndividualRolls.Count, Is.EqualTo(10));
        Assert.That(result.Modifier, Is.EqualTo(50));
        // 10d10+50 range: min 60 (10*1+50), max 150 (10*10+50)
        Assert.That(result.Total, Is.InRange(60, 150));
    }

    [Test]
    public void Roll_SingleSidedDie_AlwaysRollsOne()
    {
        for (int i = 0; i < 20; i++)
        {
            var result = DiceRoller.Roll("1d1");
            Assert.That(result.Total, Is.EqualTo(1));
            Assert.That(result.IndividualRolls[0], Is.EqualTo(1));
        }
    }

    [Test]
    public void Roll_VeryLargeNegativeModifier_CanResultInNegativeTotal()
    {
        var result = DiceRoller.Roll("1d4-10");

        Assert.That(result.Modifier, Is.EqualTo(-10));
        // 1d4-10 range: -9 to -6
        Assert.That(result.Total, Is.InRange(-9, -6));
    }

    [Test]
    public void Roll_WithWhitespace_ParsesCorrectly()
    {
        var result = DiceRoller.Roll("  2d6 + 3  ");

        Assert.That(result.IndividualRolls.Count, Is.EqualTo(2));
        Assert.That(result.Modifier, Is.EqualTo(3));
        Assert.That(result.Total, Is.InRange(5, 15));
    }

    [Test]
    public void Roll_LargePositiveModifier_AddedCorrectly()
    {
        var result = DiceRoller.Roll("1d6+100");

        Assert.That(result.Modifier, Is.EqualTo(100));
        // 1d6+100 range: 101-106
        Assert.That(result.Total, Is.InRange(101, 106));
    }

    [Test]
    public void Roll_ManyDice_AllIndividualRollsPreserved()
    {
        var result = DiceRoller.Roll("20d6");

        Assert.That(result.IndividualRolls.Count, Is.EqualTo(20));
        foreach (var roll in result.IndividualRolls)
        {
            Assert.That(roll, Is.InRange(1, 6));
        }
        Assert.That(result.Total, Is.EqualTo(result.IndividualRolls.Sum()));
    }

    [Test]
    public void RollAbilityScore_AverageIsReasonable()
    {
        // 4d6 drop lowest should average around 12-13
        int total = 0;
        int iterations = 1000;
        for (int i = 0; i < iterations; i++)
        {
            total += DiceRoller.RollAbilityScore();
        }
        double average = (double)total / iterations;

        // Statistical check: average should be between 11 and 14
        Assert.That(average, Is.InRange(11.0, 14.0));
    }

    [Test]
    public void RollInitiative_VeryHighModifier_Works()
    {
        int result = DiceRoller.RollInitiative(10);
        // 1d20+10 range: 11-30
        Assert.That(result, Is.InRange(11, 30));
    }

    [Test]
    public void RollInitiative_VeryLowModifier_CanBeNegative()
    {
        int result = DiceRoller.RollInitiative(-10);
        // 1d20-10 range: -9 to 10
        Assert.That(result, Is.InRange(-9, 10));
    }
}
