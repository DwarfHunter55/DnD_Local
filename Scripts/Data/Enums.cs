namespace ChroniclesUnbound.Data;

/// <summary>
/// The six core ability scores in D&D 5e.
/// </summary>
public enum Ability
{
    Strength,
    Dexterity,
    Constitution,
    Intelligence,
    Wisdom,
    Charisma
}

/// <summary>
/// All 18 skills from the D&D 5e SRD, each tied to an ability score.
/// </summary>
public enum Skill
{
    Acrobatics,
    AnimalHandling,
    Arcana,
    Athletics,
    Deception,
    History,
    Insight,
    Intimidation,
    Investigation,
    Medicine,
    Nature,
    Perception,
    Performance,
    Persuasion,
    Religion,
    SleightOfHand,
    Stealth,
    Survival
}

/// <summary>
/// Damage types as defined in the D&D 5e SRD.
/// </summary>
public enum DamageType
{
    Acid,
    Bludgeoning,
    Cold,
    Fire,
    Force,
    Lightning,
    Necrotic,
    Piercing,
    Poison,
    Psychic,
    Radiant,
    Slashing,
    Thunder
}

/// <summary>
/// Conditions that can affect creatures in D&D 5e.
/// </summary>
public enum Condition
{
    Blinded,
    Charmed,
    Deafened,
    Exhaustion,
    Frightened,
    Grappled,
    Incapacitated,
    Invisible,
    Paralyzed,
    Petrified,
    Poisoned,
    Prone,
    Restrained,
    Stunned,
    Unconscious
}

/// <summary>
/// Character alignment on the law/chaos and good/evil axes.
/// </summary>
public enum Alignment
{
    LawfulGood,
    NeutralGood,
    ChaoticGood,
    LawfulNeutral,
    TrueNeutral,
    ChaoticNeutral,
    LawfulEvil,
    NeutralEvil,
    ChaoticEvil
}

/// <summary>
/// Proficiency levels for skills and tools.
/// None = no bonus, Proficient = add proficiency bonus, Expertise = double proficiency bonus.
/// </summary>
public enum ProficiencyLevel
{
    None,
    Proficient,
    Expertise
}

/// <summary>
/// Magic item rarity tiers from the D&D 5e SRD.
/// </summary>
public enum ItemRarity
{
    Common,
    Uncommon,
    Rare,
    VeryRare,
    Legendary,
    Artifact
}

/// <summary>
/// The eight schools of magic in D&D 5e.
/// </summary>
public enum SpellSchool
{
    Abjuration,
    Conjuration,
    Divination,
    Enchantment,
    Evocation,
    Illusion,
    Necromancy,
    Transmutation
}

/// <summary>
/// Armor categories from the D&D 5e SRD.
/// </summary>
public enum ArmorType
{
    Light,
    Medium,
    Heavy,
    Shield
}

/// <summary>
/// Weapon properties from the D&D 5e SRD.
/// </summary>
public enum WeaponProperty
{
    Ammunition,
    Finesse,
    Heavy,
    Light,
    Loading,
    Range,
    Reach,
    Special,
    Thrown,
    TwoHanded,
    Versatile
}

/// <summary>
/// Combat AI behavior preference for companion characters.
/// </summary>
public enum CombatTacticPreference
{
    Aggressive,
    Defensive,
    Support,
    Balanced,
    Ranged
}
