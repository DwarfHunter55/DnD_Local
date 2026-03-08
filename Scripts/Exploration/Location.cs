using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Data class representing a single location in the world graph.
/// Loaded from JSON and referenced by the WorldMap manager.
/// All properties use [JsonProperty] for consistent snake_case serialization
/// matching the project's SRD JSON conventions.
/// </summary>
public class Location
{
    // ── Identity ─────────────────────────────────────────────────────

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public LocationType Type { get; set; } = LocationType.Wilderness;

    // ── Graph Connections ────────────────────────────────────────────

    /// <summary>
    /// IDs of locations directly reachable from this one. Connections are
    /// bidirectional — if A lists B, B should also list A in the data file.
    /// </summary>
    [JsonProperty("connected_location_ids")]
    public List<string> ConnectedLocationIds { get; set; } = new();

    // ── Population & Services ────────────────────────────────────────

    /// <summary>
    /// IDs of NPCs available at this location for dialogue, quests, or trade.
    /// </summary>
    [JsonProperty("available_npc_ids")]
    public List<string> AvailableNPCIds { get; set; } = new();

    /// <summary>
    /// IDs of shops the player can visit at this location.
    /// </summary>
    [JsonProperty("shop_ids")]
    public List<string> ShopIds { get; set; } = new();

    // ── Encounters ───────────────────────────────────────────────────

    /// <summary>
    /// Reference to an encounter table that defines random encounters for
    /// this location. Null or empty means no random encounters (safe area).
    /// </summary>
    [JsonProperty("encounter_table_id")]
    public string EncounterTableId { get; set; } = string.Empty;

    /// <summary>
    /// Danger level from 0 (safe town) to 5 (deadly dungeon).
    /// Used for encounter difficulty scaling and travel risk calculations.
    /// </summary>
    [JsonProperty("danger_level")]
    public int DangerLevel
    {
        get => _dangerLevel;
        set => _dangerLevel = Math.Clamp(value, 0, 5);
    }
    private int _dangerLevel;

    // ── Discovery ────────────────────────────────────────────────────

    /// <summary>
    /// Whether the player has discovered this location. Undiscovered locations
    /// do not appear on the map and cannot be traveled to directly.
    /// </summary>
    [JsonProperty("is_discovered")]
    public bool IsDiscovered { get; set; }

    /// <summary>
    /// Optional requirement that must be met before this location can be
    /// discovered. Format: "quest:quest_id", "level:N", or empty for none.
    /// </summary>
    [JsonProperty("discovery_requirement")]
    public string DiscoveryRequirement { get; set; } = string.Empty;

    // ── Tags ─────────────────────────────────────────────────────────

    /// <summary>
    /// Freeform tags describing location properties. Common tags:
    /// "safe_rest" — long rest allowed without ambush risk,
    /// "fast_travel" — can be used as a fast travel waypoint,
    /// "quest_hub" — has quest-giving NPCs,
    /// "hidden" — requires perception/investigation to find,
    /// "shop" — has vendors.
    /// </summary>
    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the location type as the lowercase string the ExplorationManager
    /// expects for its CurrentLocationType field.
    /// </summary>
    [JsonIgnore]
    public string TypeString => Type.ToString().ToLowerInvariant();

    /// <summary>
    /// Whether this location is safe for resting without ambush risk.
    /// </summary>
    [JsonIgnore]
    public bool IsSafeRest => Tags.Contains("safe_rest");

    /// <summary>
    /// Whether this location can serve as a fast travel waypoint.
    /// </summary>
    [JsonIgnore]
    public bool IsFastTravel => Tags.Contains("fast_travel");

    public override string ToString() => $"{Name} ({Type}, danger {DangerLevel})";
}
