namespace ChroniclesUnbound.Companions;

/// <summary>
/// Relationship tiers between the player and a companion character.
/// Each tier maps to a score range within [-100, +100] and unlocks
/// different gameplay behaviors, dialogue options, and combat bonuses.
/// </summary>
public enum RelationshipTier
{
    /// <summary>Score -100 to -61. May leave party or refuse orders.</summary>
    Hostile = 0,

    /// <summary>Score -60 to -21. Reduced cooperation, snarky dialogue.</summary>
    Unfriendly = 1,

    /// <summary>Score -20 to +20. Standard behavior. Starting tier.</summary>
    Neutral = 2,

    /// <summary>Score +21 to +60. Helpful suggestions, small combat bonuses.</summary>
    Friendly = 3,

    /// <summary>Score +61 to +90. Combat synergies, personal quest unlocks.</summary>
    Loyal = 4,

    /// <summary>Score +91 to +100. Ultimate abilities, will sacrifice for player.</summary>
    Devoted = 5
}

/// <summary>
/// Extension methods for converting between relationship scores and tiers.
/// </summary>
public static class RelationshipTierExtensions
{
    /// <summary>
    /// Returns the <see cref="RelationshipTier"/> corresponding to a numeric score.
    /// Score is expected to be in [-100, +100] but out-of-range values are handled gracefully.
    /// </summary>
    public static RelationshipTier TierFromScore(int score)
    {
        return score switch
        {
            <= -61 => RelationshipTier.Hostile,
            <= -21 => RelationshipTier.Unfriendly,
            <= 20  => RelationshipTier.Neutral,
            <= 60  => RelationshipTier.Friendly,
            <= 90  => RelationshipTier.Loyal,
            _      => RelationshipTier.Devoted
        };
    }

    /// <summary>
    /// Returns the inclusive score range [min, max] for a given tier.
    /// </summary>
    public static (int Min, int Max) GetScoreRange(this RelationshipTier tier)
    {
        return tier switch
        {
            RelationshipTier.Hostile    => (-100, -61),
            RelationshipTier.Unfriendly => (-60, -21),
            RelationshipTier.Neutral    => (-20, 20),
            RelationshipTier.Friendly   => (21, 60),
            RelationshipTier.Loyal      => (61, 90),
            RelationshipTier.Devoted    => (91, 100),
            _ => (-20, 20)
        };
    }
}
