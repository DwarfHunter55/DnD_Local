using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Represents a feat that a character can take in place of an Ability Score Improvement.
/// </summary>
public class FeatData
{
    /// <summary>
    /// Display name of the feat (e.g. "Great Weapon Master").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full text description of what the feat does.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Prerequisite text (e.g. "Strength 13 or higher"). Empty if none.
    /// </summary>
    public string Prerequisite { get; set; } = string.Empty;

    /// <summary>
    /// Ability score increases granted by the feat, if any (e.g. Constitution +1).
    /// </summary>
    public Dictionary<Ability, int> AbilityScoreIncrease { get; set; } = new();

    /// <summary>
    /// Proficiencies granted by the feat (skill names, tool names, etc.).
    /// </summary>
    public List<string> GrantsProficiency { get; set; } = new();

    /// <summary>
    /// Returns the five SRD-representative feats available in the game.
    /// </summary>
    public static List<FeatData> GetSRDFeats()
    {
        return new List<FeatData>
        {
            new FeatData
            {
                Name = "Alert",
                Description = "You gain a +5 bonus to initiative. You can't be surprised while you are conscious. Other creatures don't gain advantage on attack rolls against you as a result of being unseen by you.",
                Prerequisite = string.Empty,
                AbilityScoreIncrease = new Dictionary<Ability, int>(),
                GrantsProficiency = new List<string>()
            },
            new FeatData
            {
                Name = "Great Weapon Master",
                Description = "On your turn, when you score a critical hit with a melee weapon or reduce a creature to 0 hit points with one, you can make one melee weapon attack as a bonus action. Before you make a melee attack with a heavy weapon that you are proficient with, you can choose to take a -5 penalty to the attack roll. If the attack hits, you add +10 to the attack's damage.",
                Prerequisite = string.Empty,
                AbilityScoreIncrease = new Dictionary<Ability, int>(),
                GrantsProficiency = new List<string>()
            },
            new FeatData
            {
                Name = "Sharpshooter",
                Description = "Attacking at long range doesn't impose disadvantage on your ranged weapon attack rolls. Your ranged weapon attacks ignore half cover and three-quarters cover. Before you make an attack with a ranged weapon that you are proficient with, you can choose to take a -5 penalty to the attack roll. If the attack hits, you add +10 to the attack's damage.",
                Prerequisite = string.Empty,
                AbilityScoreIncrease = new Dictionary<Ability, int>(),
                GrantsProficiency = new List<string>()
            },
            new FeatData
            {
                Name = "Tough",
                Description = "Your hit point maximum increases by an amount equal to twice your level when you gain this feat. Whenever you gain a level thereafter, your hit point maximum increases by an additional 2 hit points.",
                Prerequisite = string.Empty,
                AbilityScoreIncrease = new Dictionary<Ability, int>(),
                GrantsProficiency = new List<string>()
            },
            new FeatData
            {
                Name = "War Caster",
                Description = "You have advantage on Constitution saving throws that you make to maintain your concentration on a spell when you take damage. You can perform the somatic components of spells even when you have weapons or a shield in one or both hands. When a hostile creature's movement provokes an opportunity attack from you, you can use your reaction to cast a spell at the creature rather than making an opportunity attack.",
                Prerequisite = string.Empty,
                AbilityScoreIncrease = new Dictionary<Ability, int>(),
                GrantsProficiency = new List<string>()
            }
        };
    }
}
