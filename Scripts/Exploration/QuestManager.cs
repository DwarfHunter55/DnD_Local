using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Newtonsoft.Json;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Core quest tracking system. Loads quest definitions from JSON, tracks objective
/// progress, handles quest lifecycle (accept / update / complete / fail), and
/// auto-updates objectives when game events fire.
///
/// Singleton pattern matching other managers. Add to the scene tree or register
/// as autoload; access via QuestManager.Instance.
/// </summary>
public partial class QuestManager : Node
{
    public static QuestManager? Instance { get; private set; }

    // -----------------------------------------------------------------
    // Signals
    // -----------------------------------------------------------------

    [Signal]
    public delegate void QuestAcceptedEventHandler(string questId);

    [Signal]
    public delegate void QuestCompletedEventHandler(string questId);

    [Signal]
    public delegate void QuestFailedEventHandler(string questId);

    [Signal]
    public delegate void ObjectiveUpdatedEventHandler(string questId, string objectiveId, int currentProgress, int requiredCount);

    // -----------------------------------------------------------------
    // State
    // -----------------------------------------------------------------

    /// <summary>
    /// All known quests keyed by quest_id. Includes available, active, completed, and failed.
    /// </summary>
    private readonly Dictionary<string, QuestData> _quests = new();

    /// <summary>
    /// Mapping from (ObjectiveType, TargetId) to the list of active quest objectives
    /// that care about that event. Rebuilt when quests are accepted or completed.
    /// Avoids O(N) scan of all objectives on every event notification.
    /// </summary>
    private readonly Dictionary<(ObjectiveType, string), List<(string questId, QuestObjective objective)>> _eventIndex = new();

    // -----------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[QuestManager] Initialized.");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    // -----------------------------------------------------------------
    // Loading
    // -----------------------------------------------------------------

    /// <summary>
    /// Loads quest definitions from Data/World/quests.json. Existing runtime state
    /// (progress, status) is preserved for quests already tracked — new quests from
    /// the file are added as Available.
    /// </summary>
    public void LoadQuests(string path = "res://Data/World/quests.json")
    {
        string globalPath = ProjectSettings.GlobalizePath(path);

        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PrintErr($"[QuestManager] Quest data file not found: {path}");
            return;
        }

        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"[QuestManager] Failed to open quest data: {Godot.FileAccess.GetOpenError()}");
            return;
        }

        string json = file.GetAsText();
        var loadedQuests = JsonConvert.DeserializeObject<List<QuestData>>(json);

        if (loadedQuests == null || loadedQuests.Count == 0)
        {
            GD.PrintErr("[QuestManager] Quest data deserialized to null or empty.");
            return;
        }

        foreach (var quest in loadedQuests)
        {
            if (string.IsNullOrEmpty(quest.QuestId))
            {
                GD.PrintErr("[QuestManager] Skipping quest with empty quest_id.");
                continue;
            }

            // Don't overwrite runtime state for quests already loaded (e.g. from save)
            if (!_quests.ContainsKey(quest.QuestId))
            {
                _quests[quest.QuestId] = quest;
            }
        }

        RebuildEventIndex();
        GD.Print($"[QuestManager] Loaded {_quests.Count} quests.");
    }

    // -----------------------------------------------------------------
    // Quest lifecycle
    // -----------------------------------------------------------------

    /// <summary>
    /// Accept a quest, moving it from Available to Active. Returns false if the
    /// quest doesn't exist or isn't in the Available state.
    /// </summary>
    public bool AcceptQuest(string questId)
    {
        if (!_quests.TryGetValue(questId, out var quest))
        {
            GD.PrintErr($"[QuestManager] AcceptQuest: unknown quest '{questId}'.");
            return false;
        }

        if (quest.Status != QuestStatus.Available)
        {
            GD.PrintErr($"[QuestManager] AcceptQuest: quest '{questId}' is {quest.Status}, expected Available.");
            return false;
        }

        quest.Status = QuestStatus.Active;
        RebuildEventIndex();

        GD.Print($"[QuestManager] Quest accepted: {quest.Title} ({questId})");
        EmitSignal(SignalName.QuestAccepted, questId);
        return true;
    }

    /// <summary>
    /// Manually update progress on a specific objective within a quest.
    /// Returns true if progress was actually changed.
    /// </summary>
    public bool UpdateObjective(string questId, string objectiveId, int progressDelta)
    {
        if (!_quests.TryGetValue(questId, out var quest))
        {
            GD.PrintErr($"[QuestManager] UpdateObjective: unknown quest '{questId}'.");
            return false;
        }

        if (quest.Status != QuestStatus.Active)
            return false;

        var objective = quest.Objectives.Find(o => o.ObjectiveId == objectiveId);
        if (objective == null)
        {
            GD.PrintErr($"[QuestManager] UpdateObjective: unknown objective '{objectiveId}' in quest '{questId}'.");
            return false;
        }

        int added = objective.AddProgress(progressDelta);
        if (added <= 0)
            return false;

        GD.Print($"[QuestManager] Objective '{objectiveId}' progress: {objective.CurrentProgress}/{objective.RequiredCount}");
        EmitSignal(SignalName.ObjectiveUpdated, questId, objectiveId, objective.CurrentProgress, objective.RequiredCount);

        // Auto-complete check
        if (quest.AllObjectivesComplete)
        {
            GD.Print($"[QuestManager] All objectives complete for '{quest.Title}'. Ready to turn in.");
        }

        return true;
    }

    /// <summary>
    /// Notify the quest system of a game event. All active quest objectives that
    /// match the event type and target are automatically progressed.
    /// Call this from combat (kills), exploration (location visits), dialogue
    /// (NPC interactions), inventory (item pickups), and leveling systems.
    /// </summary>
    public void NotifyEvent(QuestEventType eventType, string targetId)
    {
        var objectiveType = MapEventToObjectiveType(eventType);
        var key = (objectiveType, targetId);

        if (!_eventIndex.TryGetValue(key, out var entries))
            return;

        // Iterate a snapshot to avoid mutation issues if completion triggers removal
        foreach (var (questId, objective) in entries.ToList())
        {
            if (objective.IsComplete)
                continue;

            int added = objective.AddProgress(1);
            if (added > 0)
            {
                GD.Print($"[QuestManager] Event {eventType}:{targetId} -> {questId}/{objective.ObjectiveId} " +
                         $"({objective.CurrentProgress}/{objective.RequiredCount})");
                EmitSignal(SignalName.ObjectiveUpdated, questId, objective.ObjectiveId,
                    objective.CurrentProgress, objective.RequiredCount);

                if (_quests.TryGetValue(questId, out var quest) && quest.AllObjectivesComplete)
                {
                    GD.Print($"[QuestManager] All objectives complete for '{quest.Title}'. Ready to turn in.");
                }
            }
        }
    }

    /// <summary>
    /// Mark a quest as completed and grant its rewards. Returns false if the quest
    /// is not Active or its objectives are not all complete.
    /// </summary>
    public bool CompleteQuest(string questId)
    {
        if (!_quests.TryGetValue(questId, out var quest))
        {
            GD.PrintErr($"[QuestManager] CompleteQuest: unknown quest '{questId}'.");
            return false;
        }

        if (quest.Status != QuestStatus.Active)
        {
            GD.PrintErr($"[QuestManager] CompleteQuest: quest '{questId}' is {quest.Status}, expected Active.");
            return false;
        }

        if (!quest.AllObjectivesComplete)
        {
            GD.PrintErr($"[QuestManager] CompleteQuest: quest '{questId}' has incomplete objectives.");
            return false;
        }

        quest.Status = QuestStatus.Completed;
        RebuildEventIndex();

        GD.Print($"[QuestManager] Quest completed: {quest.Title} ({questId}) " +
                 $"[XP:{quest.XPReward} Gold:{quest.GoldReward} Items:{quest.ItemRewards.Count}]");
        EmitSignal(SignalName.QuestCompleted, questId);
        return true;
    }

    /// <summary>
    /// Mark a quest as failed. Can be called on any Active quest regardless
    /// of objective state (quest failure conditions are external).
    /// </summary>
    public bool FailQuest(string questId)
    {
        if (!_quests.TryGetValue(questId, out var quest))
        {
            GD.PrintErr($"[QuestManager] FailQuest: unknown quest '{questId}'.");
            return false;
        }

        if (quest.Status != QuestStatus.Active)
        {
            GD.PrintErr($"[QuestManager] FailQuest: quest '{questId}' is {quest.Status}, expected Active.");
            return false;
        }

        quest.Status = QuestStatus.Failed;
        RebuildEventIndex();

        GD.Print($"[QuestManager] Quest failed: {quest.Title} ({questId})");
        EmitSignal(SignalName.QuestFailed, questId);
        return true;
    }

    // -----------------------------------------------------------------
    // Queries
    // -----------------------------------------------------------------

    /// <summary>
    /// Returns all quests currently in the Active state.
    /// </summary>
    public List<QuestData> GetActiveQuests()
    {
        return _quests.Values.Where(q => q.Status == QuestStatus.Active).ToList();
    }

    /// <summary>
    /// Returns all quests that have been completed.
    /// </summary>
    public List<QuestData> GetCompletedQuests()
    {
        return _quests.Values.Where(q => q.Status == QuestStatus.Completed).ToList();
    }

    /// <summary>
    /// Returns quests that the player can accept: Available status, all prerequisites
    /// met, and player level meets the recommended minimum.
    /// </summary>
    public List<QuestData> GetAvailableQuests(Character player)
    {
        return _quests.Values
            .Where(q => q.Status == QuestStatus.Available && IsQuestAvailable(q.QuestId, player))
            .ToList();
    }

    /// <summary>
    /// Check whether a specific quest is available to the player. Validates that
    /// the quest exists, is in Available status, all prerequisite quests are
    /// completed, and the player meets the recommended level.
    /// </summary>
    public bool IsQuestAvailable(string questId, Character player)
    {
        if (!_quests.TryGetValue(questId, out var quest))
            return false;

        if (quest.Status != QuestStatus.Available)
            return false;

        // Level check
        if (player.Level < quest.RecommendedLevel)
            return false;

        // Prerequisite check: all listed quests must be completed
        foreach (string prereqId in quest.PrerequisiteQuestIds)
        {
            if (!_quests.TryGetValue(prereqId, out var prereq) || prereq.Status != QuestStatus.Completed)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Retrieve a quest by ID, or null if not found.
    /// </summary>
    public QuestData? GetQuest(string questId)
    {
        return _quests.GetValueOrDefault(questId);
    }

    /// <summary>
    /// Returns all known quests regardless of status.
    /// </summary>
    public List<QuestData> GetAllQuests()
    {
        return _quests.Values.ToList();
    }

    // -----------------------------------------------------------------
    // Save / Load
    // -----------------------------------------------------------------

    /// <summary>
    /// Serializes all quest state (including progress) to JSON for persistence.
    /// </summary>
    public string GetSaveData()
    {
        return JsonConvert.SerializeObject(_quests.Values.ToList(), Formatting.Indented);
    }

    /// <summary>
    /// Restores quest state from previously saved JSON. Replaces all current state.
    /// </summary>
    public void RestoreFromSaveData(string json)
    {
        _quests.Clear();
        _eventIndex.Clear();

        var savedQuests = JsonConvert.DeserializeObject<List<QuestData>>(json);
        if (savedQuests == null)
        {
            GD.PrintErr("[QuestManager] RestoreFromSaveData: failed to deserialize.");
            return;
        }

        foreach (var quest in savedQuests)
        {
            if (!string.IsNullOrEmpty(quest.QuestId))
                _quests[quest.QuestId] = quest;
        }

        RebuildEventIndex();
        GD.Print($"[QuestManager] Restored {_quests.Count} quests from save data.");
    }

    // -----------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Rebuilds the event-to-objective lookup index from all active quests.
    /// Called when quests are loaded, accepted, completed, or failed.
    /// </summary>
    private void RebuildEventIndex()
    {
        _eventIndex.Clear();

        foreach (var quest in _quests.Values)
        {
            if (quest.Status != QuestStatus.Active)
                continue;

            foreach (var objective in quest.Objectives)
            {
                if (objective.IsComplete)
                    continue;

                var key = (objective.Type, objective.TargetId);

                if (!_eventIndex.TryGetValue(key, out var list))
                {
                    list = new List<(string, QuestObjective)>();
                    _eventIndex[key] = list;
                }

                list.Add((quest.QuestId, objective));
            }
        }
    }

    /// <summary>
    /// Maps a QuestEventType to the corresponding ObjectiveType for index lookup.
    /// </summary>
    private static ObjectiveType MapEventToObjectiveType(QuestEventType eventType)
    {
        return eventType switch
        {
            QuestEventType.MonsterKilled => ObjectiveType.KillMonsters,
            QuestEventType.ItemCollected => ObjectiveType.CollectItems,
            QuestEventType.NPCTalkedTo => ObjectiveType.TalkToNPC,
            QuestEventType.LocationVisited => ObjectiveType.ExploreLocation,
            QuestEventType.LevelReached => ObjectiveType.ReachLevel,
            QuestEventType.CombatSurvived => ObjectiveType.SurviveCombat,
            QuestEventType.NPCEscorted => ObjectiveType.EscortNPC,
            _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown quest event type.")
        };
    }
}
