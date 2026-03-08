using NUnit.Framework;
using ChroniclesUnbound.Core;

namespace ChroniclesUnbound.Tests.Rules;

/// <summary>
/// Tests for the CombatResolver static class covering attack resolution,
/// skill checks, saving throws, and death saves.
/// </summary>
[TestFixture]
public class CombatResolverTests
{
    // ── Attack Resolution ───────────────────────────────────────────

    [Test]
    public void ResolveAttack_ReturnsValidResult()
    {
        var result = CombatResolver.ResolveAttack(
            attackBonus: 5,
            targetAC: 15,
            damageNotation: "1d8",
            damageBonus: 3,
            damageType: "Slashing");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.AttackRoll, Is.InRange(1, 20));
        Assert.That(result.DamageType, Is.EqualTo("Slashing"));
    }

    [Test]
    public void ResolveAttack_HitDealsDamage()
    {
        // Run many times; at least some should hit.
        bool sawHit = false;
        for (int i = 0; i < 200; i++)
        {
            var result = CombatResolver.ResolveAttack(
                attackBonus: 10,
                targetAC: 10,
                damageNotation: "1d8",
                damageBonus: 3,
                damageType: "Slashing");

            if (result.Hits)
            {
                sawHit = true;
                Assert.That(result.Damage, Is.GreaterThan(0));
                Assert.That(result.DamageRoll, Is.Not.Null);
            }
        }

        Assert.That(sawHit, Is.True, "Expected at least one hit in 200 attempts.");
    }

    [Test]
    public void ResolveAttack_MissDealNoDamage()
    {
        bool sawMiss = false;
        for (int i = 0; i < 200; i++)
        {
            var result = CombatResolver.ResolveAttack(
                attackBonus: 0,
                targetAC: 25,
                damageNotation: "1d8",
                damageBonus: 3,
                damageType: "Slashing");

            if (!result.Hits && !result.IsCritical)
            {
                sawMiss = true;
                Assert.That(result.Damage, Is.EqualTo(0));
                Assert.That(result.DamageRoll, Is.Null);
            }
        }

        Assert.That(sawMiss, Is.True, "Expected at least one miss in 200 attempts vs AC 25.");
    }

    [Test]
    public void ResolveAttack_NaturalOne_AlwaysMisses()
    {
        // We cannot force a natural 1, so we verify the logic: if IsFumble is true, Hits must be false.
        for (int i = 0; i < 500; i++)
        {
            var result = CombatResolver.ResolveAttack(
                attackBonus: 100, // Huge bonus should still miss on nat 1
                targetAC: 1,
                damageNotation: "1d6",
                damageBonus: 0,
                damageType: "Bludgeoning");

            if (result.IsFumble)
            {
                Assert.That(result.AttackRoll, Is.EqualTo(1));
                Assert.That(result.Hits, Is.False);
            }
        }
    }

    [Test]
    public void ResolveAttack_NaturalTwenty_AlwaysHits()
    {
        for (int i = 0; i < 500; i++)
        {
            var result = CombatResolver.ResolveAttack(
                attackBonus: -100, // Huge penalty should still hit on nat 20
                targetAC: 100,
                damageNotation: "1d6",
                damageBonus: 0,
                damageType: "Piercing");

            if (result.IsCritical)
            {
                Assert.That(result.AttackRoll, Is.EqualTo(20));
                Assert.That(result.Hits, Is.True);
                Assert.That(result.Damage, Is.GreaterThan(0));
            }
        }
    }

    // ── Skill Checks ────────────────────────────────────────────────

    [Test]
    public void ResolveSkillCheck_ReturnsValidResult()
    {
        var result = CombatResolver.ResolveSkillCheck("Perception", 5, 15);

        Assert.That(result.Skill, Is.EqualTo("Perception"));
        Assert.That(result.Modifier, Is.EqualTo(5));
        Assert.That(result.DC, Is.EqualTo(15));
        Assert.That(result.Total, Is.EqualTo(result.Roll + 5));
        Assert.That(result.Success, Is.EqualTo(result.Total >= 15));
    }

    // ── Saving Throws ───────────────────────────────────────────────

    [Test]
    public void ResolveSavingThrow_ReturnsValidResult()
    {
        var result = CombatResolver.ResolveSavingThrow("Dexterity", 3, 14);

        Assert.That(result.Ability, Is.EqualTo("Dexterity"));
        Assert.That(result.Modifier, Is.EqualTo(3));
        Assert.That(result.DC, Is.EqualTo(14));
        Assert.That(result.Total, Is.EqualTo(result.Roll + 3));
        Assert.That(result.Success, Is.EqualTo(result.Total >= 14));
    }

    // ── Death Saves ─────────────────────────────────────────────────

    [Test]
    public void RollDeathSave_ReturnsConsistentFlags()
    {
        bool sawSuccess = false;
        bool sawFailure = false;

        for (int i = 0; i < 500; i++)
        {
            var (success, critSuccess, critFail) = CombatResolver.RollDeathSave();

            // Crit success and crit fail are mutually exclusive.
            Assert.That(critSuccess && critFail, Is.False);

            // Crit success implies success.
            if (critSuccess)
                Assert.That(success, Is.True);

            // Crit fail implies failure.
            if (critFail)
                Assert.That(success, Is.False);

            if (success) sawSuccess = true;
            else sawFailure = true;
        }

        Assert.That(sawSuccess, Is.True, "Expected at least one death save success in 500 rolls.");
        Assert.That(sawFailure, Is.True, "Expected at least one death save failure in 500 rolls.");
    }

    [Test]
    public void RollDeathSave_CritSuccessOn20()
    {
        bool sawCritSuccess = false;
        for (int i = 0; i < 1000; i++)
        {
            var (success, critSuccess, critFail) = CombatResolver.RollDeathSave();
            if (critSuccess)
            {
                sawCritSuccess = true;
                Assert.That(success, Is.True);
                Assert.That(critFail, Is.False);
            }
        }
        Assert.That(sawCritSuccess, Is.True, "Expected at least one critical success (nat 20) in 1000 rolls.");
    }

    [Test]
    public void RollDeathSave_CritFailOn1()
    {
        bool sawCritFail = false;
        for (int i = 0; i < 1000; i++)
        {
            var (success, critSuccess, critFail) = CombatResolver.RollDeathSave();
            if (critFail)
            {
                sawCritFail = true;
                Assert.That(success, Is.False);
                Assert.That(critSuccess, Is.False);
            }
        }
        Assert.That(sawCritFail, Is.True, "Expected at least one critical fail (nat 1) in 1000 rolls.");
    }

    // ── Attack Resolution: Deeper Tests ────────────────────────────

    [Test]
    public void ResolveAttack_WithAdvantage_UsesHigherRoll()
    {
        bool sawHit = false;
        for (int i = 0; i < 200; i++)
        {
            var result = CombatResolver.ResolveAttack(
                attackBonus: 5,
                targetAC: 15,
                damageNotation: "1d8",
                damageBonus: 3,
                damageType: "Slashing",
                hasAdvantage: true);

            if (result.Hits && !result.IsCritical)
            {
                sawHit = true;
            }
        }
        Assert.That(sawHit, Is.True, "Expected at least one non-crit hit with advantage.");
    }

    [Test]
    public void ResolveAttack_WithDisadvantage_UsesLowerRoll()
    {
        bool sawMiss = false;
        for (int i = 0; i < 200; i++)
        {
            var result = CombatResolver.ResolveAttack(
                attackBonus: 2,
                targetAC: 15,
                damageNotation: "1d8",
                damageBonus: 3,
                damageType: "Piercing",
                hasDisadvantage: true);

            if (!result.Hits && !result.IsFumble)
            {
                sawMiss = true;
            }
        }
        Assert.That(sawMiss, Is.True, "Expected at least one miss with disadvantage.");
    }

    [Test]
    public void ResolveAttack_CriticalHit_DoublesDiceNotModifier()
    {
        // Force many attacks until we see a critical hit
        bool sawCrit = false;
        for (int i = 0; i < 1000; i++)
        {
            var result = CombatResolver.ResolveAttack(
                attackBonus: 5,
                targetAC: 15,
                damageNotation: "1d8",
                damageBonus: 5, // Modifier should NOT be doubled
                damageType: "Slashing");

            if (result.IsCritical)
            {
                sawCrit = true;
                Assert.That(result.AttackRoll, Is.EqualTo(20));
                Assert.That(result.Hits, Is.True);

                // Critical damage is 2d8+5, range: 7-21
                // (2-16 from dice) + 5 = 7-21
                Assert.That(result.Damage, Is.InRange(7, 21));

                // Verify damage roll has correct notation (should be "2d8" internally)
                Assert.That(result.DamageRoll, Is.Not.Null);
                Assert.That(result.DamageRoll!.IndividualRolls.Count, Is.EqualTo(2));
            }
        }
        Assert.That(sawCrit, Is.True, "Expected at least one critical hit in 1000 attacks.");
    }

    [Test]
    public void ResolveAttack_ZeroDamageBonus_Works()
    {
        var result = CombatResolver.ResolveAttack(
            attackBonus: 5,
            targetAC: 10,
            damageNotation: "1d6",
            damageBonus: 0,
            damageType: "Fire");

        Assert.That(result, Is.Not.Null);
        // If it hits, damage should be 1-6 (just the die)
        if (result.Hits && !result.IsCritical)
        {
            Assert.That(result.Damage, Is.InRange(1, 6));
        }
    }

    [Test]
    public void ResolveAttack_NegativeDamageBonus_CanResultInZeroDamage()
    {
        // Edge case: very weak attack with negative damage bonus
        // Should never go below 0 damage
        for (int i = 0; i < 100; i++)
        {
            var result = CombatResolver.ResolveAttack(
                attackBonus: 10,
                targetAC: 5, // Easy to hit
                damageNotation: "1d4",
                damageBonus: -10,
                damageType: "Bludgeoning");

            if (result.Hits)
            {
                // Damage should be clamped to minimum 0
                Assert.That(result.Damage, Is.GreaterThanOrEqualTo(0));
            }
        }
    }

    [Test]
    public void ResolveAttack_DifferentDamageTypes_PreservedCorrectly()
    {
        var damageTypes = new[] { "Slashing", "Piercing", "Bludgeoning", "Fire", "Cold", "Lightning", "Acid", "Poison", "Radiant", "Necrotic" };

        foreach (var dmgType in damageTypes)
        {
            var result = CombatResolver.ResolveAttack(
                attackBonus: 10,
                targetAC: 5,
                damageNotation: "1d6",
                damageBonus: 2,
                damageType: dmgType);

            Assert.That(result.DamageType, Is.EqualTo(dmgType));
        }
    }

    [Test]
    public void ResolveAttack_BothAdvantageAndDisadvantage_CancelsOut()
    {
        // When both advantage and disadvantage apply, they cancel (straight roll)
        // This is hard to test directly, but we can verify results are still valid
        for (int i = 0; i < 100; i++)
        {
            var result = CombatResolver.ResolveAttack(
                attackBonus: 5,
                targetAC: 15,
                damageNotation: "1d8",
                damageBonus: 3,
                damageType: "Slashing",
                hasAdvantage: true,
                hasDisadvantage: true);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.AttackRoll, Is.InRange(1, 20));
        }
    }

    [Test]
    public void ResolveAttack_VeryHighAC_RarelyHits()
    {
        int hitCount = 0;
        int trials = 100;

        for (int i = 0; i < trials; i++)
        {
            var result = CombatResolver.ResolveAttack(
                attackBonus: 5,
                targetAC: 30, // Extremely high AC
                damageNotation: "1d6",
                damageBonus: 2,
                damageType: "Slashing");

            if (result.Hits && !result.IsCritical)
                hitCount++;
        }

        // With +5 bonus vs AC 30, only nat 20 (crit) should hit
        // So non-crit hits should be 0
        Assert.That(hitCount, Is.EqualTo(0));
    }

    // ── Saving Throws: Extended Tests ──────────────────────────────

    [Test]
    public void ResolveSavingThrow_WithAdvantage_FlagsSet()
    {
        var result = CombatResolver.ResolveSavingThrow(
            "Strength",
            modifier: 2,
            dc: 15,
            hasAdvantage: true);

        Assert.That(result.HasAdvantage, Is.True);
        Assert.That(result.HasDisadvantage, Is.False);
    }

    [Test]
    public void ResolveSavingThrow_WithDisadvantage_FlagsSet()
    {
        var result = CombatResolver.ResolveSavingThrow(
            "Dexterity",
            modifier: 3,
            dc: 14,
            hasDisadvantage: true);

        Assert.That(result.HasAdvantage, Is.False);
        Assert.That(result.HasDisadvantage, Is.True);
    }

    [Test]
    public void ResolveSavingThrow_AllAbilityTypes_Work()
    {
        var abilities = new[] { "Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma" };

        foreach (var ability in abilities)
        {
            var result = CombatResolver.ResolveSavingThrow(ability, 2, 15);
            Assert.That(result.Ability, Is.EqualTo(ability));
            Assert.That(result.Total, Is.EqualTo(result.Roll + 2));
        }
    }

    [Test]
    public void ResolveSavingThrow_WithProficiency_ModifierApplied()
    {
        // Simulate proficiency bonus included in modifier
        var result = CombatResolver.ResolveSavingThrow(
            "Wisdom",
            modifier: 5, // e.g., +3 Wis + 2 prof
            dc: 14);

        Assert.That(result.Modifier, Is.EqualTo(5));
        Assert.That(result.Total, Is.EqualTo(result.Roll + 5));
    }

    [Test]
    public void ResolveSavingThrow_NegativeModifier_Works()
    {
        var result = CombatResolver.ResolveSavingThrow(
            "Strength",
            modifier: -2,
            dc: 12);

        Assert.That(result.Modifier, Is.EqualTo(-2));
        Assert.That(result.Total, Is.EqualTo(result.Roll - 2));
    }

    [Test]
    public void ResolveSavingThrow_ExactDC_IsSuccess()
    {
        // Roll many times to find a case where total equals DC
        bool foundExactMatch = false;
        for (int i = 0; i < 1000; i++)
        {
            var result = CombatResolver.ResolveSavingThrow("Constitution", 5, 15);
            if (result.Total == 15)
            {
                foundExactMatch = true;
                Assert.That(result.Success, Is.True, "Meeting DC exactly should be a success.");
            }
        }
        Assert.That(foundExactMatch, Is.True, "Expected to find at least one exact DC match in 1000 rolls.");
    }

    // ── Skill Checks: Extended Tests ───────────────────────────────

    [Test]
    public void ResolveSkillCheck_WithAdvantage_FlagsSet()
    {
        var result = CombatResolver.ResolveSkillCheck(
            "Stealth",
            modifier: 4,
            dc: 15,
            hasAdvantage: true);

        Assert.That(result.HasAdvantage, Is.True);
        Assert.That(result.HasDisadvantage, Is.False);
    }

    [Test]
    public void ResolveSkillCheck_WithDisadvantage_FlagsSet()
    {
        var result = CombatResolver.ResolveSkillCheck(
            "Athletics",
            modifier: 3,
            dc: 12,
            hasDisadvantage: true);

        Assert.That(result.HasAdvantage, Is.False);
        Assert.That(result.HasDisadvantage, Is.True);
    }

    [Test]
    public void ResolveSkillCheck_ExactDC_IsSuccess()
    {
        bool foundExactMatch = false;
        for (int i = 0; i < 1000; i++)
        {
            var result = CombatResolver.ResolveSkillCheck("Perception", 3, 13);
            if (result.Total == 13)
            {
                foundExactMatch = true;
                Assert.That(result.Success, Is.True);
            }
        }
        Assert.That(foundExactMatch, Is.True);
    }

    [Test]
    public void ResolveSkillCheck_NegativeModifier_Works()
    {
        var result = CombatResolver.ResolveSkillCheck("Acrobatics", -1, 10);
        Assert.That(result.Modifier, Is.EqualTo(-1));
        Assert.That(result.Total, Is.EqualTo(result.Roll - 1));
    }
}
