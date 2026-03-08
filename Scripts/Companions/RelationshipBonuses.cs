using Newtonsoft.Json;

namespace ChroniclesUnbound.Companions;

/// <summary>
/// Gameplay bonuses and behavioral modifiers granted by a relationship tier.
/// Consumed by combat AI, dialogue systems, and the LLM prompt builder.
/// </summary>
public class RelationshipBonus
{
    /// <summary>The tier these bonuses correspond to.</summary>
    [JsonProperty("tier")]
    public RelationshipTier Tier { get; set; }

    /// <summary>
    /// Modifier applied to the companion's attack rolls when fighting alongside the player.
    /// 0 = no change, negative = penalty, positive = bonus.
    /// </summary>
    [JsonProperty("attackBonus")]
    public int AttackBonus { get; set; }

    /// <summary>
    /// Modifier applied to the companion's damage rolls.
    /// </summary>
    [JsonProperty("damageBonus")]
    public int DamageBonus { get; set; }

    /// <summary>
    /// Modifier to the companion's saving throws when the player is within 30ft.
    /// Represents the emotional resolve from trust (or distrust).
    /// </summary>
    [JsonProperty("savingThrowBonus")]
    public int SavingThrowBonus { get; set; }

    /// <summary>
    /// If true, the companion may refuse direct orders or act against the player.
    /// </summary>
    [JsonProperty("mayRefuseOrders")]
    public bool MayRefuseOrders { get; set; }

    /// <summary>
    /// If true, the companion may leave the party permanently.
    /// </summary>
    [JsonProperty("mayLeaveParty")]
    public bool MayLeaveParty { get; set; }

    /// <summary>
    /// If true, the companion occasionally offers helpful suggestions during exploration.
    /// </summary>
    [JsonProperty("offersHelpfulSuggestions")]
    public bool OffersHelpfulSuggestions { get; set; }

    /// <summary>
    /// If true, the companion's personal quest can be started or advanced.
    /// </summary>
    [JsonProperty("personalQuestAvailable")]
    public bool PersonalQuestAvailable { get; set; }

    /// <summary>
    /// If true, the companion gains access to a unique ultimate ability
    /// (specific to each companion, defined in their profile).
    /// </summary>
    [JsonProperty("ultimateAbilityUnlocked")]
    public bool UltimateAbilityUnlocked { get; set; }

    /// <summary>
    /// If true, the companion will sacrifice themselves (take lethal damage)
    /// to save the player from a killing blow.
    /// </summary>
    [JsonProperty("willSacrificeForPlayer")]
    public bool WillSacrificeForPlayer { get; set; }

    /// <summary>
    /// Dialogue tone hint passed to the LLM for companion speech generation.
    /// </summary>
    [JsonProperty("dialogueTone")]
    public string DialogueTone { get; set; } = string.Empty;

    /// <summary>
    /// Brief description of this tier's effects for UI tooltips.
    /// </summary>
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Static lookup for relationship tier gameplay bonuses.
/// Each tier's bonuses are pre-defined and immutable at runtime.
/// </summary>
public static class RelationshipBonuses
{
    private static readonly Dictionary<RelationshipTier, RelationshipBonus> _bonuses = new()
    {
        [RelationshipTier.Hostile] = new RelationshipBonus
        {
            Tier = RelationshipTier.Hostile,
            AttackBonus = -2,
            DamageBonus = 0,
            SavingThrowBonus = -1,
            MayRefuseOrders = true,
            MayLeaveParty = true,
            OffersHelpfulSuggestions = false,
            PersonalQuestAvailable = false,
            UltimateAbilityUnlocked = false,
            WillSacrificeForPlayer = false,
            DialogueTone = "Hostile, contemptuous, may threaten to leave or undermine the player.",
            Description = "May leave the party or refuse orders. Combat performance reduced."
        },

        [RelationshipTier.Unfriendly] = new RelationshipBonus
        {
            Tier = RelationshipTier.Unfriendly,
            AttackBonus = -1,
            DamageBonus = 0,
            SavingThrowBonus = 0,
            MayRefuseOrders = false,
            MayLeaveParty = false,
            OffersHelpfulSuggestions = false,
            PersonalQuestAvailable = false,
            UltimateAbilityUnlocked = false,
            WillSacrificeForPlayer = false,
            DialogueTone = "Snarky, reluctant, passive-aggressive. Follows orders grudgingly.",
            Description = "Reduced cooperation. Snarky dialogue and minor combat penalties."
        },

        [RelationshipTier.Neutral] = new RelationshipBonus
        {
            Tier = RelationshipTier.Neutral,
            AttackBonus = 0,
            DamageBonus = 0,
            SavingThrowBonus = 0,
            MayRefuseOrders = false,
            MayLeaveParty = false,
            OffersHelpfulSuggestions = false,
            PersonalQuestAvailable = false,
            UltimateAbilityUnlocked = false,
            WillSacrificeForPlayer = false,
            DialogueTone = "Professional, neutral. Neither warm nor cold.",
            Description = "Standard behavior. No bonuses or penalties."
        },

        [RelationshipTier.Friendly] = new RelationshipBonus
        {
            Tier = RelationshipTier.Friendly,
            AttackBonus = 0,
            DamageBonus = 0,
            SavingThrowBonus = 0,
            MayRefuseOrders = false,
            MayLeaveParty = false,
            OffersHelpfulSuggestions = true,
            PersonalQuestAvailable = false,
            UltimateAbilityUnlocked = false,
            WillSacrificeForPlayer = false,
            DialogueTone = "Warm, supportive, occasionally shares personal stories.",
            Description = "Occasional helpful suggestions. Beginning to trust the player."
        },

        [RelationshipTier.Loyal] = new RelationshipBonus
        {
            Tier = RelationshipTier.Loyal,
            AttackBonus = 1,
            DamageBonus = 1,
            SavingThrowBonus = 1,
            MayRefuseOrders = false,
            MayLeaveParty = false,
            OffersHelpfulSuggestions = true,
            PersonalQuestAvailable = true,
            UltimateAbilityUnlocked = false,
            WillSacrificeForPlayer = false,
            DialogueTone = "Deeply trusting, openly affectionate, shares secrets and fears.",
            Description = "Significant combat synergies (+1 atk/dmg/saves). Personal quest unlocked."
        },

        [RelationshipTier.Devoted] = new RelationshipBonus
        {
            Tier = RelationshipTier.Devoted,
            AttackBonus = 2,
            DamageBonus = 2,
            SavingThrowBonus = 2,
            MayRefuseOrders = false,
            MayLeaveParty = false,
            OffersHelpfulSuggestions = true,
            PersonalQuestAvailable = true,
            UltimateAbilityUnlocked = true,
            WillSacrificeForPlayer = true,
            DialogueTone = "Fiercely loyal, would die for the player. Speaks with deep bond.",
            Description = "Ultimate abilities unlocked. Will sacrifice self for the player. +2 atk/dmg/saves."
        }
    };

    /// <summary>
    /// Returns the <see cref="RelationshipBonus"/> for the given tier.
    /// Always returns a valid bonus object (falls back to Neutral if tier is somehow invalid).
    /// </summary>
    public static RelationshipBonus GetBonusesForTier(RelationshipTier tier)
    {
        return _bonuses.TryGetValue(tier, out var bonus) ? bonus : _bonuses[RelationshipTier.Neutral];
    }

    /// <summary>
    /// Returns the bonus for a companion based on their current score.
    /// Convenience method that converts score to tier first.
    /// </summary>
    public static RelationshipBonus GetBonusesForScore(int score)
    {
        return GetBonusesForTier(RelationshipTierExtensions.TierFromScore(score));
    }
}
