using Newtonsoft.Json;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// A single item listing within a shop, linking to an equipment database entry
/// with stock quantity and an optional per-item price markup.
/// </summary>
public class ShopItem
{
    /// <summary>
    /// Name of the item, must match an entry in equipment.json.
    /// </summary>
    [JsonProperty("item_name")]
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// How many of this item the shop currently has in stock.
    /// -1 means unlimited (always available).
    /// </summary>
    [JsonProperty("quantity")]
    public int Quantity { get; set; } = -1;

    /// <summary>
    /// Per-item markup multiplier applied on top of the shop's base BuyMarkup.
    /// Defaults to 1.0 (no extra markup). Useful for rare or specialty items.
    /// </summary>
    [JsonProperty("item_markup")]
    public float ItemMarkup { get; set; } = 1.0f;
}

/// <summary>
/// Represents a shop's full inventory and pricing configuration.
/// Loaded from shops.json and referenced by Location.ShopIds.
/// </summary>
public class ShopInventory
{
    [JsonProperty("shop_id")]
    public string ShopId { get; set; } = string.Empty;

    [JsonProperty("shop_name")]
    public string ShopName { get; set; } = string.Empty;

    /// <summary>
    /// ID of the location where this shop exists, matching Location.Id.
    /// </summary>
    [JsonProperty("location_id")]
    public string LocationId { get; set; } = string.Empty;

    [JsonProperty("shopkeeper_name")]
    public string ShopkeeperName { get; set; } = string.Empty;

    /// <summary>
    /// Optional flavor text describing the shop, used for LLM narration.
    /// </summary>
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Items available for purchase at this shop.
    /// </summary>
    [JsonProperty("items")]
    public List<ShopItem> Items { get; set; } = new();

    /// <summary>
    /// Global buy markup for this shop. The player pays base_cost * BuyMarkup * ShopItem.ItemMarkup.
    /// Default 1.0 means items sell at their listed price.
    /// </summary>
    [JsonProperty("buy_markup")]
    public float BuyMarkup { get; set; } = 1.0f;

    /// <summary>
    /// Fraction of base cost the shop pays when buying items from the player.
    /// Default 0.5 means the player gets half price when selling.
    /// </summary>
    [JsonProperty("sell_markdown")]
    public float SellMarkdown { get; set; } = 0.5f;
}
