using System;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Tracks spell slot usage for a caster. Slot indices are 0-based where
/// index 0 = 1st-level slots, index 8 = 9th-level slots.
/// </summary>
public class SpellSlotTracker
{
    /// <summary>Number of spell slot levels (1st through 9th).</summary>
    public const int MaxSpellLevel = 9;

    private readonly int[] _maxSlots = new int[MaxSpellLevel];
    private readonly int[] _currentSlots = new int[MaxSpellLevel];
    private bool _initialized;

    /// <summary>
    /// Initializes the tracker with maximum slot counts.
    /// Index 0 = 1st-level max slots, index 1 = 2nd-level, etc.
    /// Current slots are set to match the max.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// If the array is null, empty, or longer than 9 elements.
    /// </exception>
    public void Initialize(int[] maxSlots)
    {
        if (maxSlots == null || maxSlots.Length == 0)
            throw new ArgumentException("Max slots array cannot be null or empty.", nameof(maxSlots));
        if (maxSlots.Length > MaxSpellLevel)
            throw new ArgumentException($"Max slots array cannot exceed {MaxSpellLevel} elements.", nameof(maxSlots));

        Array.Clear(_maxSlots, 0, MaxSpellLevel);
        Array.Clear(_currentSlots, 0, MaxSpellLevel);

        for (int i = 0; i < maxSlots.Length; i++)
        {
            int value = Math.Max(0, maxSlots[i]);
            _maxSlots[i] = value;
            _currentSlots[i] = value;
        }

        _initialized = true;
    }

    /// <summary>
    /// Initializes directly from a Character's spell slot arrays (0-indexed, 9 elements).
    /// Sets current slots from the character's current array rather than resetting to max.
    /// </summary>
    public void InitializeFromCharacter(int[] maxSlots, int[] currentSlots)
    {
        if (maxSlots == null || maxSlots.Length != MaxSpellLevel)
            throw new ArgumentException($"Max slots array must have {MaxSpellLevel} elements.", nameof(maxSlots));
        if (currentSlots == null || currentSlots.Length != MaxSpellLevel)
            throw new ArgumentException($"Current slots array must have {MaxSpellLevel} elements.", nameof(currentSlots));

        Array.Copy(maxSlots, _maxSlots, MaxSpellLevel);
        for (int i = 0; i < MaxSpellLevel; i++)
            _currentSlots[i] = Math.Clamp(currentSlots[i], 0, _maxSlots[i]);

        _initialized = true;
    }

    /// <summary>
    /// Returns remaining slots for the given spell level (1-9).
    /// </summary>
    public int GetRemainingSlots(int level)
    {
        ValidateLevel(level);
        return _currentSlots[level - 1];
    }

    /// <summary>
    /// Returns max slots for the given spell level (1-9).
    /// </summary>
    public int GetMaxSlots(int level)
    {
        ValidateLevel(level);
        return _maxSlots[level - 1];
    }

    /// <summary>
    /// Expends one spell slot at the given level. Returns true if a slot
    /// was available and expended, false if no slots remain.
    /// </summary>
    public bool ExpendSlot(int level)
    {
        ValidateLevel(level);
        int index = level - 1;

        if (_currentSlots[index] <= 0)
            return false;

        _currentSlots[index]--;
        return true;
    }

    /// <summary>
    /// Returns true if at least one slot remains at the given level (1-9).
    /// </summary>
    public bool HasAvailableSlot(int level)
    {
        ValidateLevel(level);
        return _currentSlots[level - 1] > 0;
    }

    /// <summary>
    /// Returns true if at least one slot remains at the given level or any
    /// higher level. Useful for checking if upcasting is possible.
    /// </summary>
    public bool HasAvailableSlotAtOrAbove(int level)
    {
        ValidateLevel(level);
        for (int i = level - 1; i < MaxSpellLevel; i++)
        {
            if (_currentSlots[i] > 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Restores all spell slots to their maximum values (long rest).
    /// </summary>
    public void RestoreAllSlots()
    {
        Array.Copy(_maxSlots, _currentSlots, MaxSpellLevel);
    }

    /// <summary>
    /// Restores a specific number of slots at the given level, up to the maximum.
    /// Useful for Arcane Recovery and similar features.
    /// </summary>
    public void RestoreSlots(int level, int count)
    {
        ValidateLevel(level);
        if (count <= 0)
            return;

        int index = level - 1;
        _currentSlots[index] = Math.Min(_currentSlots[index] + count, _maxSlots[index]);
    }

    /// <summary>
    /// Writes current slot state back to the character's arrays.
    /// Call after combat or rest to persist changes.
    /// </summary>
    public void SyncToCharacter(int[] targetCurrent)
    {
        if (targetCurrent == null || targetCurrent.Length != MaxSpellLevel)
            throw new ArgumentException($"Target array must have {MaxSpellLevel} elements.", nameof(targetCurrent));

        Array.Copy(_currentSlots, targetCurrent, MaxSpellLevel);
    }

    private void ValidateLevel(int level)
    {
        if (!_initialized)
            throw new InvalidOperationException("SpellSlotTracker has not been initialized. Call Initialize() first.");
        if (level < 1 || level > MaxSpellLevel)
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Spell level must be between 1 and {MaxSpellLevel}.");
    }
}
