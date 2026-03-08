using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Orchestrates D&D 5e character creation through a step-by-step builder pattern.
/// Loads SRD data, validates selections, and produces a fully initialized level 1 Character.
///
/// Typical flow:
///   1. Construct with SRD data paths (or pre-loaded lists).
///   2. SelectRace / SelectClass / SelectBackground
///   3. Set ability scores via one of: SetAbilityScores, GeneratePointBuy, RollAbilityScores
///   4. SelectSkills (class + background skills)
///   5. SetNameAndDetails
///   6. Build() -> Character
/// </summary>
public class CharacterCreator
{
    // ── SRD Data ─────────────────────────────────────────────────────

    private readonly List<RaceData> _races;
    private readonly List<ClassData> _classes;
    private readonly List<BackgroundData> _backgrounds;

    // ── Current Selections ───────────────────────────────────────────

    private RaceData? _selectedRace;
    private SubraceData? _selectedSubrace;
    private ClassData? _selectedClass;
    private BackgroundData? _selectedBackground;
    private AbilityScores? _abilityScores;
    private List<Skill> _selectedSkills = new();
    private string _name = string.Empty;
    private string _backstory = string.Empty;
    private Alignment _alignment = Alignment.TrueNeutral;

    /// <summary>
    /// Chosen ability bonuses for races with ability_bonus_choices (e.g. Half-Elf).
    /// Keys are lowercase ability names ("strength", "dexterity", etc.).
    /// </summary>
    private Dictionary<string, int>? _chosenAbilityBonuses;

    // ── Standard Array ───────────────────────────────────────────────

    private static readonly int[] StandardArray = { 15, 14, 13, 12, 10, 8 };

    // ── Point Buy Costs ──────────────────────────────────────────────

    private const int PointBuyBudget = 27;
    private const int PointBuyMin = 8;
    private const int PointBuyMax = 15;

    // ── Constructors ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a CharacterCreator by loading SRD data from the given file paths.
    /// </summary>
    public CharacterCreator(string racesJsonPath, string classesJsonPath, string backgroundsJsonPath)
    {
        _races = SRDDataLoader.LoadRaces(racesJsonPath);
        _classes = SRDDataLoader.LoadClasses(classesJsonPath);
        _backgrounds = SRDDataLoader.LoadBackgrounds(backgroundsJsonPath);
    }

    /// <summary>
    /// Creates a CharacterCreator with pre-loaded SRD data (useful for caching / tests).
    /// </summary>
    public CharacterCreator(List<RaceData> races, List<ClassData> classes, List<BackgroundData> backgrounds)
    {
        _races = races ?? throw new ArgumentNullException(nameof(races));
        _classes = classes ?? throw new ArgumentNullException(nameof(classes));
        _backgrounds = backgrounds ?? throw new ArgumentNullException(nameof(backgrounds));
    }

    // ── Data Access ──────────────────────────────────────────────────

    public List<RaceData> GetAvailableRaces() => _races;
    public List<ClassData> GetAvailableClasses() => _classes;
    public List<BackgroundData> GetAvailableBackgrounds() => _backgrounds;

    public RaceData? SelectedRace => _selectedRace;
    public SubraceData? SelectedSubrace => _selectedSubrace;
    public ClassData? SelectedClass => _selectedClass;
    public BackgroundData? SelectedBackground => _selectedBackground;

    // ── Selection Methods ────────────────────────────────────────────

    /// <summary>
    /// Selects a race by name. If the race has subraces and subraceName is provided,
    /// that subrace is also selected. Throws if the race/subrace is not found.
    /// </summary>
    public void SelectRace(string raceName, string? subraceName = null)
    {
        _selectedRace = _races.Find(r => r.Name.Equals(raceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Race not found: {raceName}");

        _selectedSubrace = null;
        if (!string.IsNullOrEmpty(subraceName))
        {
            _selectedSubrace = _selectedRace.Subraces.Find(
                s => s.Name.Equals(subraceName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(
                    $"Subrace '{subraceName}' not found for race '{raceName}'.");
        }
        else if (_selectedRace.Subraces.Count > 0)
        {
            // Race has subraces but none was selected -- caller should pick one.
            // We don't throw here to allow deferred subrace selection.
        }
    }

    /// <summary>
    /// Selects a class by name. Throws if the class is not found.
    /// </summary>
    public void SelectClass(string className)
    {
        _selectedClass = _classes.Find(c => c.Name.Equals(className, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Class not found: {className}");
    }

    /// <summary>
    /// Selects a background by name. Throws if the background is not found.
    /// </summary>
    public void SelectBackground(string backgroundName)
    {
        _selectedBackground = _backgrounds.Find(
            b => b.Name.Equals(backgroundName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Background not found: {backgroundName}");
    }

    /// <summary>
    /// For races with ability_bonus_choices (e.g. Half-Elf), set the chosen bonuses.
    /// Keys are lowercase ability names, values are the bonus amounts.
    /// </summary>
    public void SetAbilityBonusChoices(Dictionary<string, int> choices)
    {
        _chosenAbilityBonuses = choices;
    }

    // ── Ability Score Generation ─────────────────────────────────────

    /// <summary>
    /// Returns the D&D 5e standard array: [15, 14, 13, 12, 10, 8].
    /// The caller must assign these to abilities using SetAbilityScores.
    /// </summary>
    public static int[] GenerateStandardArray()
    {
        return (int[])StandardArray.Clone();
    }

    /// <summary>
    /// Validates and creates ability scores using the point buy system.
    /// Each ability starts at 8. Scores 8-13 cost 1 point each; 14-15 cost 2 points each.
    /// Total budget: 27 points. Scores must be in [8, 15] range.
    /// </summary>
    /// <exception cref="ArgumentException">If allocations are invalid or exceed budget.</exception>
    public AbilityScores GeneratePointBuy(Dictionary<Ability, int> allocations)
    {
        if (allocations.Count != 6)
            throw new ArgumentException("Must provide allocations for all 6 ability scores.");

        int totalCost = 0;
        foreach (var (ability, score) in allocations)
        {
            if (score < PointBuyMin || score > PointBuyMax)
                throw new ArgumentException(
                    $"{ability} score {score} is out of point buy range [{PointBuyMin}-{PointBuyMax}].");

            totalCost += CalculatePointBuyCost(score);
        }

        if (totalCost > PointBuyBudget)
            throw new ArgumentException(
                $"Point buy cost {totalCost} exceeds budget of {PointBuyBudget}.");

        var scores = new AbilityScores();
        foreach (var (ability, score) in allocations)
        {
            scores.SetScore(ability, score);
        }

        _abilityScores = scores;
        return scores;
    }

    /// <summary>
    /// Rolls ability scores using the 4d6 drop lowest method via DiceRoller.
    /// Returns the generated AbilityScores (in order: STR, DEX, CON, INT, WIS, CHA).
    /// The caller can then rearrange if desired by calling SetAbilityScores.
    /// </summary>
    public AbilityScores RollAbilityScores()
    {
        var scores = new AbilityScores
        {
            Strength = DiceRoller.RollAbilityScore(),
            Dexterity = DiceRoller.RollAbilityScore(),
            Constitution = DiceRoller.RollAbilityScore(),
            Intelligence = DiceRoller.RollAbilityScore(),
            Wisdom = DiceRoller.RollAbilityScore(),
            Charisma = DiceRoller.RollAbilityScore()
        };

        _abilityScores = scores;
        return scores;
    }

    /// <summary>
    /// Directly sets ability scores (e.g. after the player has assigned standard array values).
    /// These are the BASE scores before racial bonuses are applied.
    /// </summary>
    public void SetAbilityScores(AbilityScores scores)
    {
        _abilityScores = scores ?? throw new ArgumentNullException(nameof(scores));
    }

    // ── Skill Selection ──────────────────────────────────────────────

    /// <summary>
    /// Sets the skill proficiencies chosen by the player. These should include class skill
    /// choices. Background skills are applied automatically during Build().
    /// </summary>
    public void SelectSkills(List<Skill> skills)
    {
        _selectedSkills = skills ?? throw new ArgumentNullException(nameof(skills));
    }

    // ── Identity ─────────────────────────────────────────────────────

    public void SetNameAndDetails(string name, string backstory = "", Alignment alignment = Alignment.TrueNeutral)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _backstory = backstory ?? string.Empty;
        _alignment = alignment;
    }

    // ── Build ────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a fully initialized level 1 Character from the current selections.
    /// Applies racial bonuses, calculates HP, sets proficiencies, features, spell slots,
    /// starting equipment, speed, and AC.
    /// </summary>
    /// <exception cref="InvalidOperationException">If required selections are missing.</exception>
    public Character Build()
    {
        // ── Validate required selections ──
        if (_selectedRace == null)
            throw new InvalidOperationException("No race selected.");
        if (_selectedClass == null)
            throw new InvalidOperationException("No class selected.");
        if (_selectedBackground == null)
            throw new InvalidOperationException("No background selected.");
        if (_abilityScores == null)
            throw new InvalidOperationException("Ability scores have not been set.");
        if (string.IsNullOrWhiteSpace(_name))
            throw new InvalidOperationException("Character name has not been set.");

        var character = new Character
        {
            Name = _name,
            Race = _selectedSubrace != null
                ? $"{_selectedRace.Name} ({_selectedSubrace.Name})"
                : _selectedRace.Name,
            CharacterClassName = _selectedClass.Name,
            Level = 1,
            Background = _selectedBackground.Name,
            Alignment = _alignment,
            Backstory = _backstory,
        };

        // ── 1. Apply base ability scores ──
        character.AbilityScores = CloneAbilityScores(_abilityScores);

        // ── 2. Apply racial ability bonuses ──
        ApplyAbilityBonuses(character.AbilityScores, _selectedRace.AbilityBonuses);
        if (_selectedSubrace != null)
            ApplyAbilityBonuses(character.AbilityScores, _selectedSubrace.AbilityBonuses);
        if (_chosenAbilityBonuses != null)
            ApplyAbilityBonuses(character.AbilityScores, _chosenAbilityBonuses);

        // ── 3. Calculate HP: max hit die + CON modifier (+ subrace bonus) ──
        int conMod = character.AbilityScores.GetModifier(Ability.Constitution);
        int hp = _selectedClass.HitDie + conMod;
        if (_selectedSubrace != null)
            hp += _selectedSubrace.HitPointBonusPerLevel;
        hp = Math.Max(hp, 1); // Minimum 1 HP.
        character.HitPoints = hp;
        character.MaxHitPoints = hp;

        // ── 4. Speed ──
        character.Speed = _selectedSubrace?.Speed ?? _selectedRace.Speed;

        // ── 5. Saving throw proficiencies ──
        foreach (string save in _selectedClass.SavingThrows)
        {
            if (TryParseAbility(save, out var ability))
                character.SavingThrowProficiencies.Add(ability);
        }

        // ── 6. Skill proficiencies ──
        // Player-chosen class skills.
        foreach (var skill in _selectedSkills)
        {
            character.Skills[skill] = ProficiencyLevel.Proficient;
        }
        // Background skills (auto-applied).
        foreach (string skillName in _selectedBackground.SkillProficiencies)
        {
            if (TryParseSkill(skillName, out var skill))
                character.Skills[skill] = ProficiencyLevel.Proficient;
        }
        // Racial skill proficiencies (e.g. Elf Perception, Half-Orc Intimidation).
        foreach (string skillName in _selectedRace.Proficiencies.Skills)
        {
            if (TryParseSkill(skillName, out var skill))
                character.Skills[skill] = ProficiencyLevel.Proficient;
        }

        // ── 7. Languages ──
        character.Languages.AddRange(_selectedRace.Languages);
        // Background grants a number of language choices -- we add placeholders.
        // The actual choices would be handled by the UI; here we note the count.
        for (int i = 0; i < _selectedBackground.Languages; i++)
            character.Languages.Add("(choice)");

        // ── 8. Tool proficiencies ──
        character.ToolProficiencies.AddRange(_selectedBackground.ToolProficiencies);
        character.ToolProficiencies.AddRange(_selectedRace.Proficiencies.Tools);

        // ── 9. Features (level 1 class + racial traits + background feature) ──
        var level1Features = _selectedClass.GetFeaturesAtLevel(1);
        foreach (var feature in level1Features)
            character.Features.Add(feature.Name);

        foreach (string trait in _selectedRace.Traits)
            character.Features.Add(trait);

        if (_selectedSubrace != null)
        {
            foreach (string trait in _selectedSubrace.Traits)
                character.Features.Add(trait);
        }

        character.Features.Add(_selectedBackground.Feature.Name);

        // Subclass features at level 1 (e.g. Cleric Divine Domain grants features at 1).
        if (_selectedClass.SubclassLevel == 1 && _selectedClass.Subclass != null)
        {
            foreach (var feature in _selectedClass.Subclass.GetFeaturesAtLevel(1))
                character.Features.Add(feature.Name);
        }

        // ── 10. Spell slots (if caster) ──
        var slotEntry = _selectedClass.GetSpellSlotsAtLevel(1);
        if (slotEntry != null)
        {
            slotEntry.ApplyToSlotArray(character.SpellSlotsMax);
            Array.Copy(character.SpellSlotsMax, character.SpellSlotsCurrent, 9);
        }

        // ── 11. Starting equipment (from background) ──
        foreach (string item in _selectedBackground.Equipment)
        {
            character.InventoryItems.Add(new InventoryItem
            {
                Name = item,
                Quantity = 1
            });
        }

        // ── 12. Calculate AC ──
        // Base AC = 10 + DEX mod (unarmored). Special cases handled for Barbarian/Monk.
        int dexMod = character.AbilityScores.GetModifier(Ability.Dexterity);
        character.ArmorClass = CalculateBaseAC(character, dexMod, conMod);

        // ── 13. Initiative = DEX modifier ──
        character.Initiative = dexMod;

        // ── 14. Personality (leave empty for player to fill or LLM to generate) ──

        return character;
    }

    // ── Private Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Calculates base AC for a level 1 character without armor equipped.
    /// Barbarian: 10 + DEX + CON. Monk: 10 + DEX + WIS. Others: 10 + DEX.
    /// </summary>
    private int CalculateBaseAC(Character character, int dexMod, int conMod)
    {
        if (_selectedClass == null)
            return 10 + dexMod;

        // Barbarian Unarmored Defense: 10 + DEX + CON.
        if (_selectedClass.Name.Equals("Barbarian", StringComparison.OrdinalIgnoreCase))
            return 10 + dexMod + conMod;

        // Monk Unarmored Defense: 10 + DEX + WIS.
        if (_selectedClass.Name.Equals("Monk", StringComparison.OrdinalIgnoreCase))
        {
            int wisMod = character.AbilityScores.GetModifier(Ability.Wisdom);
            return 10 + dexMod + wisMod;
        }

        return 10 + dexMod;
    }

    private static void ApplyAbilityBonuses(AbilityScores scores, Dictionary<string, int> bonuses)
    {
        foreach (var (abilityName, bonus) in bonuses)
        {
            if (TryParseAbility(abilityName, out var ability))
            {
                int current = scores.GetScore(ability);
                scores.SetScore(ability, current + bonus);
            }
        }
    }

    private static AbilityScores CloneAbilityScores(AbilityScores source)
    {
        return new AbilityScores
        {
            Strength = source.Strength,
            Dexterity = source.Dexterity,
            Constitution = source.Constitution,
            Intelligence = source.Intelligence,
            Wisdom = source.Wisdom,
            Charisma = source.Charisma
        };
    }

    /// <summary>
    /// Calculates the point buy cost for a score: 1 point per step from 8-13, 2 per step for 14-15.
    /// </summary>
    private static int CalculatePointBuyCost(int score)
    {
        int cost = 0;
        for (int s = 9; s <= score; s++)
        {
            cost += s <= 13 ? 1 : 2;
        }
        return cost;
    }

    /// <summary>
    /// Attempts to parse an ability name (case-insensitive) into the Ability enum.
    /// </summary>
    private static bool TryParseAbility(string name, out Ability ability)
    {
        return Enum.TryParse(name, ignoreCase: true, out ability);
    }

    /// <summary>
    /// Attempts to parse a skill name (case-insensitive, with space handling) into the Skill enum.
    /// Handles both "Sleight of Hand" and "SleightOfHand" formats.
    /// </summary>
    private static bool TryParseSkill(string name, out Skill skill)
    {
        // Try direct parse first (handles "Perception", "Stealth", etc.).
        if (Enum.TryParse(name, ignoreCase: true, out skill))
            return true;

        // Handle space-separated names like "Animal Handling" -> "AnimalHandling".
        string normalized = name.Replace(" ", "").Replace("of", "Of");
        return Enum.TryParse(normalized, ignoreCase: true, out skill);
    }
}
