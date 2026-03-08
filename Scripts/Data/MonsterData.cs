using Newtonsoft.Json;

namespace ChroniclesUnbound.Data;

/// <summary>
/// A complete monster stat block from the D&D 5e SRD.
/// </summary>
public class MonsterData
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Size category (e.g. "Tiny", "Small", "Medium", "Large", "Huge", "Gargantuan").
    /// </summary>
    [JsonProperty("size")]
    public string Size { get; set; } = string.Empty;

    /// <summary>
    /// Creature type (e.g. "Beast", "Undead", "Fiend", "Humanoid").
    /// </summary>
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("alignment")]
    public Alignment Alignment { get; set; }

    [JsonProperty("armorClass")]
    public int ArmorClass { get; set; }

    [JsonProperty("hitPoints")]
    public int HitPoints { get; set; }

    /// <summary>
    /// Hit dice notation (e.g. "4d8+8").
    /// </summary>
    [JsonProperty("hitDice")]
    public string HitDice { get; set; } = string.Empty;

    /// <summary>
    /// Speed text including all movement modes (e.g. "30 ft., fly 60 ft.").
    /// </summary>
    [JsonProperty("speed")]
    public string Speed { get; set; } = string.Empty;

    [JsonProperty("abilityScores")]
    public AbilityScores AbilityScores { get; set; } = new();

    /// <summary>
    /// Skill bonuses as total modifier values (e.g. Perception = +5).
    /// Only lists skills the monster is proficient in.
    /// </summary>
    [JsonProperty("skills")]
    public Dictionary<Skill, int> Skills { get; set; } = new();

    [JsonProperty("damageResistances")]
    public List<DamageType> DamageResistances { get; set; } = new();

    [JsonProperty("damageImmunities")]
    public List<DamageType> DamageImmunities { get; set; } = new();

    [JsonProperty("damageVulnerabilities")]
    public List<DamageType> DamageVulnerabilities { get; set; } = new();

    [JsonProperty("conditionImmunities")]
    public List<Condition> ConditionImmunities { get; set; } = new();

    /// <summary>
    /// Senses text (e.g. "darkvision 60 ft., passive Perception 14").
    /// </summary>
    [JsonProperty("senses")]
    public string Senses { get; set; } = string.Empty;

    /// <summary>
    /// Languages text (e.g. "Common, Draconic" or "--" for none).
    /// </summary>
    [JsonProperty("languages")]
    public string Languages { get; set; } = string.Empty;

    /// <summary>
    /// Challenge Rating (e.g. 0.25 for CR 1/4, 0.5 for CR 1/2, 1.0 for CR 1).
    /// </summary>
    [JsonProperty("challengeRating")]
    public float ChallengeRating { get; set; }

    /// <summary>
    /// Experience points awarded for defeating this monster.
    /// </summary>
    [JsonProperty("xp")]
    public int XP { get; set; }

    /// <summary>
    /// Combat actions available to this monster.
    /// </summary>
    [JsonProperty("actions")]
    public List<MonsterAction> Actions { get; set; } = new();

    /// <summary>
    /// Passive traits and special abilities.
    /// </summary>
    [JsonProperty("traits")]
    public List<MonsterTrait> Traits { get; set; } = new();
}
