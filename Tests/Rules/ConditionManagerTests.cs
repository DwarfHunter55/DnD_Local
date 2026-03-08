using NUnit.Framework;
using ChroniclesUnbound.Core;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Tests.Rules;

/// <summary>
/// Tests for the ConditionManager covering condition application/removal,
/// exhaustion stacking, and all combat query methods.
/// </summary>
[TestFixture]
public class ConditionManagerTests
{
    private ConditionManager _cm;

    [SetUp]
    public void SetUp()
    {
        _cm = new ConditionManager();
    }

    // ── Apply / Remove ──────────────────────────────────────────────

    [Test]
    public void ApplyCondition_AddsCondition()
    {
        _cm.ApplyCondition(Condition.Blinded);
        Assert.That(_cm.HasCondition(Condition.Blinded), Is.True);
    }

    [Test]
    public void RemoveCondition_RemovesCondition()
    {
        _cm.ApplyCondition(Condition.Blinded);
        _cm.RemoveCondition(Condition.Blinded);
        Assert.That(_cm.HasCondition(Condition.Blinded), Is.False);
    }

    [Test]
    public void ApplyCondition_NonExhaustionDoesNotStack()
    {
        _cm.ApplyCondition(Condition.Poisoned);
        _cm.ApplyCondition(Condition.Poisoned);
        _cm.RemoveCondition(Condition.Poisoned);
        Assert.That(_cm.HasCondition(Condition.Poisoned), Is.False);
    }

    [Test]
    public void ClearAll_RemovesEverything()
    {
        _cm.ApplyCondition(Condition.Blinded);
        _cm.ApplyCondition(Condition.Poisoned);
        _cm.ApplyCondition(Condition.Exhaustion, 3);
        _cm.ClearAll();

        Assert.That(_cm.GetActiveConditions(), Is.Empty);
        Assert.That(_cm.GetExhaustionLevel(), Is.EqualTo(0));
    }

    // ── Exhaustion ──────────────────────────────────────────────────

    [Test]
    public void Exhaustion_Stacks()
    {
        _cm.ApplyCondition(Condition.Exhaustion, 1);
        Assert.That(_cm.GetExhaustionLevel(), Is.EqualTo(1));

        _cm.ApplyCondition(Condition.Exhaustion, 2);
        Assert.That(_cm.GetExhaustionLevel(), Is.EqualTo(3));
    }

    [Test]
    public void Exhaustion_CapsAtMax()
    {
        _cm.ApplyCondition(Condition.Exhaustion, 10);
        Assert.That(_cm.GetExhaustionLevel(), Is.EqualTo(ConditionManager.MaxExhaustionLevel));
    }

    [Test]
    public void ReduceExhaustion_DecrementsLevel()
    {
        _cm.ApplyCondition(Condition.Exhaustion, 3);
        _cm.ReduceExhaustion(1);
        Assert.That(_cm.GetExhaustionLevel(), Is.EqualTo(2));
    }

    [Test]
    public void ReduceExhaustion_ToZero_RemovesCondition()
    {
        _cm.ApplyCondition(Condition.Exhaustion, 1);
        _cm.ReduceExhaustion(1);
        Assert.That(_cm.HasCondition(Condition.Exhaustion), Is.False);
        Assert.That(_cm.GetExhaustionLevel(), Is.EqualTo(0));
    }

    // ── Combat Queries ──────────────────────────────────────────────

    [Test]
    public void Blinded_GrantsAdvantageOnAttacksAgainst()
    {
        _cm.ApplyCondition(Condition.Blinded);
        Assert.That(_cm.HasAdvantageOnAttacksAgainst(), Is.True);
    }

    [Test]
    public void Blinded_GrantsDisadvantageOnAttacks()
    {
        _cm.ApplyCondition(Condition.Blinded);
        Assert.That(_cm.HasDisadvantageOnAttacks(), Is.True);
    }

    [Test]
    public void Paralyzed_CannotAct_CannotMove_AutoFailsSaves()
    {
        _cm.ApplyCondition(Condition.Paralyzed);

        Assert.That(_cm.CanTakeActions(), Is.False);
        Assert.That(_cm.CanTakeReactions(), Is.False);
        Assert.That(_cm.CanMove(), Is.False);
        Assert.That(_cm.AutoFailsStrDexSaves(), Is.True);
        Assert.That(_cm.MeleeHitsAutoCrit(), Is.True);
        Assert.That(_cm.HasAdvantageOnAttacksAgainst(), Is.True);
    }

    [Test]
    public void Invisible_IsHidden()
    {
        _cm.ApplyCondition(Condition.Invisible);
        Assert.That(_cm.IsInvisible(), Is.True);
    }

    [Test]
    public void Restrained_HasDisadvantageOnDexSaves()
    {
        _cm.ApplyCondition(Condition.Restrained);
        Assert.That(_cm.HasDisadvantageOnDexSaves(), Is.True);
        Assert.That(_cm.CanMove(), Is.False);
    }

    [Test]
    public void NoConditions_AllQueriesReturnDefaults()
    {
        Assert.That(_cm.HasAdvantageOnAttacksAgainst(), Is.False);
        Assert.That(_cm.HasDisadvantageOnAttacks(), Is.False);
        Assert.That(_cm.HasDisadvantageOnAbilityChecks(), Is.False);
        Assert.That(_cm.HasDisadvantageOnDexSaves(), Is.False);
        Assert.That(_cm.AutoFailsStrDexSaves(), Is.False);
        Assert.That(_cm.CanTakeActions(), Is.True);
        Assert.That(_cm.CanTakeReactions(), Is.True);
        Assert.That(_cm.CanMove(), Is.True);
        Assert.That(_cm.IsIncapacitated(), Is.False);
        Assert.That(_cm.MeleeHitsAutoCrit(), Is.False);
        Assert.That(_cm.IsInvisible(), Is.False);
    }

    // ── Exhaustion-Level Queries ────────────────────────────────────

    [Test]
    public void Exhaustion1_DisadvantageOnAbilityChecks()
    {
        _cm.ApplyCondition(Condition.Exhaustion, 1);
        Assert.That(_cm.HasDisadvantageOnAbilityChecks(), Is.True);
        Assert.That(_cm.HasDisadvantageOnAttacks(), Is.False);
    }

    [Test]
    public void Exhaustion2_SpeedHalved()
    {
        _cm.ApplyCondition(Condition.Exhaustion, 2);
        Assert.That(_cm.GetSpeedMultiplier(), Is.EqualTo(0.5f));
    }

    [Test]
    public void Exhaustion3_DisadvantageOnAttacksAndSaves()
    {
        _cm.ApplyCondition(Condition.Exhaustion, 3);
        Assert.That(_cm.HasDisadvantageOnAttacks(), Is.True);
        Assert.That(_cm.HasDisadvantageOnSavingThrows(), Is.True);
    }

    [Test]
    public void Exhaustion4_HalfMaxHP()
    {
        _cm.ApplyCondition(Condition.Exhaustion, 4);
        Assert.That(_cm.GetMaxHPMultiplierPercent(), Is.EqualTo(50));
    }

    [Test]
    public void Exhaustion5_CannotMove()
    {
        _cm.ApplyCondition(Condition.Exhaustion, 5);
        Assert.That(_cm.CanMove(), Is.False);
    }

    [Test]
    public void Exhaustion6_Dead()
    {
        _cm.ApplyCondition(Condition.Exhaustion, 6);
        Assert.That(_cm.IsDeadFromExhaustion(), Is.True);
    }

    // ── GetActiveConditions ─────────────────────────────────────────

    [Test]
    public void GetActiveConditions_ReturnsAll()
    {
        _cm.ApplyCondition(Condition.Blinded);
        _cm.ApplyCondition(Condition.Poisoned);

        var active = _cm.GetActiveConditions();
        Assert.That(active, Has.Count.EqualTo(2));
        Assert.That(active, Does.Contain(Condition.Blinded));
        Assert.That(active, Does.Contain(Condition.Poisoned));
    }

    // ── Static GetConditionEffect ───────────────────────────────────

    [Test]
    public void GetConditionEffect_ReturnsDefinition()
    {
        var effect = ConditionManager.GetConditionEffect(Condition.Stunned);

        Assert.That(effect, Is.Not.Null);
        Assert.That(effect.Condition, Is.EqualTo(Condition.Stunned));
        Assert.That(effect.CannotTakeActions, Is.True);
        Assert.That(effect.AutoFailStrDexSaves, Is.True);
    }
}
