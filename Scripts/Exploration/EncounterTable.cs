using Newtonsoft.Json;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// A single entry in an encounter table defining a monster group that can appear.
/// </summary>
public class EncounterTableEntry
{
    /// <summary>
    /// Monster name exactly matching a name in monsters.json.
    /// </summary>
    [JsonProperty("monster_name")]
    public string MonsterName { get; set; } = string.Empty;

    /// <summary>
    /// Minimum number of this monster in the encounter.
    /// </summary>
    [JsonProperty("min_count")]
    public int MinCount { get; set; } = 1;

    /// <summary>
    /// Maximum number of this monster in the encounter.
    /// </summary>
    [JsonProperty("max_count")]
    public int MaxCount { get; set; } = 1;

    /// <summary>
    /// Relative weight for random selection. Higher = more likely.
    /// </summary>
    [JsonProperty("weight")]
    public int Weight { get; set; } = 1;

    /// <summary>
    /// Minimum danger level of the location for this entry to appear.
    /// Entries with a higher MinDangerLevel than the current location are excluded.
    /// </summary>
    [JsonProperty("min_danger_level")]
    public int MinDangerLevel { get; set; }
}

/// <summary>
/// Defines the pool of possible random encounters for a location or area.
/// Each table has a unique ID referenced by Location.EncounterTableId.
/// </summary>
public class EncounterTable
{
    [JsonProperty("table_id")]
    public string TableId { get; set; } = string.Empty;

    [JsonProperty("entries")]
    public List<EncounterTableEntry> Entries { get; set; } = new();

    /// <summary>
    /// Rolls a random encounter from the table, returning a list of monster names.
    /// Filters entries by danger level and selects one using weighted random.
    /// The count for the selected entry is randomized between MinCount and MaxCount.
    /// </summary>
    /// <param name="dangerLevel">The location's danger level (0-5).</param>
    /// <param name="rng">Random number generator for deterministic testing.</param>
    /// <returns>List of monster names for the encounter, or empty if no valid entries.</returns>
    public List<string> RollEncounter(int dangerLevel, Random rng)
    {
        var result = new List<string>();

        // Filter entries that qualify for this danger level
        var eligible = new List<EncounterTableEntry>();
        foreach (var entry in Entries)
        {
            if (entry.MinDangerLevel <= dangerLevel)
                eligible.Add(entry);
        }

        if (eligible.Count == 0)
            return result;

        // Weighted random selection
        int totalWeight = 0;
        foreach (var entry in eligible)
            totalWeight += entry.Weight;

        if (totalWeight <= 0)
            return result;

        int roll = rng.Next(totalWeight);
        EncounterTableEntry selected = eligible[0];
        int cumulative = 0;

        foreach (var entry in eligible)
        {
            cumulative += entry.Weight;
            if (roll < cumulative)
            {
                selected = entry;
                break;
            }
        }

        // Randomize count within the entry's range
        int count = selected.MinCount;
        if (selected.MaxCount > selected.MinCount)
            count = rng.Next(selected.MinCount, selected.MaxCount + 1);

        for (int i = 0; i < count; i++)
            result.Add(selected.MonsterName);

        return result;
    }
}
