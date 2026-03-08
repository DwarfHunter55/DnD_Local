using NUnit.Framework;
using ChroniclesUnbound.Core;

namespace ChroniclesUnbound.Tests.Spells;

/// <summary>
/// Tests for SpellSlotTracker covering initialization, expending,
/// restoring, and synchronization to character arrays.
/// </summary>
[TestFixture]
public class SpellSlotTrackerTests
{
    private SpellSlotTracker _tracker;

    [SetUp]
    public void SetUp()
    {
        _tracker = new SpellSlotTracker();
        // Level 5 Wizard: 4/3/2 slots for levels 1/2/3
        _tracker.Initialize(new[] { 4, 3, 2 });
    }

    // ── Initialization ──────────────────────────────────────────────

    [Test]
    public void Initialize_SetsMaxAndCurrent()
    {
        Assert.That(_tracker.GetMaxSlots(1), Is.EqualTo(4));
        Assert.That(_tracker.GetMaxSlots(2), Is.EqualTo(3));
        Assert.That(_tracker.GetMaxSlots(3), Is.EqualTo(2));
        Assert.That(_tracker.GetRemainingSlots(1), Is.EqualTo(4));
    }

    [Test]
    public void Initialize_HigherLevelsAreZero()
    {
        Assert.That(_tracker.GetMaxSlots(4), Is.EqualTo(0));
        Assert.That(_tracker.GetRemainingSlots(9), Is.EqualTo(0));
    }

    [Test]
    public void Initialize_NullArray_Throws()
    {
        var tracker = new SpellSlotTracker();
        Assert.Throws<ArgumentException>(() => tracker.Initialize(null));
    }

    [Test]
    public void Initialize_EmptyArray_Throws()
    {
        var tracker = new SpellSlotTracker();
        Assert.Throws<ArgumentException>(() => tracker.Initialize(Array.Empty<int>()));
    }

    // ── Expending ───────────────────────────────────────────────────

    [Test]
    public void ExpendSlot_DecrementsRemaining()
    {
        bool result = _tracker.ExpendSlot(1);
        Assert.That(result, Is.True);
        Assert.That(_tracker.GetRemainingSlots(1), Is.EqualTo(3));
    }

    [Test]
    public void ExpendSlot_WhenNone_ReturnsFalse()
    {
        // Level 4 has 0 slots
        bool result = _tracker.ExpendSlot(4);
        Assert.That(result, Is.False);
    }

    [Test]
    public void ExpendSlot_ExhaustAllSlots()
    {
        for (int i = 0; i < 4; i++)
            Assert.That(_tracker.ExpendSlot(1), Is.True);

        Assert.That(_tracker.ExpendSlot(1), Is.False);
        Assert.That(_tracker.GetRemainingSlots(1), Is.EqualTo(0));
    }

    // ── HasAvailableSlot ────────────────────────────────────────────

    [Test]
    public void HasAvailableSlot_TrueWhenSlotsRemain()
    {
        Assert.That(_tracker.HasAvailableSlot(1), Is.True);
    }

    [Test]
    public void HasAvailableSlot_FalseWhenEmpty()
    {
        Assert.That(_tracker.HasAvailableSlot(4), Is.False);
    }

    [Test]
    public void HasAvailableSlotAtOrAbove_ChecksHigherLevels()
    {
        // Expend all level 1 slots
        for (int i = 0; i < 4; i++)
            _tracker.ExpendSlot(1);

        // Level 1 is empty, but level 2 still has slots
        Assert.That(_tracker.HasAvailableSlotAtOrAbove(1), Is.True);
    }

    // ── Restoring ───────────────────────────────────────────────────

    [Test]
    public void RestoreAllSlots_ResetsToMax()
    {
        _tracker.ExpendSlot(1);
        _tracker.ExpendSlot(2);
        _tracker.RestoreAllSlots();

        Assert.That(_tracker.GetRemainingSlots(1), Is.EqualTo(4));
        Assert.That(_tracker.GetRemainingSlots(2), Is.EqualTo(3));
    }

    [Test]
    public void RestoreSlots_CapsAtMax()
    {
        _tracker.ExpendSlot(1);
        _tracker.RestoreSlots(1, 10); // Try to restore more than max

        Assert.That(_tracker.GetRemainingSlots(1), Is.EqualTo(4));
    }

    // ── SyncToCharacter ─────────────────────────────────────────────

    [Test]
    public void SyncToCharacter_WritesCurrentSlots()
    {
        _tracker.ExpendSlot(1);
        _tracker.ExpendSlot(2);

        var target = new int[9];
        _tracker.SyncToCharacter(target);

        Assert.That(target[0], Is.EqualTo(3)); // Level 1: 4 - 1
        Assert.That(target[1], Is.EqualTo(2)); // Level 2: 3 - 1
        Assert.That(target[2], Is.EqualTo(2)); // Level 3: unchanged
    }

    // ── Validation ──────────────────────────────────────────────────

    [Test]
    public void MethodsBeforeInitialize_ThrowInvalidOperation()
    {
        var tracker = new SpellSlotTracker();
        Assert.Throws<InvalidOperationException>(() => tracker.GetRemainingSlots(1));
        Assert.Throws<InvalidOperationException>(() => tracker.ExpendSlot(1));
    }

    [Test]
    public void InvalidLevel_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _tracker.GetRemainingSlots(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _tracker.GetRemainingSlots(10));
    }
}
