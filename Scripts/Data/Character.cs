using Newtonsoft.Json;

namespace ChroniclesUnbound.Data;

/// <summary>
/// Full D&D 5e character sheet for both the player character and AI companions.
/// All properties are serializable for JSON persistence.
/// </summary>
public class Character
{
    // ── Identity ──────────────────────────────────────────────────────

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Race name (e.g. "Human", "Elf", "Dwarf"). String for SRD flexibility.
    /// </summary>
    [JsonProperty("race")]
    public string Race { get; set; } = string.Empty;

    /// <summary>
    /// Class name (e.g. "Fighter", "Wizard"). String to support multi-class later.
    /// </summary>
    [JsonProperty("characterClassName")]
    public string CharacterClassName { get; set; } = string.Empty;

    [JsonProperty("level")]
    public int Level { get; set; } = 1;

    [JsonProperty("background")]
    public string Background { get; set; } = string.Empty;

    [JsonProperty("alignment")]
    public Alignment Alignment { get; set; } = Alignment.TrueNeutral;

    // ── Ability Scores ────────────────────────────────────────────────

    [JsonProperty("abilityScores")]
    public AbilityScores AbilityScores { get; set; } = new();

    // ── Combat Stats ──────────────────────────────────────────────────

    [JsonProperty("hitPoints")]
    public int HitPoints { get; set; }

    [JsonProperty("maxHitPoints")]
    public int MaxHitPoints { get; set; }

    [JsonProperty("tempHitPoints")]
    public int TempHitPoints { get; set; }

    [JsonProperty("armorClass")]
    public int ArmorClass { get; set; } = 10;

    [JsonProperty("initiative")]
    public int Initiative { get; set; }

    /// <summary>
    /// Base walking speed in feet.
    /// </summary>
    [JsonProperty("speed")]
    public int Speed { get; set; } = 30;

    // ── Progression ───────────────────────────────────────────────────

    [JsonProperty("experiencePoints")]
    public int ExperiencePoints { get; set; }

    /// <summary>
    /// Class features, racial traits, and feat names.
    /// </summary>
    [JsonProperty("features")]
    public List<string> Features { get; set; } = new();

    /// <summary>
    /// Spell names this character knows or has prepared.
    /// </summary>
    [JsonProperty("spellsKnown")]
    public List<string> SpellsKnown { get; set; } = new();

    /// <summary>
    /// Current remaining spell slots indexed 0-8 for spell levels 1-9.
    /// </summary>
    [JsonProperty("spellSlotsCurrent")]
    public int[] SpellSlotsCurrent { get; set; } = new int[9];

    /// <summary>
    /// Maximum spell slots indexed 0-8 for spell levels 1-9.
    /// </summary>
    [JsonProperty("spellSlotsMax")]
    public int[] SpellSlotsMax { get; set; } = new int[9];

    // ── Skills & Proficiencies ────────────────────────────────────────

    /// <summary>
    /// Skill proficiency levels. Skills not in the dictionary are treated as None.
    /// </summary>
    [JsonProperty("skills")]
    public Dictionary<Skill, ProficiencyLevel> Skills { get; set; } = new();

    [JsonProperty("toolProficiencies")]
    public List<string> ToolProficiencies { get; set; } = new();

    [JsonProperty("languages")]
    public List<string> Languages { get; set; } = new();

    [JsonProperty("savingThrowProficiencies")]
    public HashSet<Ability> SavingThrowProficiencies { get; set; } = new();

    // ── Economy ──────────────────────────────────────────────────────

    /// <summary>
    /// Gold pieces the character is carrying. D&D 5e uses GP as the base currency.
    /// </summary>
    [JsonProperty("gold")]
    public int Gold { get; set; }

    // ── Equipment ─────────────────────────────────────────────────────

    /// <summary>
    /// Equipment slots mapped to item names (e.g. "MainHand" -> "Longsword").
    /// </summary>
    [JsonProperty("equippedItems")]
    public Dictionary<string, string> EquippedItems { get; set; } = new();

    [JsonProperty("inventoryItems")]
    public List<InventoryItem> InventoryItems { get; set; } = new();

    /// <summary>
    /// Names of magic items this character is currently attuned to.
    /// D&D 5e limits attunement to 3 items.
    /// </summary>
    [JsonProperty("attunedItems")]
    public List<string> AttunedItems { get; set; } = new();

    /// <summary>
    /// Remaining charges for magic items that use them, keyed by item name.
    /// </summary>
    [JsonProperty("magicItemCharges")]
    public Dictionary<string, int> MagicItemCharges { get; set; } = new();

    // ── Personality ───────────────────────────────────────────────────

    [JsonProperty("personalityTraits")]
    public List<string> PersonalityTraits { get; set; } = new();

    [JsonProperty("ideal")]
    public string Ideal { get; set; } = string.Empty;

    [JsonProperty("bond")]
    public string Bond { get; set; } = string.Empty;

    [JsonProperty("flaw")]
    public string Flaw { get; set; } = string.Empty;

    [JsonProperty("backstory")]
    public string Backstory { get; set; } = string.Empty;

    // ── Rest & Resource Tracking ────────────────────────────────────────

    /// <summary>
    /// Number of hit dice already spent (not yet recovered). Max hit dice = Level.
    /// </summary>
    [JsonProperty("hitDiceSpent")]
    public int HitDiceSpent { get; set; }

    /// <summary>
    /// Tracks uses remaining for daily/short-rest class features.
    /// Key = feature name (e.g. "Second Wind", "Action Surge", "Ki Points"),
    /// Value = remaining uses.
    /// </summary>
    [JsonProperty("featureUsesRemaining")]
    public Dictionary<string, int> FeatureUsesRemaining { get; set; } = new();

    /// <summary>
    /// Maximum uses for each tracked class feature.
    /// Key = feature name, Value = max uses.
    /// Set during character creation or level-up.
    /// </summary>
    [JsonProperty("featureUsesMax")]
    public Dictionary<string, int> FeatureUsesMax { get; set; } = new();

    // ── Companion-Only Fields ─────────────────────────────────────────

    /// <summary>
    /// True if this character is an AI-controlled companion rather than the player.
    /// </summary>
    [JsonProperty("isCompanion")]
    public bool IsCompanion { get; set; }

    /// <summary>
    /// Relationship scores with other characters, keyed by character name.
    /// Range: -100 (hostile) to +100 (devoted).
    /// </summary>
    [JsonProperty("relationships")]
    public Dictionary<string, int> Relationships { get; set; } = new();

    /// <summary>
    /// Preferred combat behavior for the companion AI system.
    /// </summary>
    [JsonProperty("combatTactic")]
    public CombatTacticPreference CombatTactic { get; set; } = CombatTacticPreference.Balanced;

    /// <summary>
    /// How much autonomy the companion AI has during combat.
    /// Defaults to Suggest: AI proposes actions, player approves.
    /// Only meaningful when IsCompanion is true.
    /// </summary>
    [JsonProperty("companionAutonomy")]
    public ChroniclesUnbound.Companions.CompanionAutonomy CompanionAutonomy { get; set; }
        = ChroniclesUnbound.Companions.CompanionAutonomy.Suggest;

    // ── Calculated Properties ─────────────────────────────────────────

    /// <summary>
    /// Proficiency bonus derived from character level: 2 + (Level - 1) / 4.
    /// </summary>
    [JsonIgnore]
    public int ProficiencyBonus => 2 + (Level - 1) / 4;

    /// <summary>
    /// Passive Perception = 10 + Perception skill modifier.
    /// </summary>
    [JsonIgnore]
    public int PassivePerception => 10 + GetSkillModifier(Skill.Perception);

    // ── Methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Calculates the total modifier for a skill check:
    /// base ability modifier + proficiency bonus (if proficient) or double (if expertise).
    /// </summary>
    public int GetSkillModifier(Skill skill)
    {
        Ability ability = GetAbilityForSkill(skill);
        int modifier = AbilityScores.GetModifier(ability);

        if (Skills.TryGetValue(skill, out var proficiency))
        {
            modifier += proficiency switch
            {
                ProficiencyLevel.Proficient => ProficiencyBonus,
                ProficiencyLevel.Expertise => ProficiencyBonus * 2,
                _ => 0
            };
        }

        return modifier;
    }

    /// <summary>
    /// Returns the governing ability score for a given skill per the D&D 5e SRD.
    /// </summary>
    public static Ability GetAbilityForSkill(Skill skill)
    {
        return skill switch
        {
            Skill.Athletics => Ability.Strength,

            Skill.Acrobatics    => Ability.Dexterity,
            Skill.SleightOfHand => Ability.Dexterity,
            Skill.Stealth       => Ability.Dexterity,

            Skill.Arcana        => Ability.Intelligence,
            Skill.History       => Ability.Intelligence,
            Skill.Investigation => Ability.Intelligence,
            Skill.Nature        => Ability.Intelligence,
            Skill.Religion      => Ability.Intelligence,

            Skill.AnimalHandling => Ability.Wisdom,
            Skill.Insight        => Ability.Wisdom,
            Skill.Medicine       => Ability.Wisdom,
            Skill.Perception     => Ability.Wisdom,
            Skill.Survival       => Ability.Wisdom,

            Skill.Deception    => Ability.Charisma,
            Skill.Intimidation => Ability.Charisma,
            Skill.Performance  => Ability.Charisma,
            Skill.Persuasion   => Ability.Charisma,

            _ => throw new ArgumentOutOfRangeException(nameof(skill), skill, "Unknown skill.")
        };
    }
}
