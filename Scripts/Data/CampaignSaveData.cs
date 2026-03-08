using Newtonsoft.Json;
using ChroniclesUnbound.Companions;
using ChroniclesUnbound.Exploration;

namespace ChroniclesUnbound.Data;

/// <summary>
/// Serializable DTO that captures the full campaign state for a single save slot.
/// Aggregates snapshots from ExplorationManager, QuestManager, WorldMap,
/// RelationshipManager, and the narrative history. Stored as a single JSON blob
/// in the GameState.CampaignDataJson column.
///
/// All fields are nullable so partial saves work (e.g. a brand-new game won't
/// have quest data or relationship histories yet).
/// </summary>
public class CampaignSaveData
{
    // -- Exploration state --------------------------------------------------

    /// <summary>
    /// Serialized <see cref="ExplorationSnapshot"/> from ExplorationManager.GetSnapshot().
    /// Contains current state, location ID/name/type/description.
    /// </summary>
    [JsonProperty("explorationSnapshot")]
    public ExplorationSnapshot? ExplorationSnapshot { get; set; }

    // -- Quest state --------------------------------------------------------

    /// <summary>
    /// Serialized quest data from QuestManager.GetSaveData().
    /// This is already a JSON string internally, but we store the raw value
    /// and let Newtonsoft handle the nested serialization.
    /// </summary>
    [JsonProperty("questDataJson")]
    public string? QuestDataJson { get; set; }

    // -- World map discovery ------------------------------------------------

    /// <summary>
    /// Location discovery state from WorldMap.GetDiscoveryState().
    /// Key = location ID, Value = whether the location has been discovered.
    /// </summary>
    [JsonProperty("worldDiscovery")]
    public Dictionary<string, bool>? WorldDiscovery { get; set; }

    // -- Relationships ------------------------------------------------------

    /// <summary>
    /// Per-companion relationship scores from RelationshipManager.GetState().
    /// Key = companion ID, Value = score in [-100, +100].
    /// </summary>
    [JsonProperty("relationshipScores")]
    public Dictionary<string, int>? RelationshipScores { get; set; }

    /// <summary>
    /// Per-companion relationship event histories from RelationshipManager.GetState().
    /// Key = companion ID, Value = ordered list of events.
    /// </summary>
    [JsonProperty("relationshipHistories")]
    public Dictionary<string, List<RelationshipEvent>>? RelationshipHistories { get; set; }

    // -- Journal state ------------------------------------------------------

    /// <summary>
    /// Serialized journal data from JournalManager.GetSaveData().
    /// Contains all journal entries and the next ID counter.
    /// </summary>
    [JsonProperty("journalDataJson")]
    public string? JournalDataJson { get; set; }

    // -- Narrative / UI state -----------------------------------------------

    /// <summary>
    /// Recent narrative text lines displayed to the player. Capped at a
    /// reasonable limit (e.g. last 50 lines) to keep the save file small.
    /// Restored into the UI on load so the player has context.
    /// </summary>
    [JsonProperty("narrativeHistory")]
    public List<string>? NarrativeHistory { get; set; }

    /// <summary>
    /// A short description of the current scene/situation. Displayed in the
    /// save slot UI and restored as the opening text on load.
    /// </summary>
    [JsonProperty("currentScene")]
    public string? CurrentScene { get; set; }

    /// <summary>
    /// The list of suggested player actions that were showing in the UI at
    /// save time. Restored on load so the player can continue seamlessly.
    /// </summary>
    [JsonProperty("suggestedActions")]
    public List<string>? SuggestedActions { get; set; }

    // -- Serialization helpers ----------------------------------------------

    /// <summary>
    /// Serialize this DTO to a JSON string for database storage.
    /// </summary>
    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, Formatting.None,
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
    }

    /// <summary>
    /// Deserialize a CampaignSaveData from a JSON string.
    /// Returns null if the input is null, empty, or unparseable.
    /// </summary>
    public static CampaignSaveData? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonConvert.DeserializeObject<CampaignSaveData>(json);
        }
        catch (JsonException)
        {
            Godot.GD.PrintErr("[CampaignSaveData] Failed to deserialize campaign data.");
            return null;
        }
    }
}
