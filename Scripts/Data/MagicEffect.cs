using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ChroniclesUnbound.Data;

/// <summary>
/// Types of magical effects that can be applied by magic items.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum MagicEffectType
{
    /// <summary>Always-on bonus (e.g. +1 weapon, Cloak of Protection).</summary>
    Passive,
    /// <summary>Damage resistance (e.g. Ring of Fire Resistance).</summary>
    Resistance,
    /// <summary>Additional damage on hit (e.g. Flame Tongue's 2d6 fire).</summary>
    ExtraDamage,
    /// <summary>Activated ability, may cost charges (e.g. Staff of Fire's Wall of Fire).</summary>
    Active,
    /// <summary>Full immunity to a damage type or condition.</summary>
    Immunity
}

/// <summary>
/// Stats that can be modified by a magic item's passive bonus.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum MagicBonusStat
{
    AttackRoll,
    DamageRoll,
    AC,
    SavingThrows,
    AbilityScore,
    SpellDC,
    Speed,
    HP
}

/// <summary>
/// Represents a single magical effect on an item. Magic items may have
/// multiple effects (e.g. a +1 longsword has Passive/AttackRoll and Passive/DamageRoll).
/// </summary>
public class MagicEffect
{
    /// <summary>
    /// The category of this effect (passive bonus, resistance, extra damage, etc.).
    /// </summary>
    [JsonProperty("type")]
    public MagicEffectType Type { get; set; }

    /// <summary>
    /// Which stat this effect modifies. Only relevant for Passive effects.
    /// </summary>
    [JsonProperty("stat", NullValueHandling = NullValueHandling.Ignore)]
    public MagicBonusStat? Stat { get; set; }

    /// <summary>
    /// The bonus value as a string. Examples: "+1", "+2", "19" (for ability score set-to).
    /// </summary>
    [JsonProperty("value")]
    public string Value { get; set; } = "";

    /// <summary>
    /// Damage type for Resistance, Immunity, or ExtraDamage effects (e.g. "Fire", "Necrotic").
    /// </summary>
    [JsonProperty("damageType")]
    public string DamageType { get; set; } = "";

    /// <summary>
    /// Dice expression for ExtraDamage effects (e.g. "1d6", "2d6").
    /// </summary>
    [JsonProperty("dice")]
    public string Dice { get; set; } = "";

    /// <summary>
    /// Conditional trigger for the effect (e.g. "undead", "fiends", "critical hit").
    /// Empty string means the effect applies unconditionally.
    /// </summary>
    [JsonProperty("condition")]
    public string Condition { get; set; } = "";

    /// <summary>
    /// Display name for Active abilities (e.g. "Fireball", "Detect Magic").
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Human-readable description of this effect for UI display.
    /// </summary>
    [JsonProperty("description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// Number of charges consumed when using an Active effect. 0 means no charge cost.
    /// </summary>
    [JsonProperty("costCharges")]
    public int CostCharges { get; set; }
}
