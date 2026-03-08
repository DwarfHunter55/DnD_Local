using Newtonsoft.Json;

namespace ChroniclesUnbound.Companions;

/// <summary>
/// Categories of events that can affect a companion's relationship score.
/// </summary>
public enum RelationshipEventType
{
    /// <summary>Player made a choice in combat that the companion approves/disapproves of.</summary>
    CombatChoice,

    /// <summary>Player selected a dialogue option that pleased or upset the companion.</summary>
    DialogueResponse,

    /// <summary>Player made a quest-related decision aligned or opposed to the companion's values.</summary>
    QuestDecision,

    /// <summary>Player gave the companion a gift (item, gold, etc.).</summary>
    GiftGiven,

    /// <summary>The companion was knocked unconscious during combat.</summary>
    CompanionDowned,

    /// <summary>Player stabilized or healed the companion from dying.</summary>
    CompanionSaved,

    /// <summary>Player acted against the companion's interests or trust.</summary>
    Betrayal,

    /// <summary>Party won a significant battle together.</summary>
    SharedVictory,

    /// <summary>Player completed a stage of the companion's personal quest.</summary>
    PersonalQuestProgress,

    /// <summary>Player chose to rest or heal the party when companions needed it.</summary>
    RestDecision,

    /// <summary>Player protected the companion from danger at personal cost.</summary>
    SacrificialAct,

    /// <summary>Passive decay or growth from time spent together.</summary>
    TimePassage,

    /// <summary>Catch-all for events from LLM narrative or scripted encounters.</summary>
    Narrative
}

/// <summary>
/// Records a single event that affected a companion's relationship score.
/// Stored in the companion's relationship history for UI display and LLM context.
/// </summary>
public class RelationshipEvent
{
    /// <summary>
    /// The category of event that triggered this score change.
    /// </summary>
    [JsonProperty("eventType")]
    public RelationshipEventType EventType { get; set; }

    /// <summary>
    /// How much the relationship score changed. Positive = improved, negative = worsened.
    /// Typical range: -20 to +20 for most events, up to +/-50 for major story beats.
    /// </summary>
    [JsonProperty("scoreChange")]
    public int ScoreChange { get; set; }

    /// <summary>
    /// Human-readable description of what happened (e.g. "You saved Lyra from the collapsing bridge").
    /// Used in the relationship history UI and as LLM context for future interactions.
    /// </summary>
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// When this event occurred, in game time or real time.
    /// Serialized as ISO 8601 for SQLite compatibility.
    /// </summary>
    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The relationship score after this event was applied. Set by RelationshipManager.
    /// </summary>
    [JsonProperty("resultingScore")]
    public int ResultingScore { get; set; }

    public override string ToString()
    {
        string direction = ScoreChange >= 0 ? "+" : "";
        return $"[{EventType}] {Description} ({direction}{ScoreChange})";
    }
}
