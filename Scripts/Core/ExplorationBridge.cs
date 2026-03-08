using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ChroniclesUnbound.Data;
using ChroniclesUnbound.Exploration;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Bridges the GDScript ExplorationUI with the C# exploration subsystems
/// (ExplorationManager, WorldMap, TravelManager, NPCManager). Translates
/// GDScript signals into C# method calls and pushes C# state changes back
/// to the UI via Node.Call().
///
/// Follows the same pattern as GameLoop's NarrativeUI bridge: created as a
/// child of Main, initialized by SceneManager after the ExplorationUI scene
/// is instanced.
/// </summary>
public partial class ExplorationBridge : Node
{
    public static ExplorationBridge? Instance { get; private set; }

    // -----------------------------------------------------------------
    // Dependencies
    // -----------------------------------------------------------------

    private Node? _explorationUI;
    private ExplorationManager? _explorationManager;
    private WorldMap? _worldMap;
    private TravelManager? _travelManager;
    private NPCManager? _npcManager;
    private ShopManager? _shopManager;
    private QuestManager? _questManager;

    private EncounterGenerator? _encounterGenerator;

    private bool _initialized;
    private bool _isTraveling;
    private bool _isDialogueProcessing;

    /// <summary>
    /// The encounter data from the most recent combat trigger, held until
    /// CombatBridge is initialized and ready to start the fight.
    /// </summary>
    private EncounterData? _pendingEncounterData;

    /// <summary>
    /// The destination the party was traveling to when combat interrupted.
    /// Used to resume travel after combat ends successfully.
    /// </summary>
    private string? _interruptedTravelDestinationId;

    private const string LocationsJsonPath = "res://Data/World/locations.json";
    private const string MonstersJsonPath = "res://Data/SRD/monsters.json";
    private const string EncounterTablesJsonPath = "res://Data/World/encounter_tables.json";
    private const string NPCsJsonPath = "res://Data/World/npcs.json";
    private const string ShopsJsonPath = "res://Data/World/shops.json";
    private const string QuestsJsonPath = "res://Data/World/quests.json";
    private const string EquipmentJsonPath = "res://Data/SRD/equipment.json";

    // -----------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[ExplorationBridge] Ready.");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    // -----------------------------------------------------------------
    // Initialization (called by SceneManager after ExplorationUI is loaded)
    // -----------------------------------------------------------------

    /// <summary>
    /// Wire the bridge to an ExplorationUI instance. Creates or finds the
    /// ExplorationManager and WorldMap, connects all cross-language signals,
    /// sets the starting location, and refreshes the UI.
    /// </summary>
    public void Initialize(Node explorationUI)
    {
        if (_initialized)
        {
            GD.Print("[ExplorationBridge] Already initialized — reconnecting UI.");
            _explorationUI = explorationUI;
            ConnectUISignals();
            RefreshAll();
            return;
        }

        if (explorationUI == null)
        {
            GD.PrintErr("[ExplorationBridge] Initialize() received null explorationUI — aborting.");
            return;
        }

        _explorationUI = explorationUI;

        // --- ExplorationManager ---
        _explorationManager = ExplorationManager.Instance;
        if (_explorationManager == null)
        {
            _explorationManager = new ExplorationManager();
            _explorationManager.Name = "ExplorationManager";
            GetTree().Root.GetNode("Main").AddChild(_explorationManager);
        }

        // --- WorldMap ---
        _worldMap = new WorldMap();
        try
        {
            _worldMap.LoadLocations(LocationsJsonPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationBridge] Failed to load locations: {ex.Message}");
        }

        // --- TravelManager ---
        _travelManager = CreateTravelManager();

        // --- NPCManager ---
        _npcManager = NPCManager.Instance;
        if (_npcManager == null)
        {
            _npcManager = new NPCManager();
            _npcManager.Name = "NPCManager";
            GetTree().Root.GetNode("Main").AddChild(_npcManager);
        }

        try
        {
            _npcManager.LoadNPCs(NPCsJsonPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationBridge] Failed to load NPCs: {ex.Message}");
        }

        // --- ShopManager ---
        _shopManager = ShopManager.Instance;
        if (_shopManager == null)
        {
            _shopManager = new ShopManager();
            _shopManager.Name = "ShopManager";
            GetTree().Root.GetNode("Main").AddChild(_shopManager);
        }

        try
        {
            _shopManager.LoadEquipmentDatabase(EquipmentJsonPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationBridge] Failed to load equipment database: {ex.Message}");
        }

        try
        {
            _shopManager.LoadShops(ShopsJsonPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationBridge] Failed to load shops: {ex.Message}");
        }

        // --- QuestManager ---
        _questManager = QuestManager.Instance;
        if (_questManager == null)
        {
            _questManager = new QuestManager();
            _questManager.Name = "QuestManager";
            GetTree().Root.GetNode("Main").AddChild(_questManager);
        }

        try
        {
            _questManager.LoadQuests(QuestsJsonPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationBridge] Failed to load quests: {ex.Message}");
        }

        // --- Connect signals ---
        ConnectUISignals();
        ConnectManagerSignals();

        // --- Connect auto-save triggers ---
        SaveLoadBridge.Instance?.ConnectAutoSaveSignals(_explorationManager);

        // --- Set starting location ---
        SetStartingLocation();

        _initialized = true;
        GD.Print("[ExplorationBridge] Initialized.");

        // --- Initial UI refresh ---
        RefreshAll();
    }

    // -----------------------------------------------------------------
    // Signal wiring
    // -----------------------------------------------------------------

    private void ConnectUISignals()
    {
        if (_explorationUI == null)
            return;

        // Disconnect first to avoid double-connections on re-initialization.
        TryDisconnect(_explorationUI, "action_selected");
        TryDisconnect(_explorationUI, "travel_selected");
        TryDisconnect(_explorationUI, "npc_selected");
        TryDisconnect(_explorationUI, "dialogue_submitted");
        TryDisconnect(_explorationUI, "dialogue_end_requested");
        TryDisconnect(_explorationUI, "quest_accept_requested");
        TryDisconnect(_explorationUI, "shop_buy_requested");
        TryDisconnect(_explorationUI, "shop_sell_requested");
        TryDisconnect(_explorationUI, "shop_close_requested");
        TryDisconnect(_explorationUI, "save_requested");

        _explorationUI.Connect("action_selected",
            Callable.From<string, Godot.Collections.Dictionary>(OnActionSelected));
        _explorationUI.Connect("travel_selected",
            Callable.From<string>(OnTravelSelected));
        _explorationUI.Connect("npc_selected",
            Callable.From<string>(OnNPCSelected));
        _explorationUI.Connect("dialogue_submitted",
            Callable.From<string>(OnDialogueSubmitted));
        _explorationUI.Connect("dialogue_end_requested",
            Callable.From(OnDialogueEndRequested));
        _explorationUI.Connect("quest_accept_requested",
            Callable.From<string>(OnQuestAcceptRequested));
        _explorationUI.Connect("shop_buy_requested",
            Callable.From<string, int>(OnShopBuyRequested));
        _explorationUI.Connect("shop_sell_requested",
            Callable.From<string, int>(OnShopSellRequested));
        _explorationUI.Connect("shop_close_requested",
            Callable.From(OnShopCloseRequested));
        _explorationUI.Connect("save_requested",
            Callable.From(OnSaveRequested));
    }

    private void ConnectManagerSignals()
    {
        if (_explorationManager == null)
            return;

        _explorationManager.StateChanged += OnStateChanged;
        _explorationManager.LocationChanged += OnLocationChanged;
        _explorationManager.CombatTriggered += OnCombatTriggered;

        if (_npcManager != null)
        {
            _npcManager.DialogueReceived += OnNPCDialogueReceived;
            _npcManager.QuestDialogueDetected += OnQuestDialogueDetected;
            _npcManager.ShopDialogueDetected += OnShopDialogueDetected;
        }

        if (_shopManager != null)
        {
            _shopManager.ItemPurchased += OnShopItemPurchased;
            _shopManager.ItemSold += OnShopItemSold;
            _shopManager.TransactionFailed += OnShopTransactionFailed;
        }

        if (_questManager != null)
        {
            _questManager.QuestAccepted += OnQuestAccepted;
            _questManager.QuestCompleted += OnQuestCompleted;
            _questManager.ObjectiveUpdated += OnQuestObjectiveUpdated;
        }
    }

    /// <summary>
    /// Safely disconnect a signal if it is currently connected to this bridge.
    /// Prevents errors when reconnecting after scene changes.
    /// </summary>
    private void TryDisconnect(Node source, string signalName)
    {
        if (!source.HasSignal(signalName))
            return;

        // Godot C# does not expose IsConnected for arbitrary callables easily,
        // so we use a try/catch to silently handle the case where we are not connected.
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
            // Signal was not connected — that's fine.
        }
    }

    // -----------------------------------------------------------------
    // Starting location
    // -----------------------------------------------------------------

    private void SetStartingLocation()
    {
        if (_explorationManager == null || _worldMap == null)
            return;

        // Use the first discovered location, defaulting to "millhaven".
        var startLocation = _worldMap.GetLocation("millhaven");
        if (startLocation == null)
        {
            // Fall back to the first location in the world map.
            var allLocations = _worldMap.GetAllLocations();
            if (allLocations.Count > 0)
                startLocation = allLocations[0];
        }

        if (startLocation == null)
        {
            GD.PrintErr("[ExplorationBridge] No locations available in WorldMap.");
            return;
        }

        // Mark the starting location as discovered.
        _worldMap.DiscoverLocation(startLocation.Id);

        // Set the ExplorationManager's current location (sync, no LLM description).
        _ = _explorationManager.ChangeLocation(
            startLocation.Id,
            startLocation.Name,
            startLocation.TypeString,
            generateDescription: false);

        // Use the JSON description as the initial description.
        // We access the private setter via the public property on ExplorationManager
        // by calling ChangeLocation which sets the name/type, then we'll push the
        // description from the Location data directly to the UI.
    }

    // -----------------------------------------------------------------
    // UI refresh methods
    // -----------------------------------------------------------------

    /// <summary>
    /// Refresh all UI panels at once. Call after initialization or major state changes.
    /// </summary>
    public void RefreshAll()
    {
        RefreshLocationDisplay();
        RefreshActionButtons();
        RefreshConnectedLocations();
        RefreshPartyPanel();
    }

    /// <summary>
    /// Push the current location's name, type, danger level, and description
    /// to the ExplorationUI.
    /// </summary>
    public void RefreshLocationDisplay()
    {
        if (_explorationUI == null || _explorationManager == null || _worldMap == null)
            return;

        var location = _worldMap.GetLocation(_explorationManager.CurrentLocationId);
        if (location == null)
            return;

        // ExplorationUI.update_location(name, type, danger_level, description)
        string description = !string.IsNullOrWhiteSpace(_explorationManager.CurrentLocationDescription)
            ? _explorationManager.CurrentLocationDescription
            : location.Description;

        _explorationUI.Call("update_location",
            location.Name,
            location.TypeString,
            location.DangerLevel,
            description);
    }

    /// <summary>
    /// Query available actions from ExplorationManager and push them to the UI
    /// as an Array of Dictionaries matching the expected format:
    /// {id: String, display_text: String, category: String}
    /// </summary>
    public void RefreshActionButtons()
    {
        if (_explorationUI == null || _explorationManager == null)
            return;

        var actions = _explorationManager.GetAvailableActions();
        var actionsArray = new Godot.Collections.Array();

        foreach (var action in actions)
        {
            var dict = new Godot.Collections.Dictionary
            {
                { "id", action.Id },
                { "display_text", action.DisplayText },
                { "category", action.Category }
            };
            actionsArray.Add(dict);
        }

        _explorationUI.Call("update_actions", actionsArray);
    }

    /// <summary>
    /// Query connected discovered locations from the WorldMap and push them to
    /// the UI as an Array of Dictionaries:
    /// {id: String, name: String, type: String, distance: int}
    /// </summary>
    public void RefreshConnectedLocations()
    {
        if (_explorationUI == null || _explorationManager == null || _worldMap == null)
            return;

        string currentId = _explorationManager.CurrentLocationId;
        if (string.IsNullOrEmpty(currentId))
            return;

        var connected = _worldMap.GetConnectedLocations(currentId);
        var locationsArray = new Godot.Collections.Array();

        foreach (var loc in connected)
        {
            int distance = _worldMap.CalculateTravelDistance(currentId, loc.Id);
            if (distance < 0) distance = 1; // Direct connection, at least 1 hop.

            var dict = new Godot.Collections.Dictionary
            {
                { "id", loc.Id },
                { "name", loc.Name },
                { "type", loc.TypeString },
                { "distance", distance }
            };
            locationsArray.Add(dict);
        }

        _explorationUI.Call("update_connected_locations", locationsArray);
    }

    /// <summary>
    /// Pull character data from GameLoop and push party info to the UI as an
    /// Array of Dictionaries:
    /// {name: String, class_name: String, level: int, hp: int, max_hp: int}
    /// </summary>
    public void RefreshPartyPanel()
    {
        if (_explorationUI == null)
            return;

        var partyArray = new Godot.Collections.Array();

        var gameLoop = GameStateManager.Instance?.ActiveGameLoop;
        if (gameLoop == null || gameLoop.PlayerCharacter == null || gameLoop.Companions == null)
        {
            _explorationUI.Call("update_party", partyArray);
            return;
        }

        // Player character first (slot 0).
        var player = gameLoop.PlayerCharacter;
        if (player != null)
        {
            partyArray.Add(new Godot.Collections.Dictionary
            {
                { "name", player.Name },
                { "class_name", player.CharacterClassName },
                { "level", player.Level },
                { "hp", player.HitPoints },
                { "max_hp", player.MaxHitPoints }
            });
        }

        // Companions.
        var companions = gameLoop.Companions;
        if (companions != null)
        {
            foreach (var companion in companions)
            {
                partyArray.Add(new Godot.Collections.Dictionary
                {
                    { "name", companion.Name },
                    { "class_name", companion.CharacterClassName },
                    { "level", companion.Level },
                    { "hp", companion.HitPoints },
                    { "max_hp", companion.MaxHitPoints }
                });
            }
        }

        _explorationUI.Call("update_party", partyArray);
    }

    // -----------------------------------------------------------------
    // GDScript signal handlers
    // -----------------------------------------------------------------

    /// <summary>
    /// Called when the player presses an action button in ExplorationUI.
    /// Routes the action to ExplorationManager.ProcessPlayerAction().
    /// </summary>
    private async void OnActionSelected(string actionType, Godot.Collections.Dictionary actionData)
    {
        if (_explorationManager == null)
            return;

        GD.Print($"[ExplorationBridge] Action selected: {actionType}");

        try
        {
            // Intercept inventory to open the inventory overlay.
            if (actionType == "inventory")
            {
                GameStateManager.Instance?.SetPhase(GameStateManager.GamePhase.Inventory);
                return;
            }

            // Intercept journal to open the journal overlay.
            if (actionType == "journal")
            {
                GameStateManager.Instance?.SetPhase(GameStateManager.GamePhase.Journal);
                return;
            }

            // Intercept talk_npc to show the NPC selection list directly.
            if (actionType == "talk_npc")
            {
                ShowNPCListForCurrentLocation();
                return;
            }

            // Intercept visit_shop to open the shop at the current location.
            if (actionType == "visit_shop")
            {
                OpenShopAtCurrentLocation();
                return;
            }

            // Handle shop selection from the multi-shop list.
            if (actionType.StartsWith("open_shop_"))
            {
                string shopId = actionType.Substring("open_shop_".Length);
                OpenShopPanel(shopId);
                return;
            }

            // Handle "Go back" from shop selection list.
            if (actionType == "shop_go_back")
            {
                _explorationManager?.ReturnToExploring();
                RefreshActionButtons();
                return;
            }

            // Map action button IDs to action strings the ExplorationManager understands.
            string actionString = MapActionIdToString(actionType);
            bool processed = await _explorationManager.ProcessPlayerAction(actionString);

            if (!processed)
            {
                GD.Print($"[ExplorationBridge] Action '{actionType}' was not processed.");
            }

            // Refresh actions after processing — state may have changed.
            RefreshActionButtons();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationBridge] Error processing action '{actionType}': {ex.Message}");
        }
    }

    /// <summary>
    /// Called when the player selects a travel destination.
    /// Validates the route, runs travel via TravelManager, processes events,
    /// and either arrives at the destination or triggers combat if interrupted.
    /// </summary>
    private async void OnTravelSelected(string locationId)
    {
        GD.Print($"[ExplorationBridge] Travel selected: {locationId}");

        if (_explorationUI == null || _explorationManager == null || _worldMap == null)
            return;

        // Guard against double-travel
        if (_isTraveling)
        {
            GD.Print("[ExplorationBridge] Already traveling, ignoring.");
            return;
        }

        string currentId = _explorationManager.CurrentLocationId;
        var destination = _worldMap.GetLocation(locationId);
        string destName = destination?.Name ?? locationId;

        // Check if TravelManager exists
        if (_travelManager == null)
        {
            _explorationUI.Call("add_narrative_text",
                $"\n[color=#b8b0a0][i]Travel system is not available. Cannot travel to {destName}.[/i][/color]\n");
            return;
        }

        // Validate the route
        if (!_travelManager.CanTravel(currentId, locationId))
        {
            _explorationUI.Call("add_narrative_text",
                $"\n[color=#b8b0a0][i]There is no known path to {destName}, or the destination has not been discovered yet.[/i][/color]\n");
            return;
        }

        _isTraveling = true;

        try
        {
            // Transition to Traveling state
            _explorationManager.TransitionTo(ExplorationState.Traveling);

            // Show traveling status
            _explorationUI.Call("set_status", $"Traveling to {destName}...");

            // Build the party list for encounter scaling
            var party = BuildPartyList();

            // Execute travel
            var result = await _travelManager.BeginTravel(currentId, locationId, party);

            // Display the narrative description
            if (!string.IsNullOrWhiteSpace(result.NarrativeDescription))
            {
                _explorationUI.Call("add_narrative_text",
                    $"\n[color=#c9a227][b]Traveling to {destName}[/b][/color]\n" +
                    $"[color=#eee8d5]{result.NarrativeDescription}[/color]\n");
            }

            // Display individual travel events (non-nothing, non-encounter)
            foreach (var evt in result.Events)
            {
                if (evt.Type == TravelEventType.Nothing)
                    continue;

                // Encounters are handled separately below
                if (evt.Type == TravelEventType.RandomEncounter)
                    continue;

                if (!string.IsNullOrWhiteSpace(evt.Description))
                {
                    string eventColor = evt.Type == TravelEventType.Discovery
                        ? "#c9a227"
                        : "#b8b0a0";
                    _explorationUI.Call("add_narrative_text",
                        $"[color={eventColor}][i]{evt.Description}[/i][/color]\n");
                }

                // If a location was discovered, refresh connected locations
                if (evt.Type == TravelEventType.Discovery &&
                    !string.IsNullOrWhiteSpace(evt.DiscoveredLocationId))
                {
                    var discovered = _worldMap.GetLocation(evt.DiscoveredLocationId);
                    if (discovered != null)
                    {
                        _explorationUI.Call("add_narrative_text",
                            $"[color=#c9a227][b]New location discovered: {discovered.Name}[/b][/color]\n");
                    }
                }
            }

            // Handle the outcome
            if (result.WasInterrupted)
            {
                // Combat encounter — find the encounter event
                EncounterData? encounterData = null;
                foreach (var evt in result.Events)
                {
                    if (evt.Type == TravelEventType.RandomEncounter && evt.EncounterData != null)
                    {
                        encounterData = evt.EncounterData;
                        if (!string.IsNullOrWhiteSpace(evt.Description))
                        {
                            _explorationUI.Call("add_narrative_text",
                                $"\n[color=#e74c3c][b]{evt.Description}[/b][/color]\n");
                        }
                        break;
                    }
                }

                _explorationUI.Call("set_status", "");

                // Trigger combat via ExplorationManager
                // Serialize encounter data as JSON for the combat signal
                string encounterJson = encounterData != null
                    ? Newtonsoft.Json.JsonConvert.SerializeObject(encounterData)
                    : "{}";
                _explorationManager.TriggerCombat(encounterJson);

                GD.Print("[ExplorationBridge] Travel interrupted by combat encounter.");
            }
            else
            {
                // Safe arrival — change location
                if (destination != null)
                {
                    _worldMap.DiscoverLocation(locationId);

                    await _explorationManager.ChangeLocation(
                        destination.Id,
                        destination.Name,
                        destination.TypeString,
                        generateDescription: true);
                }

                // Return to Exploring state
                _explorationManager.TransitionTo(ExplorationState.Exploring);

                _explorationUI.Call("set_status", "");
                _explorationUI.Call("add_narrative_text",
                    $"\n[color=#c9a227][b]Arrived at {destName}.[/b][/color]\n");

                // Full refresh to show new location data
                RefreshAll();
            }
        }
        catch (OperationCanceledException)
        {
            GD.Print("[ExplorationBridge] Travel was cancelled.");
            _explorationManager.TransitionTo(ExplorationState.Exploring);
            _explorationUI.Call("set_status", "");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationBridge] Error during travel to '{locationId}': {ex.Message}");
            _explorationUI.Call("add_narrative_text",
                $"\n[color=#e74c3c][i]An error occurred during travel: {ex.Message}[/i][/color]\n");
            _explorationUI.Call("set_status", "");

            // Return to Exploring state on error so the player is not stuck
            if (_explorationManager.CurrentState == ExplorationState.Traveling)
                _explorationManager.TransitionTo(ExplorationState.Exploring);
        }
        finally
        {
            _isTraveling = false;
        }
    }

    /// <summary>
    /// Called when the player selects an NPC from the list to talk to.
    /// Transitions to InDialogue state, begins a dialogue session via NPCManager,
    /// and shows the dialogue panel in the UI with the NPC's greeting.
    /// </summary>
    private void OnNPCSelected(string npcId)
    {
        GD.Print($"[ExplorationBridge] NPC selected: {npcId}");

        if (_explorationUI == null || _explorationManager == null)
            return;

        if (_npcManager == null)
        {
            _explorationUI.Call("add_narrative_text",
                "\n[color=#b8b0a0][i]NPC system is not available.[/i][/color]\n");
            RefreshActionButtons();
            return;
        }

        // Transition to InDialogue state
        if (!_explorationManager.StartDialogue(npcId))
        {
            GD.PrintErr($"[ExplorationBridge] Failed to transition to InDialogue for NPC '{npcId}'.");
            RefreshActionButtons();
            return;
        }

        // Begin dialogue session with NPCManager
        var dialogueResult = _npcManager.BeginDialogue(npcId);
        if (dialogueResult == null)
        {
            GD.PrintErr($"[ExplorationBridge] NPCManager.BeginDialogue returned null for '{npcId}'.");
            _explorationManager.ReturnToExploring();
            RefreshActionButtons();
            return;
        }

        // Show dialogue panel in the UI
        var npc = _npcManager.GetNPC(npcId);
        string npcName = npc?.DisplayName ?? dialogueResult.SpeakerName;
        string npcRole = npc?.Role.ToString() ?? "Unknown";

        _explorationUI.Call("show_dialogue_panel", npcName, npcRole, dialogueResult.ResponseText);
    }

    // -----------------------------------------------------------------
    // Dialogue signal handlers (ExplorationUI)
    // -----------------------------------------------------------------

    /// <summary>
    /// Called when the player submits text in the dialogue input field.
    /// Sends the text to NPCManager for LLM-powered (or fallback) response
    /// and displays the result in the UI.
    /// </summary>
    private async void OnDialogueSubmitted(string text)
    {
        if (_explorationUI == null || _npcManager == null)
            return;

        if (string.IsNullOrWhiteSpace(text))
            return;

        var activeNpc = _npcManager.ActiveNPC;
        if (activeNpc == null)
        {
            GD.PrintErr("[ExplorationBridge] OnDialogueSubmitted: No active NPC dialogue session.");
            return;
        }

        // Guard against double-submission while awaiting LLM
        if (_isDialogueProcessing)
        {
            GD.Print("[ExplorationBridge] Already processing dialogue, ignoring.");
            return;
        }

        _isDialogueProcessing = true;

        try
        {
            // Show player's message in the narrative
            _explorationUI.Call("update_dialogue_response", "You", text);

            // Show thinking indicator
            _explorationUI.Call("set_status", "DM is thinking...");

            // Send to NPCManager (async — may call LLM)
            var result = await _npcManager.TalkToNPC(activeNpc.NpcId, text);

            // Clear status
            _explorationUI.Call("set_status", "");

            // Display NPC response
            _explorationUI.Call("update_dialogue_response", result.SpeakerName, result.ResponseText);

            // Handle special intents from the response
            HandleDialogueIntent(activeNpc.NpcId, result);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationBridge] Error during dialogue: {ex.Message}");
            _explorationUI.Call("set_status", "");
            _explorationUI.Call("update_dialogue_response", "System",
                "Something went wrong. The NPC stares at you blankly.");
        }
        finally
        {
            _isDialogueProcessing = false;
        }
    }

    /// <summary>
    /// Called when the player clicks "Say goodbye" in the dialogue panel.
    /// Ends the dialogue session and returns to Exploring state.
    /// </summary>
    private void OnDialogueEndRequested()
    {
        GD.Print("[ExplorationBridge] Dialogue end requested.");

        if (_npcManager != null)
            _npcManager.EndDialogue();

        if (_explorationUI != null)
            _explorationUI.Call("hide_dialogue_panel");

        if (_explorationManager != null)
            _explorationManager.ReturnToExploring();

        RefreshActionButtons();
    }

    /// <summary>
    /// Called when the player accepts a quest offered during dialogue.
    /// Routes to QuestManager.AcceptQuest() and shows a notification on success.
    /// </summary>
    private void OnQuestAcceptRequested(string questId)
    {
        GD.Print($"[ExplorationBridge] Quest accept requested: {questId}");

        if (_questManager == null)
        {
            GD.PrintErr("[ExplorationBridge] QuestManager not available — cannot accept quest.");
            _explorationUI?.Call("show_quest_notification", $"Quest system unavailable.");
            return;
        }

        bool accepted = _questManager.AcceptQuest(questId);
        if (!accepted)
        {
            // AcceptQuest logs the reason internally. Show a generic message to the player.
            var quest = _questManager.GetQuest(questId);
            if (quest != null && quest.Status != QuestStatus.Available)
            {
                _explorationUI?.Call("show_quest_notification",
                    $"Quest \"{quest.Title}\" is already {quest.Status}.");
            }
            else
            {
                _explorationUI?.Call("show_quest_notification",
                    "Cannot accept this quest right now.");
            }
        }
        // Success notification is handled by the OnQuestAccepted signal handler.
    }

    // -----------------------------------------------------------------
    // NPCManager signal handlers
    // -----------------------------------------------------------------

    /// <summary>
    /// Called when NPCManager emits DialogueReceived. This fires for every
    /// dialogue exchange and can be used for logging or additional narrative.
    /// We do not duplicate the UI update here since OnDialogueSubmitted
    /// already calls update_dialogue_response directly after TalkToNPC.
    /// </summary>
    private void OnNPCDialogueReceived(string npcId, string speakerName, string responseText)
    {
        // Intentionally minimal — the UI update is driven by OnDialogueSubmitted.
        // This handler exists for other systems (e.g. relationship tracking,
        // journal logging) that may want to observe all dialogue exchanges.
        GD.Print($"[ExplorationBridge] Dialogue received from {speakerName} ({npcId}).");
    }

    /// <summary>
    /// Called when NPCManager detects quest-related dialogue intent.
    /// Adds a quest notification to the narrative panel so the player
    /// knows a quest is available.
    /// </summary>
    private void OnQuestDialogueDetected(string npcId, string questId)
    {
        GD.Print($"[ExplorationBridge] Quest dialogue detected: NPC={npcId}, Quest={questId}");

        if (_explorationUI != null)
        {
            _explorationUI.Call("show_quest_notification",
                $"A quest is available from this NPC. (Quest: {questId})");
        }
    }

    /// <summary>
    /// Called when NPCManager detects shop-related dialogue intent.
    /// Transitions from InDialogue to InShop state via ExplorationManager,
    /// opens the shop in ShopManager, and displays the shop panel in the UI.
    /// </summary>
    private void OnShopDialogueDetected(string npcId, string shopId)
    {
        GD.Print($"[ExplorationBridge] Shop dialogue detected: NPC={npcId}, Shop={shopId}");

        if (_explorationManager == null)
            return;

        // Close dialogue panel first
        _explorationUI?.Call("hide_dialogue_panel");

        // End the NPC dialogue session
        _npcManager?.EndDialogue();

        // Transition InDialogue -> InShop
        _explorationManager.OpenShop(shopId);

        // Open shop and display panel
        OpenShopPanel(shopId);
    }

    // -----------------------------------------------------------------
    // Shop signal handlers (ExplorationUI)
    // -----------------------------------------------------------------

    /// <summary>
    /// Called when the player clicks Buy on a shop item.
    /// Routes to ShopManager.BuyItem() with the player character.
    /// </summary>
    private void OnShopBuyRequested(string itemName, int quantity)
    {
        GD.Print($"[ExplorationBridge] Shop buy requested: {itemName} x{quantity}");

        if (_shopManager == null || _explorationUI == null)
            return;

        if (GameStateManager.Instance == null || GameStateManager.Instance.ActiveGameLoop == null)
        {
            GD.PrintErr("[ExplorationBridge] Cannot buy — GameStateManager or ActiveGameLoop is null.");
            return;
        }

        var player = GameStateManager.Instance.ActiveGameLoop.PlayerCharacter;
        if (player == null)
        {
            GD.PrintErr("[ExplorationBridge] Cannot buy — no player character.");
            return;
        }

        bool success = _shopManager.BuyItem(player, itemName, quantity);
        if (success)
        {
            // Update gold display and refresh the shop panel to reflect stock changes
            _explorationUI.Call("update_shop_gold", player.Gold);
            RefreshShopPanel();
        }
        // Failure notification is handled by the OnShopTransactionFailed signal handler.
    }

    /// <summary>
    /// Called when the player clicks Sell on a shop item.
    /// Routes to ShopManager.SellItem() with the player character.
    /// </summary>
    private void OnShopSellRequested(string itemName, int quantity)
    {
        GD.Print($"[ExplorationBridge] Shop sell requested: {itemName} x{quantity}");

        if (_shopManager == null || _explorationUI == null)
            return;

        if (GameStateManager.Instance == null || GameStateManager.Instance.ActiveGameLoop == null)
        {
            GD.PrintErr("[ExplorationBridge] Cannot sell — GameStateManager or ActiveGameLoop is null.");
            return;
        }

        var player = GameStateManager.Instance.ActiveGameLoop.PlayerCharacter;
        if (player == null)
        {
            GD.PrintErr("[ExplorationBridge] Cannot sell — no player character.");
            return;
        }

        bool success = _shopManager.SellItem(player, itemName, quantity);
        if (success)
        {
            _explorationUI.Call("update_shop_gold", player.Gold);
            RefreshShopPanel();
        }
        // Failure notification is handled by the OnShopTransactionFailed signal handler.
    }

    /// <summary>
    /// Called when the player clicks "Leave Shop" in the shop panel.
    /// Closes the shop, hides the shop panel, and returns to Exploring state.
    /// </summary>
    private void OnShopCloseRequested()
    {
        GD.Print("[ExplorationBridge] Shop close requested.");

        _shopManager?.CloseShop();

        _explorationUI?.Call("hide_shop_panel");

        _explorationManager?.ReturnToExploring();

        RefreshActionButtons();
    }

    private void OnSaveRequested()
    {
        GD.Print("[ExplorationBridge] Save requested.");
        var bridge = SaveLoadBridge.Instance;
        if (bridge == null)
        {
            GD.PrintErr("[ExplorationBridge] SaveLoadBridge not available.");
            _explorationUI?.Call("set_status", "Save system not available.");
            return;
        }

        var gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            _explorationUI?.Call("set_status", "Cannot save — game state not initialized.");
            return;
        }

        int? slotId = gsm.ActiveSaveSlotId;
        int result;
        if (slotId.HasValue)
        {
            result = bridge.SaveGame(slotId.Value);
        }
        else
        {
            // First save for this session — create a new slot
            var gameLoop = gsm.ActiveGameLoop;
            string slotName = gameLoop?.PlayerCharacter?.Name ?? "Manual Save";
            result = bridge.SaveGameToNewSlot(slotName);
            if (result >= 0)
            {
                gsm.ActiveSaveSlotId = result;
            }
        }

        if (result >= 0)
        {
            _explorationUI?.Call("set_status", "Game saved.");
        }
        else
        {
            _explorationUI?.Call("set_status", "Save failed.");
        }
    }

    // -----------------------------------------------------------------
    // ShopManager signal handlers
    // -----------------------------------------------------------------

    /// <summary>
    /// Called when ShopManager confirms a successful purchase.
    /// Shows a narrative notification with the transaction details.
    /// </summary>
    private void OnShopItemPurchased(string buyerName, string itemName, int quantity, int totalCost)
    {
        GD.Print($"[ExplorationBridge] Item purchased: {buyerName} bought {quantity}x {itemName} for {totalCost} gp.");

        string quantityText = quantity > 1 ? $"{quantity}x " : "";
        _explorationUI?.Call("add_narrative_text",
            $"\n[color=#2ecc71][i]Purchased {quantityText}{itemName} for {totalCost} gp.[/i][/color]\n");
    }

    /// <summary>
    /// Called when ShopManager confirms a successful sale.
    /// Shows a narrative notification with the gold received.
    /// </summary>
    private void OnShopItemSold(string sellerName, string itemName, int quantity, int goldReceived)
    {
        GD.Print($"[ExplorationBridge] Item sold: {sellerName} sold {quantity}x {itemName} for {goldReceived} gp.");

        string quantityText = quantity > 1 ? $"{quantity}x " : "";
        _explorationUI?.Call("add_narrative_text",
            $"\n[color=#2ecc71][i]Sold {quantityText}{itemName} for {goldReceived} gp.[/i][/color]\n");
    }

    /// <summary>
    /// Called when a ShopManager transaction fails (insufficient gold, out of stock, etc.).
    /// Shows an error notification in the narrative panel.
    /// </summary>
    private void OnShopTransactionFailed(string characterName, string itemName, string reason)
    {
        GD.Print($"[ExplorationBridge] Transaction failed: {characterName}, {itemName} — {reason}");

        _explorationUI?.Call("add_narrative_text",
            $"\n[color=#e74c3c][i]{reason}[/i][/color]\n");
    }

    // -----------------------------------------------------------------
    // QuestManager signal handlers
    // -----------------------------------------------------------------

    /// <summary>
    /// Called when QuestManager confirms a quest has been accepted.
    /// Shows a quest notification with the quest title and description.
    /// </summary>
    private void OnQuestAccepted(string questId)
    {
        GD.Print($"[ExplorationBridge] Quest accepted: {questId}");

        if (_explorationUI == null || _questManager == null)
            return;

        var quest = _questManager.GetQuest(questId);
        if (quest == null)
            return;

        _explorationUI.Call("show_quest_notification",
            $"New Quest: {quest.Title}\n{quest.Description}");
    }

    /// <summary>
    /// Called when QuestManager confirms a quest has been completed.
    /// Shows a completion notification with reward summary.
    /// </summary>
    private void OnQuestCompleted(string questId)
    {
        GD.Print($"[ExplorationBridge] Quest completed: {questId}");

        if (_explorationUI == null || _questManager == null)
            return;

        var quest = _questManager.GetQuest(questId);
        if (quest == null)
            return;

        string rewardText = $"Quest Complete: {quest.Title}!";
        if (quest.XPReward > 0 || quest.GoldReward > 0)
        {
            var rewards = new List<string>();
            if (quest.XPReward > 0) rewards.Add($"{quest.XPReward} XP");
            if (quest.GoldReward > 0) rewards.Add($"{quest.GoldReward} gp");
            if (quest.ItemRewards.Count > 0) rewards.Add(string.Join(", ", quest.ItemRewards));
            rewardText += $"\nRewards: {string.Join(", ", rewards)}";
        }

        _explorationUI.Call("show_quest_notification", rewardText);
    }

    /// <summary>
    /// Called when a quest objective is updated (progress changed).
    /// Shows a progress notification in the narrative panel.
    /// </summary>
    private void OnQuestObjectiveUpdated(string questId, string objectiveId, int currentProgress, int requiredCount)
    {
        GD.Print($"[ExplorationBridge] Objective updated: {questId}/{objectiveId} ({currentProgress}/{requiredCount})");

        if (_explorationUI == null || _questManager == null)
            return;

        var quest = _questManager.GetQuest(questId);
        if (quest == null)
            return;

        var objective = quest.Objectives.Find(o => o.ObjectiveId == objectiveId);
        string objectiveDesc = objective?.Description ?? objectiveId;

        string progressText = currentProgress >= requiredCount
            ? $"{quest.Title}: {objectiveDesc} (Complete!)"
            : $"{quest.Title}: {objectiveDesc} ({currentProgress}/{requiredCount})";

        _explorationUI.Call("show_quest_notification", progressText);
    }

    // -----------------------------------------------------------------
    // Shop helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Opens a shop by ID: sets the active shop in ShopManager, builds the
    /// item array for the UI, and calls show_shop_panel().
    /// </summary>
    private void OpenShopPanel(string shopId)
    {
        if (_shopManager == null || _explorationUI == null)
        {
            GD.PrintErr("[ExplorationBridge] Cannot open shop — ShopManager or UI not available.");
            return;
        }

        _shopManager.OpenShop(shopId);
        var shop = _shopManager.ActiveShop;

        if (shop == null)
        {
            GD.PrintErr($"[ExplorationBridge] Shop '{shopId}' not found in ShopManager.");
            _explorationUI.Call("add_narrative_text",
                "\n[color=#e74c3c][i]This shop could not be found.[/i][/color]\n");
            _explorationManager?.ReturnToExploring();
            RefreshActionButtons();
            return;
        }

        // Build item array for UI consumption
        var itemsArray = BuildShopItemsArray(shop);

        // Get player gold
        int playerGold = GameStateManager.Instance?.ActiveGameLoop?.PlayerCharacter?.Gold ?? 0;

        _explorationUI.Call("show_shop_panel", shop.ShopName, itemsArray, playerGold);

        // Add narrative flavor text
        if (!string.IsNullOrWhiteSpace(shop.Description))
        {
            _explorationUI.Call("add_narrative_text",
                $"\n[color=#c9a227][b]{shop.ShopName}[/b][/color]\n" +
                $"[color=#eee8d5]{shop.Description}[/color]\n");
        }
    }

    /// <summary>
    /// Refreshes the shop panel with current inventory and gold.
    /// Called after buy/sell transactions to update stock and gold display.
    /// </summary>
    private void RefreshShopPanel()
    {
        if (_shopManager?.ActiveShop == null || _explorationUI == null)
            return;

        var shop = _shopManager.ActiveShop;
        var itemsArray = BuildShopItemsArray(shop);
        int playerGold = GameStateManager.Instance?.ActiveGameLoop?.PlayerCharacter?.Gold ?? 0;

        _explorationUI.Call("show_shop_panel", shop.ShopName, itemsArray, playerGold);
    }

    /// <summary>
    /// Converts a ShopInventory's items into an Array of Dictionary for the GDScript UI.
    /// Each dictionary contains: {name, price, quantity, description}
    /// </summary>
    private Godot.Collections.Array BuildShopItemsArray(ShopInventory shop)
    {
        var itemsArray = new Godot.Collections.Array();

        foreach (var shopItem in shop.Items)
        {
            // Skip out-of-stock items (quantity == 0)
            if (shopItem.Quantity == 0)
                continue;

            int buyPrice = _shopManager!.GetBuyPrice(shop.ShopId, shopItem.ItemName);
            string description = "";

            // Try to get the item description from the equipment database
            var template = _shopManager.GetEquipmentTemplate(shopItem.ItemName);
            if (template != null)
                description = template.Description;

            var dict = new Godot.Collections.Dictionary
            {
                { "name", shopItem.ItemName },
                { "price", buyPrice },
                { "quantity", shopItem.Quantity },
                { "description", description }
            };
            itemsArray.Add(dict);
        }

        return itemsArray;
    }

    /// <summary>
    /// Finds shops at the current location. If exactly one shop exists, opens it
    /// directly. If multiple shops exist, shows a selection list. If none exist,
    /// shows a narrative message.
    /// </summary>
    private void OpenShopAtCurrentLocation()
    {
        if (_shopManager == null || _explorationManager == null || _explorationUI == null)
            return;

        string locationId = _explorationManager.CurrentLocationId;
        var shops = _shopManager.GetShopsAtLocation(locationId);

        if (shops.Count == 0)
        {
            _explorationUI.Call("add_narrative_text",
                "\n[color=#b8b0a0][i]There are no shops at this location.[/i][/color]\n");
            return;
        }

        if (shops.Count == 1)
        {
            // Single shop — open it directly
            _explorationManager.OpenShop(shops[0].ShopId);
            OpenShopPanel(shops[0].ShopId);
            return;
        }

        // Multiple shops — show selection buttons
        // Transition to InShop state so the action buttons update
        _explorationManager.TransitionTo(ExplorationState.InShop);

        // Reuse the NPC list pattern: replace action buttons with shop selection
        _explorationUI.Call("add_narrative_text",
            "\n[color=#c9a227][b]Which shop would you like to visit?[/b][/color]\n");

        var actionsArray = new Godot.Collections.Array();
        foreach (var shop in shops)
        {
            actionsArray.Add(new Godot.Collections.Dictionary
            {
                { "id", $"open_shop_{shop.ShopId}" },
                { "display_text", $"{shop.ShopName} ({shop.ShopkeeperName})" },
                { "category", "social" }
            });
        }

        // Add a "Go back" button
        actionsArray.Add(new Godot.Collections.Dictionary
        {
            { "id", "shop_go_back" },
            { "display_text", "Go back" },
            { "category", "system" }
        });

        _explorationUI.Call("update_actions", actionsArray);
    }

    // -----------------------------------------------------------------
    // Dialogue helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Queries NPCManager for NPCs at the current location and shows
    /// them in the UI as a selectable list.
    /// </summary>
    private void ShowNPCListForCurrentLocation()
    {
        if (_explorationUI == null || _explorationManager == null)
            return;

        if (_npcManager == null)
        {
            _explorationUI.Call("add_narrative_text",
                "\n[color=#b8b0a0][i]NPC system is not available.[/i][/color]\n");
            return;
        }

        string locationId = _explorationManager.CurrentLocationId;
        var npcs = _npcManager.GetNPCsAtLocation(locationId);

        if (npcs.Count == 0)
        {
            _explorationUI.Call("add_narrative_text",
                "\n[color=#b8b0a0][i]There is no one here to talk to.[/i][/color]\n");
            return;
        }

        // Convert to Array of Dictionary for GDScript consumption
        var npcsArray = new Godot.Collections.Array();
        foreach (var npc in npcs)
        {
            npcsArray.Add(new Godot.Collections.Dictionary
            {
                { "id", npc.NpcId },
                { "name", npc.DisplayName },
                { "role", npc.Role.ToString() }
            });
        }

        _explorationUI.Call("show_npc_list", npcsArray);
    }

    /// <summary>
    /// Reacts to special dialogue intents after an NPC response is displayed.
    /// For example, a farewell intent auto-ends the dialogue session.
    /// </summary>
    private void HandleDialogueIntent(string npcId, NPCDialogueResult result)
    {
        switch (result.DetectedIntent)
        {
            case DialogueIntent.Farewell:
                // NPC said goodbye — auto-end the dialogue after a brief moment.
                // Use CallDeferred so the farewell text is visible before the panel closes.
                Callable.From(OnDialogueEndRequested).CallDeferred();
                break;

            case DialogueIntent.QuestOffer:
            case DialogueIntent.QuestInfo:
                // Quest notification is handled by the NPCManager signal
                // (OnQuestDialogueDetected) which fires from EmitDialogueSignals.
                break;

            case DialogueIntent.OpenShop:
                // Shop transition is handled by the NPCManager signal
                // (OnShopDialogueDetected) which fires from EmitDialogueSignals.
                break;
        }
    }

    // -----------------------------------------------------------------
    // C# signal handlers (ExplorationManager events)
    // -----------------------------------------------------------------

    /// <summary>
    /// Called when the ExplorationManager transitions between states.
    /// Refreshes actions since available actions are state-dependent.
    /// </summary>
    private void OnStateChanged(int previousState, int newState)
    {
        var prev = (ExplorationState)previousState;
        var next = (ExplorationState)newState;
        GD.Print($"[ExplorationBridge] State changed: {prev} -> {next}");

        RefreshActionButtons();

        // If returning to Exploring, do a full refresh.
        if (next == ExplorationState.Exploring)
        {
            RefreshAll();
        }
    }

    /// <summary>
    /// Called when the ExplorationManager's current location changes.
    /// Refreshes the location display, connected locations, and party panel.
    /// </summary>
    private void OnLocationChanged(string previousLocationId, string newLocationId)
    {
        GD.Print($"[ExplorationBridge] Location changed: {previousLocationId} -> {newLocationId}");

        // Discover the new location if it wasn't already.
        _worldMap?.DiscoverLocation(newLocationId);

        RefreshLocationDisplay();
        RefreshConnectedLocations();
        RefreshPartyPanel();
    }

    // -----------------------------------------------------------------
    // Combat initiation and resolution
    // -----------------------------------------------------------------

    /// <summary>
    /// Called when ExplorationManager emits CombatTriggered (from TriggerCombat()).
    /// Deserializes encounter data, stores it for the pending combat, and transitions
    /// to the Combat game phase. SceneManager will push CombatUI and call
    /// InitializeCombatBridge(), which triggers StartPendingCombat() via deferred call.
    /// </summary>
    private void OnCombatTriggered(string encounterJson)
    {
        GD.Print("[ExplorationBridge] CombatTriggered signal received.");

        // Deserialize encounter data
        try
        {
            _pendingEncounterData = Newtonsoft.Json.JsonConvert.DeserializeObject<EncounterData>(encounterJson);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationBridge] Failed to deserialize encounter data: {ex.Message}");
            _pendingEncounterData = null;
        }

        if (_pendingEncounterData == null || _pendingEncounterData.Monsters.Count == 0)
        {
            GD.PrintErr("[ExplorationBridge] No valid encounter data — aborting combat.");
            _explorationManager?.ReturnToExploring();
            RefreshAll();
            return;
        }

        // Transition to Combat phase. SceneManager.OnPhaseChanged will push CombatUI,
        // which calls InitializeCombatBridge -> CombatBridge.Initialize().
        // We subscribe to SceneManager.SceneChanged to start combat only after
        // the CombatUI scene is fully loaded and CombatBridge is initialized.
        var sceneManager = SceneManager.Instance;
        if (sceneManager != null)
        {
            void OnSceneChangedForCombat(string prev, string next)
            {
                if (next == SceneManager.Scenes.CombatUI)
                {
                    sceneManager.SceneChanged -= OnSceneChangedForCombat;
                    Callable.From(StartPendingCombat).CallDeferred();
                }
            }
            sceneManager.SceneChanged += OnSceneChangedForCombat;
        }

        GameStateManager.Instance?.SetPhase(GameStateManager.GamePhase.Combat);
    }

    /// <summary>
    /// Called deferred after Combat phase is set and CombatUI is pushed.
    /// Builds ally and enemy lists, then calls CombatBridge.StartCombat().
    /// </summary>
    private void StartPendingCombat()
    {
        if (_pendingEncounterData == null)
        {
            GD.PrintErr("[ExplorationBridge] StartPendingCombat: no pending encounter data.");
            return;
        }

        var combatBridge = CombatBridge.Instance;
        if (combatBridge == null)
        {
            GD.PrintErr("[ExplorationBridge] StartPendingCombat: CombatBridge not available.");
            _pendingEncounterData = null;
            _explorationManager?.ReturnToExploring();
            return;
        }

        // Build allies from the party
        var allies = BuildPartyList();
        if (allies.Count == 0)
        {
            GD.PrintErr("[ExplorationBridge] StartPendingCombat: no party members available.");
            _pendingEncounterData = null;
            GameStateManager.Instance?.SetPhase(GameStateManager.GamePhase.Exploration);
            return;
        }

        // Build enemies from encounter data via EncounterGenerator.CreateCombatants()
        List<Character> enemies;
        if (_encounterGenerator != null)
        {
            var combatants = _encounterGenerator.CreateCombatants(_pendingEncounterData);
            enemies = new List<Character>();
            foreach (var combatant in combatants)
            {
                enemies.Add(combatant.Character);
            }
        }
        else
        {
            GD.PrintErr("[ExplorationBridge] StartPendingCombat: EncounterGenerator not available.");
            _pendingEncounterData = null;
            GameStateManager.Instance?.SetPhase(GameStateManager.GamePhase.Exploration);
            return;
        }

        if (enemies.Count == 0)
        {
            GD.PrintErr("[ExplorationBridge] StartPendingCombat: no enemies generated.");
            _pendingEncounterData = null;
            GameStateManager.Instance?.SetPhase(GameStateManager.GamePhase.Exploration);
            return;
        }

        // Grid size scales with total combatant count for reasonable spacing
        int totalCombatants = allies.Count + enemies.Count;
        int gridSize = Math.Max(8, (int)Math.Ceiling(Math.Sqrt(totalCombatants * 6)));

        GD.Print($"[ExplorationBridge] Starting combat: {allies.Count} allies vs {enemies.Count} enemies on {gridSize}x{gridSize} grid.");

        // Subscribe to combat end (one-shot via CombatManager signal through CombatBridge)
        SubscribeToCombatEnd();

        combatBridge.StartCombat(allies, enemies, gridSize, gridSize);

        _pendingEncounterData = null;
    }

    /// <summary>
    /// Subscribes to the CombatManager.CombatEnded signal to handle post-combat cleanup.
    /// Uses the GameStateManager.PhaseChanged signal instead, since CombatBridge.EndCombat()
    /// restores the previous phase — we detect the phase transition back from Combat.
    /// </summary>
    private void SubscribeToCombatEnd()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null)
            return;

        // We use a lambda that auto-unsubscribes after combat ends.
        // PhaseChanged fires with (previousPhaseInt, newPhaseInt).
        void OnPhaseChangedForCombatEnd(int prevInt, int newInt)
        {
            var prev = (GameStateManager.GamePhase)prevInt;
            var next = (GameStateManager.GamePhase)newInt;

            if (prev == GameStateManager.GamePhase.Combat && next != GameStateManager.GamePhase.Combat)
            {
                // Unsubscribe immediately
                gsm.PhaseChanged -= OnPhaseChangedForCombatEnd;

                // Return exploration to normal state
                Callable.From(OnCombatResolved).CallDeferred();
            }
        }

        gsm.PhaseChanged += OnPhaseChangedForCombatEnd;
    }

    /// <summary>
    /// Called after combat ends and the phase returns from Combat.
    /// Restores exploration state and refreshes the UI.
    /// </summary>
    private void OnCombatResolved()
    {
        GD.Print("[ExplorationBridge] Combat resolved — returning to exploration.");

        if (_explorationManager != null &&
            _explorationManager.CurrentState == ExplorationState.InCombat)
        {
            _explorationManager.ReturnToExploring();
        }

        // Notify the player that combat is over
        _explorationUI?.Call("add_narrative_text",
            "\n[color=#c9a227][b]The battle is over. The party regroups.[/b][/color]\n");

        RefreshAll();
    }

    // -----------------------------------------------------------------
    // Save / Load support
    // -----------------------------------------------------------------

    /// <summary>
    /// Exposes the WorldMap instance for save/load operations. WorldMap is a
    /// plain C# object (not a Node), so it cannot be accessed via the scene tree.
    /// Returns null if the bridge has not been initialized.
    /// </summary>
    public WorldMap? GetWorldMap() => _worldMap;

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Creates and configures a TravelManager instance. Loads the EncounterGenerator
    /// with monster and encounter table data. Returns null if WorldMap is unavailable.
    /// </summary>
    private TravelManager? CreateTravelManager()
    {
        if (_worldMap == null)
        {
            GD.PrintErr("[ExplorationBridge] Cannot create TravelManager — WorldMap is null.");
            return null;
        }

        _encounterGenerator = new EncounterGenerator();

        try
        {
            _encounterGenerator.LoadMonsters(MonstersJsonPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationBridge] Failed to load monsters for TravelManager: {ex.Message}");
            // Continue — EncounterGenerator will generate empty encounters but won't crash.
        }

        try
        {
            _encounterGenerator.LoadEncounterTables(EncounterTablesJsonPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationBridge] Failed to load encounter tables for TravelManager: {ex.Message}");
            // Continue — table-based generation will return null, fallback generation still works.
        }

        var travelManager = new TravelManager(_worldMap, _encounterGenerator);
        GD.Print("[ExplorationBridge] TravelManager created.");
        return travelManager;
    }

    /// <summary>
    /// Builds a List&lt;Character&gt; from the GameLoop's player and companions
    /// for use by TravelManager's encounter scaling. Returns an empty list if
    /// GameLoop is not available.
    /// </summary>
    private static List<Character> BuildPartyList()
    {
        var party = new List<Character>();

        var gameLoop = GameStateManager.Instance?.ActiveGameLoop;
        if (gameLoop == null)
            return party;

        if (gameLoop.PlayerCharacter != null)
            party.Add(gameLoop.PlayerCharacter);

        if (gameLoop.Companions != null)
        {
            foreach (var companion in gameLoop.Companions)
                party.Add(companion);
        }

        return party;
    }

    /// <summary>
    /// Maps UI action button IDs to the action strings that
    /// ExplorationManager.ProcessPlayerAction() understands.
    /// </summary>
    private static string MapActionIdToString(string actionId)
    {
        return actionId switch
        {
            "look_around"    => "look around",
            "search"         => "search the area",
            "inventory"      => "check inventory",
            "talk_npc"       => "talk to someone",
            "visit_shop"     => "visit shop",
            "rest_inn"       => "rest at the inn",
            "gather_rumors"  => "gather rumors",
            "order_drink"    => "order a drink",
            "proceed"        => "proceed deeper",
            "check_traps"    => "check for traps",
            "short_rest"     => "short rest",
            "long_rest"      => "long rest",
            "forage"         => "forage for supplies",
            "set_camp"       => "set up camp",
            "break_camp"     => "break camp",
            "investigate"    => "investigate",
            "travel"         => "travel",
            "stop"           => "stop",
            "end_rest"       => "end rest",
            _                => actionId // Pass through unrecognized IDs as-is.
        };
    }
}
