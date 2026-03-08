using Newtonsoft.Json;

namespace ChroniclesUnbound.Data;

/// <summary>
/// Deserialization model for a background entry from the SRD backgrounds.json.
/// </summary>
public class BackgroundData
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("skill_proficiencies")]
    public List<string> SkillProficiencies { get; set; } = new();

    [JsonProperty("tool_proficiencies")]
    public List<string> ToolProficiencies { get; set; } = new();

    /// <summary>
    /// Number of additional languages the background grants (player's choice).
    /// </summary>
    [JsonProperty("languages")]
    public int Languages { get; set; }

    [JsonProperty("equipment")]
    public List<string> Equipment { get; set; } = new();

    [JsonProperty("feature")]
    public BackgroundFeature Feature { get; set; } = new();

    [JsonProperty("personality_traits")]
    public List<string> PersonalityTraits { get; set; } = new();

    [JsonProperty("ideals")]
    public List<string> Ideals { get; set; } = new();

    [JsonProperty("bonds")]
    public List<string> Bonds { get; set; } = new();

    [JsonProperty("flaws")]
    public List<string> Flaws { get; set; } = new();
}

/// <summary>
/// A background's named feature with a text description
/// (e.g. "Shelter of the Faithful", "Criminal Contact").
/// </summary>
public class BackgroundFeature
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;
}
