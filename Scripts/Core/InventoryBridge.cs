using System;
using System.Collections.Generic;
using Godot;
using ChroniclesUnbound.Data;
using ChroniclesUnbound.Exploration;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Bridges the GDScript InventoryUI with the C# InventoryManager and character
/// data. Translates GDScript signals into C# method calls and pushes C# state
/// changes back to the UI via Node.Call().
///
/// Follows the same pattern as ExplorationBridge: created as a child of Main,
/// initialized by SceneManager after the InventoryUI scene is instanced.
/// </summary>
public partial class InventoryBridge : Node
{
    public static InventoryBridge? Instance { get; private set; }

    // -----------------------------------------------------------------
    // Dependencies
    // -----------------------------------------------------------------

    private Node? _inventoryUI;
    private InventoryManager? _inventoryManager;

    // -----------------------------------------------------------------
    // State
    // -----------------------------------------------------------------

    /// <summary>
    /// Index of the active character: 0 = player, 1+ = companion index.
    /// </summary>
    private int _activeCharacterIndex;

    /// <summary>
    /// The character currently being viewed in the inventory screen.
    /// </summary>
    private Character? _activeCharacter;

    /// <summary>
    /// The game phase that was active before inventory was opened,
    /// so we can restore it when the inventory closes.
    /// </summary>
    private GameStateManager.GamePhase _previousPhase = GameStateManager.GamePhase.Exploration;

    private bool _initialized;

    // -----------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[InventoryBridge] Ready.");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    // -----------------------------------------------------------------
    // Initialization (called by SceneManager after InventoryUI is loaded)
    // -----------------------------------------------------------------

    /// <summary>
    /// Wire the bridge to an InventoryUI instance. Finds or creates the
    /// InventoryManager, connects all cross-language signals, sets the
    /// active character to the player, and refreshes the UI.
    /// </summary>
    public void Initialize(Node inventoryUI)
    {
        if (_initialized)
        {
            GD.Print("[InventoryBridge] Already initialized -- reconnecting UI.");
            _inventoryUI = inventoryUI;
            ConnectUISignals();
            SetActiveCharacter(0);
            RefreshAll();
            return;
        }

        if (inventoryUI == null)
        {
            GD.PrintErr("[InventoryBridge] Initialize() received null inventoryUI -- aborting.");
            return;
        }

        _inventoryUI = inventoryUI;

        // --- InventoryManager ---
        _inventoryManager = InventoryManager.Instance;
        if (_inventoryManager == null)
        {
            _inventoryManager = new InventoryManager();
            _inventoryManager.Name = "InventoryManager";
            GetTree().Root.GetNode("Main").AddChild(_inventoryManager);
        }

        // --- Remember previous phase for close ---
        if (GameStateManager.Instance != null)
        {
            // The phase has already been set to Inventory by OnPhaseChanged,
            // but we captured the previous phase from the signal args in SceneManager.
            // As a fallback, default to Exploration.
            _previousPhase = GameStateManager.GamePhase.Exploration;
        }

        // --- Connect signals ---
        ConnectUISignals();
        ConnectManagerSignals();

        // --- Set active character to player ---
        SetActiveCharacter(0);

        _initialized = true;
        GD.Print("[InventoryBridge] Initialized.");

        // --- Initial UI refresh ---
        RefreshAll();
    }

    /// <summary>
    /// Allows SceneManager to inform us what phase was active before
    /// the Inventory phase was entered, so we can restore it on close.
    /// </summary>
    public void SetPreviousPhase(GameStateManager.GamePhase phase)
    {
        _previousPhase = phase;
    }

    // -----------------------------------------------------------------
    // Signal wiring
    // -----------------------------------------------------------------

    private void ConnectUISignals()
    {
        if (_inventoryUI == null)
            return;

        // Disconnect first to avoid double-connections on re-initialization.
        TryDisconnect(_inventoryUI, "item_selected");
        TryDisconnect(_inventoryUI, "equip_requested");
        TryDisconnect(_inventoryUI, "unequip_requested");
        TryDisconnect(_inventoryUI, "use_item_requested");
        TryDisconnect(_inventoryUI, "drop_item_requested");
        TryDisconnect(_inventoryUI, "attune_requested");
        TryDisconnect(_inventoryUI, "unattune_requested");
        TryDisconnect(_inventoryUI, "party_member_selected");
        TryDisconnect(_inventoryUI, "close_requested");

        _inventoryUI.Connect("item_selected",
            Callable.From<string>(OnItemSelected));
        _inventoryUI.Connect("equip_requested",
            Callable.From<string, string>(OnEquipRequested));
        _inventoryUI.Connect("unequip_requested",
            Callable.From<string>(OnUnequipRequested));
        _inventoryUI.Connect("use_item_requested",
            Callable.From<string>(OnUseItemRequested));
        _inventoryUI.Connect("drop_item_requested",
            Callable.From<string>(OnDropItemRequested));
        _inventoryUI.Connect("attune_requested",
            Callable.From<string>(OnAttuneRequested));
        _inventoryUI.Connect("unattune_requested",
            Callable.From<string>(OnUnattuneRequested));
        _inventoryUI.Connect("party_member_selected",
            Callable.From<int>(OnPartyMemberSelected));
        _inventoryUI.Connect("close_requested",
            Callable.From(OnCloseRequested));
    }

    private void ConnectManagerSignals()
    {
        if (_inventoryManager == null)
            return;

        _inventoryManager.ItemEquipped += OnInventoryItemEquipped;
        _inventoryManager.ItemUnequipped += OnInventoryItemUnequipped;
        _inventoryManager.ItemUsed += OnInventoryItemUsed;
        _inventoryManager.ItemDropped += OnInventoryItemDropped;
        _inventoryManager.InventoryChanged += OnInventoryChanged;
    }

    /// <summary>
    /// Safely disconnect a signal if it is currently connected to this bridge.
    /// </summary>
    private void TryDisconnect(Node source, string signalName)
    {
        if (!source.HasSignal(signalName))
            return;

        try
        {
            var connections = source.GetSignalConnectionList(signalName);
            foreach (var conn in connections)
            {
                if (conn.TryGetValue("callable", out var callableVariant))
                {
                    var callable = callableVariant.AsCallable();
                    if (callable.Target == this)
                    {
                        source.Disconnect(signalName, callable);
                    }
                }
            }
        }
        catch
        {
            // Signal was not connected -- that's fine.
        }
    }

    // -----------------------------------------------------------------
    // Active character management
    // -----------------------------------------------------------------

    /// <summary>
    /// Sets the active character by party index (0 = player, 1+ = companions).
    /// </summary>
    private void SetActiveCharacter(int index)
    {
        var gameLoop = GameStateManager.Instance?.ActiveGameLoop;
        if (gameLoop == null)
        {
            GD.PrintErr("[InventoryBridge] No active GameLoop -- cannot set active character.");
            return;
        }

        if (index == 0)
        {
            _activeCharacter = gameLoop.PlayerCharacter;
            _activeCharacterIndex = 0;
        }
        else
        {
            var companions = gameLoop.Companions;
            if (companions == null || index - 1 >= companions.Count)
            {
                GD.PrintErr($"[InventoryBridge] Invalid companion index: {index}");
                return;
            }
            _activeCharacter = companions[index - 1];
            _activeCharacterIndex = index;
        }
    }

    // -----------------------------------------------------------------
    // UI refresh methods
    // -----------------------------------------------------------------

    /// <summary>
    /// Refresh all UI panels at once.
    /// </summary>
    public void RefreshAll()
    {
        RefreshPartyTabs();
        RefreshEquipmentSlots();
        RefreshInventoryList();
        RefreshGoldWeight();
        RefreshAttunementCounter();
    }

    /// <summary>
    /// Push the equipment slot data to the InventoryUI.
    /// Iterates all EquipmentSlot values and builds an Array of Dict for each.
    /// </summary>
    public void RefreshEquipmentSlots()
    {
        if (_inventoryUI == null || _activeCharacter == null)
            return;

        var slotsArray = new Godot.Collections.Array();

        foreach (var slot in EquipmentSlotResolver.AllSlots)
        {
            string slotKey = EquipmentSlotResolver.SlotToString(slot);
            string displayName = EquipmentSlotResolver.GetSlotDisplayName(slot);
            string itemName = "";
            bool isEmpty = true;

            if (_activeCharacter.EquippedItems.TryGetValue(slotKey, out string? equippedName)
                && !string.IsNullOrEmpty(equippedName))
            {
                itemName = equippedName;
                isEmpty = false;
            }

            slotsArray.Add(new Godot.Collections.Dictionary
            {
                { "slot", slotKey },
                { "display_name", displayName },
                { "item_name", itemName },
                { "is_empty", isEmpty }
            });
        }

        _inventoryUI.Call("update_equipment_slots", slotsArray);
    }

    /// <summary>
    /// Push the inventory item list to the InventoryUI.
    /// Converts the character's InventoryItems to an Array of Dict.
    /// </summary>
    public void RefreshInventoryList()
    {
        if (_inventoryUI == null || _activeCharacter == null)
            return;

        var itemsArray = new Godot.Collections.Array();

        foreach (var item in _activeCharacter.InventoryItems)
        {
            itemsArray.Add(new Godot.Collections.Dictionary
            {
                { "name", item.Name },
                { "quantity", item.Quantity },
                { "weight", item.Weight },
                { "rarity", item.Rarity.ToString() },
                { "is_equippable", item.IsEquippable },
                { "is_consumable", item.IsConsumable }
            });
        }

        _inventoryUI.Call("update_inventory_list", itemsArray);
    }

    /// <summary>
    /// Push the gold and weight/capacity data to the InventoryUI.
    /// </summary>
    public void RefreshGoldWeight()
    {
        if (_inventoryUI == null || _activeCharacter == null || _inventoryManager == null)
            return;

        int gold = _activeCharacter.Gold;
        float weight = _inventoryManager.GetCurrentWeight(_activeCharacter);
        float capacity = _inventoryManager.GetCarryCapacity(_activeCharacter);

        _inventoryUI.Call("update_gold_weight", gold, weight, capacity);
    }

    /// <summary>
    /// Push the attunement slot counter to the InventoryUI.
    /// Shows how many of the 3 attunement slots are used.
    /// </summary>
    public void RefreshAttunementCounter()
    {
        if (_inventoryUI == null || _activeCharacter == null)
            return;

        int used = AttunementManager.GetAttunedCount(_activeCharacter);
        _inventoryUI.Call("update_attunement_counter", used, AttunementManager.MaxAttunementSlots);
    }

    /// <summary>
    /// Build the party tabs from GameLoop data and push to the InventoryUI.
    /// </summary>
    public void RefreshPartyTabs()
    {
        if (_inventoryUI == null)
            return;

        var partyArray = new Godot.Collections.Array();

        var gameLoop = GameStateManager.Instance?.ActiveGameLoop;
        if (gameLoop == null)
        {
            _inventoryUI.Call("update_party_tabs", partyArray, _activeCharacterIndex);
            return;
        }

        // Player character (index 0).
        var player = gameLoop.PlayerCharacter;
        if (player != null)
        {
            partyArray.Add(new Godot.Collections.Dictionary
            {
                { "name", player.Name },
                { "class_name", player.CharacterClassName },
                { "level", player.Level }
            });
        }

        // Companions (index 1+).
        var companions = gameLoop.Companions;
        if (companions != null)
        {
            foreach (var companion in companions)
            {
                partyArray.Add(new Godot.Collections.Dictionary
                {
                    { "name", companion.Name },
                    { "class_name", companion.CharacterClassName },
                    { "level", companion.Level }
                });
            }
        }

        _inventoryUI.Call("update_party_tabs", partyArray, _activeCharacterIndex);
    }

    // -----------------------------------------------------------------
    // GDScript signal handlers
    // -----------------------------------------------------------------

    /// <summary>
    /// Called when the player clicks an item in the inventory list or an
    /// equipped item slot. Builds a details dictionary and pushes it to
    /// the UI. If the item is equippable, also builds a stat preview.
    /// </summary>
    private void OnItemSelected(string itemName)
    {
        if (_inventoryUI == null || _activeCharacter == null)
            return;

        GD.Print($"[InventoryBridge] Item selected: {itemName}");

        // Find the item — check inventory first, then equipped items via InventoryManager.
        InventoryItem? item = FindItem(itemName);
        if (item == null)
        {
            GD.Print($"[InventoryBridge] Could not find item definition for '{itemName}'.");
            _inventoryUI.Call("clear_item_details");
            return;
        }

        // Check if this item is currently equipped (and in which slot).
        string equippedSlot = "";
        bool isEquipped = false;
        foreach (var kvp in _activeCharacter.EquippedItems)
        {
            if (string.Equals(kvp.Value, itemName, StringComparison.OrdinalIgnoreCase))
            {
                equippedSlot = kvp.Key;
                isEquipped = true;
                break;
            }
        }

        // Build the item details dictionary.
        string itemType = item.Properties.GetValueOrDefault("type", "");
        string damage = item.Properties.GetValueOrDefault("damage", "");
        string armorClass = item.Properties.GetValueOrDefault("armorClass", "");
        string weaponProps = item.Properties.GetValueOrDefault("weaponProperties", "");
        int cost = 0;
        if (item.Properties.TryGetValue("cost", out string? costStr))
            int.TryParse(costStr, out cost);

        // Attunement state.
        bool requiresAttunement = AttunementManager.RequiresAttunement(item);
        bool isAttuned = AttunementManager.IsAttuned(_activeCharacter, item.Name);
        bool canAttune = AttunementManager.CanAttune(_activeCharacter, item);
        string attunementRestriction = AttunementManager.GetAttunementRestriction(item);

        var detailsDict = new Godot.Collections.Dictionary
        {
            { "name", item.Name },
            { "type", itemType },
            { "rarity", item.Rarity.ToString() },
            { "description", item.Description },
            { "damage", damage },
            { "armor_class", armorClass },
            { "properties", weaponProps },
            { "weight", item.Weight },
            { "cost", cost },
            { "is_equippable", item.IsEquippable },
            { "is_consumable", item.IsConsumable },
            { "is_equipped", isEquipped },
            { "equipped_slot", equippedSlot },
            { "requires_attunement", requiresAttunement },
            { "is_attuned", isAttuned },
            { "can_attune", canAttune },
            { "attunement_restriction", attunementRestriction }
        };

        _inventoryUI.Call("update_item_details", detailsDict);

        // Build stat preview if the item is equippable and not currently equipped.
        if (item.IsEquippable && !isEquipped)
        {
            BuildAndPushStatPreview(item);
        }
        else
        {
            // Clear any existing stat preview.
            _inventoryUI.Call("update_stats_preview", new Godot.Collections.Dictionary());
        }
    }

    /// <summary>
    /// Called when the player clicks Equip on an item. If the slot string is
    /// empty, determine the valid slots. If multiple slots are valid, show
    /// the slot selection popup. Otherwise equip directly.
    /// </summary>
    private void OnEquipRequested(string itemName, string slot)
    {
        if (_activeCharacter == null || _inventoryManager == null || _inventoryUI == null)
            return;

        GD.Print($"[InventoryBridge] Equip requested: {itemName} -> slot '{slot}'");

        InventoryItem? item = FindItem(itemName);
        if (item == null)
        {
            GD.PrintErr($"[InventoryBridge] Cannot equip — item '{itemName}' not found.");
            return;
        }

        // If a specific slot was provided, equip directly.
        if (!string.IsNullOrEmpty(slot))
        {
            var parsedSlot = EquipmentSlotResolver.StringToSlot(slot);
            if (parsedSlot == null)
            {
                GD.PrintErr($"[InventoryBridge] Unknown equipment slot: '{slot}'");
                return;
            }

            bool success = _inventoryManager.EquipItem(_activeCharacter, item, parsedSlot.Value);
            if (success)
            {
                GD.Print($"[InventoryBridge] Equipped '{itemName}' in {slot}.");
            }
            RefreshAll();
            return;
        }

        // No slot specified — determine valid slots.
        var validSlots = EquipmentSlotResolver.GetValidSlots(item);
        if (validSlots.Count == 0)
        {
            GD.Print($"[InventoryBridge] No valid slots for '{itemName}'.");
            return;
        }

        if (validSlots.Count == 1)
        {
            // Single valid slot — equip directly.
            bool success = _inventoryManager.EquipItem(_activeCharacter, item, validSlots[0]);
            if (success)
            {
                GD.Print($"[InventoryBridge] Equipped '{itemName}' in {validSlots[0]}.");
            }
            RefreshAll();
            return;
        }

        // Multiple valid slots — show slot selection popup in the UI.
        var slotsArray = new Godot.Collections.Array();
        foreach (var s in validSlots)
        {
            slotsArray.Add(EquipmentSlotResolver.SlotToString(s));
        }
        _inventoryUI.Call("show_slot_selection", slotsArray);
    }

    /// <summary>
    /// Called when the player clicks Unequip on an equipped item.
    /// </summary>
    private void OnUnequipRequested(string slot)
    {
        if (_activeCharacter == null || _inventoryManager == null)
            return;

        GD.Print($"[InventoryBridge] Unequip requested: slot '{slot}'");

        var parsedSlot = EquipmentSlotResolver.StringToSlot(slot);
        if (parsedSlot == null)
        {
            GD.PrintErr($"[InventoryBridge] Unknown equipment slot: '{slot}'");
            return;
        }

        var unequipped = _inventoryManager.UnequipItem(_activeCharacter, parsedSlot.Value);
        if (unequipped != null)
        {
            GD.Print($"[InventoryBridge] Unequipped '{unequipped.Name}' from {slot}.");
        }

        RefreshAll();
        _inventoryUI?.Call("clear_item_details");
    }

    /// <summary>
    /// Called when the player clicks Use on a consumable item.
    /// </summary>
    private void OnUseItemRequested(string itemName)
    {
        if (_activeCharacter == null || _inventoryManager == null || _inventoryUI == null)
            return;

        GD.Print($"[InventoryBridge] Use item requested: {itemName}");

        InventoryItem? item = FindItem(itemName);
        if (item == null)
        {
            GD.PrintErr($"[InventoryBridge] Cannot use — item '{itemName}' not found.");
            return;
        }

        string? description = _inventoryManager.UseItem(_activeCharacter, item);
        if (description != null)
        {
            GD.Print($"[InventoryBridge] Used item: {description}");
        }

        RefreshAll();
        _inventoryUI.Call("clear_item_details");
    }

    /// <summary>
    /// Called when the player clicks Drop on an item.
    /// </summary>
    private void OnDropItemRequested(string itemName)
    {
        if (_activeCharacter == null || _inventoryManager == null || _inventoryUI == null)
            return;

        GD.Print($"[InventoryBridge] Drop item requested: {itemName}");

        InventoryItem? item = FindItem(itemName);
        if (item == null)
        {
            GD.PrintErr($"[InventoryBridge] Cannot drop — item '{itemName}' not found.");
            return;
        }

        bool success = _inventoryManager.DropItem(_activeCharacter, item, 1);
        if (success)
        {
            GD.Print($"[InventoryBridge] Dropped '{itemName}'.");
        }

        RefreshAll();
        _inventoryUI.Call("clear_item_details");
    }

    /// <summary>
    /// Called when the player clicks Attune on an item that requires attunement.
    /// </summary>
    private void OnAttuneRequested(string itemName)
    {
        if (_activeCharacter == null || _inventoryUI == null)
            return;

        GD.Print($"[InventoryBridge] Attune requested: {itemName}");

        InventoryItem? item = FindItem(itemName);
        if (item == null)
        {
            GD.PrintErr($"[InventoryBridge] Cannot attune -- item '{itemName}' not found.");
            return;
        }

        bool success = AttunementManager.Attune(_activeCharacter, item);
        if (success)
        {
            GD.Print($"[InventoryBridge] {_activeCharacter.Name} attuned to '{itemName}'.");
        }
        else
        {
            GD.Print($"[InventoryBridge] Cannot attune '{itemName}' -- requirements not met or slots full.");
        }

        RefreshAttunementCounter();
        // Re-select the item to update the details panel with new attunement state.
        OnItemSelected(itemName);
    }

    /// <summary>
    /// Called when the player clicks Unattune on a currently attuned item.
    /// </summary>
    private void OnUnattuneRequested(string itemName)
    {
        if (_activeCharacter == null || _inventoryUI == null)
            return;

        GD.Print($"[InventoryBridge] Unattune requested: {itemName}");

        bool success = AttunementManager.Unattune(_activeCharacter, itemName);
        if (success)
        {
            GD.Print($"[InventoryBridge] {_activeCharacter.Name} unattuned from '{itemName}'.");
        }

        RefreshAttunementCounter();
        // Re-select the item to update the details panel.
        OnItemSelected(itemName);
    }

    /// <summary>
    /// Called when the player clicks a party member tab to switch characters.
    /// </summary>
    private void OnPartyMemberSelected(int index)
    {
        GD.Print($"[InventoryBridge] Party member selected: index {index}");

        SetActiveCharacter(index);
        RefreshAll();
        _inventoryUI?.Call("clear_item_details");
    }

    /// <summary>
    /// Called when the player closes the inventory (Close button or Escape key).
    /// Restores the previous game phase, which triggers SceneManager to pop.
    /// </summary>
    private void OnCloseRequested()
    {
        GD.Print("[InventoryBridge] Close requested.");

        GameStateManager.Instance?.SetPhase(_previousPhase);
    }

    // -----------------------------------------------------------------
    // InventoryManager signal handlers (C# events)
    // -----------------------------------------------------------------

    private void OnInventoryItemEquipped(string characterName, string itemName, string slot)
    {
        GD.Print($"[InventoryBridge] Event: {characterName} equipped {itemName} in {slot}.");
    }

    private void OnInventoryItemUnequipped(string characterName, string itemName, string slot)
    {
        GD.Print($"[InventoryBridge] Event: {characterName} unequipped {itemName} from {slot}.");
    }

    private void OnInventoryItemUsed(string characterName, string itemName, string description)
    {
        GD.Print($"[InventoryBridge] Event: {characterName} used {itemName} — {description}");
    }

    private void OnInventoryItemDropped(string characterName, string itemName, int quantity)
    {
        GD.Print($"[InventoryBridge] Event: {characterName} dropped {quantity}x {itemName}.");
    }

    private void OnInventoryChanged(string characterName)
    {
        // Only refresh if the changed character is the one currently being viewed.
        if (_activeCharacter != null
            && string.Equals(_activeCharacter.Name, characterName, StringComparison.OrdinalIgnoreCase))
        {
            RefreshInventoryList();
            RefreshEquipmentSlots();
            RefreshGoldWeight();
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Finds an item definition by name. Checks the active character's inventory
    /// first, then falls back to ShopManager's equipment database (for items
    /// that are currently equipped and therefore not in inventory).
    /// </summary>
    private InventoryItem? FindItem(string itemName)
    {
        if (_activeCharacter == null || string.IsNullOrEmpty(itemName))
            return null;

        // Check character's inventory.
        var invItem = _activeCharacter.InventoryItems.Find(
            i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase));
        if (invItem != null)
            return invItem;

        // Fall back to equipment database (for equipped items not in inventory).
        if (ShopManager.Instance != null)
            return ShopManager.Instance.GetEquipmentTemplate(itemName);

        return null;
    }

    /// <summary>
    /// Builds a stat preview dictionary comparing current stats to what
    /// they would be if the given item were equipped, and pushes it to the UI.
    /// Currently compares AC and main-hand damage.
    /// </summary>
    private void BuildAndPushStatPreview(InventoryItem item)
    {
        if (_inventoryUI == null || _activeCharacter == null || _inventoryManager == null)
            return;

        var preview = new Godot.Collections.Dictionary();

        int currentAC = _activeCharacter.ArmorClass;
        string currentDamage = GetMainHandDamage();

        // Determine what slot this item would go into (use first valid slot).
        var validSlots = EquipmentSlotResolver.GetValidSlots(item);
        if (validSlots.Count == 0)
        {
            _inventoryUI.Call("update_stats_preview", preview);
            return;
        }

        string itemType = item.Properties.GetValueOrDefault("type", "");

        // AC preview: relevant for Armor and Shield items.
        if (string.Equals(itemType, "Armor", StringComparison.OrdinalIgnoreCase))
        {
            // Simulate AC change by temporarily equipping (rough estimate).
            int newAC = EstimateACWithItem(item);
            if (newAC != currentAC)
            {
                preview["current_ac"] = currentAC;
                preview["new_ac"] = newAC;
            }
        }

        // Damage preview: relevant for Weapon items.
        if (string.Equals(itemType, "Weapon", StringComparison.OrdinalIgnoreCase))
        {
            string newDamage = item.Properties.GetValueOrDefault("damage", "");
            if (!string.IsNullOrEmpty(newDamage) && newDamage != currentDamage)
            {
                preview["current_damage"] = currentDamage;
                preview["new_damage"] = newDamage;
            }
        }

        _inventoryUI.Call("update_stats_preview", preview);
    }

    /// <summary>
    /// Gets the damage string of the currently equipped main-hand weapon,
    /// or empty string if nothing is equipped.
    /// </summary>
    private string GetMainHandDamage()
    {
        if (_activeCharacter == null || _inventoryManager == null)
            return "";

        var mainHand = _inventoryManager.GetEquippedItem(_activeCharacter, EquipmentSlot.MainHand);
        if (mainHand == null)
            return "";

        return mainHand.Properties.GetValueOrDefault("damage", "");
    }

    /// <summary>
    /// Estimates the AC the character would have if they equipped the given
    /// armor or shield item. This is a rough preview; it does not actually
    /// modify character state.
    /// </summary>
    private int EstimateACWithItem(InventoryItem item)
    {
        if (_activeCharacter == null)
            return 0;

        int dexMod = _activeCharacter.AbilityScores.GetModifier(Ability.Dexterity);
        string category = item.Properties.GetValueOrDefault("category", "");
        int armorAC = 0;
        if (item.Properties.TryGetValue("armorClass", out string? acStr))
            int.TryParse(acStr, out armorAC);

        int baseAC;

        if (string.Equals(category, "Shield", StringComparison.OrdinalIgnoreCase))
        {
            // Shield — add shield bonus to current AC (minus existing shield if any).
            int shieldBonus = armorAC > 0 ? armorAC : 2;
            baseAC = _activeCharacter.ArmorClass;

            // Check if there's already a shield equipped.
            var currentOffHand = _inventoryManager?.GetEquippedItem(_activeCharacter, EquipmentSlot.OffHand);
            if (currentOffHand != null)
            {
                string offHandType = currentOffHand.Properties.GetValueOrDefault("type", "");
                string offHandCategory = currentOffHand.Properties.GetValueOrDefault("category", "");
                if (string.Equals(offHandType, "Armor", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(offHandCategory, "Shield", StringComparison.OrdinalIgnoreCase))
                {
                    int existingShieldBonus = 2;
                    if (currentOffHand.Properties.TryGetValue("armorClass", out string? existingAcStr))
                        int.TryParse(existingAcStr, out existingShieldBonus);
                    baseAC = baseAC - existingShieldBonus + shieldBonus;
                    return baseAC;
                }
            }

            return baseAC + shieldBonus;
        }

        // Body armor — recalculate from scratch.
        switch (category.ToLowerInvariant())
        {
            case "light":
                baseAC = armorAC + dexMod;
                break;
            case "medium":
                baseAC = armorAC + Math.Min(dexMod, 2);
                break;
            case "heavy":
                baseAC = armorAC;
                break;
            default:
                baseAC = 10 + dexMod;
                break;
        }

        // Add existing shield bonus if one is equipped.
        var offHand = _inventoryManager?.GetEquippedItem(_activeCharacter, EquipmentSlot.OffHand);
        if (offHand != null)
        {
            string ohType = offHand.Properties.GetValueOrDefault("type", "");
            string ohCategory = offHand.Properties.GetValueOrDefault("category", "");
            if (string.Equals(ohType, "Armor", StringComparison.OrdinalIgnoreCase)
                && string.Equals(ohCategory, "Shield", StringComparison.OrdinalIgnoreCase))
            {
                int shieldBonus = 2;
                if (offHand.Properties.TryGetValue("armorClass", out string? shieldAcStr))
                    int.TryParse(shieldAcStr, out shieldBonus);
                baseAC += shieldBonus;
            }
        }

        return baseAC;
    }
}
