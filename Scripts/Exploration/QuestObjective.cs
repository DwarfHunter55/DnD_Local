using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Types of quest objectives that can be tracked by the quest system.
/// Each type maps to a specific game event for automatic progress updates.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum ObjectiveType
{
    KillMonsters,
    CollectItems,
    TalkToNPC,
    ExploreLocation,
    ReachLevel,
    SurviveCombat,
    EscortNPC
}

/// <summary>
/// A single trackable objective within a quest. Progress is incremented
/// by the QuestManager when matching game events fire (kills, item pickups,
/// NPC dialogue, location visits, etc.).
/// </summary>
public class QuestObjective
{
    [JsonProperty("objective_id")]
    public string ObjectiveId { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("type")]
    public ObjectiveType Type { get; set; }

    /// <summary>
    /// Identifier for the target: monster type name, item name, NPC id,
    /// location id, or level number (as string) depending on <see cref="Type"/>.
    /// </summary>
    [JsonProperty("target_id")]
    public string TargetId { get; set; } = string.Empty;

    [JsonProperty("required_count")]
    public int RequiredCount { get; set; } = 1;

    [JsonProperty("current_progress")]
    public int CurrentProgress { get; set; }

    /// <summary>
    /// True when the objective has been fulfilled.
    /// </summary>
    [JsonIgnore]
    public bool IsComplete => CurrentProgress >= RequiredCount;

    /// <summary>
    /// Increments progress by the given delta, clamped to RequiredCount.
    /// Returns the actual amount added.
    /// </summary>
    public int AddProgress(int delta)
    {
        if (delta <= 0 || IsComplete)
            return 0;

        int before = CurrentProgress;
        CurrentProgress = Math.Min(CurrentProgress + delta, RequiredCount);
        return CurrentProgress - before;
    }
}
