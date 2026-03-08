using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ChroniclesUnbound.Journal;

[JsonConverter(typeof(StringEnumConverter))]
public enum JournalCategory
{
    Quest,
    NPC,
    Location,
    Combat,
    Item,
    LevelUp,
    Trade,
    Note
}

public class JournalEntry
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("category")]
    public JournalCategory Category { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; } = "";

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    [JsonProperty("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonProperty("realTimestamp")]
    public string RealTimestamp { get; set; } = "";

    [JsonProperty("relatedEntityId", NullValueHandling = NullValueHandling.Ignore)]
    public string? RelatedEntityId { get; set; }

    [JsonProperty("locationName", NullValueHandling = NullValueHandling.Ignore)]
    public string? LocationName { get; set; }

    [JsonProperty("isRead")]
    public bool IsRead { get; set; }

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();
}
