using Newtonsoft.Json;

namespace ChroniclesUnbound.Data;

/// <summary>
/// Definition of a spell from the D&D 5e SRD.
/// Level 0 represents cantrips.
/// </summary>
public class SpellData
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Spell level from 0 (cantrip) to 9.
    /// </summary>
    [JsonProperty("level")]
    public int Level { get; set; }

    [JsonProperty("school")]
    public SpellSchool School { get; set; }

    /// <summary>
    /// Casting time text (e.g. "1 action", "1 bonus action", "1 minute").
    /// </summary>
    [JsonProperty("castingTime")]
    public string CastingTime { get; set; } = string.Empty;

    /// <summary>
    /// Range text (e.g. "120 feet", "Self", "Touch", "Self (30-foot cone)").
    /// </summary>
    [JsonProperty("range")]
    public string Range { get; set; } = string.Empty;

    /// <summary>
    /// Components string (e.g. "V, S, M (a pinch of sulfur)").
    /// </summary>
    [JsonProperty("components")]
    public string Components { get; set; } = string.Empty;

    /// <summary>
    /// Duration text (e.g. "Instantaneous", "Concentration, up to 1 minute", "1 hour").
    /// </summary>
    [JsonProperty("duration")]
    public string Duration { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("isConcentration")]
    public bool IsConcentration { get; set; }

    [JsonProperty("isRitual")]
    public bool IsRitual { get; set; }

    /// <summary>
    /// Class names that can learn this spell (e.g. "Wizard", "Cleric").
    /// </summary>
    [JsonProperty("classes")]
    public List<string> Classes { get; set; } = new();

    /// <summary>
    /// Damage dice notation (e.g. "8d6"). Empty string for non-damaging spells.
    /// </summary>
    [JsonProperty("damage")]
    public string Damage { get; set; } = string.Empty;

    [JsonProperty("damageType")]
    public DamageType DamageType { get; set; }

    /// <summary>
    /// Ability score used for saving throw, if any. Null for spells with no save.
    /// </summary>
    [JsonProperty("savingThrow")]
    public Ability? SavingThrow { get; set; }

    /// <summary>
    /// Description of effects when cast at a higher spell slot level.
    /// Empty if the spell does not scale.
    /// </summary>
    [JsonProperty("higherLevelDescription")]
    public string HigherLevelDescription { get; set; } = string.Empty;
}
