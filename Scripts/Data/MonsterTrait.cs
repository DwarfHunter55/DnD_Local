using Newtonsoft.Json;

namespace ChroniclesUnbound.Data;

/// <summary>
/// A passive trait or special ability on a monster stat block
/// (e.g. "Keen Senses", "Pack Tactics", "Spellcasting").
/// </summary>
public class MonsterTrait
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;
}
