using Newtonsoft.Json;

namespace ChroniclesUnbound.Data;

/// <summary>
/// Represents a D&D 5e race as defined in the SRD JSON data.
/// Maps directly to the races.json schema.
/// </summary>
public class RaceData
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("speed")]
    public int Speed { get; set; } = 30;

    [JsonProperty("size")]
    public string Size { get; set; } = "Medium";

    [JsonProperty("darkvision")]
    public int Darkvision { get; set; }

    [JsonProperty("speed_note")]
    public string SpeedNote { get; set; } = string.Empty;

    [JsonProperty("ability_bonuses")]
    public Dictionary<string, int> AbilityBonuses { get; set; } = new();

    /// <summary>
    /// Optional ability bonus choices (e.g. Half-Elf picks 2 abilities for +1 each).
    /// </summary>
    [JsonProperty("ability_bonus_choices")]
    public AbilityBonusChoice? AbilityBonusChoices { get; set; }

    [JsonProperty("traits")]
    public List<string> Traits { get; set; } = new();

    [JsonProperty("languages")]
    public List<string> Languages { get; set; } = new();

    [JsonProperty("proficiencies")]
    public RaceProficiencies Proficiencies { get; set; } = new();

    [JsonProperty("skill_choices")]
    public SkillChoiceData? SkillChoices { get; set; }

    [JsonProperty("damage_resistances")]
    public List<string> DamageResistances { get; set; } = new();

    [JsonProperty("condition_advantages")]
    public List<string> ConditionAdvantages { get; set; } = new();

    [JsonProperty("spells")]
    public RaceSpellData? Spells { get; set; }

    [JsonProperty("draconic_ancestry")]
    public List<DraconicAncestry> DraconicAncestryOptions { get; set; } = new();

    [JsonProperty("subraces")]
    public List<SubraceData> Subraces { get; set; } = new();
}

/// <summary>
/// Ability bonus choice for races like Half-Elf that let you pick which scores to boost.
/// </summary>
public class AbilityBonusChoice
{
    [JsonProperty("count")]
    public int Count { get; set; }

    [JsonProperty("amount")]
    public int Amount { get; set; } = 1;

    [JsonProperty("from")]
    public List<string> From { get; set; } = new();
}

/// <summary>
/// Proficiencies granted by a race (weapons, armor, skills, tools).
/// </summary>
public class RaceProficiencies
{
    [JsonProperty("weapons")]
    public List<string> Weapons { get; set; } = new();

    [JsonProperty("armor")]
    public List<string> Armor { get; set; } = new();

    [JsonProperty("skills")]
    public List<string> Skills { get; set; } = new();

    [JsonProperty("tools")]
    public List<string> Tools { get; set; } = new();

    [JsonProperty("tools_choice")]
    public ToolChoiceData? ToolsChoice { get; set; }
}

/// <summary>
/// A choice of tools from a list (e.g. Dwarf picks 1 from smith's/brewer's/mason's).
/// </summary>
public class ToolChoiceData
{
    [JsonProperty("count")]
    public int Count { get; set; }

    [JsonProperty("from")]
    public List<string> From { get; set; } = new();
}

/// <summary>
/// Skill choice data for races that grant skill proficiency choices.
/// </summary>
public class SkillChoiceData
{
    [JsonProperty("count")]
    public int Count { get; set; }

    /// <summary>
    /// List of skill names, or "any" for any skill.
    /// </summary>
    [JsonProperty("from")]
    public object From { get; set; } = new List<string>();

    /// <summary>
    /// Returns the skill list. If "any", returns all 18 skills as strings.
    /// </summary>
    [JsonIgnore]
    public List<string> AvailableSkills
    {
        get
        {
            if (From is string s && s.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                return Enum.GetNames<Skill>().ToList();
            }

            if (From is Newtonsoft.Json.Linq.JArray arr)
            {
                return arr.Select(t => t.ToString()).ToList();
            }

            if (From is List<string> list)
            {
                return list;
            }

            return new List<string>();
        }
    }
}

/// <summary>
/// Innate spellcasting granted by a race (e.g. Drow Magic, Infernal Legacy).
/// </summary>
public class RaceSpellData
{
    [JsonProperty("innate")]
    public bool Innate { get; set; }

    [JsonProperty("spellcasting_ability")]
    public string SpellcastingAbility { get; set; } = string.Empty;

    [JsonProperty("known")]
    public List<RaceSpellEntry> Known { get; set; } = new();
}

public class RaceSpellEntry
{
    [JsonProperty("spell")]
    public string Spell { get; set; } = string.Empty;

    [JsonProperty("level_required")]
    public int LevelRequired { get; set; }

    [JsonProperty("usage")]
    public string Usage { get; set; } = string.Empty;

    [JsonProperty("cast_at_level")]
    public int? CastAtLevel { get; set; }
}

/// <summary>
/// Dragonborn draconic ancestry option.
/// </summary>
public class DraconicAncestry
{
    [JsonProperty("dragon")]
    public string Dragon { get; set; } = string.Empty;

    [JsonProperty("damage_type")]
    public string DamageType { get; set; } = string.Empty;

    [JsonProperty("breath_weapon")]
    public string BreathWeapon { get; set; } = string.Empty;
}

/// <summary>
/// A subrace within a parent race (e.g. High Elf, Hill Dwarf).
/// </summary>
public class SubraceData
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("speed")]
    public int? Speed { get; set; }

    [JsonProperty("darkvision")]
    public int? Darkvision { get; set; }

    [JsonProperty("ability_bonuses")]
    public Dictionary<string, int> AbilityBonuses { get; set; } = new();

    [JsonProperty("traits")]
    public List<string> Traits { get; set; } = new();

    [JsonProperty("proficiencies")]
    public RaceProficiencies? Proficiencies { get; set; }

    [JsonProperty("damage_resistances")]
    public List<string> DamageResistances { get; set; } = new();

    [JsonProperty("condition_advantages")]
    public List<string> ConditionAdvantages { get; set; } = new();

    [JsonProperty("hit_point_bonus_per_level")]
    public int HitPointBonusPerLevel { get; set; }

    [JsonProperty("cantrip_choices")]
    public CantripChoiceData? CantripChoices { get; set; }

    [JsonProperty("spells")]
    public RaceSpellData? Spells { get; set; }
}

/// <summary>
/// Cantrip choices granted by a subrace (e.g. High Elf wizard cantrip, Forest Gnome minor illusion).
/// </summary>
public class CantripChoiceData
{
    [JsonProperty("count")]
    public int Count { get; set; }

    /// <summary>
    /// Class spell list to choose from (e.g. "wizard"), or null if fixed.
    /// </summary>
    [JsonProperty("from")]
    public string? From { get; set; }

    /// <summary>
    /// Fixed cantrip list (e.g. Forest Gnome always gets minor illusion).
    /// </summary>
    [JsonProperty("fixed")]
    public List<string> Fixed { get; set; } = new();

    [JsonProperty("spellcasting_ability")]
    public string SpellcastingAbility { get; set; } = string.Empty;
}
