using Newtonsoft.Json;
using ChroniclesUnbound.Core;
using ChroniclesUnbound.Combat;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Result of encounter generation containing the monster roster and metadata.
/// </summary>
public class EncounterData
{
    /// <summary>Monsters in this encounter, keyed by name with count.</summary>
    public List<EncounterMonster> Monsters { get; set; } = new();

    /// <summary>The total base XP of all monsters before encounter multiplier.</summary>
    public int TotalBaseXP { get; set; }

    /// <summary>The adjusted XP after applying the encounter multiplier.</summary>
    public int AdjustedXP { get; set; }

    /// <summary>The encounter multiplier applied (based on monster count).</summary>
    public float Multiplier { get; set; } = 1f;

    /// <summary>The effective difficulty of this encounter.</summary>
    public EncounterDifficulty Difficulty { get; set; }

    /// <summary>The encounter table ID this was generated from, if any.</summary>
    public string SourceTableId { get; set; } = string.Empty;

    /// <summary>Total number of individual monsters.</summary>
    public int TotalMonsterCount
    {
        get
        {
            int count = 0;
            foreach (var m in Monsters)
                count += m.Count;
            return count;
        }
    }
}

/// <summary>
/// A monster type and count within an encounter.
/// </summary>
public class EncounterMonster
{
    public string MonsterName { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
    public int XPEach { get; set; }
}

/// <summary>
/// Generates random and scripted combat encounters using D&D 5e XP budget rules.
/// Loads monster data from the SRD and encounter tables from world data.
/// </summary>
public class EncounterGenerator
{
    private Dictionary<string, EncounterTable> _tables = new();
    private Dictionary<string, MonsterData> _monsters = new();
    private List<MonsterData> _monsterList = new();
    private readonly Random _rng;

    // ── D&D 5e encounter multipliers (DMG p.82) ──────────────────────
    // Applied based on the number of monsters in the encounter.
    private static readonly (int MaxCount, float Multiplier)[] EncounterMultipliers =
    {
        (1,  1.0f),
        (2,  1.5f),
        (6,  2.0f),
        (10, 2.5f),
        (14, 3.0f),
        (int.MaxValue, 4.0f)
    };

    public EncounterGenerator() : this(new Random()) { }

    public EncounterGenerator(Random rng)
    {
        _rng = rng;
    }

    // ── Data Loading ─────────────────────────────────────────────────

    /// <summary>
    /// Loads encounter tables from the specified JSON file.
    /// </summary>
    /// <param name="jsonPath">Path to encounter_tables.json (supports res:// paths).</param>
    public void LoadEncounterTables(string jsonPath)
    {
        string resolvedPath = ResolvePath(jsonPath);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException(
                $"Encounter tables file not found at: {resolvedPath}", resolvedPath);

        string json = File.ReadAllText(resolvedPath);
        var tables = JsonConvert.DeserializeObject<List<EncounterTable>>(json);

        if (tables == null)
            throw new JsonException($"Failed to deserialize encounter tables from: {resolvedPath}");

        _tables.Clear();
        foreach (var table in tables)
        {
            _tables[table.TableId] = table;
        }
    }

    /// <summary>
    /// Loads monster data from the specified JSON file.
    /// Must be called before generating encounters.
    /// </summary>
    /// <param name="jsonPath">Path to monsters.json (supports res:// paths).</param>
    public void LoadMonsters(string jsonPath)
    {
        string resolvedPath = ResolvePath(jsonPath);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException(
                $"Monsters file not found at: {resolvedPath}", resolvedPath);

        string json = File.ReadAllText(resolvedPath);

        // monsters.json uses snake_case keys; deserialize with a custom settings
        // to handle the mismatch with MonsterData's camelCase JsonProperty attributes.
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                NamingStrategy = new Newtonsoft.Json.Serialization.SnakeCaseNamingStrategy()
            }
        };

        var monsters = JsonConvert.DeserializeObject<List<MonsterData>>(json, settings);

        if (monsters == null)
            throw new JsonException($"Failed to deserialize monsters from: {resolvedPath}");

        _monsters.Clear();
        _monsterList.Clear();

        foreach (var monster in monsters)
        {
            // Use case-insensitive key for flexible lookup
            _monsters[monster.Name] = monster;
            _monsterList.Add(monster);
        }
    }

    // ── Encounter Generation ─────────────────────────────────────────

    /// <summary>
    /// Generates a random encounter based on party parameters and desired difficulty.
    /// Selects monsters whose total adjusted XP fits within the party's XP budget.
    /// </summary>
    /// <param name="partyLevel">Average level of the party.</param>
    /// <param name="partySize">Number of characters in the party.</param>
    /// <param name="difficulty">Desired encounter difficulty.</param>
    /// <returns>An EncounterData with a balanced monster roster.</returns>
    public EncounterData GenerateEncounter(int partyLevel, int partySize, EncounterDifficulty difficulty)
    {
        partyLevel = Math.Clamp(partyLevel, 1, 20);
        partySize = Math.Max(1, partySize);

        int xpBudget = CalculateXPBudget(partyLevel, partySize, difficulty);

        // Get monsters that could fit in this budget (base XP <= budget)
        var candidates = GetCandidateMonsters(xpBudget);
        if (candidates.Count == 0)
        {
            // Fallback: return an empty encounter if no monsters fit
            return new EncounterData { Difficulty = difficulty };
        }

        return BuildEncounterFromBudget(candidates, xpBudget, difficulty);
    }

    /// <summary>
    /// Generates an encounter from a specific encounter table, scaling to party parameters.
    /// Rolls the table for monster selection, then adjusts count to fit the XP budget
    /// for Medium difficulty (or the closest achievable difficulty).
    /// </summary>
    /// <param name="tableId">The encounter table ID (from Location.EncounterTableId).</param>
    /// <param name="partyLevel">Average level of the party.</param>
    /// <param name="partySize">Number of characters in the party.</param>
    /// <returns>An EncounterData based on the table roll, or null if the table is not found.</returns>
    public EncounterData GenerateFromTable(string tableId, int partyLevel, int partySize)
    {
        return GenerateFromTable(tableId, partyLevel, partySize, dangerLevel: 1);
    }

    /// <summary>
    /// Generates an encounter from a specific encounter table with a given danger level filter.
    /// </summary>
    public EncounterData GenerateFromTable(string tableId, int partyLevel, int partySize, int dangerLevel)
    {
        partyLevel = Math.Clamp(partyLevel, 1, 20);
        partySize = Math.Max(1, partySize);

        if (string.IsNullOrEmpty(tableId) || !_tables.TryGetValue(tableId, out var table))
            return null;

        var rolledMonsters = table.RollEncounter(dangerLevel, _rng);
        if (rolledMonsters.Count == 0)
            return null;

        // Resolve monster data and count duplicates
        var monsterCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in rolledMonsters)
        {
            monsterCounts.TryGetValue(name, out int count);
            monsterCounts[name] = count + 1;
        }

        // Build the encounter data
        var encounter = new EncounterData { SourceTableId = tableId };
        int totalBaseXP = 0;
        int totalCount = 0;

        foreach (var kvp in monsterCounts)
        {
            if (!_monsters.TryGetValue(kvp.Key, out var monsterData))
                continue; // Skip unknown monsters

            encounter.Monsters.Add(new EncounterMonster
            {
                MonsterName = kvp.Key,
                Count = kvp.Value,
                XPEach = monsterData.XP
            });

            totalBaseXP += monsterData.XP * kvp.Value;
            totalCount += kvp.Value;
        }

        if (encounter.Monsters.Count == 0)
            return null;

        float multiplier = GetEncounterMultiplier(totalCount);
        int adjustedXP = (int)(totalBaseXP * multiplier);

        encounter.TotalBaseXP = totalBaseXP;
        encounter.AdjustedXP = adjustedXP;
        encounter.Multiplier = multiplier;
        encounter.Difficulty = ClassifyDifficulty(adjustedXP, partyLevel, partySize);

        return encounter;
    }

    /// <summary>
    /// Converts an EncounterData into a list of Combatant objects ready for CombatManager.
    /// Each monster is converted to a Character and wrapped in a Combatant.
    /// </summary>
    /// <param name="encounter">The encounter to convert.</param>
    /// <returns>List of enemy Combatant objects.</returns>
    public List<Combatant> CreateCombatants(EncounterData encounter)
    {
        var combatants = new List<Combatant>();
        if (encounter == null)
            return combatants;

        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in encounter.Monsters)
        {
            if (!_monsters.TryGetValue(entry.MonsterName, out var monsterData))
                continue;

            for (int i = 0; i < entry.Count; i++)
            {
                nameCounts.TryGetValue(entry.MonsterName, out int index);
                index++;
                nameCounts[entry.MonsterName] = index;

                var character = MonsterToCharacter(monsterData);
                string baseName = monsterData.Name.ToLowerInvariant().Replace(' ', '_');
                string id = index > 1 || entry.Count > 1
                    ? $"{baseName}_{index}"
                    : baseName;

                var combatant = new Combatant
                {
                    Character = character,
                    Id = id,
                    IsAlly = false,
                    IsPlayerControlled = false,
                    HasExtraAttack = MonsterHasMultiattack(monsterData),
                    Conditions = new ConditionManager()
                };

                combatants.Add(combatant);
            }
        }

        return combatants;
    }

    /// <summary>
    /// Returns the probability (0.0 to 1.0) of a random encounter occurring
    /// based on the location's danger level.
    /// Danger 0 = safe (0%), Danger 5 = very dangerous (50%).
    /// </summary>
    public static float GetRandomEncounterChance(int dangerLevel)
    {
        return dangerLevel switch
        {
            0 => 0.00f,   // Safe areas (towns, temples)
            1 => 0.10f,   // Low danger (outskirts, farmland)
            2 => 0.20f,   // Moderate (roads, shallow caves)
            3 => 0.30f,   // Elevated (forests, mines)
            4 => 0.40f,   // High (ruins, marshes, towers)
            5 => 0.50f,   // Deadly (blighted areas, deep dungeons)
            _ => Math.Clamp(dangerLevel * 0.10f, 0f, 0.60f)
        };
    }

    // ── XP Budget Calculation ────────────────────────────────────────

    /// <summary>
    /// Calculates the party's total XP budget for the given difficulty.
    /// This is the sum of per-character thresholds from the DMG table.
    /// </summary>
    private static int CalculateXPBudget(int partyLevel, int partySize, EncounterDifficulty difficulty)
    {
        int perCharacterXP = difficulty switch
        {
            EncounterDifficulty.Easy   => XPThresholds.GetEasyThreshold(partyLevel),
            EncounterDifficulty.Medium => XPThresholds.GetMediumThreshold(partyLevel),
            EncounterDifficulty.Hard   => XPThresholds.GetHardThreshold(partyLevel),
            EncounterDifficulty.Deadly => XPThresholds.GetDeadlyThreshold(partyLevel),
            _ => XPThresholds.GetMediumThreshold(partyLevel)
        };

        return perCharacterXP * partySize;
    }

    /// <summary>
    /// Returns the encounter multiplier based on monster count (DMG p.82).
    /// </summary>
    public static float GetEncounterMultiplier(int monsterCount)
    {
        if (monsterCount <= 0)
            return 1.0f;

        foreach (var (maxCount, multiplier) in EncounterMultipliers)
        {
            if (monsterCount <= maxCount)
                return multiplier;
        }

        return 4.0f;
    }

    // ── Private Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Gets monsters whose individual XP fits within the budget.
    /// Sorted by XP descending for the greedy fill algorithm.
    /// </summary>
    private List<MonsterData> GetCandidateMonsters(int xpBudget)
    {
        var candidates = new List<MonsterData>();
        foreach (var monster in _monsterList)
        {
            if (monster.XP > 0 && monster.XP <= xpBudget)
                candidates.Add(monster);
        }

        // Sort by XP descending so we can fill from top
        candidates.Sort((a, b) => b.XP.CompareTo(a.XP));
        return candidates;
    }

    /// <summary>
    /// Builds a balanced encounter by selecting monsters to fit the XP budget.
    /// Uses a constrained random approach: pick a monster type, then fill quantity.
    /// </summary>
    private EncounterData BuildEncounterFromBudget(List<MonsterData> candidates,
        int xpBudget, EncounterDifficulty difficulty)
    {
        var encounter = new EncounterData { Difficulty = difficulty };

        // Strategy: pick 1-3 different monster types, then fill quantities to budget.
        // This creates more interesting encounters than a single monster type.
        int numTypes = _rng.Next(1, Math.Min(4, candidates.Count + 1));

        // Shuffle candidates and pick the first N types
        var shuffled = new List<MonsterData>(candidates);
        ShuffleList(shuffled);

        var selectedTypes = new List<MonsterData>();
        for (int i = 0; i < Math.Min(numTypes, shuffled.Count); i++)
            selectedTypes.Add(shuffled[i]);

        // Distribute the budget across selected types
        int remainingBudget = xpBudget;
        int totalCount = 0;
        int totalBaseXP = 0;

        foreach (var monster in selectedTypes)
        {
            if (remainingBudget <= 0)
                break;

            // How many of this monster can we fit? Account for multiplier increasing.
            int maxAffordable = CalculateMaxAffordable(monster.XP, remainingBudget, totalCount);
            if (maxAffordable <= 0)
                continue;

            // Pick a random count between 1 and max affordable
            int count = _rng.Next(1, maxAffordable + 1);

            encounter.Monsters.Add(new EncounterMonster
            {
                MonsterName = monster.Name,
                Count = count,
                XPEach = monster.XP
            });

            int entryXP = monster.XP * count;
            totalBaseXP += entryXP;
            totalCount += count;

            // Recalculate remaining budget accounting for the new multiplier
            float currentMultiplier = GetEncounterMultiplier(totalCount);
            int adjustedSoFar = (int)(totalBaseXP * currentMultiplier);
            remainingBudget = xpBudget - adjustedSoFar;
        }

        // If we ended up with nothing (budget too small), force at least one weak monster
        if (encounter.Monsters.Count == 0 && candidates.Count > 0)
        {
            var weakest = candidates[candidates.Count - 1]; // Already sorted XP desc
            encounter.Monsters.Add(new EncounterMonster
            {
                MonsterName = weakest.Name,
                Count = 1,
                XPEach = weakest.XP
            });
            totalBaseXP = weakest.XP;
            totalCount = 1;
        }

        float multiplier = GetEncounterMultiplier(totalCount);
        encounter.TotalBaseXP = totalBaseXP;
        encounter.AdjustedXP = (int)(totalBaseXP * multiplier);
        encounter.Multiplier = multiplier;

        return encounter;
    }

    /// <summary>
    /// Calculates how many of a monster at a given XP can be added without
    /// exceeding the remaining budget, accounting for the multiplier increase
    /// as more monsters are added.
    /// </summary>
    private static int CalculateMaxAffordable(int monsterXP, int remainingBudget, int existingCount)
    {
        if (monsterXP <= 0)
            return 0;

        // Binary search for the max count that fits
        int maxCount = remainingBudget / monsterXP; // Upper bound ignoring multiplier
        maxCount = Math.Min(maxCount, 8); // Cap at 8 per type for reasonable encounters

        for (int count = maxCount; count >= 1; count--)
        {
            int newTotal = existingCount + count;
            float newMultiplier = GetEncounterMultiplier(newTotal);
            float oldMultiplier = GetEncounterMultiplier(existingCount);

            // The adjusted cost of adding 'count' monsters is:
            // (new total adjusted XP) - (current adjusted XP)
            // But we simplify: the additional adjusted cost is approximately
            // count * monsterXP * newMultiplier (conservative estimate)
            int adjustedCost = (int)(count * monsterXP * newMultiplier);

            if (adjustedCost <= remainingBudget)
                return count;
        }

        return 0;
    }

    /// <summary>
    /// Classifies the actual difficulty of an encounter by comparing its adjusted XP
    /// against the party's threshold brackets.
    /// </summary>
    private static EncounterDifficulty ClassifyDifficulty(int adjustedXP, int partyLevel, int partySize)
    {
        int deadlyThreshold = XPThresholds.GetDeadlyThreshold(partyLevel) * partySize;
        int hardThreshold = XPThresholds.GetHardThreshold(partyLevel) * partySize;
        int mediumThreshold = XPThresholds.GetMediumThreshold(partyLevel) * partySize;

        if (adjustedXP >= deadlyThreshold)
            return EncounterDifficulty.Deadly;
        if (adjustedXP >= hardThreshold)
            return EncounterDifficulty.Hard;
        if (adjustedXP >= mediumThreshold)
            return EncounterDifficulty.Medium;

        return EncounterDifficulty.Easy;
    }

    /// <summary>
    /// Converts MonsterData into a Character object for use by the combat system.
    /// Maps monster stats to the Character model's fields.
    /// </summary>
    private static Character MonsterToCharacter(MonsterData monster)
    {
        var character = new Character
        {
            Name = monster.Name,
            Race = monster.Type,
            CharacterClassName = "Monster",
            Level = Math.Max(1, (int)Math.Ceiling(monster.ChallengeRating)),
            ArmorClass = monster.ArmorClass,
            HitPoints = monster.HitPoints,
            MaxHitPoints = monster.HitPoints,
            ExperiencePoints = monster.XP,
            AbilityScores = monster.AbilityScores ?? new AbilityScores(),
            IsCompanion = false
        };

        // Parse speed -- monsters store speed as a string like "30 ft., fly 60 ft."
        // or as a structured object. Use walk speed as the base.
        character.Speed = ParseWalkSpeed(monster.Speed);

        // Map monster actions to equipped weapon data
        if (monster.Actions != null && monster.Actions.Count > 0)
        {
            var primaryAttack = FindPrimaryAttack(monster.Actions);
            if (primaryAttack != null)
            {
                string damageType = primaryAttack.DamageType != 0
                    ? primaryAttack.DamageType.ToString()
                    : "Bludgeoning";

                var weapon = new InventoryItem
                {
                    Name = primaryAttack.Name,
                    IsEquippable = true,
                    Properties = new Dictionary<string, string>
                    {
                        ["type"] = "Weapon",
                        ["damage"] = string.IsNullOrEmpty(primaryAttack.Damage) ? "1d4" : primaryAttack.Damage,
                        ["damageType"] = damageType
                    }
                };

                character.InventoryItems.Add(weapon);
                character.EquippedItems["MainHand"] = primaryAttack.Name;
            }
        }

        // Copy features from traits
        if (monster.Traits != null)
        {
            foreach (var trait in monster.Traits)
                character.Features.Add(trait.Name);
        }

        return character;
    }

    /// <summary>
    /// Checks if a monster has a Multiattack action, which maps to HasExtraAttack.
    /// </summary>
    private static bool MonsterHasMultiattack(MonsterData monster)
    {
        if (monster.Actions == null)
            return false;

        foreach (var action in monster.Actions)
        {
            if (action.Name.Equals("Multiattack", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the primary attack action for a monster.
    /// Prefers melee attacks (has Reach) over ranged (has Range).
    /// Falls back to the first action with an attack bonus.
    /// </summary>
    private static MonsterAction FindPrimaryAttack(List<MonsterAction> actions)
    {
        MonsterAction firstMelee = null;
        MonsterAction firstRanged = null;
        MonsterAction firstAttack = null;

        foreach (var action in actions)
        {
            // Skip Multiattack -- it's a meta-action, not an actual attack
            if (action.Name.Equals("Multiattack", StringComparison.OrdinalIgnoreCase))
                continue;

            bool hasMeleeReach = !string.IsNullOrEmpty(action.Reach);
            bool hasRange = !string.IsNullOrEmpty(action.Range);
            bool hasAttackBonus = action.AttackBonus.HasValue;

            if (hasMeleeReach)
                firstMelee ??= action;
            else if (hasRange)
                firstRanged ??= action;
            else if (hasAttackBonus)
                firstAttack ??= action;
        }

        return firstMelee ?? firstRanged ?? firstAttack;
    }

    /// <summary>
    /// Parses walk speed from the monster's speed string or structured data.
    /// Returns the base walking speed in feet, defaulting to 30.
    /// </summary>
    private static int ParseWalkSpeed(string speedText)
    {
        if (string.IsNullOrEmpty(speedText))
            return 30;

        // Try to parse as a plain number first
        if (int.TryParse(speedText, out int plainSpeed))
            return plainSpeed;

        // Parse "30 ft." or "30 ft., fly 60 ft." -- take the first number
        foreach (char c in speedText)
        {
            if (char.IsDigit(c))
            {
                int start = speedText.IndexOf(c);
                int end = start;
                while (end < speedText.Length && char.IsDigit(speedText[end]))
                    end++;

                if (int.TryParse(speedText.Substring(start, end - start), out int parsed))
                    return parsed;
            }
        }

        return 30;
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static string ResolvePath(string path)
    {
        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Godot.ProjectSettings.GlobalizePath(path);
            }
            catch
            {
                return path.Replace("res://", "");
            }
        }

        return path;
    }
}
