using Newtonsoft.Json;

namespace ChroniclesUnbound.Data;

/// <summary>
/// Represents the six core ability scores for a character or monster.
/// Provides modifier calculation per the D&D 5e formula: (score - 10) / 2 (rounded down).
/// </summary>
public class AbilityScores
{
    [JsonProperty("strength")]
    public int Strength { get; set; } = 10;

    [JsonProperty("dexterity")]
    public int Dexterity { get; set; } = 10;

    [JsonProperty("constitution")]
    public int Constitution { get; set; } = 10;

    [JsonProperty("intelligence")]
    public int Intelligence { get; set; } = 10;

    [JsonProperty("wisdom")]
    public int Wisdom { get; set; } = 10;

    [JsonProperty("charisma")]
    public int Charisma { get; set; } = 10;

    /// <summary>
    /// Returns the ability modifier: (score - 10) / 2, rounded toward negative infinity.
    /// </summary>
    public int GetModifier(Ability ability)
    {
        int score = GetScore(ability);
        // Integer division in C# truncates toward zero; we need floor division.
        // For odd scores below 10 (e.g. 9 -> -1, 7 -> -2), Math.Floor handles it.
        return (int)Math.Floor((score - 10) / 2.0);
    }

    /// <summary>
    /// Gets the raw score value for the specified ability.
    /// </summary>
    public int GetScore(Ability ability)
    {
        return ability switch
        {
            Ability.Strength => Strength,
            Ability.Dexterity => Dexterity,
            Ability.Constitution => Constitution,
            Ability.Intelligence => Intelligence,
            Ability.Wisdom => Wisdom,
            Ability.Charisma => Charisma,
            _ => throw new ArgumentOutOfRangeException(nameof(ability), ability, "Unknown ability score.")
        };
    }

    /// <summary>
    /// Sets the raw score value for the specified ability.
    /// </summary>
    public void SetScore(Ability ability, int value)
    {
        switch (ability)
        {
            case Ability.Strength:     Strength = value; break;
            case Ability.Dexterity:    Dexterity = value; break;
            case Ability.Constitution: Constitution = value; break;
            case Ability.Intelligence: Intelligence = value; break;
            case Ability.Wisdom:       Wisdom = value; break;
            case Ability.Charisma:     Charisma = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(ability), ability, "Unknown ability score.");
        }
    }
}
