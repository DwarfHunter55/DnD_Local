using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Loads and indexes spell data from the SRD spells.json file.
/// Provides fast lookups by name, level, and class.
/// </summary>
public class SpellDatabase
{
    private readonly Dictionary<string, SpellData> _spellsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, List<SpellData>> _spellsByLevel = new();
    private readonly Dictionary<string, List<SpellData>> _spellsByClass = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Total number of spells loaded.</summary>
    public int Count => _spellsByName.Count;

    /// <summary>
    /// Loads spells from a JSON file at the given path.
    /// The JSON uses snake_case keys which are mapped to SpellData properties.
    /// </summary>
    /// <exception cref="System.IO.FileNotFoundException">If the file does not exist.</exception>
    /// <exception cref="JsonException">If the JSON is malformed.</exception>
    public void LoadSpells(string path)
    {
        if (!System.IO.File.Exists(path))
            throw new System.IO.FileNotFoundException($"Spell data file not found: {path}", path);

        string json = System.IO.File.ReadAllText(path);
        var rawArray = JArray.Parse(json);

        _spellsByName.Clear();
        _spellsByLevel.Clear();
        _spellsByClass.Clear();

        foreach (var token in rawArray)
        {
            var spell = DeserializeSpell(token);
            if (spell == null)
                continue;

            _spellsByName[spell.Name] = spell;

            // Index by level.
            if (!_spellsByLevel.TryGetValue(spell.Level, out var levelList))
            {
                levelList = new List<SpellData>();
                _spellsByLevel[spell.Level] = levelList;
            }
            levelList.Add(spell);

            // Index by class.
            foreach (string className in spell.Classes)
            {
                if (!_spellsByClass.TryGetValue(className, out var classList))
                {
                    classList = new List<SpellData>();
                    _spellsByClass[className] = classList;
                }
                classList.Add(spell);
            }
        }
    }

    /// <summary>
    /// Gets a spell by exact name (case-insensitive).
    /// Returns null if not found.
    /// </summary>
    public SpellData? GetSpell(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        _spellsByName.TryGetValue(name, out var spell);
        return spell;
    }

    /// <summary>
    /// Gets all spells of a given level. Level 0 returns cantrips.
    /// Returns an empty list if no spells exist at that level.
    /// </summary>
    public IReadOnlyList<SpellData> GetSpellsByLevel(int level)
    {
        return _spellsByLevel.TryGetValue(level, out var list)
            ? list.AsReadOnly()
            : Array.Empty<SpellData>();
    }

    /// <summary>
    /// Gets all spells available to a class (case-insensitive).
    /// Returns an empty list if the class has no spells.
    /// </summary>
    public IReadOnlyList<SpellData> GetSpellsByClass(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return Array.Empty<SpellData>();

        return _spellsByClass.TryGetValue(className, out var list)
            ? list.AsReadOnly()
            : Array.Empty<SpellData>();
    }

    /// <summary>
    /// Gets all cantrips (level 0) available to a class.
    /// </summary>
    public IReadOnlyList<SpellData> GetCantrips(string className)
    {
        return GetSpellsByClass(className)
            .Where(s => s.Level == 0)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Searches spells by name or description containing the query (case-insensitive).
    /// Returns an empty list if no matches found.
    /// </summary>
    public IReadOnlyList<SpellData> SearchSpells(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SpellData>();

        string lowerQuery = query.ToLowerInvariant();
        return _spellsByName.Values
            .Where(s => s.Name.ToLowerInvariant().Contains(lowerQuery)
                     || s.Description.ToLowerInvariant().Contains(lowerQuery))
            .OrderBy(s => s.Level)
            .ThenBy(s => s.Name)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Deserializes a single spell from a JToken, mapping snake_case JSON keys
    /// to the SpellData model. Handles null damage/damage_type/saving_throw fields.
    /// </summary>
    private static SpellData? DeserializeSpell(JToken token)
    {
        string? name = token["name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var spell = new SpellData
        {
            Name = name,
            Level = token["level"]?.Value<int>() ?? 0,
            CastingTime = token["casting_time"]?.Value<string>() ?? string.Empty,
            Range = token["range"]?.Value<string>() ?? string.Empty,
            Components = token["components"]?.Value<string>() ?? string.Empty,
            Duration = token["duration"]?.Value<string>() ?? string.Empty,
            Description = token["description"]?.Value<string>() ?? string.Empty,
            IsConcentration = token["concentration"]?.Value<bool>() ?? false,
            IsRitual = token["ritual"]?.Value<bool>() ?? false,
            Damage = token["damage"]?.Value<string>() ?? string.Empty,
            HigherLevelDescription = token["higher_levels"]?.Value<string>() ?? string.Empty,
        };

        // Parse school enum.
        string? schoolStr = token["school"]?.Value<string>();
        if (!string.IsNullOrEmpty(schoolStr) && Enum.TryParse<SpellSchool>(schoolStr, true, out var school))
            spell.School = school;

        // Parse damage type enum. JSON uses lowercase (e.g. "fire", "radiant").
        string? dmgTypeStr = token["damage_type"]?.Value<string>();
        if (!string.IsNullOrEmpty(dmgTypeStr) && Enum.TryParse<DamageType>(dmgTypeStr, true, out var dmgType))
            spell.DamageType = dmgType;

        // Parse saving throw ability. JSON uses full ability name (e.g. "Dexterity").
        string? saveStr = token["saving_throw"]?.Value<string>();
        if (!string.IsNullOrEmpty(saveStr) && Enum.TryParse<Ability>(saveStr, true, out var saveAbility))
            spell.SavingThrow = saveAbility;

        // Parse class list.
        var classArray = token["classes"] as JArray;
        if (classArray != null)
            spell.Classes = classArray.Select(c => c.Value<string>() ?? string.Empty)
                                      .Where(c => c.Length > 0)
                                      .ToList();

        return spell;
    }
}
