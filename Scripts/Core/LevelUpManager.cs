using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Handles all level-up logic: XP awards, eligibility checks, computing what a character
/// gains at each level, and applying the player's choices. Reads class data from the SRD
/// JSON via SRDDataLoader.
/// </summary>
public class LevelUpManager
{
    private readonly List<ClassData> _classData;

    // Classes with extra ASI levels beyond the standard 4, 8, 12, 16, 19.
    private static readonly HashSet<int> StandardASILevels = new() { 4, 8, 12, 16, 19 };
    private static readonly HashSet<int> FighterASILevels = new() { 4, 6, 8, 12, 14, 16, 19 };
    private static readonly HashSet<int> RogueASILevels = new() { 4, 8, 10, 12, 16, 19 };

    // Known-spell casters and their spells known count per level (1-indexed).
    // Index 0 is unused; indices 1-20 give spells known at that class level.
    private static readonly Dictionary<string, int[]> SpellsKnownByLevel = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bard"] = new[] { 0, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 15, 15, 16, 18, 19, 19, 20, 22, 22, 22 },
        ["Ranger"] = new[] { 0, 0, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11 },
        ["Sorcerer"] = new[] { 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 12, 13, 13, 14, 14, 15, 15, 15, 15 },
        ["Warlock"] = new[] { 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15 }
    };

    // Prepared casters choose spells daily; they don't have a fixed "spells known" count
    // tied to leveling. The list is used to flag CanLearnNewSpells correctly.
    private static readonly HashSet<string> PreparedCasters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cleric", "Druid", "Paladin", "Wizard"
    };

    /// <summary>
    /// Creates a LevelUpManager with pre-loaded class data.
    /// </summary>
    /// <param name="classData">Class definitions loaded from classes.json via SRDDataLoader.LoadClasses().</param>
    public LevelUpManager(List<ClassData> classData)
    {
        _classData = classData ?? throw new ArgumentNullException(nameof(classData));
    }

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the proficiency bonus for a given character level (1-20).
    /// Formula: 2 + (level - 1) / 4 (integer division).
    /// </summary>
    public static int GetProficiencyBonus(int level)
    {
        if (level < 1) return 2;
        if (level > 20) return 6;
        return 2 + (level - 1) / 4;
    }

    /// <summary>
    /// Returns true if the character has enough XP to gain a level and is below level 20.
    /// </summary>
    public bool CanLevelUp(Character character)
    {
        if (character == null) return false;
        return XPThresholds.CanLevelUp(character.Level, character.ExperiencePoints);
    }

    /// <summary>
    /// Awards XP to a character and returns true if a level-up is now available.
    /// </summary>
    public bool AwardXP(Character character, int xp)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));
        if (xp < 0)
            throw new ArgumentOutOfRangeException(nameof(xp), "XP award cannot be negative.");

        character.ExperiencePoints += xp;
        return CanLevelUp(character);
    }

    /// <summary>
    /// Computes what the character will gain at their next level. Call this before
    /// presenting level-up choices to the player.
    /// </summary>
    public LevelUpInfo GetLevelUpInfo(Character character)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        int newLevel = character.Level + 1;
        if (newLevel > 20)
            throw new InvalidOperationException("Character is already at maximum level (20).");

        ClassData? classData = FindClassData(character.CharacterClassName);
        if (classData == null)
            throw new InvalidOperationException(
                $"Unknown class \"{character.CharacterClassName}\". Cannot compute level-up info.");

        var info = new LevelUpInfo
        {
            NewLevel = newLevel,
            HPRollDie = classData.HitDie,
            NewProficiencyBonus = GetProficiencyBonus(newLevel),
            ProficiencyBonusChanged = GetProficiencyBonus(newLevel) != GetProficiencyBonus(character.Level),
            HasASI = IsASILevel(character.CharacterClassName, newLevel),
            SubclassChoice = newLevel == classData.SubclassLevel
        };

        // Gather features from class and subclass at this level.
        var features = classData.GetFeaturesAtLevel(newLevel);
        foreach (var f in features)
            info.NewFeatures.Add(f.Name);

        if (classData.Subclass != null)
        {
            var subFeatures = classData.Subclass.GetFeaturesAtLevel(newLevel);
            foreach (var f in subFeatures)
                info.NewFeatures.Add(f.Name);
        }

        // Spell slot info.
        var slotEntry = classData.GetSpellSlotsAtLevel(newLevel);
        if (slotEntry != null)
        {
            int[] slots = new int[9];
            slotEntry.ApplyToSlotArray(slots);
            info.NewSpellSlots = slots;
            info.CanLearnNewSpells = true;

            // For known-spell casters, compute how many new spells they learn.
            if (SpellsKnownByLevel.TryGetValue(character.CharacterClassName, out var knownTable))
            {
                int prevKnown = character.Level >= 1 && character.Level <= 20
                    ? knownTable[character.Level]
                    : 0;
                int newKnown = newLevel <= 20 ? knownTable[newLevel] : prevKnown;
                info.NewSpellsCount = Math.Max(0, newKnown - prevKnown);
            }
            else if (PreparedCasters.Contains(character.CharacterClassName))
            {
                // Prepared casters don't gain a fixed number of new spells on level-up;
                // they can change their prepared list after a long rest.
                info.NewSpellsCount = 0;
            }
        }

        return info;
    }

    /// <summary>
    /// Applies the level-up to the character using the player's choices.
    /// Increases level, adds HP, applies features, handles ASI/feat, updates spell slots.
    /// </summary>
    public void ApplyLevelUp(Character character, LevelUpChoices choices)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));
        if (choices == null)
            throw new ArgumentNullException(nameof(choices));

        int newLevel = character.Level + 1;
        if (newLevel > 20)
            throw new InvalidOperationException("Character is already at maximum level (20).");

        ClassData? classData = FindClassData(character.CharacterClassName);
        if (classData == null)
            throw new InvalidOperationException(
                $"Unknown class \"{character.CharacterClassName}\". Cannot apply level-up.");

        // 1. Increase level.
        character.Level = newLevel;

        // 2. Roll or calculate HP.
        int conMod = character.AbilityScores.GetModifier(Ability.Constitution);
        int hpGain = CalculateHPGain(classData.HitDie, conMod, choices.HPChoice);
        character.MaxHitPoints += hpGain;
        character.HitPoints += hpGain; // Heal by the amount gained.

        // 3. Add class features.
        var features = classData.GetFeaturesAtLevel(newLevel);
        foreach (var f in features)
        {
            if (!character.Features.Contains(f.Name))
                character.Features.Add(f.Name);
        }

        // Add subclass features.
        if (classData.Subclass != null)
        {
            var subFeatures = classData.Subclass.GetFeaturesAtLevel(newLevel);
            foreach (var f in subFeatures)
            {
                if (!character.Features.Contains(f.Name))
                    character.Features.Add(f.Name);
            }
        }

        // 4. Handle ASI or Feat.
        if (IsASILevel(character.CharacterClassName, newLevel))
        {
            if (!string.IsNullOrEmpty(choices.FeatName))
            {
                ApplyFeat(character, choices.FeatName);
            }
            else
            {
                ApplyASI(character, choices.ASIAllocations);
            }
        }

        // 5. Update spell slots.
        var slotEntry = classData.GetSpellSlotsAtLevel(newLevel);
        if (slotEntry != null)
        {
            int[] oldMax = (int[])character.SpellSlotsMax.Clone();
            slotEntry.ApplyToSlotArray(character.SpellSlotsMax);

            // Grant any new slots immediately (add the difference to current).
            for (int i = 0; i < 9; i++)
            {
                int gained = character.SpellSlotsMax[i] - oldMax[i];
                if (gained > 0)
                    character.SpellSlotsCurrent[i] += gained;
            }
        }

        // 6. Add new spells (for known-spell casters).
        if (choices.NewSpells != null)
        {
            foreach (string spell in choices.NewSpells)
            {
                if (!string.IsNullOrWhiteSpace(spell) && !character.SpellsKnown.Contains(spell))
                    character.SpellsKnown.Add(spell);
            }
        }

        // 7. Apply Tough feat retroactive bonus if the character just picked it.
        // Tough grants +2 HP per level. The base +2 for this level is already handled
        // by the feat application (which adds 2 * current level). No extra work needed here.
    }

    // ── Internal Helpers ──────────────────────────────────────────────

    private ClassData? FindClassData(string className)
    {
        return _classData.Find(c =>
            string.Equals(c.Name, className, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true if the given level is an ASI level for the given class.
    /// Fighter gets extra ASI at 6 and 14. Rogue gets extra ASI at 10.
    /// </summary>
    private static bool IsASILevel(string className, int level)
    {
        if (string.Equals(className, "Fighter", StringComparison.OrdinalIgnoreCase))
            return FighterASILevels.Contains(level);

        if (string.Equals(className, "Rogue", StringComparison.OrdinalIgnoreCase))
            return RogueASILevels.Contains(level);

        return StandardASILevels.Contains(level);
    }

    /// <summary>
    /// Calculates HP gained on level-up. Hit die + CON modifier, minimum 1 HP.
    /// </summary>
    private static int CalculateHPGain(int hitDie, int conMod, HPRollChoice choice)
    {
        int roll;
        if (choice == HPRollChoice.TakeAverage)
        {
            // SRD average: (die / 2) + 1 (e.g. d10 = 6, d8 = 5, d6 = 4, d12 = 7).
            roll = (hitDie / 2) + 1;
        }
        else
        {
            roll = DiceRoller.RollSingle(hitDie);
        }

        return Math.Max(1, roll + conMod);
    }

    /// <summary>
    /// Applies Ability Score Improvement: allocations must total exactly 2,
    /// each ability capped at 20.
    /// </summary>
    private static void ApplyASI(Character character, Dictionary<Ability, int> allocations)
    {
        if (allocations == null || allocations.Count == 0)
            return;

        int total = 0;
        foreach (var kvp in allocations)
            total += kvp.Value;

        if (total != 2)
            throw new ArgumentException($"ASI allocations must total exactly 2, got {total}.");

        foreach (var kvp in allocations)
        {
            if (kvp.Value < 0)
                throw new ArgumentException($"ASI allocation for {kvp.Key} cannot be negative.");

            int current = character.AbilityScores.GetScore(kvp.Key);
            int newScore = Math.Min(20, current + kvp.Value);
            character.AbilityScores.SetScore(kvp.Key, newScore);
        }
    }

    /// <summary>
    /// Applies a feat to the character. Adds the feat name to features and
    /// applies any mechanical effects (ability score increases, Tough HP bonus).
    /// </summary>
    private static void ApplyFeat(Character character, string featName)
    {
        var allFeats = FeatData.GetSRDFeats();
        var feat = allFeats.Find(f =>
            string.Equals(f.Name, featName, StringComparison.OrdinalIgnoreCase));

        if (feat == null)
            throw new ArgumentException($"Unknown feat: \"{featName}\".");

        // Add to character features.
        if (!character.Features.Contains(feat.Name))
            character.Features.Add(feat.Name);

        // Apply ability score increases from the feat (capped at 20).
        foreach (var kvp in feat.AbilityScoreIncrease)
        {
            int current = character.AbilityScores.GetScore(kvp.Key);
            int newScore = Math.Min(20, current + kvp.Value);
            character.AbilityScores.SetScore(kvp.Key, newScore);
        }

        // Apply proficiencies from the feat.
        foreach (string prof in feat.GrantsProficiency)
        {
            if (!character.ToolProficiencies.Contains(prof))
                character.ToolProficiencies.Add(prof);
        }

        // Special feat effects.
        if (string.Equals(feat.Name, "Tough", StringComparison.OrdinalIgnoreCase))
        {
            // Tough grants +2 HP per character level (retroactive).
            int bonus = 2 * character.Level;
            character.MaxHitPoints += bonus;
            character.HitPoints += bonus;
        }
    }
}
