using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using ChroniclesUnbound.Core;
using ChroniclesUnbound.LLM;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Exploration state machine and action router. Manages the player's movement
/// through the world outside of combat: location tracking, state transitions,
/// LLM-driven narrative descriptions, and event dispatch to subsystems
/// (combat, dialogue, shops, rest).
///
/// Singleton pattern matching other managers. Register as autoload or add to
/// scene tree; other systems access via ExplorationManager.Instance.
/// </summary>
public partial class ExplorationManager : Node
{
    public static ExplorationManager? Instance { get; private set; }

    // -----------------------------------------------------------------
    // Signals
    // -----------------------------------------------------------------

    [Signal]
    public delegate void StateChangedEventHandler(int previousState, int newState);

    [Signal]
    public delegate void LocationChangedEventHandler(string previousLocationId, string newLocationId);

    [Signal]
    public delegate void CombatTriggeredEventHandler(string encounterData);

    [Signal]
    public delegate void DialogueStartedEventHandler(string npcId);

    [Signal]
    public delegate void ShopOpenedEventHandler(string shopId);

    [Signal]
    public delegate void RestStartedEventHandler(int restType);

    // -----------------------------------------------------------------
    // State
    // -----------------------------------------------------------------

    public ExplorationState CurrentState { get; private set; } = ExplorationState.Exploring;
    public string CurrentLocationId { get; private set; } = string.Empty;
    public string CurrentLocationName { get; private set; } = string.Empty;
    public string CurrentLocationDescription { get; private set; } = string.Empty;

    /// <summary>
    /// The type of the current location (e.g. "town", "dungeon", "wilderness", "inn").
    /// Used to determine available actions and encounter types.
    /// </summary>
    public string CurrentLocationType { get; private set; } = "wilderness";

    private bool _isProcessing;

    // -----------------------------------------------------------------
    // Dependencies — set via Initialize() after the node enters the tree
    // -----------------------------------------------------------------

    private LLMService? _llmService;

    // -----------------------------------------------------------------
    // Transition validation table
    // -----------------------------------------------------------------

    /// <summary>
    /// Defines which state transitions are legal. If a source state is not
    /// listed, all transitions from it are blocked.
    /// </summary>
    private static readonly Dictionary<ExplorationState, HashSet<ExplorationState>> _validTransitions = new()
    {
        [ExplorationState.Exploring] = new HashSet<ExplorationState>
        {
            ExplorationState.InDialogue,
            ExplorationState.InShop,
            ExplorationState.InCombat,
            ExplorationState.Resting,
            ExplorationState.InMenu,
            ExplorationState.Traveling
        },
        [ExplorationState.InDialogue] = new HashSet<ExplorationState>
        {
            ExplorationState.Exploring,
            ExplorationState.InCombat, // dialogue can escalate to combat
            ExplorationState.InShop    // NPC dialogue can open a shop
        },
        [ExplorationState.InShop] = new HashSet<ExplorationState>
        {
            ExplorationState.Exploring,
            ExplorationState.InDialogue // return to dialogue after shopping
        },
        [ExplorationState.InCombat] = new HashSet<ExplorationState>
        {
            ExplorationState.Exploring // combat ends, return to exploration
        },
        [ExplorationState.Resting] = new HashSet<ExplorationState>
        {
            ExplorationState.Exploring,
            ExplorationState.InCombat // ambush during rest
        },
        [ExplorationState.InMenu] = new HashSet<ExplorationState>
        {
            ExplorationState.Exploring // menus always return to exploring
        },
        [ExplorationState.Traveling] = new HashSet<ExplorationState>
        {
            ExplorationState.Exploring, // arrive at destination
            ExplorationState.InCombat   // ambush during travel
        }
    };

    // -----------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[ExplorationManager] Initialized.");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Inject runtime dependencies. Call this after the node enters the tree
    /// and the LLM service is available (typically from GameLoop or a setup routine).
    /// </summary>
    public void Initialize(LLMService llmService)
    {
        _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        GD.Print("[ExplorationManager] Dependencies injected.");
    }

    // -----------------------------------------------------------------
    // State transitions
    // -----------------------------------------------------------------

    /// <summary>
    /// Attempt to transition to a new exploration state. Returns false and
    /// logs a warning if the transition is not allowed from the current state.
    /// Fires state exit/entry hooks and the StateChanged signal.
    /// </summary>
    public bool TransitionTo(ExplorationState newState)
    {
        if (newState == CurrentState)
            return true;

        if (!IsTransitionValid(CurrentState, newState))
        {
            GD.PrintErr($"[ExplorationManager] Invalid transition: {CurrentState} -> {newState}");
            return false;
        }

        var previous = CurrentState;

        OnStateExit(previous);
        CurrentState = newState;
        OnStateEnter(newState);

        GD.Print($"[ExplorationManager] State: {previous} -> {newState}");
        EmitSignal(SignalName.StateChanged, (int)previous, (int)newState);

        // Sync with GameStateManager when entering/leaving certain states
        SyncGamePhase(newState);

        return true;
    }

    /// <summary>
    /// Check whether a given transition is allowed without performing it.
    /// </summary>
    public static bool IsTransitionValid(ExplorationState from, ExplorationState to)
    {
        return _validTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>
    /// Force-set the state without validation or hooks. Use only for
    /// save/load restoration where the state is known-good.
    /// </summary>
    public void ForceState(ExplorationState state)
    {
        var previous = CurrentState;
        CurrentState = state;
        GD.Print($"[ExplorationManager] Force state: {previous} -> {state}");
        EmitSignal(SignalName.StateChanged, (int)previous, (int)state);
    }

    // -----------------------------------------------------------------
    // State entry/exit hooks
    // -----------------------------------------------------------------

    private void OnStateExit(ExplorationState state)
    {
        switch (state)
        {
            case ExplorationState.Resting:
                GD.Print("[ExplorationManager] Rest ended.");
                break;
            case ExplorationState.Traveling:
                GD.Print("[ExplorationManager] Travel ended.");
                break;
        }
    }

    private void OnStateEnter(ExplorationState state)
    {
        switch (state)
        {
            case ExplorationState.Resting:
                GD.Print("[ExplorationManager] Rest started.");
                break;
            case ExplorationState.Traveling:
                GD.Print("[ExplorationManager] Travel started.");
                break;
            case ExplorationState.InCombat:
                GD.Print("[ExplorationManager] Entering combat.");
                break;
        }
    }

    /// <summary>
    /// Keep the top-level GameStateManager phase in sync with exploration state.
    /// Not every exploration sub-state maps to a distinct GamePhase.
    /// </summary>
    private static void SyncGamePhase(ExplorationState newState)
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null)
            return;

        switch (newState)
        {
            case ExplorationState.Exploring:
            case ExplorationState.Traveling:
                gsm.SetPhase(GameStateManager.GamePhase.Exploration);
                break;
            case ExplorationState.InDialogue:
                gsm.SetPhase(GameStateManager.GamePhase.Dialogue);
                break;
            case ExplorationState.InCombat:
                gsm.SetPhase(GameStateManager.GamePhase.Combat);
                break;
            case ExplorationState.Resting:
                gsm.SetPhase(GameStateManager.GamePhase.Rest);
                break;
            case ExplorationState.InMenu:
                gsm.SetPhase(GameStateManager.GamePhase.Inventory);
                break;
            // InShop stays in Exploration phase at the GameStateManager level
        }
    }

    // -----------------------------------------------------------------
    // Location management
    // -----------------------------------------------------------------

    /// <summary>
    /// Change the current location. Emits LocationChanged signal and optionally
    /// generates an LLM description of the new location.
    /// </summary>
    public async Task ChangeLocation(
        string locationId,
        string locationName,
        string locationType,
        bool generateDescription = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(locationId))
        {
            GD.PrintErr("[ExplorationManager] ChangeLocation called with empty locationId.");
            return;
        }

        string previousId = CurrentLocationId;
        CurrentLocationId = locationId;
        CurrentLocationName = locationName;
        CurrentLocationType = locationType;

        GD.Print($"[ExplorationManager] Location: {previousId} -> {locationId} ({locationName}, {locationType})");
        EmitSignal(SignalName.LocationChanged, previousId, locationId);

        if (generateDescription)
        {
            CurrentLocationDescription = await DescribeLocation(locationId, cancellationToken);
        }
    }

    // -----------------------------------------------------------------
    // Player action processing
    // -----------------------------------------------------------------

    /// <summary>
    /// Main input handler. Routes a player action string to the appropriate
    /// subsystem based on the current state and action content.
    /// Returns true if the action was accepted and processed.
    /// </summary>
    public async Task<bool> ProcessPlayerAction(string action, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(action))
            return false;

        if (_isProcessing)
        {
            GD.Print("[ExplorationManager] Already processing an action, ignoring.");
            return false;
        }

        _isProcessing = true;

        try
        {
            string actionLower = action.Trim().ToLowerInvariant();

            switch (CurrentState)
            {
                case ExplorationState.Exploring:
                    return await ProcessExploringAction(actionLower, action, cancellationToken);

                case ExplorationState.Traveling:
                    return await ProcessTravelingAction(actionLower, action, cancellationToken);

                case ExplorationState.InDialogue:
                case ExplorationState.InShop:
                case ExplorationState.InMenu:
                    // These states have their own managers that handle input.
                    // The exploration manager just needs to know when they're done.
                    GD.Print($"[ExplorationManager] Action '{action}' deferred to active subsystem ({CurrentState}).");
                    return false;

                case ExplorationState.InCombat:
                case ExplorationState.Resting:
                    GD.Print($"[ExplorationManager] Cannot process exploration actions in {CurrentState} state.");
                    return false;

                default:
                    GD.PrintErr($"[ExplorationManager] Unhandled state: {CurrentState}");
                    return false;
            }
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>
    /// Handle an action while in the Exploring state. Detects intent from
    /// the action string and routes accordingly.
    /// </summary>
    private async Task<bool> ProcessExploringAction(
        string actionLower,
        string originalAction,
        CancellationToken cancellationToken)
    {
        // Check for explicit system-level commands first
        if (actionLower.StartsWith("talk to ") || actionLower.StartsWith("speak with "))
        {
            string npcId = ExtractTarget(actionLower, "talk to ", "speak with ");
            TransitionTo(ExplorationState.InDialogue);
            EmitSignal(SignalName.DialogueStarted, npcId);
            return true;
        }

        if (actionLower == "shop" || actionLower.StartsWith("visit shop") ||
            actionLower.StartsWith("browse wares") || actionLower.StartsWith("open shop"))
        {
            string shopId = CurrentLocationId; // default to current location
            TransitionTo(ExplorationState.InShop);
            EmitSignal(SignalName.ShopOpened, shopId);
            return true;
        }

        if (actionLower == "rest" || actionLower.StartsWith("take a rest") ||
            actionLower == "short rest" || actionLower == "long rest")
        {
            int restType = actionLower.Contains("long") ? 1 : 0; // 0 = short, 1 = long
            TransitionTo(ExplorationState.Resting);
            EmitSignal(SignalName.RestStarted, restType);
            return true;
        }

        if (actionLower.StartsWith("travel to ") || actionLower.StartsWith("go to ") ||
            actionLower.StartsWith("head to "))
        {
            string destination = ExtractTarget(actionLower, "travel to ", "go to ", "head to ");
            TransitionTo(ExplorationState.Traveling);
            // The travel system will handle the actual movement and arrival.
            // For now, narrate the departure.
            await NarrateEvent(
                $"The party prepares to travel to {destination} from {CurrentLocationName}.",
                cancellationToken);
            return true;
        }

        if (actionLower == "inventory" || actionLower == "open inventory" ||
            actionLower == "check inventory")
        {
            TransitionTo(ExplorationState.InMenu);
            return true;
        }

        // Everything else is a free-form exploration action handled by the LLM
        // via the GameLoop's existing narrative pipeline. We just narrate it.
        await NarrateEvent(originalAction, cancellationToken);
        return true;
    }

    /// <summary>
    /// Handle an action while in the Traveling state. Limited actions available.
    /// </summary>
    private async Task<bool> ProcessTravelingAction(
        string actionLower,
        string originalAction,
        CancellationToken cancellationToken)
    {
        if (actionLower == "stop" || actionLower == "make camp" || actionLower == "halt")
        {
            TransitionTo(ExplorationState.Exploring);
            await NarrateEvent("The party halts their travel and surveys the surroundings.", cancellationToken);
            return true;
        }

        // Most actions during travel are narrated but limited
        await NarrateEvent($"While traveling: {originalAction}", cancellationToken);
        return true;
    }

    // -----------------------------------------------------------------
    // Available actions
    // -----------------------------------------------------------------

    /// <summary>
    /// Descriptor for a UI-facing action button/option.
    /// </summary>
    public readonly struct ActionDescriptor
    {
        public string Id { get; init; }
        public string DisplayText { get; init; }
        public string Category { get; init; } // "explore", "social", "travel", "system"

        public ActionDescriptor(string id, string displayText, string category)
        {
            Id = id;
            DisplayText = displayText;
            Category = category;
        }
    }

    /// <summary>
    /// Returns context-sensitive actions available to the player based on the
    /// current exploration state and location type. The UI consumes this to
    /// populate action buttons.
    /// </summary>
    public List<ActionDescriptor> GetAvailableActions()
    {
        var actions = new List<ActionDescriptor>();

        switch (CurrentState)
        {
            case ExplorationState.Exploring:
                AddExploringActions(actions);
                break;

            case ExplorationState.Traveling:
                actions.Add(new ActionDescriptor("stop", "Stop and make camp", "travel"));
                actions.Add(new ActionDescriptor("look_around", "Scout the surroundings", "explore"));
                break;

            case ExplorationState.Resting:
                actions.Add(new ActionDescriptor("end_rest", "End rest", "system"));
                break;

            case ExplorationState.InDialogue:
            case ExplorationState.InShop:
            case ExplorationState.InMenu:
                // These states are managed by their own subsystems.
                // The subsystem provides its own action list.
                break;

            case ExplorationState.InCombat:
                // Combat has its own action system.
                break;
        }

        return actions;
    }

    private void AddExploringActions(List<ActionDescriptor> actions)
    {
        // Universal exploration actions
        actions.Add(new ActionDescriptor("look_around", "Look around", "explore"));
        actions.Add(new ActionDescriptor("search", "Search the area", "explore"));
        actions.Add(new ActionDescriptor("inventory", "Check inventory", "system"));

        // Location-type-specific actions
        switch (CurrentLocationType)
        {
            case "town":
            case "city":
            case "village":
                actions.Add(new ActionDescriptor("talk_npc", "Talk to someone", "social"));
                actions.Add(new ActionDescriptor("visit_shop", "Visit a shop", "social"));
                actions.Add(new ActionDescriptor("rest_inn", "Rest at the inn", "explore"));
                actions.Add(new ActionDescriptor("gather_rumors", "Gather rumors", "social"));
                break;

            case "inn":
            case "tavern":
                actions.Add(new ActionDescriptor("talk_npc", "Talk to someone", "social"));
                actions.Add(new ActionDescriptor("rest_inn", "Rest here", "explore"));
                actions.Add(new ActionDescriptor("gather_rumors", "Listen for rumors", "social"));
                actions.Add(new ActionDescriptor("order_drink", "Order a drink", "social"));
                break;

            case "dungeon":
                actions.Add(new ActionDescriptor("proceed", "Proceed deeper", "explore"));
                actions.Add(new ActionDescriptor("check_traps", "Check for traps", "explore"));
                actions.Add(new ActionDescriptor("short_rest", "Take a short rest", "explore"));
                break;

            case "wilderness":
                actions.Add(new ActionDescriptor("forage", "Forage for supplies", "explore"));
                actions.Add(new ActionDescriptor("short_rest", "Take a short rest", "explore"));
                actions.Add(new ActionDescriptor("set_camp", "Set up camp", "explore"));
                break;

            case "camp":
                actions.Add(new ActionDescriptor("long_rest", "Take a long rest", "explore"));
                actions.Add(new ActionDescriptor("short_rest", "Take a short rest", "explore"));
                actions.Add(new ActionDescriptor("break_camp", "Break camp", "explore"));
                break;

            default:
                actions.Add(new ActionDescriptor("investigate", "Investigate", "explore"));
                break;
        }

        // Travel is always available unless we're already somewhere very specific
        actions.Add(new ActionDescriptor("travel", "Travel to...", "travel"));
    }

    // -----------------------------------------------------------------
    // LLM narrative integration
    // -----------------------------------------------------------------

    /// <summary>
    /// Generate an atmospheric LLM description of a location. Returns a fallback
    /// string if the LLM is unavailable.
    /// </summary>
    public async Task<string> DescribeLocation(string locationId, CancellationToken cancellationToken = default)
    {
        if (_llmService == null)
        {
            GD.Print("[ExplorationManager] LLM service not available, using fallback description.");
            return BuildFallbackDescription(locationId);
        }

        var messages = BuildLocationDescriptionPrompt(locationId);

        try
        {
            var response = await _llmService.GenerateAsync(messages, jsonMode: true, cancellationToken);

            if (response.IsSuccess && response.ParsedJson != null)
            {
                string description = response.ParsedJson["description"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(description))
                    return description;
            }

            // Fallback: use raw text if JSON parsing failed but we got text
            if (response.IsSuccess && !string.IsNullOrWhiteSpace(response.RawText))
                return response.RawText;

            GD.PrintErr($"[ExplorationManager] LLM description failed: {response.ErrorMessage}");
            return BuildFallbackDescription(locationId);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationManager] DescribeLocation error: {ex.Message}");
            return BuildFallbackDescription(locationId);
        }
    }

    /// <summary>
    /// Generate LLM narration for a game event (discovery, environmental change, etc.).
    /// Returns the narration text, or a fallback on failure.
    /// </summary>
    public async Task<string> NarrateEvent(string eventDescription, CancellationToken cancellationToken = default)
    {
        if (_llmService == null)
        {
            GD.Print("[ExplorationManager] LLM service not available, returning event as-is.");
            return eventDescription;
        }

        var messages = BuildEventNarrationPrompt(eventDescription);

        try
        {
            var response = await _llmService.GenerateAsync(messages, jsonMode: true, cancellationToken);

            if (response.IsSuccess && response.ParsedJson != null)
            {
                string narration = response.ParsedJson["narration"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(narration))
                    return narration;
            }

            if (response.IsSuccess && !string.IsNullOrWhiteSpace(response.RawText))
                return response.RawText;

            GD.PrintErr($"[ExplorationManager] LLM narration failed: {response.ErrorMessage}");
            return eventDescription;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ExplorationManager] NarrateEvent error: {ex.Message}");
            return eventDescription;
        }
    }

    // -----------------------------------------------------------------
    // Event triggers — called by subsystems or the action processor
    // -----------------------------------------------------------------

    /// <summary>
    /// Trigger a combat encounter. Transitions state and emits CombatTriggered.
    /// Called by encounter generators, ambush checks, or dialogue escalation.
    /// </summary>
    public bool TriggerCombat(string encounterData)
    {
        if (!TransitionTo(ExplorationState.InCombat))
            return false;

        EmitSignal(SignalName.CombatTriggered, encounterData);
        return true;
    }

    /// <summary>
    /// Start a dialogue with an NPC. Transitions state and emits DialogueStarted.
    /// </summary>
    public bool StartDialogue(string npcId)
    {
        if (!TransitionTo(ExplorationState.InDialogue))
            return false;

        EmitSignal(SignalName.DialogueStarted, npcId);
        return true;
    }

    /// <summary>
    /// Open a shop. Transitions state and emits ShopOpened.
    /// </summary>
    public bool OpenShop(string shopId)
    {
        if (!TransitionTo(ExplorationState.InShop))
            return false;

        EmitSignal(SignalName.ShopOpened, shopId);
        return true;
    }

    /// <summary>
    /// Start a rest. restType: 0 = short rest, 1 = long rest.
    /// Transitions state and emits RestStarted.
    /// </summary>
    public bool StartRest(int restType)
    {
        if (!TransitionTo(ExplorationState.Resting))
            return false;

        EmitSignal(SignalName.RestStarted, restType);
        return true;
    }

    /// <summary>
    /// Return to the Exploring state from any subsystem state.
    /// Called by combat, dialogue, shop, rest, and menu managers when they complete.
    /// </summary>
    public bool ReturnToExploring()
    {
        return TransitionTo(ExplorationState.Exploring);
    }

    // -----------------------------------------------------------------
    // Prompt building (exploration-specific)
    // -----------------------------------------------------------------

    private List<LLMMessage> BuildLocationDescriptionPrompt(string locationId)
    {
        string systemPrompt =
            "You are a Dungeon Master describing a location in a D&D 5e campaign. " +
            "Write an atmospheric, immersive description of the location. " +
            "Include sensory details (sights, sounds, smells), notable features, " +
            "and hints about what the player might do or find here. " +
            "Keep it to 2-3 paragraphs.\n\n" +
            "You MUST respond with ONLY a JSON object matching this exact schema:\n" +
            "{\n" +
            "  \"description\": \"<atmospheric location description>\",\n" +
            "  \"notable_features\": [\"<feature 1>\", \"<feature 2>\"],\n" +
            "  \"ambient_mood\": \"<one word: peaceful|tense|mysterious|foreboding|lively|desolate>\"\n" +
            "}\n\n" +
            "Do not include any text outside the JSON object.";

        string userContent =
            $"=== LOCATION ===\n" +
            $"ID: {locationId}\n" +
            $"Name: {CurrentLocationName}\n" +
            $"Type: {CurrentLocationType}\n\n" +
            "Describe this location for the party as they arrive.";

        return new List<LLMMessage>
        {
            new LLMMessage("system", systemPrompt),
            new LLMMessage("user", userContent)
        };
    }

    private List<LLMMessage> BuildEventNarrationPrompt(string eventDescription)
    {
        string systemPrompt =
            "You are a Dungeon Master narrating events in a D&D 5e campaign. " +
            "Write vivid, concise narration for the event described. " +
            "Keep it to 1-2 paragraphs. Do not make decisions for the player.\n\n" +
            "You MUST respond with ONLY a JSON object matching this exact schema:\n" +
            "{\n" +
            "  \"narration\": \"<event narration text>\",\n" +
            "  \"triggers_encounter\": false,\n" +
            "  \"encounter_type\": null\n" +
            "}\n\n" +
            "Set triggers_encounter to true if this event should logically " +
            "lead to combat (e.g. ambush, hostile creature). Set encounter_type " +
            "to \"combat\", \"social\", or \"puzzle\" if triggered.\n\n" +
            "Do not include any text outside the JSON object.";

        string userContent =
            $"=== CURRENT LOCATION ===\n" +
            $"{CurrentLocationName} ({CurrentLocationType})\n\n" +
            $"=== EVENT ===\n{eventDescription}";

        return new List<LLMMessage>
        {
            new LLMMessage("system", systemPrompt),
            new LLMMessage("user", userContent)
        };
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Extract a target noun from an action string by stripping known prefixes.
    /// </summary>
    private static string ExtractTarget(string actionLower, params string[] prefixes)
    {
        foreach (string prefix in prefixes)
        {
            if (actionLower.StartsWith(prefix))
                return actionLower.Substring(prefix.Length).Trim();
        }

        return actionLower;
    }

    private string BuildFallbackDescription(string locationId)
    {
        return CurrentLocationType switch
        {
            "town" or "city" or "village" =>
                $"You arrive at {CurrentLocationName}. The settlement stretches before you, " +
                "its streets busy with the rhythm of daily life. Buildings of timber and stone " +
                "line the main road, and the sounds of commerce and conversation fill the air.",

            "inn" or "tavern" =>
                $"You step inside {CurrentLocationName}. The warmth of a hearth fire greets you, " +
                "along with the smell of cooking food and spilled ale. " +
                "A few patrons glance up from their drinks as you enter.",

            "dungeon" =>
                $"You stand at the entrance to {CurrentLocationName}. " +
                "Cold air seeps from the darkness beyond, carrying the scent of damp stone " +
                "and something old. The walls are rough-hewn and slick with moisture.",

            "wilderness" =>
                $"You find yourself in the wilds near {CurrentLocationName}. " +
                "The terrain stretches out around you under an open sky. " +
                "The wind carries the sounds of nature and the faint promise of the unknown.",

            "camp" =>
                $"Your campsite at {CurrentLocationName} is set. " +
                "A fire crackles in the center, casting flickering light against the surrounding darkness. " +
                "The party's bedrolls are arranged nearby.",

            _ =>
                $"You survey {CurrentLocationName}. " +
                "The area is quiet, though something about it holds your attention."
        };
    }

    /// <summary>
    /// Serializable snapshot of exploration state for save/load.
    /// </summary>
    public ExplorationSnapshot GetSnapshot()
    {
        return new ExplorationSnapshot
        {
            State = CurrentState,
            LocationId = CurrentLocationId,
            LocationName = CurrentLocationName,
            LocationType = CurrentLocationType,
            LocationDescription = CurrentLocationDescription
        };
    }

    /// <summary>
    /// Restore exploration state from a snapshot. Uses ForceState to skip validation.
    /// </summary>
    public void RestoreFromSnapshot(ExplorationSnapshot snapshot)
    {
        ForceState(snapshot.State);
        CurrentLocationId = snapshot.LocationId;
        CurrentLocationName = snapshot.LocationName;
        CurrentLocationType = snapshot.LocationType;
        CurrentLocationDescription = snapshot.LocationDescription;
        GD.Print($"[ExplorationManager] Restored: {snapshot.LocationName} ({snapshot.State})");
    }
}
