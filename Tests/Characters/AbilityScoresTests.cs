using NUnit.Framework;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Tests.Characters;

/// <summary>
/// Tests for AbilityScores calculation and edge cases.
/// </summary>
[TestFixture]
public class AbilityScoresTests
{
    // ── Modifier Calculation Edge Cases ─────────────────────────────────

    [TestCase(1, -5, Description = "Minimum score")]
    [TestCase(2, -4)]
    [TestCase(3, -4)]
    [TestCase(4, -3)]
    [TestCase(5, -3)]
    [TestCase(6, -2)]
    [TestCase(7, -2)]
    [TestCase(8, -1)]
    [TestCase(9, -1)]
    [TestCase(10, 0, Description = "Average score lower bound")]
    [TestCase(11, 0, Description = "Average score upper bound")]
    [TestCase(12, 1)]
    [TestCase(13, 1)]
    [TestCase(14, 2)]
    [TestCase(15, 2)]
    [TestCase(16, 3)]
    [TestCase(17, 3)]
    [TestCase(18, 4)]
    [TestCase(19, 4)]
    [TestCase(20, 5, Description = "Max standard score")]
    [TestCase(22, 6, Description = "Above cap (Barbarian Capstone)")]
    [TestCase(24, 7, Description = "Above cap (Barbarian + belt)")]
    [TestCase(30, 10, Description = "Extreme score")]
    public void GetModifier_AllScores_CalculatesCorrectly(int score, int expectedModifier)
    {
        var scores = new AbilityScores { Strength = score };
        Assert.That(scores.GetModifier(Ability.Strength), Is.EqualTo(expectedModifier));
    }

    // ── All Six Abilities ──────────────────────────────────────────────

    [Test]
    public void GetModifier_AllAbilities_ReturnsCorrectModifiers()
    {
        var scores = new AbilityScores
        {
            Strength = 8,      // -1
            Dexterity = 14,    // +2
            Constitution = 12, // +1
            Intelligence = 16, // +3
            Wisdom = 10,       // 0
            Charisma = 20      // +5
        };

        Assert.That(scores.GetModifier(Ability.Strength), Is.EqualTo(-1));
        Assert.That(scores.GetModifier(Ability.Dexterity), Is.EqualTo(2));
        Assert.That(scores.GetModifier(Ability.Constitution), Is.EqualTo(1));
        Assert.That(scores.GetModifier(Ability.Intelligence), Is.EqualTo(3));
        Assert.That(scores.GetModifier(Ability.Wisdom), Is.EqualTo(0));
        Assert.That(scores.GetModifier(Ability.Charisma), Is.EqualTo(5));
    }

    // ── GetScore / SetScore ────────────────────────────────────────────

    [Test]
    public void GetScore_AllAbilities_RoundTrips()
    {
        var scores = new AbilityScores();

        scores.SetScore(Ability.Strength, 15);
        scores.SetScore(Ability.Dexterity, 14);
        scores.SetScore(Ability.Constitution, 13);
        scores.SetScore(Ability.Intelligence, 12);
        scores.SetScore(Ability.Wisdom, 10);
        scores.SetScore(Ability.Charisma, 8);

        Assert.That(scores.GetScore(Ability.Strength), Is.EqualTo(15));
        Assert.That(scores.GetScore(Ability.Dexterity), Is.EqualTo(14));
        Assert.That(scores.GetScore(Ability.Constitution), Is.EqualTo(13));
        Assert.That(scores.GetScore(Ability.Intelligence), Is.EqualTo(12));
        Assert.That(scores.GetScore(Ability.Wisdom), Is.EqualTo(10));
        Assert.That(scores.GetScore(Ability.Charisma), Is.EqualTo(8));
    }

    [Test]
    public void SetScore_UpdatesModifier()
    {
        var scores = new AbilityScores { Strength = 10 };
        Assert.That(scores.GetModifier(Ability.Strength), Is.EqualTo(0));

        scores.SetScore(Ability.Strength, 18);
        Assert.That(scores.GetModifier(Ability.Strength), Is.EqualTo(4));
    }

    // ── Default Values ─────────────────────────────────────────────────

    [Test]
    public void Constructor_DefaultsToTen()
    {
        var scores = new AbilityScores();

        Assert.That(scores.Strength, Is.EqualTo(10));
        Assert.That(scores.Dexterity, Is.EqualTo(10));
        Assert.That(scores.Constitution, Is.EqualTo(10));
        Assert.That(scores.Intelligence, Is.EqualTo(10));
        Assert.That(scores.Wisdom, Is.EqualTo(10));
        Assert.That(scores.Charisma, Is.EqualTo(10));

        Assert.That(scores.GetModifier(Ability.Strength), Is.EqualTo(0));
    }
}
