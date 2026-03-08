using Newtonsoft.Json;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Static utility class that resolves magic item effects for characters.
/// Handles parsing magicEffects/magicBonus from item properties,
/// and aggregating bonuses across all equipped items on a character.
/// </summary>
public static class MagicEffectResolver
{
    // ── Parsing ───────────────────────────────────────────────────────

    /// <summary>
    /// Deserializes the "magicEffects" JSON string from an item's Properties.
    /// Returns an empty list if the property is missing, empty, or invalid.
    /// </summary>
    public static List<MagicEffect> ParseEffects(InventoryItem item)
    {
        if (item == null)
            return new List<MagicEffect>();

        if (!item.Properties.TryGetValue("magicEffects", out string effectsJson))
            return new List<MagicEffect>();

        if (string.IsNullOrWhiteSpace(effectsJson))
            return new List<MagicEffect>();

        try
        {
            var effects = JsonConvert.DeserializeObject<List<MagicEffect>>(effectsJson);
            return effects ?? new List<MagicEffect>();
        }
        catch (JsonException)
        {
            return new List<MagicEffect>();
        }
    }

    /// <summary>
    /// Returns the magic bonus (e.g. +1, +2, +3) from an item's Properties.
    /// Returns 0 if not present or not a valid integer.
    /// </summary>
    public static int GetMagicBonus(InventoryItem item)
    {
        if (item == null)
            return 0;

        if (item.Properties.TryGetValue("magicBonus", out string bonusStr)
            && int.TryParse(bonusStr, out int bonus))
        {
            return bonus;
        }

        return 0;
    }

    /// <summary>
    /// Returns true if the item requires attunement. Delegates to AttunementManager.
    /// </summary>
    public static bool RequiresAttunement(InventoryItem item)
    {
        return AttunementManager.RequiresAttunement(item);
    }

    /// <summary>
    /// Returns a human-readable summary of all magical properties on an item.
    /// Includes magic bonus and all parsed magic effects.
    /// </summary>
    public static string GetEffectsDescription(InventoryItem item)
    {
        if (item == null)
            return string.Empty;

        var parts = new List<string>();

        int bonus = GetMagicBonus(item);
        if (bonus != 0)
        {
            string type = item.Properties.GetValueOrDefault("type", "");
            if (string.Equals(type, "Weapon", StringComparison.OrdinalIgnoreCase))
                parts.Add($"+{bonus} to attack and damage rolls");
            else if (string.Equals(type, "Armor", StringComparison.OrdinalIgnoreCase))
                parts.Add($"+{bonus} to AC");
            else
                parts.Add($"+{bonus} bonus");
        }

        var effects = ParseEffects(item);
        foreach (var effect in effects)
        {
            string desc = FormatEffect(effect);
            if (!string.IsNullOrEmpty(desc))
                parts.Add(desc);
        }

        if (RequiresAttunement(item))
            parts.Add("(requires attunement)");

        return parts.Count > 0 ? string.Join("; ", parts) : string.Empty;
    }

    // ── Aggregate Bonus Methods ───────────────────────────────────────

    /// <summary>
    /// Returns the total magic attack bonus from the character's equipped weapon(s).
    /// Sums magicBonus from MainHand (or Ranged) weapon, plus any Passive/AttackRoll
    /// effects from all equipped items.
    /// </summary>
    public static int GetTotalAttackBonus(Character character)
    {
        if (character == null)
            return 0;

        int total = 0;
        foreach (var (slotKey, item) in GetAllEffectiveEquippedItems(character))
        {
            string type = item.Properties.GetValueOrDefault("type", "");

            // magicBonus on weapons adds to attack rolls.
            if (string.Equals(type, "Weapon", StringComparison.OrdinalIgnoreCase))
            {
                total += GetMagicBonus(item);
            }

            // Passive AttackRoll effects from any equipped item.
            foreach (var effect in ParseEffects(item))
            {
                if (effect.Type == MagicEffectType.Passive && effect.Stat == MagicBonusStat.AttackRoll)
                    total += ParseBonusValue(effect.Value);
            }
        }

        return total;
    }

    /// <summary>
    /// Returns the total magic damage bonus from equipped weapon(s).
    /// Magic weapons add their magicBonus to both attack and damage.
    /// </summary>
    public static int GetTotalDamageBonus(Character character)
    {
        if (character == null)
            return 0;

        int total = 0;
        foreach (var (slotKey, item) in GetAllEffectiveEquippedItems(character))
        {
            string type = item.Properties.GetValueOrDefault("type", "");

            // magicBonus on weapons adds to damage rolls.
            if (string.Equals(type, "Weapon", StringComparison.OrdinalIgnoreCase))
            {
                total += GetMagicBonus(item);
            }

            // Passive DamageRoll effects from any equipped item.
            foreach (var effect in ParseEffects(item))
            {
                if (effect.Type == MagicEffectType.Passive && effect.Stat == MagicBonusStat.DamageRoll)
                    total += ParseBonusValue(effect.Value);
            }
        }

        return total;
    }

    /// <summary>
    /// Collects all ExtraDamage dice from equipped items. Optionally filters by
    /// targetType (e.g. "undead", "fiends") matching the effect's Condition.
    /// Returns a combined dice string like "2d6 fire, 1d6 radiant" or empty string.
    /// </summary>
    public static string GetExtraDamageDice(Character character, string targetType = null)
    {
        if (character == null)
            return string.Empty;

        var extraDice = new List<string>();

        foreach (var (slotKey, item) in GetAllEffectiveEquippedItems(character))
        {
            foreach (var effect in ParseEffects(item))
            {
                if (effect.Type != MagicEffectType.ExtraDamage)
                    continue;

                // Check condition filter: empty condition always applies,
                // otherwise must match the target type.
                if (!string.IsNullOrEmpty(effect.Condition)
                    && !string.Equals(effect.Condition, targetType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string dice = effect.Dice;
                if (!string.IsNullOrEmpty(dice))
                {
                    string label = !string.IsNullOrEmpty(effect.DamageType)
                        ? $"{dice} {effect.DamageType}"
                        : dice;
                    extraDice.Add(label);
                }
            }
        }

        return extraDice.Count > 0 ? string.Join(", ", extraDice) : string.Empty;
    }

    /// <summary>
    /// Returns the total AC bonus from magic items. Includes:
    /// - magicBonus from armor and shields
    /// - Passive AC effects from wondrous items (e.g. Cloak of Protection)
    /// </summary>
    public static int GetTotalACBonus(Character character)
    {
        if (character == null)
            return 0;

        int total = 0;
        foreach (var (slotKey, item) in GetAllEffectiveEquippedItems(character))
        {
            string type = item.Properties.GetValueOrDefault("type", "");

            // magicBonus on armor/shields adds to AC.
            if (string.Equals(type, "Armor", StringComparison.OrdinalIgnoreCase))
            {
                total += GetMagicBonus(item);
            }

            // Passive AC effects from any equipped item.
            foreach (var effect in ParseEffects(item))
            {
                if (effect.Type == MagicEffectType.Passive && effect.Stat == MagicBonusStat.AC)
                    total += ParseBonusValue(effect.Value);
            }
        }

        return total;
    }

    /// <summary>
    /// Returns the total saving throw bonus from all equipped magic items
    /// (e.g. Cloak of Protection gives +1 to all saving throws).
    /// </summary>
    public static int GetSavingThrowBonus(Character character)
    {
        if (character == null)
            return 0;

        int total = 0;
        foreach (var (slotKey, item) in GetAllEffectiveEquippedItems(character))
        {
            foreach (var effect in ParseEffects(item))
            {
                if (effect.Type == MagicEffectType.Passive && effect.Stat == MagicBonusStat.SavingThrows)
                    total += ParseBonusValue(effect.Value);
            }
        }

        return total;
    }

    /// <summary>
    /// Collects all damage type resistances from equipped magic items.
    /// </summary>
    public static HashSet<string> GetResistances(Character character)
    {
        var resistances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (character == null)
            return resistances;

        foreach (var (slotKey, item) in GetAllEffectiveEquippedItems(character))
        {
            foreach (var effect in ParseEffects(item))
            {
                if (effect.Type == MagicEffectType.Resistance && !string.IsNullOrEmpty(effect.DamageType))
                    resistances.Add(effect.DamageType);
            }
        }

        return resistances;
    }

    /// <summary>
    /// Collects all damage type immunities from equipped magic items.
    /// </summary>
    public static HashSet<string> GetImmunities(Character character)
    {
        var immunities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (character == null)
            return immunities;

        foreach (var (slotKey, item) in GetAllEffectiveEquippedItems(character))
        {
            foreach (var effect in ParseEffects(item))
            {
                if (effect.Type == MagicEffectType.Immunity && !string.IsNullOrEmpty(effect.DamageType))
                    immunities.Add(effect.DamageType);
            }
        }

        return immunities;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all effectively equipped items on a character, filtered by:
    /// - Items that require attunement must be attuned to count.
    /// - Deduplicated by item name (5e: identical items don't stack bonuses).
    /// Each entry is a (slotKey, item) tuple.
    /// </summary>
    private static List<(string slotKey, InventoryItem item)> GetAllEffectiveEquippedItems(Character character)
    {
        var result = new List<(string, InventoryItem)>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var slot in EquipmentSlotResolver.AllSlots)
        {
            string slotKey = EquipmentSlotResolver.SlotToString(slot);

            if (!character.EquippedItems.TryGetValue(slotKey, out string itemName))
                continue;

            if (string.IsNullOrEmpty(itemName))
                continue;

            // Deduplicate by item name (same item equipped in multiple slots shouldn't stack).
            if (!seenNames.Add(itemName))
                continue;

            // Resolve the item definition.
            InventoryItem item = ResolveItemDefinition(character, itemName);
            if (item == null)
                continue;

            // Skip items that require attunement but aren't attuned.
            if (AttunementManager.RequiresAttunement(item)
                && !AttunementManager.IsAttuned(character, item.Name))
            {
                continue;
            }

            result.Add((slotKey, item));
        }

        return result;
    }

    /// <summary>
    /// Resolves an item definition by name: checks character inventory first,
    /// then falls back to ShopManager's equipment database (which includes magic items).
    /// Does not use InventoryManager.GetEquippedItem to avoid circular calls.
    /// </summary>
    private static InventoryItem ResolveItemDefinition(Character character, string itemName)
    {
        // Check character's inventory list.
        var invItem = character.InventoryItems.Find(
            i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));

        if (invItem != null)
            return invItem;

        // Fall back to ShopManager's equipment database (includes magic items after merge).
        if (Exploration.ShopManager.Instance != null)
            return Exploration.ShopManager.Instance.GetEquipmentTemplate(itemName);

        return null;
    }

    /// <summary>
    /// Parses a bonus value string like "+1", "+2", "19", "-1" into an integer.
    /// </summary>
    private static int ParseBonusValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        // Strip leading '+' for int.TryParse.
        string cleaned = value.TrimStart('+');
        if (int.TryParse(cleaned, out int result))
            return result;

        return 0;
    }

    /// <summary>
    /// Formats a single MagicEffect into a human-readable description string.
    /// </summary>
    private static string FormatEffect(MagicEffect effect)
    {
        if (effect == null)
            return string.Empty;

        // If the effect has a custom description, prefer it.
        if (!string.IsNullOrEmpty(effect.Description))
            return effect.Description;

        switch (effect.Type)
        {
            case MagicEffectType.Passive:
                string statName = effect.Stat?.ToString() ?? "unknown";
                return $"+{effect.Value} {statName}";

            case MagicEffectType.Resistance:
                return $"Resistance to {effect.DamageType} damage";

            case MagicEffectType.Immunity:
                return $"Immunity to {effect.DamageType} damage";

            case MagicEffectType.ExtraDamage:
                string cond = !string.IsNullOrEmpty(effect.Condition)
                    ? $" vs {effect.Condition}"
                    : "";
                return $"+{effect.Dice} {effect.DamageType} damage{cond}";

            case MagicEffectType.Active:
                string name = !string.IsNullOrEmpty(effect.Name) ? effect.Name : "Ability";
                string charges = effect.CostCharges > 0 ? $" ({effect.CostCharges} charges)" : "";
                return $"{name}{charges}";

            default:
                return string.Empty;
        }
    }
}
