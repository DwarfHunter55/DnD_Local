namespace ChroniclesUnbound.Data;

/// <summary>
/// Equipment slots on a character's body. Maps to the string keys
/// stored in Character.EquippedItems.
/// </summary>
public enum EquipmentSlot
{
    MainHand,
    OffHand,
    Armor,
    Ranged,
    Head,
    Neck,
    Ring1,
    Ring2,
    Cloak,
    Boots,
    Gloves,
    Belt
}

/// <summary>
/// Resolves which equipment slots an item can occupy, converts between
/// EquipmentSlot enum values and the string keys in Character.EquippedItems,
/// and provides display names for the UI.
/// </summary>
public static class EquipmentSlotResolver
{
    /// <summary>
    /// Returns the valid equipment slots for the given item based on its
    /// Properties["type"] and Properties["category"]/Properties["weaponProperties"].
    /// Returns an empty list for items that cannot be equipped.
    /// </summary>
    public static List<EquipmentSlot> GetValidSlots(InventoryItem item)
    {
        var slots = new List<EquipmentSlot>();
        if (item == null || !item.IsEquippable)
            return slots;

        string type = item.Properties.GetValueOrDefault("type", "");
        string category = item.Properties.GetValueOrDefault("category", "");
        string weaponProps = item.Properties.GetValueOrDefault("weaponProperties", "");

        switch (type)
        {
            case "Weapon":
                if (HasWeaponProperty(weaponProps, "Ammunition"))
                {
                    // Ranged weapons with Ammunition go in the Ranged slot.
                    slots.Add(EquipmentSlot.Ranged);
                }
                else
                {
                    // Melee weapons go in MainHand.
                    slots.Add(EquipmentSlot.MainHand);

                    // Light weapons can also be used in the OffHand (dual-wielding).
                    if (HasWeaponProperty(weaponProps, "Light"))
                        slots.Add(EquipmentSlot.OffHand);
                }

                // Thrown weapons can also go in Ranged if they don't already.
                if (HasWeaponProperty(weaponProps, "Thrown") && !slots.Contains(EquipmentSlot.Ranged))
                    slots.Add(EquipmentSlot.Ranged);
                break;

            case "Armor":
                if (string.Equals(category, "Shield", StringComparison.OrdinalIgnoreCase))
                    slots.Add(EquipmentSlot.OffHand);
                else
                    slots.Add(EquipmentSlot.Armor);
                break;

            // Future magic item categories.
            case "Wondrous":
            case "MagicItem":
                switch (category.ToLowerInvariant())
                {
                    case "head":
                    case "helm":
                    case "hat":
                        slots.Add(EquipmentSlot.Head);
                        break;
                    case "neck":
                    case "amulet":
                    case "necklace":
                        slots.Add(EquipmentSlot.Neck);
                        break;
                    case "ring":
                        slots.Add(EquipmentSlot.Ring1);
                        slots.Add(EquipmentSlot.Ring2);
                        break;
                    case "cloak":
                    case "cape":
                        slots.Add(EquipmentSlot.Cloak);
                        break;
                    case "boots":
                    case "feet":
                        slots.Add(EquipmentSlot.Boots);
                        break;
                    case "gloves":
                    case "hands":
                    case "gauntlets":
                        slots.Add(EquipmentSlot.Gloves);
                        break;
                    case "belt":
                    case "waist":
                        slots.Add(EquipmentSlot.Belt);
                        break;
                }
                break;
        }

        return slots;
    }

    /// <summary>
    /// Returns a human-readable display name for a slot (e.g. "Main Hand", "Off Hand").
    /// </summary>
    public static string GetSlotDisplayName(EquipmentSlot slot)
    {
        return slot switch
        {
            EquipmentSlot.MainHand => "Main Hand",
            EquipmentSlot.OffHand  => "Off Hand",
            EquipmentSlot.Armor    => "Armor",
            EquipmentSlot.Ranged   => "Ranged",
            EquipmentSlot.Head     => "Head",
            EquipmentSlot.Neck     => "Neck",
            EquipmentSlot.Ring1    => "Ring 1",
            EquipmentSlot.Ring2    => "Ring 2",
            EquipmentSlot.Cloak    => "Cloak",
            EquipmentSlot.Boots    => "Boots",
            EquipmentSlot.Gloves   => "Gloves",
            EquipmentSlot.Belt     => "Belt",
            _                      => slot.ToString()
        };
    }

    /// <summary>
    /// Returns true if the item is a two-handed weapon (has "Two-Handed" in weaponProperties).
    /// </summary>
    public static bool IsWeaponTwoHanded(InventoryItem item)
    {
        if (item == null)
            return false;

        string weaponProps = item.Properties.GetValueOrDefault("weaponProperties", "");
        return HasWeaponProperty(weaponProps, "Two-Handed");
    }

    /// <summary>
    /// Returns true if the item is a versatile weapon (can be used one- or two-handed).
    /// </summary>
    public static bool IsWeaponVersatile(InventoryItem item)
    {
        if (item == null)
            return false;

        string weaponProps = item.Properties.GetValueOrDefault("weaponProperties", "");
        return HasWeaponProperty(weaponProps, "Versatile");
    }

    /// <summary>
    /// Converts an EquipmentSlot enum value to its string key for Character.EquippedItems.
    /// </summary>
    public static string SlotToString(EquipmentSlot slot)
    {
        return slot.ToString();
    }

    /// <summary>
    /// Converts a string key from Character.EquippedItems to an EquipmentSlot enum value.
    /// Returns null if the string does not match any slot.
    /// </summary>
    public static EquipmentSlot? StringToSlot(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        if (Enum.TryParse<EquipmentSlot>(key, ignoreCase: true, out var slot))
            return slot;

        return null;
    }

    /// <summary>
    /// Returns all currently valid equipment slots as an array (useful for UI iteration).
    /// </summary>
    public static EquipmentSlot[] AllSlots => Enum.GetValues<EquipmentSlot>();

    /// <summary>
    /// Checks whether a comma-separated weapon properties string contains a specific property.
    /// </summary>
    private static bool HasWeaponProperty(string weaponProperties, string property)
    {
        if (string.IsNullOrEmpty(weaponProperties))
            return false;

        foreach (var prop in weaponProperties.Split(',', StringSplitOptions.TrimEntries))
        {
            if (string.Equals(prop, property, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
