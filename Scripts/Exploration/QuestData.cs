using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Tracks the lifecycle of a quest from availability through completion or failure.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum QuestStatus
{
    Available,
    Active,
    Completed,
    Failed
}

/// <summary>
/// Event types that the quest system listens to for automatic objective updates.
/// External systems call <see cref="QuestManager.NotifyEvent"/> with these types.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum QuestEventType
{
    MonsterKilled,
    ItemCollected,
    NPCTalkedTo,
    LocationVisited,
    LevelReached,
    CombatSurvived,
    NPCEscorted
}

/// <summary>
/// Full quest definition loaded from quests.json. Contains objectives, rewards,
/// prerequisites, and status tracking. Uses snake_case JSON properties to match
/// the project's data file conventions (see locations.json, races.json, etc.).
/// </summary>
public class QuestData
{
    [JsonProperty("quest_id")]
    public string QuestId { get; set; } = string.Empty;

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// NPC who gives or introduces this quest. Empty for quests discovered organically.
    /// </summary>
    [JsonProperty("giver_npc_id")]
    public string GiverNPCId { get; set; } = string.Empty;

    [JsonProperty("objectives")]
    public List<QuestObjective> Objectives { get; set; } = new();

    // ── Rewards ──────────────────────────────────────────────────────

    [JsonProperty("xp_reward")]
    public int XPReward { get; set; }

    [JsonProperty("gold_reward")]
    public int GoldReward { get; set; }

    [JsonProperty("item_rewards")]
    public List<string> ItemRewards { get; set; } = new();

    // ── Prerequisites & Metadata ─────────────────────────────────────

    /// <summary>
    /// Quest IDs that must be completed before this quest becomes available.
    /// </summary>
    [JsonProperty("prerequisite_quest_ids")]
    public List<string> PrerequisiteQuestIds { get; set; } = new();

    /// <summary>
    /// True for quests that are part of the main story arc.
    /// </summary>
    [JsonProperty("is_main_story")]
    public bool IsMainStory { get; set; }

    /// <summary>
    /// Suggested minimum player level for this quest.
    /// </summary>
    [JsonProperty("recommended_level")]
    public int RecommendedLevel { get; set; } = 1;

    [JsonProperty("status")]
    public QuestStatus Status { get; set; } = QuestStatus.Available;

    // ── Computed ─────────────────────────────────────────────────────

    /// <summary>
    /// True when every objective in the quest is complete.
    /// </summary>
    [JsonIgnore]
    public bool AllObjectivesComplete => Objectives.Count > 0 && Objectives.TrueForAll(o => o.IsComplete);
}
