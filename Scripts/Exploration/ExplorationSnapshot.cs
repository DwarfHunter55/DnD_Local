// Canonical definitions of ExplorationState enum and ExplorationSnapshot class.
// Kept in a standalone file (outside ExplorationManager.cs) so the test project
// can include it without pulling in the Godot-dependent ExplorationManager.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Exploration state enum used by ExplorationManager and ExplorationSnapshot.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum ExplorationState
{
    Exploring,
    InDialogue,
    InShop,
    InCombat,
    Resting,
    InMenu,
    Traveling
}

/// <summary>
/// Serializable snapshot of ExplorationManager state for save/load.
/// </summary>
public class ExplorationSnapshot
{
    [JsonProperty("state")]
    [JsonConverter(typeof(StringEnumConverter))]
    public ExplorationState State { get; set; }

    [JsonProperty("locationId")]
    public string LocationId { get; set; } = string.Empty;

    [JsonProperty("locationName")]
    public string LocationName { get; set; } = string.Empty;

    [JsonProperty("locationType")]
    public string LocationType { get; set; } = string.Empty;

    [JsonProperty("locationDescription")]
    public string LocationDescription { get; set; } = string.Empty;
}
