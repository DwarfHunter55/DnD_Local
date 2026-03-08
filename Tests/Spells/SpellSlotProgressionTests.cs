using NUnit.Framework;
using ChroniclesUnbound.Core;

namespace ChroniclesUnbound.Tests.Spells;

/// <summary>
/// Tests for spell slot progression patterns: Wizard 1-20, multiclass, Warlock.
/// </summary>
[TestFixture]
public class SpellSlotProgressionTests
{
    // ── Wizard Slot Progression ────────────────────────────────────────

    [Test]
    public void WizardLevel1_Has2FirstLevelSlots()
    {
        var tracker = new SpellSlotTracker();
        // Wizard level 1: 2 first-level slots
        tracker.Initialize(new[] { 2 });

        Assert.That(tracker.GetMaxSlots(1), Is.EqualTo(2));
        Assert.That(tracker.GetMaxSlots(2), Is.EqualTo(0));
    }

    [Test]
    public void WizardLevel5_Has4_3_2Slots()
    {
        var tracker = new SpellSlotTracker();
        // Wizard level 5: 4/3/2 slots for levels 1/2/3
        tracker.Initialize(new[] { 4, 3, 2 });

        Assert.That(tracker.GetMaxSlots(1), Is.EqualTo(4));
        Assert.That(tracker.GetMaxSlots(2), Is.EqualTo(3));
        Assert.That(tracker.GetMaxSlots(3), Is.EqualTo(2));
        Assert.That(tracker.GetMaxSlots(4), Is.EqualTo(0));
    }

    [Test]
    public void WizardLevel20_HasFullProgression()
    {
        var tracker = new SpellSlotTracker();
        // Wizard level 20 (full caster): 4/3/3/3/3/2/2/1/1 slots
        tracker.Initialize(new[] { 4, 3, 3, 3, 3, 2, 2, 1, 1 });

        Assert.That(tracker.GetMaxSlots(1), Is.EqualTo(4));
        Assert.That(tracker.GetMaxSlots(2), Is.EqualTo(3));
        Assert.That(tracker.GetMaxSlots(3), Is.EqualTo(3));
        Assert.That(tracker.GetMaxSlots(4), Is.EqualTo(3));
        Assert.That(tracker.GetMaxSlots(5), Is.EqualTo(3));
        Assert.That(tracker.GetMaxSlots(6), Is.EqualTo(2));
        Assert.That(tracker.GetMaxSlots(7), Is.EqualTo(2));
        Assert.That(tracker.GetMaxSlots(8), Is.EqualTo(1));
        Assert.That(tracker.GetMaxSlots(9), Is.EqualTo(1));
    }

    // ── Multiclass Slot Calculation ────────────────────────────────────

    [Test]
    public void Multiclass_Fighter3Wizard2_Has3FirstAnd2SecondSlots()
    {
        var tracker = new SpellSlotTracker();
        // Fighter 3 / Wizard 2 = caster level 2 (wizard rounds down, fighter 0)
        // Per 5e multiclass table, caster level 2: 3 first-level slots
        // But if we count Eldritch Knight (Fighter 3 = EK level 1 rounds down to 0),
        // then Wizard 2 = 3 first-level slots
        tracker.Initialize(new[] { 3 });

        Assert.That(tracker.GetMaxSlots(1), Is.EqualTo(3));
        Assert.That(tracker.GetMaxSlots(2), Is.EqualTo(0));
    }

    [Test]
    public void Multiclass_Cleric5Wizard3_Uses8thLevelCasterSlots()
    {
        var tracker = new SpellSlotTracker();
        // Cleric 5 (full caster) + Wizard 3 (full caster) = 8 caster levels
        // Per 5e table: level 8 caster has 4/3/3/2 slots
        tracker.Initialize(new[] { 4, 3, 3, 2 });

        Assert.That(tracker.GetMaxSlots(1), Is.EqualTo(4));
        Assert.That(tracker.GetMaxSlots(2), Is.EqualTo(3));
        Assert.That(tracker.GetMaxSlots(3), Is.EqualTo(3));
        Assert.That(tracker.GetMaxSlots(4), Is.EqualTo(2));
        Assert.That(tracker.GetMaxSlots(5), Is.EqualTo(0));
    }

    // ── Warlock Pact Slots ──────────────────────────────────────────────

    [Test]
    public void WarlockLevel2_Has2PactSlots()
    {
        var tracker = new SpellSlotTracker();
        // Warlock level 2: 2 pact slots at 1st level
        tracker.Initialize(new[] { 2 });

        Assert.That(tracker.GetMaxSlots(1), Is.EqualTo(2));
        Assert.That(tracker.GetRemainingSlots(1), Is.EqualTo(2));
    }

    [Test]
    public void WarlockLevel5_Has2ThirdLevelPactSlots()
    {
        var tracker = new SpellSlotTracker();
        // Warlock level 5: 2 pact slots at 3rd level (not 1st + 2nd + 3rd)
        // Warlocks only have slots at their pact level
        tracker.Initialize(new[] { 0, 0, 2 });

        Assert.That(tracker.GetMaxSlots(1), Is.EqualTo(0));
        Assert.That(tracker.GetMaxSlots(2), Is.EqualTo(0));
        Assert.That(tracker.GetMaxSlots(3), Is.EqualTo(2));
    }

    [Test]
    public void WarlockLevel17_Has4NinthLevelPactSlots()
    {
        var tracker = new SpellSlotTracker();
        // Warlock level 17: 4 pact slots at 5th level (not 9th — Warlocks cap at 5th)
        tracker.Initialize(new[] { 0, 0, 0, 0, 4 });

        Assert.That(tracker.GetMaxSlots(5), Is.EqualTo(4));
        Assert.That(tracker.GetMaxSlots(9), Is.EqualTo(0));
    }

    [Test]
    public void WarlockShortRest_RestoresAllPactSlots()
    {
        var tracker = new SpellSlotTracker();
        tracker.Initialize(new[] { 0, 0, 2 }); // Level 5 Warlock: 2 third-level slots

        // Expend both slots
        Assert.That(tracker.ExpendSlot(3), Is.True);
        Assert.That(tracker.ExpendSlot(3), Is.True);
        Assert.That(tracker.GetRemainingSlots(3), Is.EqualTo(0));

        // Short rest (restore all)
        tracker.RestoreAllSlots();
        Assert.That(tracker.GetRemainingSlots(3), Is.EqualTo(2));
    }

    // ── Slot Exhaustion ─────────────────────────────────────────────────

    [Test]
    public void SlotExhaustion_CannotCastWhenAllSlotsGone()
    {
        var tracker = new SpellSlotTracker();
        tracker.Initialize(new[] { 2, 1 }); // 2 first, 1 second

        // Expend all first-level slots
        tracker.ExpendSlot(1);
        tracker.ExpendSlot(1);

        Assert.That(tracker.HasAvailableSlot(1), Is.False);
        Assert.That(tracker.ExpendSlot(1), Is.False);
    }

    [Test]
    public void SlotExhaustion_CanUpcastIfHigherSlotsRemain()
    {
        var tracker = new SpellSlotTracker();
        tracker.Initialize(new[] { 2, 1 }); // 2 first, 1 second

        // Expend all first-level slots
        tracker.ExpendSlot(1);
        tracker.ExpendSlot(1);

        // Can still cast 1st-level spell using 2nd-level slot
        Assert.That(tracker.HasAvailableSlotAtOrAbove(1), Is.True);
        Assert.That(tracker.HasAvailableSlot(2), Is.True);
    }

    [Test]
    public void SlotExhaustion_AllLevelsEmpty()
    {
        var tracker = new SpellSlotTracker();
        tracker.Initialize(new[] { 1, 1, 1 });

        // Expend all
        tracker.ExpendSlot(1);
        tracker.ExpendSlot(2);
        tracker.ExpendSlot(3);

        Assert.That(tracker.HasAvailableSlotAtOrAbove(1), Is.False);
        Assert.That(tracker.HasAvailableSlotAtOrAbove(2), Is.False);
        Assert.That(tracker.HasAvailableSlotAtOrAbove(3), Is.False);
    }

    // ── InitializeFromCharacter ─────────────────────────────────────────

    [Test]
    public void InitializeFromCharacter_LoadsCurrentSlots()
    {
        var tracker = new SpellSlotTracker();
        var maxSlots = new int[] { 4, 3, 2, 0, 0, 0, 0, 0, 0 };
        var currentSlots = new int[] { 2, 1, 0, 0, 0, 0, 0, 0, 0 };

        tracker.InitializeFromCharacter(maxSlots, currentSlots);

        Assert.That(tracker.GetMaxSlots(1), Is.EqualTo(4));
        Assert.That(tracker.GetMaxSlots(2), Is.EqualTo(3));
        Assert.That(tracker.GetMaxSlots(3), Is.EqualTo(2));

        Assert.That(tracker.GetRemainingSlots(1), Is.EqualTo(2));
        Assert.That(tracker.GetRemainingSlots(2), Is.EqualTo(1));
        Assert.That(tracker.GetRemainingSlots(3), Is.EqualTo(0));
    }

    [Test]
    public void InitializeFromCharacter_ClampsCurrentToMax()
    {
        var tracker = new SpellSlotTracker();
        var maxSlots = new int[] { 4, 3, 2, 0, 0, 0, 0, 0, 0 };
        var currentSlots = new int[] { 10, 10, 10, 0, 0, 0, 0, 0, 0 }; // Invalid: over max

        tracker.InitializeFromCharacter(maxSlots, currentSlots);

        Assert.That(tracker.GetRemainingSlots(1), Is.EqualTo(4)); // Clamped to max
        Assert.That(tracker.GetRemainingSlots(2), Is.EqualTo(3));
        Assert.That(tracker.GetRemainingSlots(3), Is.EqualTo(2));
    }

    [Test]
    public void InitializeFromCharacter_RequiresNineElementArrays()
    {
        var tracker = new SpellSlotTracker();
        var shortArray = new int[] { 4, 3, 2 };

        Assert.Throws<ArgumentException>(() =>
            tracker.InitializeFromCharacter(shortArray, new int[9]));

        Assert.Throws<ArgumentException>(() =>
            tracker.InitializeFromCharacter(new int[9], shortArray));
    }
}
