using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChroniclesUnbound.Data;

/// <summary>
/// Deserialization model for a class entry from the SRD classes.json.
/// Supports full casters (Bard, Cleric, Druid, Sorcerer, Wizard),
/// half casters (Paladin, Ranger), and pact magic (Warlock).
/// </summary>
public class ClassData
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("hit_die")]
    public int HitDie { get; set; }

    [JsonProperty("primary_ability")]
    public string PrimaryAbility { get; set; } = string.Empty;

    [JsonProperty("saving_throws")]
    public List<string> SavingThrows { get; set; } = new();

    [JsonProperty("armor_proficiencies")]
    public List<string> ArmorProficiencies { get; set; } = new();

    [JsonProperty("weapon_proficiencies")]
    public List<string> WeaponProficiencies { get; set; } = new();

    [JsonProperty("skill_choices")]
    public ClassSkillChoices SkillChoices { get; set; } = new();

    [JsonProperty("subclass_name")]
    public string SubclassName { get; set; } = string.Empty;

    [JsonProperty("subclass_level")]
    public int SubclassLevel { get; set; }

    /// <summary>
    /// Null for non-casters (Barbarian, Fighter, Monk, Rogue).
    /// </summary>
    [JsonProperty("spellcasting")]
    public SpellcastingData? Spellcasting { get; set; }

    /// <summary>
    /// Features keyed by level (string "1"-"20"), each containing a list of feature info.
    /// </summary>
    [JsonProperty("features")]
    public Dictionary<string, List<FeatureInfo>> Features { get; set; } = new();

    /// <summary>
    /// Default subclass provided in the SRD data (e.g. "Path of the Berserker", "College of Lore").
    /// </summary>
    [JsonProperty("subclass")]
    public SubclassData? Subclass { get; set; }

    // ── Convenience Methods ──────────────────────────────────────────

    /// <summary>
    /// Returns all features for a given character level (1-indexed).
    /// </summary>
    public List<FeatureInfo> GetFeaturesAtLevel(int level)
    {
        string key = level.ToString();
        return Features.TryGetValue(key, out var features) ? features : new List<FeatureInfo>();
    }

    /// <summary>
    /// Returns the spell slot table entry for a given character level, or null if the class
    /// has no spellcasting or no entry for that level.
    /// </summary>
    public SpellSlotEntry? GetSpellSlotsAtLevel(int level)
    {
        return Spellcasting?.GetSlotsAtLevel(level);
    }
}

public class ClassSkillChoices
{
    [JsonProperty("count")]
    public int Count { get; set; }

    [JsonProperty("from")]
    public List<string> From { get; set; } = new();
}

/// <summary>
/// A named class or subclass feature with a text description.
/// </summary>
public class FeatureInfo
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Spellcasting data for a class. The spell_slots object uses level numbers as keys.
/// Each value is a dictionary of slot-level names ("cantrips", "1st", "2nd", etc.)
/// to slot counts. Warlock pact magic also includes "slots", "slot_level", and
/// mystic arcanum fields.
/// </summary>
public class SpellcastingData
{
    [JsonProperty("ability")]
    public string Ability { get; set; } = string.Empty;

    /// <summary>
    /// Raw spell slot table keyed by class level ("1"-"20").
    /// Each value is a JObject with fields like "cantrips", "1st", "2nd", ..., "9th",
    /// and for Warlock: "slots", "slot_level", "6th_mystic_arcanum", etc.
    /// </summary>
    [JsonProperty("spell_slots")]
    public Dictionary<string, JObject> SpellSlots { get; set; } = new();

    /// <summary>
    /// Returns a parsed SpellSlotEntry for a given class level.
    /// </summary>
    public SpellSlotEntry? GetSlotsAtLevel(int level)
    {
        string key = level.ToString();
        if (!SpellSlots.TryGetValue(key, out var raw))
            return null;

        return SpellSlotEntry.FromJObject(raw);
    }
}

/// <summary>
/// Parsed spell slot information for a single class level.
/// Handles both standard casters and Warlock pact magic.
/// </summary>
public class SpellSlotEntry
{
    public int Cantrips { get; set; }

    /// <summary>
    /// Standard spell slots indexed 0-8 for spell levels 1-9.
    /// For Warlocks, all pact slots are stored at their slot_level index.
    /// </summary>
    public int[] Slots { get; set; } = new int[9];

    // ── Warlock Pact Magic ───────────────────────────────────────────

    /// <summary>
    /// True if this class uses pact magic (Warlock). Pact slots are all the same level
    /// and recharge on short rest.
    /// </summary>
    public bool IsPactMagic { get; set; }

    /// <summary>
    /// For Warlock: the level at which all pact slots are cast (1-5).
    /// </summary>
    public int PactSlotLevel { get; set; }

    /// <summary>
    /// For Warlock: the number of pact magic slots.
    /// </summary>
    public int PactSlotCount { get; set; }

    /// <summary>
    /// Mystic Arcanum slots (one each for spell levels 6-9), gained at higher Warlock levels.
    /// Indexed 0-3 for spell levels 6-9.
    /// </summary>
    public int[] MysticArcanum { get; set; } = new int[4];

    /// <summary>
    /// Populates the Slots array for a character's SpellSlotsMax from this entry.
    /// For standard casters, copies Slots directly. For Warlocks, fills pact slots
    /// at the correct level and adds mystic arcanum.
    /// </summary>
    public void ApplyToSlotArray(int[] target)
    {
        if (target.Length < 9)
            throw new ArgumentException("Target array must have at least 9 elements (spell levels 1-9).");

        if (IsPactMagic)
        {
            // Pact slots go into the slot_level index.
            if (PactSlotLevel >= 1 && PactSlotLevel <= 9)
                target[PactSlotLevel - 1] = PactSlotCount;

            // Mystic Arcanum: one use each for levels 6-9.
            for (int i = 0; i < 4; i++)
            {
                if (MysticArcanum[i] > 0)
                    target[5 + i] = MysticArcanum[i];
            }
        }
        else
        {
            Array.Copy(Slots, target, 9);
        }
    }

    // ── Parsing ──────────────────────────────────────────────────────

    private static readonly string[] StandardSlotKeys =
        { "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "9th" };

    private static readonly string[] MysticArcanumKeys =
        { "6th_mystic_arcanum", "7th_mystic_arcanum", "8th_mystic_arcanum", "9th_mystic_arcanum" };

    internal static SpellSlotEntry FromJObject(JObject obj)
    {
        var entry = new SpellSlotEntry();

        entry.Cantrips = obj.Value<int?>("cantrips") ?? 0;

        // Detect Warlock pact magic by the presence of "slot_level".
        if (obj.ContainsKey("slot_level"))
        {
            entry.IsPactMagic = true;
            entry.PactSlotLevel = obj.Value<int?>("slot_level") ?? 0;
            entry.PactSlotCount = obj.Value<int?>("slots") ?? 0;

            for (int i = 0; i < MysticArcanumKeys.Length; i++)
            {
                entry.MysticArcanum[i] = obj.Value<int?>(MysticArcanumKeys[i]) ?? 0;
            }
        }
        else
        {
            // Standard or half-caster spell slots.
            for (int i = 0; i < StandardSlotKeys.Length; i++)
            {
                entry.Slots[i] = obj.Value<int?>(StandardSlotKeys[i]) ?? 0;
            }
        }

        return entry;
    }
}

/// <summary>
/// SRD-provided default subclass for a class (e.g. "Path of the Berserker" for Barbarian).
/// </summary>
public class SubclassData
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("features")]
    public Dictionary<string, List<FeatureInfo>> Features { get; set; } = new();

    /// <summary>
    /// Returns all subclass features for a given character level.
    /// </summary>
    public List<FeatureInfo> GetFeaturesAtLevel(int level)
    {
        string key = level.ToString();
        return Features.TryGetValue(key, out var features) ? features : new List<FeatureInfo>();
    }
}
