using Godot;
using ChroniclesUnbound.Data;
using ChroniclesUnbound.Exploration;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Manages character inventories: equipping/unequipping items, using consumables,
/// dropping items, AC recalculation, and carry capacity.
/// Created as a child of Main when needed (not autoloaded).
/// </summary>
public partial class InventoryManager : Node
{
    // ── Singleton ─────────────────────────────────────────────────────

    public static InventoryManager Instance { get; private set; }

    // ── Signals ───────────────────────────────────────────────────────

    [Signal]
    public delegate void ItemEquippedEventHandler(string characterName, string itemName, string slot);

    [Signal]
    public delegate void ItemUnequippedEventHandler(string characterName, string itemName, string slot);

    [Signal]
    public delegate void ItemUsedEventHandler(string characterName, string itemName, string description);

    [Signal]
    public delegate void ItemDroppedEventHandler(string characterName, string itemName, int quantity);

    [Signal]
    public delegate void InventoryChangedEventHandler(string characterName);

    // ── Lifecycle ─────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    // ── Equip / Unequip ───────────────────────────────────────────────

    /// <summary>
    /// Equips an item from the character's inventory into the specified slot.
    /// Validates the item can go in the slot, handles two-handed weapon rules,
    /// and unequips any existing item in the slot first.
    /// Returns true on success.
    /// </summary>
    public bool EquipItem(Character character, InventoryItem item, EquipmentSlot slot)
    {
        if (character == null || item == null)
            return false;

        if (!item.IsEquippable)
        {
            GD.Print($"[InventoryManager] '{item.Name}' is not equippable.");
            return false;
        }

        // Validate this item can go in the requested slot.
        var validSlots = EquipmentSlotResolver.GetValidSlots(item);
        if (!validSlots.Contains(slot))
        {
            GD.Print($"[InventoryManager] '{item.Name}' cannot be equipped in {EquipmentSlotResolver.GetSlotDisplayName(slot)}.");
            return false;
        }

        // Two-handed weapon rules:
        // If equipping a two-handed weapon to MainHand, auto-unequip OffHand.
        if (slot == EquipmentSlot.MainHand && EquipmentSlotResolver.IsWeaponTwoHanded(item))
        {
            string offHandKey = EquipmentSlotResolver.SlotToString(EquipmentSlot.OffHand);
            if (character.EquippedItems.ContainsKey(offHandKey))
                UnequipItem(character, EquipmentSlot.OffHand);
        }

        // If equipping something in OffHand, check if MainHand is a two-handed weapon.
        if (slot == EquipmentSlot.OffHand)
        {
            string mainHandKey = EquipmentSlotResolver.SlotToString(EquipmentSlot.MainHand);
            if (character.EquippedItems.TryGetValue(mainHandKey, out string mainHandName))
            {
                var mainHandItem = FindItemDefinition(character, mainHandName);
                if (mainHandItem != null && EquipmentSlotResolver.IsWeaponTwoHanded(mainHandItem))
                {
                    GD.Print($"[InventoryManager] Cannot equip '{item.Name}' in Off Hand — Main Hand has two-handed weapon '{mainHandName}'.");
                    return false;
                }
            }
        }

        // Unequip existing item in this slot (returns it to inventory).
        string slotKey = EquipmentSlotResolver.SlotToString(slot);
        if (character.EquippedItems.ContainsKey(slotKey))
            UnequipItem(character, slot);

        // Set the equipped item.
        character.EquippedItems[slotKey] = item.Name;

        // Remove one from inventory (reduce quantity or remove entry).
        RemoveFromInventory(character, item, 1);

        RecalculateAC(character);

        EmitSignal(SignalName.ItemEquipped, character.Name, item.Name, slotKey);
        EmitSignal(SignalName.InventoryChanged, character.Name);

        return true;
    }

    /// <summary>
    /// Unequips the item in the specified slot and returns it to inventory.
    /// Returns the unequipped item, or null if the slot was empty.
    /// </summary>
    public InventoryItem UnequipItem(Character character, EquipmentSlot slot)
    {
        if (character == null)
            return null;

        string slotKey = EquipmentSlotResolver.SlotToString(slot);

        if (!character.EquippedItems.TryGetValue(slotKey, out string itemName))
            return null;

        if (string.IsNullOrEmpty(itemName))
        {
            character.EquippedItems.Remove(slotKey);
            return null;
        }

        // Find the item definition to add back to inventory.
        var itemDef = FindItemDefinition(character, itemName);
        if (itemDef == null)
        {
            GD.PrintErr($"[InventoryManager] Cannot find definition for '{itemName}' to return to inventory.");
            character.EquippedItems.Remove(slotKey);
            return null;
        }

        // Remove from equipped.
        character.EquippedItems.Remove(slotKey);

        // Add back to inventory.
        AddToInventory(character, itemDef, 1);

        RecalculateAC(character);

        EmitSignal(SignalName.ItemUnequipped, character.Name, itemName, slotKey);
        EmitSignal(SignalName.InventoryChanged, character.Name);

        return itemDef;
    }

    // ── Use Item ──────────────────────────────────────────────────────

    /// <summary>
    /// Uses a consumable item on the character (e.g. healing potion).
    /// Returns a description of the effect, or null if the item cannot be used.
    /// </summary>
    public string UseItem(Character character, InventoryItem item)
    {
        if (character == null || item == null)
            return null;

        if (!item.IsConsumable)
        {
            GD.Print($"[InventoryManager] '{item.Name}' is not consumable.");
            return null;
        }

        string description = ApplyConsumableEffect(character, item);

        // Consume one.
        RemoveFromInventory(character, item, 1);

        EmitSignal(SignalName.ItemUsed, character.Name, item.Name, description);
        EmitSignal(SignalName.InventoryChanged, character.Name);

        return description;
    }

    // ── Drop Item ─────────────────────────────────────────────────────

    /// <summary>
    /// Drops an item from the character's inventory, removing it permanently.
    /// Returns true if the item was successfully removed.
    /// </summary>
    public bool DropItem(Character character, InventoryItem item, int quantity = 1)
    {
        if (character == null || item == null || quantity <= 0)
            return false;

        var invItem = character.InventoryItems.Find(
            i => string.Equals(i.Name, item.Name, StringComparison.OrdinalIgnoreCase));

        if (invItem == null || invItem.Quantity < quantity)
            return false;

        RemoveFromInventory(character, invItem, quantity);

        EmitSignal(SignalName.ItemDropped, character.Name, item.Name, quantity);
        EmitSignal(SignalName.InventoryChanged, character.Name);

        return true;
    }

    // ── Query ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the InventoryItem currently equipped in the specified slot,
    /// or null if nothing is equipped there. Checks the character's inventory
    /// first, then falls back to the equipment database.
    /// </summary>
    public InventoryItem GetEquippedItem(Character character, EquipmentSlot slot)
    {
        if (character == null)
            return null;

        string slotKey = EquipmentSlotResolver.SlotToString(slot);
        if (!character.EquippedItems.TryGetValue(slotKey, out string itemName))
            return null;

        if (string.IsNullOrEmpty(itemName))
            return null;

        return FindItemDefinition(character, itemName);
    }

    /// <summary>
    /// Returns the maximum weight (in pounds) the character can carry.
    /// D&D 5e: Strength score * 15.
    /// </summary>
    public float GetCarryCapacity(Character character)
    {
        if (character == null)
            return 0f;

        return character.AbilityScores.Strength * 15f;
    }

    /// <summary>
    /// Returns the total weight of all items in the character's inventory
    /// plus all currently equipped items.
    /// </summary>
    public float GetCurrentWeight(Character character)
    {
        if (character == null)
            return 0f;

        float total = 0f;

        // Sum inventory items.
        foreach (var item in character.InventoryItems)
            total += item.TotalWeight;

        // Sum equipped items (they are not in InventoryItems while equipped).
        foreach (var kvp in character.EquippedItems)
        {
            if (string.IsNullOrEmpty(kvp.Value))
                continue;

            var itemDef = FindItemDefinition(character, kvp.Value);
            if (itemDef != null)
                total += itemDef.Weight; // Single item, quantity 1.
        }

        return total;
    }

    /// <summary>
    /// Returns true if the character is over their carry capacity.
    /// </summary>
    public bool IsEncumbered(Character character)
    {
        return GetCurrentWeight(character) > GetCarryCapacity(character);
    }

    // ── AC Recalculation ──────────────────────────────────────────────

    /// <summary>
    /// Recalculates the character's Armor Class based on equipped armor and shield.
    /// D&D 5e rules:
    ///   - Unarmored: 10 + Dex modifier
    ///   - Light armor: armorAC + Dex modifier
    ///   - Medium armor: armorAC + min(Dex modifier, 2)
    ///   - Heavy armor: armorAC (no Dex)
    ///   - Shield: +2 AC bonus (stacks with armor)
    /// </summary>
    public void RecalculateAC(Character character)
    {
        if (character == null)
            return;

        int dexMod = character.AbilityScores.GetModifier(Ability.Dexterity);
        int baseAC = 10 + dexMod; // Unarmored default.

        // Check for equipped armor.
        var armor = GetEquippedItem(character, EquipmentSlot.Armor);
        if (armor != null)
        {
            string category = armor.Properties.GetValueOrDefault("category", "");
            int armorAC = 0;
            if (armor.Properties.TryGetValue("armorClass", out string acStr))
                int.TryParse(acStr, out armorAC);

            switch (category.ToLowerInvariant())
            {
                case "light":
                    // Light armor: armor AC + full Dex modifier.
                    baseAC = armorAC + dexMod;
                    break;

                case "medium":
                    // Medium armor: armor AC + Dex modifier (max 2).
                    baseAC = armorAC + Math.Min(dexMod, 2);
                    break;

                case "heavy":
                    // Heavy armor: flat armor AC, no Dex.
                    baseAC = armorAC;
                    break;
            }
        }

        // Check for equipped shield (OffHand with type Armor, category Shield).
        var offHand = GetEquippedItem(character, EquipmentSlot.OffHand);
        if (offHand != null)
        {
            string offHandType = offHand.Properties.GetValueOrDefault("type", "");
            string offHandCategory = offHand.Properties.GetValueOrDefault("category", "");

            if (string.Equals(offHandType, "Armor", StringComparison.OrdinalIgnoreCase)
                && string.Equals(offHandCategory, "Shield", StringComparison.OrdinalIgnoreCase))
            {
                // Shield AC bonus (typically +2, read from armorClass property).
                if (offHand.Properties.TryGetValue("armorClass", out string shieldAcStr)
                    && int.TryParse(shieldAcStr, out int shieldBonus))
                {
                    baseAC += shieldBonus;
                }
                else
                {
                    baseAC += 2; // Default shield bonus per SRD.
                }
            }
        }

        character.ArmorClass = baseAC;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Finds an item definition by name. Checks the character's inventory first,
    /// then falls back to ShopManager's equipment database for items that are
    /// currently equipped (and therefore not in inventory).
    /// </summary>
    private InventoryItem FindItemDefinition(Character character, string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return null;

        // Check character inventory.
        var invItem = character.InventoryItems.Find(
            i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));

        if (invItem != null)
            return invItem;

        // Fall back to equipment database (for items currently equipped and not in inventory).
        if (ShopManager.Instance != null)
            return ShopManager.Instance.GetEquipmentTemplate(itemName);

        return null;
    }

    /// <summary>
    /// Removes a specified quantity of an item from inventory.
    /// If quantity reaches 0, removes the entry entirely.
    /// </summary>
    private static void RemoveFromInventory(Character character, InventoryItem item, int quantity)
    {
        var invItem = character.InventoryItems.Find(
            i => string.Equals(i.Name, item.Name, StringComparison.OrdinalIgnoreCase));

        if (invItem == null)
            return;

        invItem.Quantity -= quantity;
        if (invItem.Quantity <= 0)
            character.InventoryItems.Remove(invItem);
    }

    /// <summary>
    /// Adds an item to inventory. Stacks non-equippable items; equippable items
    /// get separate entries to support individual enchantments.
    /// </summary>
    private static void AddToInventory(Character character, InventoryItem template, int quantity)
    {
        // Stackable: non-equippable items.
        if (!template.IsEquippable)
        {
            var existing = character.InventoryItems.Find(
                i => string.Equals(i.Name, template.Name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Quantity += quantity;
                return;
            }
        }

        // Create a new inventory entry (clone the template).
        var newItem = new InventoryItem
        {
            Name = template.Name,
            Description = template.Description,
            Quantity = quantity,
            Weight = template.Weight,
            Rarity = template.Rarity,
            IsEquippable = template.IsEquippable,
            IsConsumable = template.IsConsumable,
            Properties = new Dictionary<string, string>(template.Properties)
        };

        character.InventoryItems.Add(newItem);
    }

    /// <summary>
    /// Applies the effect of a consumable item to the character and returns
    /// a description of what happened.
    /// </summary>
    private static string ApplyConsumableEffect(Character character, InventoryItem item)
    {
        // Healing items (e.g. Healing Potion with healAmount "2d4+2").
        if (item.Properties.TryGetValue("healAmount", out string healDice))
        {
            var roll = DiceRoller.Roll(healDice);
            int healed = roll.Total;

            int before = character.HitPoints;
            character.HitPoints = Math.Min(character.HitPoints + healed, character.MaxHitPoints);
            int actual = character.HitPoints - before;

            return $"{character.Name} drinks {item.Name} and heals {actual} HP (rolled {roll.Total}).";
        }

        // Generic effect items (antitoxin, oil, etc.) — apply the described effect.
        if (item.Properties.TryGetValue("effect", out string effect))
        {
            return $"{character.Name} uses {item.Name}: {effect}";
        }

        return $"{character.Name} uses {item.Name}.";
    }
}
