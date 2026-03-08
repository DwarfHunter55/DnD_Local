using System;
using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;
using ChroniclesUnbound.Companions;
using ChroniclesUnbound.Data;
using ChroniclesUnbound.Exploration;
using ChroniclesUnbound.Journal;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Autoloaded singleton that orchestrates save/load operations. Bridges
/// GDScript UI (MainMenu, LoadGame) with the C# persistence layer
/// (DatabaseManager, SaveManager) and all runtime managers.
///
/// Also handles auto-save triggers (on location change, before combat)
/// and play time tracking (accumulated in _Process, flushed periodically).
///
/// All public methods use Godot.Collections types so GDScript can call them
/// directly. Follows the same singleton pattern as GameStateManager.
/// </summary>
public partial class SaveLoadBridge : Node
{
    public static SaveLoadBridge? Instance { get; private set; }

    // -----------------------------------------------------------------
    // Signals (GDScript-visible)
    // -----------------------------------------------------------------

    [Signal]
    public delegate void SaveCompletedEventHandler(int slotId);

    [Signal]
    public delegate void LoadCompletedEventHandler(int slotId);

    [Signal]
    public delegate void SaveFailedEventHandler(string error);

    [Signal]
    public delegate void LoadFailedEventHandler(string error);

    // -----------------------------------------------------------------
    // Dependencies
    // -----------------------------------------------------------------

    private DatabaseManager? _db;
    private SaveManager? _saveManager;

    // -----------------------------------------------------------------
    // Auto-save state
    // -----------------------------------------------------------------

    private DateTime _lastAutoSaveTime = DateTime.MinValue;
    private const double AutoSaveDebounceSeconds = 60.0;
    private bool _autoSaveSignalsConnected;

    // -----------------------------------------------------------------
    // Play time tracking state
    // -----------------------------------------------------------------

    private double _playTimeAccumulator;
    private DateTime _lastPlayTimeFlush = DateTime.UtcNow;
    private const double PlayTimeFlushIntervalSeconds = 60.0;

    // -----------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------

    public override void _Ready()
    {
        Instance = this;

        try
        {
            _db = new DatabaseManager();
            _db.InitializeDatabase();
            _saveManager = new SaveManager(_db);
            GD.Print("[SaveLoadBridge] Initialized with DatabaseManager and SaveManager.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] Failed to initialize persistence layer: {ex.Message}");
        }
    }

    public override void _Process(double delta)
    {
        // Only track play time when in an active game session
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.CurrentPhase == GameStateManager.GamePhase.MainMenu)
            return;

        int? slotId = gsm.ActiveSaveSlotId;
        if (slotId == null || _saveManager == null)
            return;

        _playTimeAccumulator += delta;

        // Flush to DB periodically
        double secondsSinceFlush = (DateTime.UtcNow - _lastPlayTimeFlush).TotalSeconds;
        if (secondsSinceFlush >= PlayTimeFlushIntervalSeconds)
        {
            FlushPlayTime();
        }
    }

    public override void _ExitTree()
    {
        // Flush any remaining accumulated play time before shutdown
        FlushPlayTime();

        if (Instance == this)
            Instance = null;

        _db?.Dispose();
        _db = null;
        _saveManager = null;
    }

    // -----------------------------------------------------------------
    // Public API — GDScript-callable
    // -----------------------------------------------------------------

    /// <summary>
    /// Returns metadata for all save slots as an Array of Dictionary.
    /// Each dictionary: {id, name, character_name, character_level, play_time, play_time_formatted, updated_at}
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAllSaveSlots()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();

        if (_saveManager == null)
        {
            GD.PrintErr("[SaveLoadBridge] SaveManager not initialized.");
            return result;
        }

        try
        {
            var slots = _saveManager.GetAllSaveSlots();
            foreach (var slot in slots)
            {
                result.Add(new Godot.Collections.Dictionary
                {
                    { "id", slot.Id },
                    { "name", slot.SlotName },
                    { "character_name", slot.CharacterName ?? "" },
                    { "character_level", slot.CharacterLevel },
                    { "play_time", slot.PlayTimeSeconds },
                    { "play_time_formatted", slot.FormattedPlayTime },
                    { "updated_at", slot.UpdatedAt ?? "" }
                });
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] GetAllSaveSlots failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Returns true if at least one save slot exists.
    /// </summary>
    public bool HasAnySaves()
    {
        if (_saveManager == null)
            return false;

        try
        {
            return _saveManager.HasAnySaves();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] HasAnySaves failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes a save slot and all associated data.
    /// </summary>
    public void DeleteSaveSlot(int slotId)
    {
        if (_saveManager == null)
        {
            GD.PrintErr("[SaveLoadBridge] SaveManager not initialized.");
            return;
        }

        try
        {
            _saveManager.DeleteSaveSlot(slotId);
            GD.Print($"[SaveLoadBridge] Deleted save slot {slotId}.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] DeleteSaveSlot failed: {ex.Message}");
            EmitSignal(SignalName.SaveFailed, $"Failed to delete save slot: {ex.Message}");
        }
    }

    /// <summary>
    /// Orchestrates a full game save to the specified slot.
    /// Gathers state from all managers, serializes, and persists.
    /// Returns the slot ID on success, or -1 on failure.
    /// </summary>
    public int SaveGame(int slotId)
    {
        if (_saveManager == null)
        {
            EmitSignal(SignalName.SaveFailed, "Save system not initialized.");
            return -1;
        }

        try
        {
            // Flush accumulated play time so it's captured in this save
            FlushPlayTime();

            var gameLoop = GameStateManager.Instance?.ActiveGameLoop;

            // -- Save player character --
            if (gameLoop?.PlayerCharacter != null)
            {
                string playerJson = JsonConvert.SerializeObject(gameLoop.PlayerCharacter);
                _saveManager.SavePlayerCharacter(slotId, playerJson);
            }
            else
            {
                GD.Print("[SaveLoadBridge] No player character to save.");
            }

            // -- Save companions --
            if (gameLoop?.Companions != null)
            {
                _saveManager.SaveCompanions(slotId, gameLoop.Companions);
            }
            else
            {
                _saveManager.SaveCompanions(slotId, new List<Character>());
            }

            // -- Build and save campaign data --
            var campaignData = BuildCampaignSaveData(gameLoop);
            string campaignJson = campaignData.ToJson();

            // -- Determine current phase and location --
            string phase = GameStateManager.Instance?.CurrentPhase.ToString() ?? "Exploration";
            string location = ExplorationManager.Instance?.CurrentLocationId ?? "";

            // -- Save game state (phase + location + campaign data) --
            _saveManager.SaveGameState(slotId, phase, location, campaignJson);

            GD.Print($"[SaveLoadBridge] Game saved to slot {slotId}.");
            EmitSignal(SignalName.SaveCompleted, slotId);
            return slotId;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] SaveGame failed: {ex.Message}");
            EmitSignal(SignalName.SaveFailed, $"Save failed: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Creates a new save slot with the given name, then saves to it.
    /// Returns the new slot ID on success, or -1 on failure.
    /// </summary>
    public int SaveGameToNewSlot(string slotName)
    {
        if (_saveManager == null)
        {
            EmitSignal(SignalName.SaveFailed, "Save system not initialized.");
            return -1;
        }

        try
        {
            int slotId = _saveManager.CreateSaveSlot(slotName);
            return SaveGame(slotId);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] SaveGameToNewSlot failed: {ex.Message}");
            EmitSignal(SignalName.SaveFailed, $"Failed to create save slot: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Quick save to a dedicated "QuickSave" slot. Creates the slot if
    /// it doesn't exist, otherwise overwrites the existing one.
    /// </summary>
    public void QuickSave()
    {
        if (_saveManager == null)
        {
            EmitSignal(SignalName.SaveFailed, "Save system not initialized.");
            return;
        }

        try
        {
            // Find existing QuickSave slot
            int quickSlotId = -1;
            var slots = _saveManager.GetAllSaveSlots();
            foreach (var slot in slots)
            {
                if (string.Equals(slot.SlotName, "QuickSave", StringComparison.OrdinalIgnoreCase))
                {
                    quickSlotId = slot.Id;
                    break;
                }
            }

            // Create if it doesn't exist
            if (quickSlotId < 0)
            {
                quickSlotId = _saveManager.CreateSaveSlot("QuickSave");
            }

            SaveGame(quickSlotId);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] QuickSave failed: {ex.Message}");
            EmitSignal(SignalName.SaveFailed, $"Quick save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Orchestrates a full game load from the specified slot.
    /// Restores characters, campaign data, and all manager states,
    /// then navigates to the appropriate scene.
    /// </summary>
    public void LoadGame(int slotId)
    {
        if (_saveManager == null)
        {
            EmitSignal(SignalName.LoadFailed, "Save system not initialized.");
            return;
        }

        try
        {
            // -- Load player character --
            string? playerJson = _saveManager.LoadPlayerCharacter(slotId);
            Character? playerCharacter = null;
            if (!string.IsNullOrWhiteSpace(playerJson))
            {
                playerCharacter = JsonConvert.DeserializeObject<Character>(playerJson);
            }

            if (playerCharacter == null)
            {
                EmitSignal(SignalName.LoadFailed, "No player character found in save slot.");
                return;
            }

            // -- Load companions --
            var companions = _saveManager.LoadCompanions(slotId);

            // -- Load game state --
            var gameState = _saveManager.LoadGameState(slotId);
            string phase = gameState?.CurrentPhase ?? "Exploration";
            string location = gameState?.CurrentLocation ?? "";

            // -- Load campaign data --
            CampaignSaveData? campaignData = null;
            if (!string.IsNullOrWhiteSpace(gameState?.CampaignDataJson))
            {
                campaignData = CampaignSaveData.FromJson(gameState.CampaignDataJson);
            }

            // -- Determine the target scene based on phase --
            string targetScene = ResolveSceneForPhase(phase);

            // -- Navigate to the appropriate scene --
            // SceneManager.SceneChanged fires after the scene is fully loaded.
            // We subscribe once to restore state after the scene transition.
            var sceneManager = SceneManager.Instance;
            if (sceneManager == null)
            {
                EmitSignal(SignalName.LoadFailed, "SceneManager not available.");
                return;
            }

            // Capture state in locals for the deferred callback
            int capturedSlotId = slotId;
            var capturedPlayer = playerCharacter;
            var capturedCompanions = companions;
            var capturedCampaign = campaignData;
            string capturedPhase = phase;
            string capturedLocation = location;

            // Use a one-shot handler to restore state after scene transition.
            void OnSceneLoaded(string previousScene, string newScene)
            {
                sceneManager.SceneChanged -= OnSceneLoaded;

                // Restore all manager states on the next frame so the scene
                // tree is fully ready.
                Callable.From(() =>
                {
                    RestoreManagerStates(
                        capturedSlotId,
                        capturedPlayer,
                        capturedCompanions,
                        capturedCampaign,
                        capturedPhase,
                        capturedLocation);
                }).CallDeferred();
            }

            sceneManager.SceneChanged += OnSceneLoaded;
            sceneManager.ChangeScene(targetScene);

            GD.Print($"[SaveLoadBridge] Loading game from slot {slotId}, target scene: {targetScene}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] LoadGame failed: {ex.Message}");
            EmitSignal(SignalName.LoadFailed, $"Load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads the most recently saved slot. No-op if no saves exist.
    /// </summary>
    public void LoadLatestSave()
    {
        if (_saveManager == null)
        {
            EmitSignal(SignalName.LoadFailed, "Save system not initialized.");
            return;
        }

        try
        {
            var latest = _saveManager.GetLatestSaveSlot();
            if (latest == null)
            {
                GD.Print("[SaveLoadBridge] No save slots found.");
                EmitSignal(SignalName.LoadFailed, "No saves found.");
                return;
            }

            LoadGame(latest.Id);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] LoadLatestSave failed: {ex.Message}");
            EmitSignal(SignalName.LoadFailed, $"Load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Exposes the SaveManager for other C# systems that need direct access
    /// (e.g. play time tracking, story thread persistence).
    /// </summary>
    public SaveManager? GetSaveManager() => _saveManager;

    // -----------------------------------------------------------------
    // Auto-save
    // -----------------------------------------------------------------

    /// <summary>
    /// Connects auto-save triggers to an ExplorationManager instance.
    /// Call this after the ExplorationManager is created and in the tree.
    /// Auto-saves fire on location changes and before combat encounters,
    /// with a 60-second debounce to prevent thrashing.
    /// </summary>
    public void ConnectAutoSaveSignals(Node explorationManager)
    {
        if (explorationManager == null)
        {
            GD.PrintErr("[SaveLoadBridge] ConnectAutoSaveSignals called with null explorationManager.");
            return;
        }

        if (_autoSaveSignalsConnected)
        {
            GD.Print("[SaveLoadBridge] Auto-save signals already connected.");
            return;
        }

        if (explorationManager is ExplorationManager em)
        {
            em.LocationChanged += OnAutoSaveLocationChanged;
            em.CombatTriggered += OnAutoSaveCombatTriggered;
            _autoSaveSignalsConnected = true;
            GD.Print("[SaveLoadBridge] Auto-save signals connected to ExplorationManager.");
        }
        else
        {
            GD.PrintErr("[SaveLoadBridge] ConnectAutoSaveSignals: node is not an ExplorationManager.");
        }
    }

    private void OnAutoSaveLocationChanged(string previousLocationId, string newLocationId)
    {
        AutoSave("location change");
    }

    private void OnAutoSaveCombatTriggered(string encounterData)
    {
        AutoSave("pre-combat");
    }

    /// <summary>
    /// Performs a debounced auto-save to a dedicated "AutoSave" slot.
    /// Skips if no active save slot is set, or if an auto-save was performed
    /// within the last 60 seconds. Never throws — all errors are logged.
    /// </summary>
    private void AutoSave(string reason)
    {
        try
        {
            var gsm = GameStateManager.Instance;
            if (gsm?.ActiveSaveSlotId == null)
                return;

            if (_saveManager == null)
                return;

            // Debounce: skip if last auto-save was too recent
            double elapsed = (DateTime.UtcNow - _lastAutoSaveTime).TotalSeconds;
            if (elapsed < AutoSaveDebounceSeconds)
            {
                GD.Print($"[SaveLoadBridge] Auto-save skipped ({reason}): only {elapsed:F0}s since last auto-save.");
                return;
            }

            // Find or create the AutoSave slot
            int autoSlotId = FindOrCreateAutoSaveSlot();
            if (autoSlotId < 0)
                return;

            // Flush play time before saving so the auto-save captures it
            FlushPlayTime();

            int result = SaveGame(autoSlotId);
            if (result >= 0)
            {
                _lastAutoSaveTime = DateTime.UtcNow;
                GD.Print($"[SaveLoadBridge] Auto-save completed ({reason}) to slot {autoSlotId}.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] Auto-save failed ({reason}): {ex.Message}");
        }
    }

    /// <summary>
    /// Finds the existing "AutoSave" slot or creates a new one.
    /// Returns the slot ID, or -1 on failure.
    /// </summary>
    private int FindOrCreateAutoSaveSlot()
    {
        try
        {
            if (_saveManager == null)
                return -1;

            var slots = _saveManager.GetAllSaveSlots();
            foreach (var slot in slots)
            {
                if (string.Equals(slot.SlotName, "AutoSave", StringComparison.OrdinalIgnoreCase))
                    return slot.Id;
            }

            return _saveManager.CreateSaveSlot("AutoSave");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] Failed to find/create AutoSave slot: {ex.Message}");
            return -1;
        }
    }

    // -----------------------------------------------------------------
    // Play time tracking
    // -----------------------------------------------------------------

    /// <summary>
    /// Flushes the accumulated play time to the database for the active
    /// save slot. Resets the accumulator and updates the flush timestamp.
    /// Safe to call at any time — no-ops if there is nothing to flush.
    /// </summary>
    private void FlushPlayTime()
    {
        int secondsToFlush = (int)_playTimeAccumulator;
        if (secondsToFlush <= 0)
            return;

        var slotId = GameStateManager.Instance?.ActiveSaveSlotId;
        if (slotId == null || _saveManager == null)
            return;

        try
        {
            _saveManager.UpdatePlayTime(slotId.Value, secondsToFlush);
            _playTimeAccumulator -= secondsToFlush; // Keep fractional remainder
            _lastPlayTimeFlush = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] Failed to flush play time: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------
    // Internal — Building campaign save data
    // -----------------------------------------------------------------

    /// <summary>
    /// Gathers state from all runtime managers into a CampaignSaveData DTO.
    /// Handles null managers gracefully (game may not be fully initialized).
    /// </summary>
    private static CampaignSaveData BuildCampaignSaveData(GameLoop? gameLoop)
    {
        var data = new CampaignSaveData();

        // -- Exploration snapshot --
        var explorationManager = ExplorationManager.Instance;
        if (explorationManager != null)
        {
            data.ExplorationSnapshot = explorationManager.GetSnapshot();
        }

        // -- Quest data --
        var questManager = QuestManager.Instance;
        if (questManager != null)
        {
            data.QuestDataJson = questManager.GetSaveData();
        }

        // -- World discovery state --
        // WorldMap is not a singleton Node — it's owned by ExplorationBridge.
        // Access it through ExplorationBridge if available.
        var explorationBridge = ExplorationBridge.Instance;
        if (explorationBridge != null)
        {
            data.WorldDiscovery = GetWorldDiscoveryFromBridge(explorationBridge);
        }

        // -- Relationship state --
        var relationshipManager = RelationshipManager.Instance;
        if (relationshipManager != null)
        {
            var (scores, histories) = relationshipManager.GetState();
            data.RelationshipScores = scores;
            data.RelationshipHistories = histories;
        }

        // -- Journal data --
        var journalManager = JournalManager.Instance;
        if (journalManager != null)
        {
            data.JournalDataJson = journalManager.GetSaveData();
        }

        // -- Narrative history from GameLoop --
        if (gameLoop != null)
        {
            data.NarrativeHistory = gameLoop.GetNarrativeHistory();
            data.SuggestedActions = gameLoop.GetSuggestedActions();
            data.CurrentScene = gameLoop.GetCurrentScene();
        }

        return data;
    }

    /// <summary>
    /// WorldMap is a plain C# object (not a Node) owned by ExplorationBridge.
    /// We access it via a helper method on ExplorationBridge. If the bridge
    /// doesn't expose WorldMap directly, we fall back to null.
    /// </summary>
    private static Dictionary<string, bool>? GetWorldDiscoveryFromBridge(ExplorationBridge bridge)
    {
        try
        {
            // ExplorationBridge stores _worldMap as a private field.
            // We access it through the WorldMap property we'll add,
            // or fall back to reflection if needed.
            var worldMap = bridge.GetWorldMap();
            return worldMap?.GetDiscoveryState();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] Failed to get world discovery state: {ex.Message}");
            return null;
        }
    }

    // -----------------------------------------------------------------
    // Internal — Restoring state after scene load
    // -----------------------------------------------------------------

    /// <summary>
    /// Restores all manager states after a scene transition. Called deferred
    /// so the scene tree is fully ready and GameLoop.Initialize() has been called.
    /// </summary>
    private void RestoreManagerStates(
        int slotId,
        Character playerCharacter,
        List<Character> companions,
        CampaignSaveData? campaignData,
        string phase,
        string location)
    {
        try
        {
            // -- Restore GameLoop characters --
            var gameLoop = GameStateManager.Instance?.ActiveGameLoop;
            if (gameLoop != null)
            {
                gameLoop.RestoreFromSave(playerCharacter, companions, campaignData);
            }
            else
            {
                GD.PrintErr("[SaveLoadBridge] GameLoop not available after scene load. " +
                            "Characters and campaign data may not be restored.");
            }

            // -- Restore exploration state --
            if (campaignData?.ExplorationSnapshot != null)
            {
                var explorationManager = ExplorationManager.Instance;
                explorationManager?.RestoreFromSnapshot(campaignData.ExplorationSnapshot);
            }

            // -- Restore quest state --
            if (!string.IsNullOrWhiteSpace(campaignData?.QuestDataJson))
            {
                var questManager = QuestManager.Instance;
                questManager?.RestoreFromSaveData(campaignData.QuestDataJson);
            }

            // -- Restore world discovery --
            if (campaignData?.WorldDiscovery != null)
            {
                var bridge = ExplorationBridge.Instance;
                if (bridge != null)
                {
                    try
                    {
                        var worldMap = bridge.GetWorldMap();
                        worldMap?.RestoreDiscoveryState(campaignData.WorldDiscovery);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[SaveLoadBridge] Failed to restore world discovery: {ex.Message}");
                    }
                }
            }

            // -- Restore relationship state --
            if (campaignData?.RelationshipScores != null)
            {
                var relationshipManager = RelationshipManager.Instance;
                if (relationshipManager != null)
                {
                    relationshipManager.RestoreState(
                        campaignData.RelationshipScores,
                        campaignData.RelationshipHistories ?? new Dictionary<string, List<RelationshipEvent>>());
                }
            }

            // -- Restore journal state --
            if (!string.IsNullOrWhiteSpace(campaignData?.JournalDataJson))
            {
                var journalManager = JournalManager.Instance;
                journalManager?.RestoreFromSaveData(campaignData.JournalDataJson);
            }

            // -- Set game phase --
            if (Enum.TryParse<GameStateManager.GamePhase>(phase, out var gamePhase))
            {
                GameStateManager.Instance?.SetPhase(gamePhase);
            }

            // -- Track active save slot --
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.ActiveSaveSlotId = slotId;
            }

            GD.Print($"[SaveLoadBridge] Game restored from slot {slotId}.");
            EmitSignal(SignalName.LoadCompleted, slotId);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SaveLoadBridge] RestoreManagerStates failed: {ex.Message}");
            EmitSignal(SignalName.LoadFailed, $"Restore failed: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------
    // Internal — Scene resolution
    // -----------------------------------------------------------------

    /// <summary>
    /// Maps a saved game phase string to the appropriate scene path.
    /// Defaults to ExplorationUI for most phases since the game is primarily
    /// exploration-driven outside of combat.
    /// </summary>
    private static string ResolveSceneForPhase(string phase)
    {
        // Parse the phase string to the enum for a clean switch
        if (!Enum.TryParse<GameStateManager.GamePhase>(phase, out var gamePhase))
        {
            GD.Print($"[SaveLoadBridge] Unknown phase '{phase}', defaulting to ExplorationUI.");
            return SceneManager.Scenes.ExplorationUI;
        }

        return gamePhase switch
        {
            GameStateManager.GamePhase.MainMenu => SceneManager.Scenes.MainMenu,
            GameStateManager.GamePhase.CharacterCreation => SceneManager.Scenes.CharacterCreation,
            GameStateManager.GamePhase.Combat => SceneManager.Scenes.ExplorationUI, // Don't reload into combat — return to exploration
            _ => SceneManager.Scenes.ExplorationUI
        };
    }
}
