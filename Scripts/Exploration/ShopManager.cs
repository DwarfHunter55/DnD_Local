using Godot;
using Newtonsoft.Json;
using ChroniclesUnbound.Core;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Manages shop inventories, equipment database, and buy/sell transactions.
/// Designed as a Node singleton (register as autoload or access via ExplorationManager).
/// </summary>
public partial class ShopManager : Node
{
    // ── Singleton ─────────────────────────────────────────────────────

    public static ShopManager Instance { get; private set; }

    // ── Signals ───────────────────────────────────────────────────────

    [Signal]
    public delegate void ItemPurchasedEventHandler(string buyerName, string itemName, int quantity, int totalCost);

    [Signal]
    public delegate void ItemSoldEventHandler(string sellerName, string itemName, int quantity, int goldReceived);

    [Signal]
    public delegate void TransactionFailedEventHandler(string characterName, string itemName, string reason);

    // ── Data ──────────────────────────────────────────────────────────

    /// <summary>
    /// Master equipment database keyed by item name (case-insensitive).
    /// Loaded once from equipment.json.
    /// </summary>
    private Dictionary<string, InventoryItem> _equipmentDb = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All shop inventories keyed by shop ID.
    /// </summary>
    private Dictionary<string, ShopInventory> _shops = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The shop the player is currently browsing, or null if not in a shop.
    /// </summary>
    public ShopInventory ActiveShop { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;
    }

    // ── Initialization ────────────────────────────────────────────────

    /// <summary>
    /// Loads the equipment database from equipment.json and magic_items.json.
    /// Must be called before any shop operations.
    /// </summary>
    public void LoadEquipmentDatabase(
        string equipmentJsonPath = "res://Data/SRD/equipment.json",
        string magicItemsJsonPath = "res://Data/SRD/magic_items.json")
    {
        var items = SRDDataLoader.LoadEquipment(equipmentJsonPath);
        _equipmentDb.Clear();

        foreach (var item in items)
        {
            // Store with quantity 1 as the template — actual quantities live in inventories.
            item.Quantity = 1;
            _equipmentDb[item.Name] = item;
        }

        GD.Print($"[ShopManager] Loaded {_equipmentDb.Count} equipment entries.");

        // Merge magic items into the same database so they are findable via GetEquipmentTemplate().
        LoadMagicItemsIntoDatabase(magicItemsJsonPath);
    }

    /// <summary>
    /// Loads magic item definitions and merges them into the equipment database.
    /// Magic items override mundane items if names collide (unlikely given naming like "+1 Longsword").
    /// </summary>
    private void LoadMagicItemsIntoDatabase(string magicItemsJsonPath)
    {
        try
        {
            var magicItems = SRDDataLoader.LoadMagicItems(magicItemsJsonPath);
            int count = 0;

            foreach (var item in magicItems)
            {
                item.Quantity = 1;
                _equipmentDb[item.Name] = item;
                count++;
            }

            GD.Print($"[ShopManager] Loaded {count} magic item entries (total database: {_equipmentDb.Count}).");
        }
        catch (FileNotFoundException)
        {
            GD.Print("[ShopManager] Magic items file not found — skipping magic items. This is normal during early development.");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[ShopManager] Failed to load magic items: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads all shop inventories from shops.json.
    /// </summary>
    public void LoadShops(string shopsJsonPath = "res://Data/World/shops.json")
    {
        string resolvedPath = ResolveResPath(shopsJsonPath);

        if (!Godot.FileAccess.FileExists(resolvedPath) && !System.IO.File.Exists(resolvedPath))
        {
            GD.PrintErr($"[ShopManager] Shops file not found: {resolvedPath}");
            return;
        }

        string json;
        if (resolvedPath.StartsWith("res://"))
        {
            using var file = Godot.FileAccess.Open(resolvedPath, Godot.FileAccess.ModeFlags.Read);
            json = file.GetAsText();
        }
        else
        {
            json = System.IO.File.ReadAllText(resolvedPath);
        }

        var shops = JsonConvert.DeserializeObject<List<ShopInventory>>(json);
        if (shops == null)
        {
            GD.PrintErr($"[ShopManager] Failed to deserialize shops from: {resolvedPath}");
            return;
        }

        _shops.Clear();
        foreach (var shop in shops)
        {
            _shops[shop.ShopId] = shop;
        }

        GD.Print($"[ShopManager] Loaded {_shops.Count} shops.");
    }

    // ── Shop Access ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the shop inventory for the given shop ID, or null if not found.
    /// </summary>
    public ShopInventory GetShopInventory(string shopId)
    {
        _shops.TryGetValue(shopId, out var shop);
        return shop;
    }

    /// <summary>
    /// Returns all shops at a given location.
    /// </summary>
    public List<ShopInventory> GetShopsAtLocation(string locationId)
    {
        var result = new List<ShopInventory>();
        foreach (var shop in _shops.Values)
        {
            if (string.Equals(shop.LocationId, locationId, StringComparison.OrdinalIgnoreCase))
                result.Add(shop);
        }
        return result;
    }

    /// <summary>
    /// Sets the active shop for browsing. Pass null to close the shop.
    /// </summary>
    public void OpenShop(string shopId)
    {
        ActiveShop = GetShopInventory(shopId);
        if (ActiveShop == null)
            GD.PrintErr($"[ShopManager] Cannot open shop '{shopId}' — not found.");
    }

    /// <summary>
    /// Closes the currently active shop.
    /// </summary>
    public void CloseShop()
    {
        ActiveShop = null;
    }

    // ── Equipment Database ────────────────────────────────────────────

    /// <summary>
    /// Returns the base equipment template for an item by name, or null if not found.
    /// </summary>
    public InventoryItem GetEquipmentTemplate(string itemName)
    {
        _equipmentDb.TryGetValue(itemName, out var item);
        return item;
    }

    /// <summary>
    /// Returns all items in the equipment database.
    /// </summary>
    public IReadOnlyDictionary<string, InventoryItem> GetEquipmentDatabase() => _equipmentDb;

    // ── Pricing ───────────────────────────────────────────────────────

    /// <summary>
    /// Gets the base cost of an item in gold pieces from the equipment database.
    /// Returns 0 if the item is not found or has no cost property.
    /// </summary>
    public int GetBaseCost(string itemName)
    {
        if (!_equipmentDb.TryGetValue(itemName, out var item))
            return 0;

        if (item.Properties.TryGetValue("cost", out var costStr) && int.TryParse(costStr, out int cost))
            return cost;

        return 0;
    }

    /// <summary>
    /// Gets the price a player must pay to buy an item from a specific shop.
    /// Applies the shop's BuyMarkup and the item's individual ItemMarkup.
    /// Returns 0 if the item or shop is not found.
    /// </summary>
    public int GetBuyPrice(string shopId, string itemName)
    {
        if (!_shops.TryGetValue(shopId, out var shop))
            return 0;

        int baseCost = GetBaseCost(itemName);
        if (baseCost <= 0)
            return 0;

        float itemMarkup = 1.0f;
        var shopItem = FindShopItem(shop, itemName);
        if (shopItem != null)
            itemMarkup = shopItem.ItemMarkup;

        return Math.Max(1, (int)Math.Ceiling(baseCost * shop.BuyMarkup * itemMarkup));
    }

    /// <summary>
    /// Gets the gold a player receives when selling an item.
    /// Uses the base cost multiplied by the global sell markdown (default 50%).
    /// If a specific shop is active, uses that shop's SellMarkdown.
    /// </summary>
    public int GetSellPrice(string itemName)
    {
        int baseCost = GetBaseCost(itemName);
        if (baseCost <= 0)
            return 0;

        float markdown = ActiveShop?.SellMarkdown ?? 0.5f;
        return Math.Max(1, (int)Math.Floor(baseCost * markdown));
    }

    /// <summary>
    /// Gets the sell price using a specific shop's markdown rate.
    /// </summary>
    public int GetSellPrice(string shopId, string itemName)
    {
        int baseCost = GetBaseCost(itemName);
        if (baseCost <= 0)
            return 0;

        float markdown = 0.5f;
        if (_shops.TryGetValue(shopId, out var shop))
            markdown = shop.SellMarkdown;

        return Math.Max(1, (int)Math.Floor(baseCost * markdown));
    }

    // ── Transactions ──────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the character has enough gold to buy the given quantity
    /// of an item from the specified shop.
    /// </summary>
    public bool CanAfford(Character buyer, string shopId, string itemName, int quantity = 1)
    {
        if (buyer == null || quantity <= 0)
            return false;

        int unitPrice = GetBuyPrice(shopId, itemName);
        if (unitPrice <= 0)
            return false;

        return buyer.Gold >= unitPrice * quantity;
    }

    /// <summary>
    /// Purchases an item from the active shop (or a specified shop).
    /// Deducts gold from the buyer and adds the item to their inventory.
    /// Returns true on success.
    /// </summary>
    public bool BuyItem(Character buyer, string itemName, int quantity = 1, string shopId = null)
    {
        shopId ??= ActiveShop?.ShopId;
        if (string.IsNullOrEmpty(shopId))
        {
            EmitSignal(SignalName.TransactionFailed, buyer?.Name ?? "", itemName, "No shop is open.");
            return false;
        }

        if (buyer == null)
        {
            EmitSignal(SignalName.TransactionFailed, "", itemName, "No buyer specified.");
            return false;
        }

        if (quantity <= 0)
        {
            EmitSignal(SignalName.TransactionFailed, buyer.Name, itemName, "Invalid quantity.");
            return false;
        }

        if (!_shops.TryGetValue(shopId, out var shop))
        {
            EmitSignal(SignalName.TransactionFailed, buyer.Name, itemName, $"Shop '{shopId}' not found.");
            return false;
        }

        // Check item exists in equipment database.
        if (!_equipmentDb.TryGetValue(itemName, out var template))
        {
            EmitSignal(SignalName.TransactionFailed, buyer.Name, itemName, $"Item '{itemName}' not found in equipment database.");
            return false;
        }

        // Check item is stocked by this shop.
        var shopItem = FindShopItem(shop, itemName);
        if (shopItem == null)
        {
            EmitSignal(SignalName.TransactionFailed, buyer.Name, itemName, $"'{itemName}' is not sold at {shop.ShopName}.");
            return false;
        }

        // Check stock.
        if (shopItem.Quantity >= 0 && shopItem.Quantity < quantity)
        {
            EmitSignal(SignalName.TransactionFailed, buyer.Name, itemName,
                $"Not enough stock. {shop.ShopName} has {shopItem.Quantity} '{itemName}' remaining.");
            return false;
        }

        // Check gold.
        int unitPrice = GetBuyPrice(shopId, itemName);
        int totalCost = unitPrice * quantity;

        if (buyer.Gold < totalCost)
        {
            EmitSignal(SignalName.TransactionFailed, buyer.Name, itemName,
                $"Not enough gold. Need {totalCost} gp, have {buyer.Gold} gp.");
            return false;
        }

        // Execute transaction.
        buyer.Gold -= totalCost;

        // Reduce shop stock (skip for unlimited stock, quantity == -1).
        if (shopItem.Quantity >= 0)
            shopItem.Quantity -= quantity;

        // Add to buyer's inventory — stack if already present and not equippable.
        AddItemToInventory(buyer, template, quantity);

        EmitSignal(SignalName.ItemPurchased, buyer.Name, itemName, quantity, totalCost);
        return true;
    }

    /// <summary>
    /// Sells an item from the character's inventory to the active shop (or a specified shop).
    /// Adds gold to the seller and removes the item from their inventory.
    /// Returns true on success.
    /// </summary>
    public bool SellItem(Character seller, string itemName, int quantity = 1, string shopId = null)
    {
        shopId ??= ActiveShop?.ShopId;
        if (string.IsNullOrEmpty(shopId))
        {
            EmitSignal(SignalName.TransactionFailed, seller?.Name ?? "", itemName, "No shop is open.");
            return false;
        }

        if (seller == null)
        {
            EmitSignal(SignalName.TransactionFailed, "", itemName, "No seller specified.");
            return false;
        }

        if (quantity <= 0)
        {
            EmitSignal(SignalName.TransactionFailed, seller.Name, itemName, "Invalid quantity.");
            return false;
        }

        // Find the item in the seller's inventory.
        var invItem = seller.InventoryItems.Find(
            i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));

        if (invItem == null || invItem.Quantity < quantity)
        {
            int have = invItem?.Quantity ?? 0;
            EmitSignal(SignalName.TransactionFailed, seller.Name, itemName,
                $"Not enough '{itemName}' to sell. Have {have}, want to sell {quantity}.");
            return false;
        }

        // Calculate gold received.
        int sellPrice = GetSellPrice(shopId, itemName);
        int goldReceived = sellPrice * quantity;

        // Execute transaction.
        seller.Gold += goldReceived;

        // Remove from inventory.
        invItem.Quantity -= quantity;
        if (invItem.Quantity <= 0)
            seller.InventoryItems.Remove(invItem);

        // Add stock back to the shop if it tracks quantity.
        if (_shops.TryGetValue(shopId, out var shop))
        {
            var shopItem = FindShopItem(shop, itemName);
            if (shopItem != null && shopItem.Quantity >= 0)
            {
                shopItem.Quantity += quantity;
            }
            else if (shopItem == null)
            {
                // Shop didn't previously stock this item — add it.
                shop.Items.Add(new ShopItem
                {
                    ItemName = itemName,
                    Quantity = quantity,
                    ItemMarkup = 1.0f
                });
            }
        }

        EmitSignal(SignalName.ItemSold, seller.Name, itemName, quantity, goldReceived);
        return true;
    }

    // ── Item Comparison ───────────────────────────────────────────────

    /// <summary>
    /// Compares two items and returns a human-readable summary of the differences.
    /// Useful for the UI to show upgrade/downgrade indicators.
    /// </summary>
    public ItemComparison CompareItems(InventoryItem current, InventoryItem potential)
    {
        var result = new ItemComparison
        {
            CurrentName = current?.Name ?? "(empty)",
            PotentialName = potential?.Name ?? "(empty)"
        };

        if (current == null && potential == null)
            return result;

        // Compare weapon damage.
        string currentType = current?.Properties.GetValueOrDefault("type", "");
        string potentialType = potential?.Properties.GetValueOrDefault("type", "");

        if (potentialType == "Weapon")
        {
            string potentialDmg = potential.Properties.GetValueOrDefault("damage", "0");
            string currentDmg = current?.Properties.GetValueOrDefault("damage", "0") ?? "0";

            int potentialAvg = EstimateAverageDamage(potentialDmg);
            int currentAvg = EstimateAverageDamage(currentDmg);

            result.DamageDifference = potentialAvg - currentAvg;

            string potentialProps = potential.Properties.GetValueOrDefault("weaponProperties", "");
            string currentProps = current?.Properties.GetValueOrDefault("weaponProperties", "") ?? "";
            result.NewProperties = potentialProps;
            result.LostProperties = currentProps;
        }

        // Compare armor class.
        if (potentialType == "Armor")
        {
            int potentialAC = 0;
            int currentAC = 0;

            if (potential.Properties.TryGetValue("armorClass", out var pAC))
                int.TryParse(pAC, out potentialAC);

            if (current?.Properties.TryGetValue("armorClass", out var cAC) == true)
                int.TryParse(cAC, out currentAC);

            result.ArmorClassDifference = potentialAC - currentAC;

            bool potentialStealth = potential.Properties.GetValueOrDefault("stealthDisadvantage", "false") == "true";
            bool currentStealth = current?.Properties.GetValueOrDefault("stealthDisadvantage", "false") == "true";

            if (potentialStealth && !currentStealth)
                result.StealthChange = -1; // Worse stealth.
            else if (!potentialStealth && currentStealth)
                result.StealthChange = 1;  // Better stealth.
        }

        // Weight comparison.
        result.WeightDifference = (potential?.Weight ?? 0f) - (current?.Weight ?? 0f);

        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Finds a ShopItem by item name within a shop's inventory (case-insensitive).
    /// </summary>
    private static ShopItem FindShopItem(ShopInventory shop, string itemName)
    {
        return shop.Items.Find(
            si => string.Equals(si.ItemName, itemName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds an item to a character's inventory. Stacks consumables and non-equippable
    /// items; equippable items get separate entries to support individual enchantments.
    /// </summary>
    private static void AddItemToInventory(Character character, InventoryItem template, int quantity)
    {
        // Stackable: consumables and non-equippable gear.
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
    /// Estimates average damage from a dice expression like "1d8", "2d6", etc.
    /// Returns the statistical average (rounded down) for comparison purposes.
    /// </summary>
    private static int EstimateAverageDamage(string diceExpr)
    {
        if (string.IsNullOrEmpty(diceExpr))
            return 0;

        // Handle "XdY+Z" format.
        int bonus = 0;
        string dicePart = diceExpr;

        int plusIndex = diceExpr.IndexOf('+');
        if (plusIndex >= 0)
        {
            int.TryParse(diceExpr[(plusIndex + 1)..], out bonus);
            dicePart = diceExpr[..plusIndex];
        }

        int dIndex = dicePart.IndexOf('d', StringComparison.OrdinalIgnoreCase);
        if (dIndex < 0)
        {
            int.TryParse(dicePart, out int flat);
            return flat + bonus;
        }

        if (!int.TryParse(dicePart[..dIndex], out int numDice))
            numDice = 1;

        if (!int.TryParse(dicePart[(dIndex + 1)..], out int dieSize))
            return bonus;

        // Average of XdY = X * (Y + 1) / 2.
        return (int)(numDice * (dieSize + 1.0) / 2.0) + bonus;
    }

    /// <summary>
    /// Resolves a Godot res:// path. Matches the pattern used by SRDDataLoader.
    /// </summary>
    private static string ResolveResPath(string path)
    {
        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Godot.ProjectSettings.GlobalizePath(path);
            }
            catch
            {
                return path.Replace("res://", "");
            }
        }
        return path;
    }
}

/// <summary>
/// Result of comparing two inventory items. Positive values mean the
/// potential item is better; negative values mean worse.
/// </summary>
public class ItemComparison
{
    public string CurrentName { get; set; } = string.Empty;
    public string PotentialName { get; set; } = string.Empty;

    /// <summary>
    /// Difference in estimated average damage (potential - current).
    /// Only meaningful for weapons.
    /// </summary>
    public int DamageDifference { get; set; }

    /// <summary>
    /// Difference in armor class (potential - current).
    /// Only meaningful for armor.
    /// </summary>
    public int ArmorClassDifference { get; set; }

    /// <summary>
    /// +1 = gained stealth, -1 = lost stealth, 0 = no change.
    /// </summary>
    public int StealthChange { get; set; }

    /// <summary>
    /// Weight difference in pounds (potential - current). Negative is lighter.
    /// </summary>
    public float WeightDifference { get; set; }

    /// <summary>
    /// Weapon properties on the potential item (e.g. "Finesse, Light").
    /// </summary>
    public string NewProperties { get; set; } = string.Empty;

    /// <summary>
    /// Weapon properties on the current item that would be lost.
    /// </summary>
    public string LostProperties { get; set; } = string.Empty;

    /// <summary>
    /// Returns a brief human-readable summary of the comparison.
    /// </summary>
    public string GetSummary()
    {
        var parts = new List<string>();

        if (DamageDifference > 0) parts.Add($"+{DamageDifference} avg damage");
        else if (DamageDifference < 0) parts.Add($"{DamageDifference} avg damage");

        if (ArmorClassDifference > 0) parts.Add($"+{ArmorClassDifference} AC");
        else if (ArmorClassDifference < 0) parts.Add($"{ArmorClassDifference} AC");

        if (StealthChange > 0) parts.Add("stealth improved");
        else if (StealthChange < 0) parts.Add("stealth disadvantage");

        if (Math.Abs(WeightDifference) > 0.1f)
        {
            string sign = WeightDifference > 0 ? "+" : "";
            parts.Add($"{sign}{WeightDifference:F1} lb");
        }

        return parts.Count > 0
            ? $"{PotentialName} vs {CurrentName}: {string.Join(", ", parts)}"
            : $"{PotentialName} vs {CurrentName}: equivalent";
    }
}
