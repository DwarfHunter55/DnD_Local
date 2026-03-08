using Newtonsoft.Json;

namespace ChroniclesUnbound.Data;

/// <summary>
/// An item that can be carried in a character's inventory.
/// Uses a flexible Properties dictionary for weapon stats, armor bonuses,
/// consumable effects, and other item-specific data.
/// </summary>
public class InventoryItem
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("quantity")]
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Weight in pounds per single item.
    /// </summary>
    [JsonProperty("weight")]
    public float Weight { get; set; }

    [JsonProperty("rarity")]
    public ItemRarity Rarity { get; set; } = ItemRarity.Common;

    [JsonProperty("isEquippable")]
    public bool IsEquippable { get; set; }

    [JsonProperty("isConsumable")]
    public bool IsConsumable { get; set; }

    /// <summary>
    /// Flexible key-value store for item-specific data such as
    /// "damage" = "1d8", "armorBonus" = "2", "healAmount" = "2d4+2", etc.
    /// </summary>
    [JsonProperty("properties")]
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <summary>
    /// Total weight for the full stack of this item.
    /// </summary>
    [JsonIgnore]
    public float TotalWeight => Weight * Quantity;
}
