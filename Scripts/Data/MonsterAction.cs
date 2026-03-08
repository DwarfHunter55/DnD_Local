using Newtonsoft.Json;

namespace ChroniclesUnbound.Data;

/// <summary>
/// A single action in a monster's stat block (melee attack, ranged attack,
/// breath weapon, etc.).
/// </summary>
public class MonsterAction
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Attack bonus to hit, if this is an attack action. Null for non-attack actions.
    /// </summary>
    [JsonProperty("attackBonus")]
    public int? AttackBonus { get; set; }

    /// <summary>
    /// Damage dice notation (e.g. "2d6+3"). Empty for non-damaging actions.
    /// </summary>
    [JsonProperty("damage")]
    public string Damage { get; set; } = string.Empty;

    [JsonProperty("damageType")]
    public DamageType DamageType { get; set; }

    /// <summary>
    /// Melee reach (e.g. "5 ft.", "10 ft."). Empty for non-melee actions.
    /// </summary>
    [JsonProperty("reach")]
    public string Reach { get; set; } = string.Empty;

    /// <summary>
    /// Ranged attack range (e.g. "80/320 ft."). Empty for melee-only actions.
    /// </summary>
    [JsonProperty("range")]
    public string Range { get; set; } = string.Empty;
}
