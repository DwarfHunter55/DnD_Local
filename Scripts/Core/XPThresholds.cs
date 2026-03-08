namespace ChroniclesUnbound.Core;

/// <summary>
/// Static XP table from the D&D 5e SRD. Provides level-up thresholds,
/// XP-to-level conversions, and encounter difficulty thresholds (DMG values).
/// </summary>
public static class XPThresholds
{
    // ── XP required to reach each level (index 0 = level 1) ──────────
    private static readonly int[] XPByLevel =
    {
        0,       // Level 1
        300,     // Level 2
        900,     // Level 3
        2700,    // Level 4
        6500,    // Level 5
        14000,   // Level 6
        23000,   // Level 7
        34000,   // Level 8
        48000,   // Level 9
        64000,   // Level 10
        85000,   // Level 11
        100000,  // Level 12
        120000,  // Level 13
        140000,  // Level 14
        165000,  // Level 15
        195000,  // Level 16
        225000,  // Level 17
        265000,  // Level 18
        305000,  // Level 19
        355000   // Level 20
    };

    // ── Encounter difficulty thresholds per character level (DMG p.82) ──
    // Indexed 0 = level 1. Each sub-array: [Easy, Medium, Hard, Deadly].
    private static readonly int[,] DifficultyThresholds =
    {
        {  25,   50,   75,  100 },  // Level 1
        {  50,  100,  150,  200 },  // Level 2
        {  75,  150,  225,  400 },  // Level 3
        { 125,  250,  375,  500 },  // Level 4
        { 250,  500,  750, 1100 },  // Level 5
        { 300,  600,  900, 1400 },  // Level 6
        { 350,  750, 1100, 1700 },  // Level 7
        { 450,  900, 1400, 2100 },  // Level 8
        { 550, 1100, 1600, 2400 },  // Level 9
        { 600, 1200, 1900, 2800 },  // Level 10
        { 800, 1600, 2400, 3600 },  // Level 11
        { 1000, 2000, 3000, 4500 }, // Level 12
        { 1100, 2200, 3400, 5100 }, // Level 13
        { 1250, 2500, 3800, 5700 }, // Level 14
        { 1400, 2800, 4300, 6400 }, // Level 15
        { 1600, 3200, 4800, 7200 }, // Level 16
        { 2000, 3900, 5900, 8800 }, // Level 17
        { 2100, 4200, 6300, 9500 }, // Level 18
        { 2400, 4900, 7300, 10900 }, // Level 19
        { 2800, 5700, 8500, 12700 }  // Level 20
    };

    /// <summary>
    /// Returns the total XP required to reach the given level (1-20).
    /// </summary>
    public static int GetXPForLevel(int level)
    {
        if (level < 1 || level > 20)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be between 1 and 20.");

        return XPByLevel[level - 1];
    }

    /// <summary>
    /// Returns the highest level achievable with the given XP total.
    /// </summary>
    public static int GetLevelForXP(int xp)
    {
        if (xp < 0)
            return 1;

        for (int i = XPByLevel.Length - 1; i >= 0; i--)
        {
            if (xp >= XPByLevel[i])
                return i + 1;
        }

        return 1;
    }

    /// <summary>
    /// Returns true if the character has enough XP to advance beyond their current level.
    /// Level 20 characters cannot level up further.
    /// </summary>
    public static bool CanLevelUp(int currentLevel, int currentXP)
    {
        if (currentLevel < 1 || currentLevel >= 20)
            return false;

        return currentXP >= XPByLevel[currentLevel]; // XPByLevel[currentLevel] = threshold for (currentLevel + 1)
    }

    /// <summary>
    /// Returns how much more XP is needed to reach the next level.
    /// Returns 0 if already at level 20 or if already eligible to level up.
    /// </summary>
    public static int GetXPToNextLevel(int currentLevel, int currentXP)
    {
        if (currentLevel < 1 || currentLevel >= 20)
            return 0;

        int needed = XPByLevel[currentLevel] - currentXP;
        return Math.Max(0, needed);
    }

    // ── Encounter Difficulty Thresholds ───────────────────────────────

    /// <summary>
    /// Returns the Easy encounter XP threshold for a character of the given level.
    /// </summary>
    public static int GetEasyThreshold(int level)
    {
        return GetDifficultyThreshold(level, 0);
    }

    /// <summary>
    /// Returns the Medium encounter XP threshold for a character of the given level.
    /// </summary>
    public static int GetMediumThreshold(int level)
    {
        return GetDifficultyThreshold(level, 1);
    }

    /// <summary>
    /// Returns the Hard encounter XP threshold for a character of the given level.
    /// </summary>
    public static int GetHardThreshold(int level)
    {
        return GetDifficultyThreshold(level, 2);
    }

    /// <summary>
    /// Returns the Deadly encounter XP threshold for a character of the given level.
    /// </summary>
    public static int GetDeadlyThreshold(int level)
    {
        return GetDifficultyThreshold(level, 3);
    }

    private static int GetDifficultyThreshold(int level, int difficultyIndex)
    {
        if (level < 1 || level > 20)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be between 1 and 20.");

        return DifficultyThresholds[level - 1, difficultyIndex];
    }
}
