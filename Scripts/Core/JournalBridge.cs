using System;
using System.Collections.Generic;
using Godot;
using ChroniclesUnbound.Exploration;
using ChroniclesUnbound.Journal;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Bridges the GDScript JournalUI with the C# JournalManager and other game
/// managers (QuestManager, NPCManager, ShopManager). Translates GDScript
/// signals into C# method calls and auto-creates journal entries in response
/// to game events.
///
/// Follows the same pattern as InventoryBridge: created as a child of Main,
/// initialized by SceneManager after the JournalUI scene is instanced.
/// </summary>
public partial class JournalBridge : Node
{
    public static JournalBridge? Instance { get; private set; }

    // -----------------------------------------------------------------
    // Dependencies
    // -----------------------------------------------------------------

    private Node? _ui;
    private JournalManager? _journalManager;

    // -----------------------------------------------------------------
    // State
    // -----------------------------------------------------------------

    /// <summary>
    /// Current category filter. Null means show all categories.
    /// </summary>
    private JournalCategory? _currentFilter;

    /// <summary>
    /// The game phase that was active before the journal was opened,
    /// so we can restore it when the journal closes.
    /// </summary>
    private GameStateManager.GamePhase _previousPhase = GameStateManager.GamePhase.Exploration;

    private bool _initialized;

    // -----------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[JournalBridge] Ready.");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    // -----------------------------------------------------------------
    // Initialization (called by SceneManager after JournalUI is loaded)
    // -----------------------------------------------------------------

    /// <summary>
    /// Wire the bridge to a JournalUI instance. Finds or creates the
    /// JournalManager, connects all cross-language signals, and refreshes
    /// the UI with current journal data.
    /// </summary>
    public void Initialize(Node journalUI)
    {
        if (_initialized)
        {
            GD.Print("[JournalBridge] Already initialized -- reconnecting UI.");
            _ui = journalUI;
            ConnectUISignals();
            RefreshAll();
            return;
        }

        if (journalUI == null)
        {
            GD.PrintErr("[JournalBridge] Initialize() received null journalUI -- aborting.");
            return;
        }

        _ui = journalUI;

        // --- JournalManager ---
        _journalManager = JournalManager.Instance;
        if (_journalManager == null)
        {
            _journalManager = new JournalManager();
            _journalManager.Name = "JournalManager";
            GetTree().Root.GetNode("Main").AddChild(_journalManager);
        }

        // --- Remember previous phase for close ---
        if (GameStateManager.Instance != null)
        {
            _previousPhase = GameStateManager.GamePhase.Exploration;
        }

        // --- Connect signals ---
        ConnectUISignals();
        ConnectManagerSignals();

        _initialized = true;
        GD.Print("[JournalBridge] Initialized.");

        // --- Initial UI refresh ---
        RefreshAll();
    }

    /// <summary>
    /// Allows SceneManager to inform us what phase was active before
    /// the Journal phase was entered, so we can restore it on close.
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
        if (_ui == null)
            return;

        // Disconnect first to avoid double-connections on re-initialization.
        TryDisconnect(_ui, "entry_selected");
        TryDisconnect(_ui, "filter_changed");
        TryDisconnect(_ui, "close_requested");
        TryDisconnect(_ui, "add_note_requested");

        _ui.Connect("entry_selected",
            Callable.From<int>(OnEntrySelected));
        _ui.Connect("filter_changed",
            Callable.From<string>(OnFilterChanged));
        _ui.Connect("close_requested",
            Callable.From(OnCloseRequested));
        _ui.Connect("add_note_requested",
            Callable.From<string, string>(OnAddNoteRequested));
    }

    private void ConnectManagerSignals()
    {
        // --- JournalManager signals ---
        if (_journalManager != null)
        {
            _journalManager.EntryAdded += OnJournalEntryAdded;
        }

        // --- QuestManager signals ---
        var questManager = QuestManager.Instance;
        if (questManager != null)
        {
            questManager.QuestAccepted += OnQuestAccepted;
            questManager.QuestCompleted += OnQuestCompleted;
            questManager.QuestFailed += OnQuestFailed;
            questManager.ObjectiveUpdated += OnQuestObjectiveUpdated;
        }
        else
        {
            GD.Print("[JournalBridge] QuestManager not available -- quest signals skipped.");
        }

        // --- NPCManager signals ---
        var npcManager = NPCManager.Instance;
        if (npcManager != null)
        {
            npcManager.DialogueReceived += OnNPCDialogueReceived;
        }
        else
        {
            GD.Print("[JournalBridge] NPCManager not available -- NPC signals skipped.");
        }

        // --- ShopManager signals ---
        var shopManager = ShopManager.Instance;
        if (shopManager != null)
        {
            shopManager.ItemPurchased += OnShopItemPurchased;
            shopManager.ItemSold += OnShopItemSold;
        }
        else
        {
            GD.Print("[JournalBridge] ShopManager not available -- shop signals skipped.");
        }

        // NOTE: TravelManager has no [Signal] declarations -- location change
        // journal entries could be added when TravelManager gains signals.
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
    // UI refresh methods
    // -----------------------------------------------------------------

    /// <summary>
    /// Refresh all journal UI panels at once.
    /// </summary>
    public void RefreshAll()
    {
        RefreshEntries();
        RefreshUnreadCounts();
    }

    /// <summary>
    /// Push the journal entry list to the JournalUI, respecting the
    /// current category filter. Entries are sorted newest-first by
    /// JournalManager.
    /// </summary>
    public void RefreshEntries()
    {
        if (_ui == null || _journalManager == null)
            return;

        var entries = _journalManager.GetEntries(_currentFilter);
        var entriesArray = new Godot.Collections.Array();

        foreach (var entry in entries)
        {
            entriesArray.Add(new Godot.Collections.Dictionary
            {
                { "id", entry.Id },
                { "category", entry.Category.ToString() },
                { "title", entry.Title },
                { "description", entry.Description },
                { "timestamp", !string.IsNullOrEmpty(entry.RealTimestamp) ? entry.RealTimestamp : entry.Timestamp },
                { "location_name", entry.LocationName ?? "" },
                { "is_read", entry.IsRead }
            });
        }

        _ui.Call("update_entries", entriesArray);
    }

    /// <summary>
    /// Push unread counts per category to the JournalUI for badge display.
    /// </summary>
    public void RefreshUnreadCounts()
    {
        if (_ui == null || _journalManager == null)
            return;

        var unreadCounts = _journalManager.GetUnreadCounts();
        var countsDict = new Godot.Collections.Dictionary();

        foreach (var kvp in unreadCounts)
        {
            countsDict[kvp.Key.ToString()] = kvp.Value;
        }

        // Also include total unread for convenience.
        countsDict["Total"] = _journalManager.GetTotalUnreadCount();

        _ui.Call("set_unread_counts", countsDict);
    }

    // -----------------------------------------------------------------
    // GDScript signal handlers (JournalUI)
    // -----------------------------------------------------------------

    /// <summary>
    /// Called when the player selects a journal entry in the list.
    /// Marks it as read and pushes the full entry details to the UI.
    /// </summary>
    private void OnEntrySelected(int entryId)
    {
        if (_journalManager == null || _ui == null)
            return;

        GD.Print($"[JournalBridge] Entry selected: id {entryId}");

        _journalManager.MarkAsRead(entryId);

        var entry = _journalManager.GetEntry(entryId);
        if (entry == null)
        {
            GD.PrintErr($"[JournalBridge] Entry {entryId} not found.");
            return;
        }

        var detailDict = new Godot.Collections.Dictionary
        {
            { "id", entry.Id },
            { "category", entry.Category.ToString() },
            { "title", entry.Title },
            { "description", entry.Description },
            { "timestamp", !string.IsNullOrEmpty(entry.RealTimestamp) ? entry.RealTimestamp : entry.Timestamp },
            { "location_name", entry.LocationName ?? "" },
            { "is_read", entry.IsRead },
            { "related_entity_id", entry.RelatedEntityId ?? "" }
        };

        var tagsArray = new Godot.Collections.Array();
        foreach (var tag in entry.Tags)
        {
            tagsArray.Add(tag);
        }
        detailDict["tags"] = tagsArray;

        _ui.Call("update_entry_details", detailDict);

        // Refresh unread counts since we just marked one as read.
        RefreshUnreadCounts();
    }

    /// <summary>
    /// Called when the player changes the category filter dropdown/tab.
    /// An empty string or "All" means no filter.
    /// </summary>
    private void OnFilterChanged(string category)
    {
        GD.Print($"[JournalBridge] Filter changed: '{category}'");

        if (string.IsNullOrEmpty(category) || string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            _currentFilter = null;
        }
        else if (Enum.TryParse<JournalCategory>(category, ignoreCase: true, out var parsed))
        {
            _currentFilter = parsed;
        }
        else
        {
            GD.PrintErr($"[JournalBridge] Unknown journal category filter: '{category}'");
            _currentFilter = null;
        }

        RefreshEntries();
    }

    /// <summary>
    /// Called when the player closes the journal (Close button or Escape key).
    /// Restores the previous game phase, which triggers SceneManager to pop.
    /// </summary>
    private void OnCloseRequested()
    {
        GD.Print("[JournalBridge] Close requested.");
        GameStateManager.Instance?.SetPhase(_previousPhase);
    }

    /// <summary>
    /// Called when the player submits a custom note via the journal UI.
    /// </summary>
    private void OnAddNoteRequested(string title, string text)
    {
        if (_journalManager == null)
            return;

        if (string.IsNullOrWhiteSpace(title))
        {
            GD.Print("[JournalBridge] Add note ignored -- empty title.");
            return;
        }

        GD.Print($"[JournalBridge] Adding player note: '{title}'");

        string? locationName = GetCurrentLocationName();
        _journalManager.AddEntry(JournalCategory.Note, title, text, locationName: locationName);
    }

    // -----------------------------------------------------------------
    // JournalManager signal handler
    // -----------------------------------------------------------------

    /// <summary>
    /// Called whenever a new journal entry is added (from any source).
    /// Refreshes the UI so the new entry appears immediately.
    /// </summary>
    private void OnJournalEntryAdded(int entryId)
    {
        RefreshAll();
    }

    // -----------------------------------------------------------------
    // QuestManager signal handlers — auto-create Quest journal entries
    // -----------------------------------------------------------------

    private void OnQuestAccepted(string questId)
    {
        if (_journalManager == null)
            return;

        var quest = QuestManager.Instance?.GetQuest(questId);
        string questTitle = quest?.Title ?? questId;
        string questDesc = quest?.Description ?? "A new quest has been accepted.";
        string? locationName = GetCurrentLocationName();

        _journalManager.AddEntry(
            JournalCategory.Quest,
            $"Quest Accepted: {questTitle}",
            questDesc,
            relatedEntityId: questId,
            locationName: locationName,
            tags: new List<string> { "accepted" });
    }

    private void OnQuestCompleted(string questId)
    {
        if (_journalManager == null)
            return;

        var quest = QuestManager.Instance?.GetQuest(questId);
        string questTitle = quest?.Title ?? questId;
        string? locationName = GetCurrentLocationName();

        _journalManager.AddEntry(
            JournalCategory.Quest,
            $"Quest Completed: {questTitle}",
            $"The quest \"{questTitle}\" has been completed.",
            relatedEntityId: questId,
            locationName: locationName,
            tags: new List<string> { "completed" });
    }

    private void OnQuestFailed(string questId)
    {
        if (_journalManager == null)
            return;

        var quest = QuestManager.Instance?.GetQuest(questId);
        string questTitle = quest?.Title ?? questId;
        string? locationName = GetCurrentLocationName();

        _journalManager.AddEntry(
            JournalCategory.Quest,
            $"Quest Failed: {questTitle}",
            $"The quest \"{questTitle}\" has been failed.",
            relatedEntityId: questId,
            locationName: locationName,
            tags: new List<string> { "failed" });
    }

    private void OnQuestObjectiveUpdated(string questId, string objectiveId, int currentProgress, int requiredCount)
    {
        if (_journalManager == null)
            return;

        var quest = QuestManager.Instance?.GetQuest(questId);
        string questTitle = quest?.Title ?? questId;
        string? locationName = GetCurrentLocationName();

        _journalManager.AddEntry(
            JournalCategory.Quest,
            $"Objective Updated: {questTitle}",
            $"Progress on \"{questTitle}\": {currentProgress}/{requiredCount} (objective: {objectiveId}).",
            relatedEntityId: questId,
            locationName: locationName,
            tags: new List<string> { "objective_updated" });
    }

    // -----------------------------------------------------------------
    // NPCManager signal handlers — auto-create NPC journal entries
    // -----------------------------------------------------------------

    private void OnNPCDialogueReceived(string npcId, string speakerName, string responseText)
    {
        if (_journalManager == null)
            return;

        // Truncate long dialogue for the journal description.
        string description = responseText.Length > 300
            ? responseText[..300] + "..."
            : responseText;

        string? locationName = GetCurrentLocationName();

        _journalManager.AddEntry(
            JournalCategory.NPC,
            $"Spoke with {speakerName}",
            description,
            relatedEntityId: npcId,
            locationName: locationName,
            tags: new List<string> { "dialogue" });
    }

    // -----------------------------------------------------------------
    // ShopManager signal handlers — auto-create Trade journal entries
    // -----------------------------------------------------------------

    private void OnShopItemPurchased(string buyerName, string itemName, int quantity, int totalCost)
    {
        if (_journalManager == null)
            return;

        string desc = quantity > 1
            ? $"{buyerName} purchased {quantity}x {itemName} for {totalCost} gold."
            : $"{buyerName} purchased {itemName} for {totalCost} gold.";

        string? locationName = GetCurrentLocationName();

        _journalManager.AddEntry(
            JournalCategory.Trade,
            $"Purchased: {itemName}",
            desc,
            locationName: locationName,
            tags: new List<string> { "purchase" });
    }

    private void OnShopItemSold(string sellerName, string itemName, int quantity, int goldReceived)
    {
        if (_journalManager == null)
            return;

        string desc = quantity > 1
            ? $"{sellerName} sold {quantity}x {itemName} for {goldReceived} gold."
            : $"{sellerName} sold {itemName} for {goldReceived} gold.";

        string? locationName = GetCurrentLocationName();

        _journalManager.AddEntry(
            JournalCategory.Trade,
            $"Sold: {itemName}",
            desc,
            locationName: locationName,
            tags: new List<string> { "sale" });
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Gets the current location name from ExplorationManager, or null
    /// if not available.
    /// </summary>
    private string? GetCurrentLocationName()
    {
        var explorationManager = ExplorationManager.Instance;
        if (explorationManager == null)
            return null;

        string name = explorationManager.CurrentLocationName;
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
