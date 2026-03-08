using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Static utility class for D&D 5e attunement rules.
/// Handles attuning/unattuning magic items with the 3-slot limit,
/// class restrictions, and attunement requirement checks.
/// </summary>
public static class AttunementManager
{
    /// <summary>
    /// Maximum number of magic items a character can be attuned to simultaneously (D&D 5e SRD).
    /// </summary>
    public const int MaxAttunementSlots = 3;

    /// <summary>
    /// Returns true if the character can attune to the given item.
    /// Checks: item requires attunement, not already attuned, slots available,
    /// and any class/race restriction on the item is satisfied.
    /// </summary>
    public static bool CanAttune(Character character, InventoryItem item)
    {
        if (character == null || item == null)
            return false;

        if (!RequiresAttunement(item))
            return false;

        if (IsAttuned(character, item.Name))
            return false;

        if (GetAttunedCount(character) >= MaxAttunementSlots)
            return false;

        // Check class/race restriction if present.
        string restriction = GetAttunementRestriction(item);
        if (!string.IsNullOrEmpty(restriction))
        {
            if (!MeetsRestriction(character, restriction))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Attunes the character to the item if all requirements are met.
    /// Returns true on success, false if attunement is not possible.
    /// </summary>
    public static bool Attune(Character character, InventoryItem item)
    {
        if (!CanAttune(character, item))
            return false;

        character.AttunedItems.Add(item.Name);
        return true;
    }

    /// <summary>
    /// Removes attunement to the named item. Returns true if the item
    /// was attuned and has been removed, false if it was not attuned.
    /// </summary>
    public static bool Unattune(Character character, string itemName)
    {
        if (character == null || string.IsNullOrEmpty(itemName))
            return false;

        return character.AttunedItems.Remove(itemName);
    }

    /// <summary>
    /// Returns how many attunement slots are currently occupied.
    /// </summary>
    public static int GetAttunedCount(Character character)
    {
        if (character == null)
            return 0;

        return character.AttunedItems.Count;
    }

    /// <summary>
    /// Returns true if the character is currently attuned to the named item.
    /// </summary>
    public static bool IsAttuned(Character character, string itemName)
    {
        if (character == null || string.IsNullOrEmpty(itemName))
            return false;

        return character.AttunedItems.Contains(itemName);
    }

    /// <summary>
    /// Returns a copy of the character's attuned item names.
    /// </summary>
    public static List<string> GetAttunedItems(Character character)
    {
        if (character == null)
            return new List<string>();

        return new List<string>(character.AttunedItems);
    }

    /// <summary>
    /// Returns true if the item has a "requiresAttunement" property set to "true".
    /// </summary>
    public static bool RequiresAttunement(InventoryItem item)
    {
        if (item == null)
            return false;

        return item.Properties.TryGetValue("requiresAttunement", out string val)
               && string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the attunement restriction string (e.g. "Cleric", "Spellcaster")
    /// or empty string if there is no restriction.
    /// </summary>
    public static string GetAttunementRestriction(InventoryItem item)
    {
        if (item == null)
            return string.Empty;

        return item.Properties.GetValueOrDefault("attunementRestriction", string.Empty);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Checks if a character meets a restriction string. Supports class names
    /// (exact match on CharacterClassName) and the generic "Spellcaster" keyword
    /// (any class with spell slots).
    /// </summary>
    private static bool MeetsRestriction(Character character, string restriction)
    {
        // Exact class match (e.g. "Cleric", "Wizard").
        if (string.Equals(character.CharacterClassName, restriction, StringComparison.OrdinalIgnoreCase))
            return true;

        // "Spellcaster" — character has at least one max spell slot > 0.
        if (string.Equals(restriction, "Spellcaster", StringComparison.OrdinalIgnoreCase))
        {
            if (character.SpellSlotsMax != null)
            {
                for (int i = 0; i < character.SpellSlotsMax.Length; i++)
                {
                    if (character.SpellSlotsMax[i] > 0)
                        return true;
                }
            }
            return false;
        }

        // Race match (e.g. "Dwarf", "Elf").
        if (string.Equals(character.Race, restriction, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
