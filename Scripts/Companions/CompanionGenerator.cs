using ChroniclesUnbound.Core;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Companions;

/// <summary>
/// Generates a party of 3 AI companions that complement the player's class.
/// Uses CharacterCreator to build each companion's Character, then attaches
/// an AI profile and personality for combat AI and LLM dialogue.
/// </summary>
public static class CompanionGenerator
{
    // ── Fantasy Name Pool ────────────────────────────────────────────

    private static readonly string[] NamePool =
    {
        "Aldric", "Brenna", "Caelum", "Daria", "Erevan",
        "Freya", "Gareth", "Halwen", "Isolde", "Jorath",
        "Kestrel", "Lyara", "Morden", "Nyssa", "Orin",
        "Petra", "Quillan", "Rowena", "Saren", "Theron",
        "Valen", "Wren", "Zephyr", "Mireth", "Dorin"
    };

    // ── Race/Subrace Pairs ───────────────────────────────────────────

    /// <summary>
    /// Weighted pool of (Race, Subrace?) pairs for random selection.
    /// Subraces are required for Elf/Dwarf/Halfling/Gnome; null for others.
    /// </summary>
    private static readonly (string Race, string? Subrace)[] RacePool =
    {
        ("Human", null),
        ("Elf", "High Elf"),
        ("Elf", "Wood Elf"),
        ("Dwarf", "Hill Dwarf"),
        ("Dwarf", "Mountain Dwarf"),
        ("Halfling", "Lightfoot"),
        ("Halfling", "Stout"),
        ("Gnome", "Rock Gnome"),
        ("Half-Elf", null),
        ("Half-Orc", null),
        ("Tiefling", null),
        ("Dragonborn", null),
    };

    // ── Background Pool ──────────────────────────────────────────────

    private static readonly string[] BackgroundPool =
    {
        "Acolyte", "Charlatan", "Criminal", "Entertainer", "Folk Hero",
        "Guild Artisan", "Hermit", "Noble", "Outlander", "Sage",
        "Sailor", "Soldier", "Urchin"
    };

    // ── Class Complement Mapping ─────────────────────────────────────

    /// <summary>
    /// For each known player class, the 3 companion classes that fill party gaps.
    /// </summary>
    private static readonly Dictionary<string, string[]> ClassComplements = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Fighter",   new[] { "Cleric", "Rogue", "Wizard" } },
        { "Wizard",    new[] { "Fighter", "Cleric", "Rogue" } },
        { "Rogue",     new[] { "Fighter", "Cleric", "Wizard" } },
        { "Cleric",    new[] { "Fighter", "Rogue", "Wizard" } },
        { "Barbarian", new[] { "Cleric", "Rogue", "Wizard" } },
        { "Paladin",   new[] { "Rogue", "Wizard", "Ranger" } },
        { "Ranger",    new[] { "Fighter", "Cleric", "Wizard" } },
        { "Sorcerer",  new[] { "Fighter", "Cleric", "Rogue" } },
        { "Warlock",   new[] { "Fighter", "Cleric", "Rogue" } },
        { "Bard",      new[] { "Fighter", "Rogue", "Wizard" } },
        { "Druid",     new[] { "Fighter", "Rogue", "Wizard" } },
        { "Monk",      new[] { "Cleric", "Wizard", "Ranger" } },
    };

    // ── Ability Score Distributions (Standard Array assignments) ─────

    /// <summary>
    /// Standard array values [15, 14, 13, 12, 10, 8] mapped to abilities
    /// in an order that suits each class archetype.
    /// Key = class name, Value = ability order (highest to lowest priority).
    /// </summary>
    private static readonly Dictionary<string, Ability[]> AbilityPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Fighter",  new[] { Ability.Strength, Ability.Constitution, Ability.Dexterity, Ability.Wisdom, Ability.Charisma, Ability.Intelligence } },
        { "Cleric",   new[] { Ability.Wisdom, Ability.Constitution, Ability.Strength, Ability.Charisma, Ability.Dexterity, Ability.Intelligence } },
        { "Rogue",    new[] { Ability.Dexterity, Ability.Constitution, Ability.Charisma, Ability.Intelligence, Ability.Wisdom, Ability.Strength } },
        { "Wizard",   new[] { Ability.Intelligence, Ability.Constitution, Ability.Dexterity, Ability.Wisdom, Ability.Charisma, Ability.Strength } },
        { "Ranger",   new[] { Ability.Dexterity, Ability.Wisdom, Ability.Constitution, Ability.Strength, Ability.Intelligence, Ability.Charisma } },
    };

    // ── Skill Selections Per Class ───────────────────────────────────

    private static readonly Dictionary<string, Skill[]> ClassSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Fighter",  new[] { Skill.Athletics, Skill.Perception } },
        { "Cleric",   new[] { Skill.Medicine, Skill.Religion } },
        { "Rogue",    new[] { Skill.Stealth, Skill.Perception, Skill.SleightOfHand, Skill.Acrobatics } },
        { "Wizard",   new[] { Skill.Arcana, Skill.Investigation } },
        { "Ranger",   new[] { Skill.Perception, Skill.Survival, Skill.Stealth } },
    };

    // ── Personality Templates ────────────────────────────────────────

    private static readonly PersonalityTemplate[] FighterPersonalities =
    {
        new("Steadfast guardian who never leaves an ally behind",
            "Speaks bluntly with military cadence",
            "Always says 'steel holds' before combat",
            "To prove worthy of the family name tarnished by a disgraced parent",
            "Was dishonorably discharged from the king's guard for a crime they didn't commit",
            new[] { "Protective of those weaker than them", "Keeps a strict daily training regimen" },
            "Honor is not given, it is forged through deeds",
            "I will protect those who protected me when I had nothing",
            "I refuse to retreat, even when it's the smart thing to do"),
        new("Battle-scarred veteran with a dry sense of humor",
            "Laconic, rarely uses more words than needed",
            "Chuckles darkly before saying something ominous",
            "To find the warlord who destroyed their homeland",
            "Carries a locket belonging to someone they failed to save",
            new[] { "Laughs at danger, serious about meals", "Sharpens weapons obsessively when nervous" },
            "Strength is nothing without something worth fighting for",
            "My fallen comrades deserve to have their story told",
            "I drink too much to quiet the memories of war"),
        new("Cheerful brawler who sees combat as sport",
            "Boisterous and loud, peppers speech with fighting metaphors",
            "Cracks knuckles before making a point",
            "To become the most renowned warrior in the land",
            "Is terrified of magic but hides it behind bravado",
            new[] { "Challenges everyone to arm wrestling", "Never passes up a tavern brawl" },
            "A good fight is the truest test of character",
            "My mentor believed in me when no one else did",
            "I cannot resist a challenge, even an obvious trap"),
    };

    private static readonly PersonalityTemplate[] ClericPersonalities =
    {
        new("Compassionate healer devoted to easing suffering",
            "Speaks gently, often in parables and blessings",
            "Whispers a short prayer before healing",
            "To build a sanctuary for war orphans",
            "Secretly doubts their faith but clings to it for others' sake",
            new[] { "Cannot ignore anyone in pain", "Hums hymns while working" },
            "Mercy is the greatest strength",
            "The temple that raised me still needs my tithes",
            "I trust too easily and have been burned for it"),
        new("Stern but fair priest who sees divine purpose in everything",
            "Formal and measured, quotes scripture frequently",
            "Closes eyes and touches holy symbol when stressed",
            "To root out a heretical cult that has infiltrated the church",
            "Was once a member of the very cult they now hunt",
            new[] { "Judges others by their deeds, not words", "Wakes before dawn to pray" },
            "The divine plan unfolds whether we understand it or not",
            "I owe everything to the high priest who showed me the true path",
            "I am rigid in my beliefs and struggle to accept other viewpoints"),
        new("Wandering healer who left the temple to serve the common folk",
            "Warm and folksy, uses homespun wisdom",
            "Pats people on the shoulder when comforting them",
            "To find a legendary relic that can cure any disease",
            "Left the temple after a plague they couldn't stop killed hundreds",
            new[] { "Collects herbs and remedies everywhere they go", "Tells long-winded stories about former patients" },
            "Faith without action is just empty words",
            "The villagers who sheltered me during my darkest hour",
            "I carry guilt for those I couldn't save and it clouds my judgment"),
    };

    private static readonly PersonalityTemplate[] RoguePersonalities =
    {
        new("Quick-witted scoundrel with a heart of gold",
            "Speaks fast with street slang, always has a quip ready",
            "Flips a coin when thinking",
            "To pull off one legendary heist and retire",
            "Is secretly the heir to a noble house they ran away from",
            new[] { "Cannot resist picking locks, even when unnecessary", "Winks after every clever remark" },
            "Everyone has a price, but some things are priceless",
            "The street kids in the slums who depend on my stolen coin",
            "I lie even when the truth would serve me better"),
        new("Brooding loner who trusts no one easily",
            "Terse and guarded, speaks in half-truths",
            "Narrows eyes suspiciously at anyone being too friendly",
            "To take revenge on the thieves' guild that betrayed them",
            "Was an assassin for the guild and is haunted by past kills",
            new[] { "Always sits with back to the wall", "Checks every room for exits before relaxing" },
            "Trust is a luxury I can't afford",
            "There's one person from my old life I still care about protecting",
            "I push away anyone who gets close to protect them, or maybe myself"),
        new("Charming trickster who talks their way out of everything",
            "Silver-tongued and theatrical, loves dramatic entrances",
            "Bows with a flourish after a successful deception",
            "To con the wealthiest noble in every major city",
            "Is running from a debt owed to a powerful and dangerous patron",
            new[] { "Adopts fake accents and personas for fun", "Always has an elaborate escape plan" },
            "Why fight when you can simply talk your way past?",
            "My partner-in-crime who took the fall for me years ago",
            "I can never stay in one place or with one group for long"),
    };

    private static readonly PersonalityTemplate[] WizardPersonalities =
    {
        new("Endlessly curious scholar fascinated by the arcane",
            "Academic and precise, corrects others' grammar",
            "Mutters incantation fragments under their breath",
            "To decipher an ancient tome written in a dead language",
            "Accidentally caused a magical catastrophe at their academy",
            new[] { "Takes notes on everything, including conversations", "Gets distracted by any new magical phenomenon" },
            "Knowledge is the only true power",
            "My research could change the world if I can complete it",
            "I value knowledge over people and sometimes forget they have feelings"),
        new("Eccentric hermit who spent decades alone studying magic",
            "Rambling and tangential, often talks to themselves",
            "Snaps fingers to emphasize points, sometimes producing sparks",
            "To understand the fundamental nature of magic itself",
            "Was expelled from the mages' guild for forbidden research",
            new[] { "Forgets to eat when researching", "Names their spell components" },
            "Rules exist to be questioned, especially magical ones",
            "My familiar is the only friend who has never judged me",
            "I have no concept of personal space or social norms"),
        new("Pragmatic battle-mage who values results over theory",
            "Clipped and efficient, no patience for long explanations",
            "Taps staff rhythmically when impatient",
            "To master a spell thought to be lost to history",
            "Served a tyrant with their magic before defecting",
            new[] { "Tests new spells at inappropriate times", "Respects competence above all else" },
            "Magic is a tool. What matters is how you wield it",
            "The students I taught still look up to me despite my fall from grace",
            "I can be cold and calculating, treating people like variables in an equation"),
    };

    private static readonly PersonalityTemplate[] RangerPersonalities =
    {
        new("Silent wilderness tracker at home in the wild",
            "Speaks softly and rarely, lets actions speak instead",
            "Whistles birdcalls as signals",
            "To find and protect a sacred grove threatened by corruption",
            "Was raised by a reclusive druid after being abandoned as a child",
            new[] { "Prefers sleeping outdoors even when an inn is available", "Communicates better with animals than people" },
            "Nature provides for those who listen",
            "The forest that sheltered me is being destroyed and I must stop it",
            "I distrust civilization and those who come from it"),
        new("Gregarious hunter who loves telling campfire tales",
            "Animated and descriptive, exaggerates heroic feats",
            "Mimics animal sounds in conversation",
            "To track down a legendary beast no one has ever caught",
            "Let a dangerous creature escape once and people died because of it",
            new[] { "Identifies every plant and animal encountered", "Keeps trophies from hunts" },
            "The hunt teaches you everything about your quarry and yourself",
            "My hunting partner who was killed by the creature I'm tracking",
            "I underestimate threats from people because I'm used to reading animals"),
    };

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>
    /// Generates 3 companion characters that complement the player's class,
    /// each with a fully built Character, AI combat profile, and LLM personality.
    /// </summary>
    /// <param name="playerCharacter">The player's built Character (must have class and level set).</param>
    /// <param name="racesJsonPath">Path to races.json (res:// or absolute).</param>
    /// <param name="classesJsonPath">Path to classes.json (res:// or absolute).</param>
    /// <param name="backgroundsJsonPath">Path to backgrounds.json (res:// or absolute).</param>
    /// <returns>List of 3 tuples, each containing the companion's Character, AI profile, and personality.</returns>
    public static List<(Character Character, CompanionAIProfile AIProfile, CompanionPersonality Personality)> GenerateParty(
        Character playerCharacter,
        string racesJsonPath = "res://Data/SRD/races.json",
        string classesJsonPath = "res://Data/SRD/classes.json",
        string backgroundsJsonPath = "res://Data/SRD/backgrounds.json")
    {
        if (playerCharacter == null)
            throw new ArgumentNullException(nameof(playerCharacter));
        if (string.IsNullOrWhiteSpace(playerCharacter.CharacterClassName))
            throw new ArgumentException("Player character must have a class assigned.");

        var rng = new Random();
        string[] companionClasses = ResolveCompanionClasses(playerCharacter.CharacterClassName, rng);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { playerCharacter.Name };
        var usedRaces = new HashSet<int>(); // Indices into RacePool to encourage diversity.
        var party = new List<(Character, CompanionAIProfile, CompanionPersonality)>(3);

        for (int i = 0; i < 3; i++)
        {
            string companionClass = companionClasses[i];

            // Pick a unique name.
            string name = PickUniqueName(rng, usedNames);
            usedNames.Add(name);

            // Pick a race, encouraging diversity.
            var (raceName, subraceName) = PickRace(rng, usedRaces);

            // Pick a random background.
            string background = BackgroundPool[rng.Next(BackgroundPool.Length)];

            // Build the character via CharacterCreator.
            var creator = new CharacterCreator(racesJsonPath, classesJsonPath, backgroundsJsonPath);
            creator.SelectRace(raceName, subraceName);
            creator.SelectClass(companionClass);
            creator.SelectBackground(background);
            AssignAbilityScores(creator, companionClass);
            AssignClassSkills(creator, companionClass);
            creator.SetNameAndDetails(name, backstory: "", alignment: PickAlignment(companionClass, rng));

            Character character = creator.Build();
            character.IsCompanion = true;
            character.Level = playerCharacter.Level;

            // Build AI profile.
            CompanionAIProfile aiProfile = BuildAIProfile(companionClass, rng);

            // Build personality.
            CompanionPersonality personality = BuildPersonality(companionClass, name, character.Race, rng);

            party.Add((character, aiProfile, personality));
        }

        // Wire up inter-companion attitudes now that all 3 exist.
        WireCompanionAttitudes(party, rng);

        return party;
    }

    // ── Private: Class Resolution ────────────────────────────────────

    /// <summary>
    /// Returns the 3 companion classes to fill party gaps based on the player's class.
    /// Falls back to a balanced default if the player's class isn't in the lookup.
    /// </summary>
    private static string[] ResolveCompanionClasses(string playerClass, Random rng)
    {
        if (ClassComplements.TryGetValue(playerClass, out var classes))
            return classes;

        // Unknown class: pick 3 from the core set excluding any overlap with the player.
        var candidates = new List<string> { "Fighter", "Cleric", "Rogue", "Wizard", "Ranger" };
        candidates.RemoveAll(c => c.Equals(playerClass, StringComparison.OrdinalIgnoreCase));

        // Shuffle and take 3.
        Shuffle(candidates, rng);
        return candidates.Take(3).ToArray();
    }

    // ── Private: Name Selection ──────────────────────────────────────

    private static string PickUniqueName(Random rng, HashSet<string> usedNames)
    {
        // Try random picks first (fast path for the common case).
        for (int attempts = 0; attempts < 50; attempts++)
        {
            string candidate = NamePool[rng.Next(NamePool.Length)];
            if (!usedNames.Contains(candidate))
                return candidate;
        }

        // Fallback: linear scan for any unused name.
        foreach (string name in NamePool)
        {
            if (!usedNames.Contains(name))
                return name;
        }

        // Extremely unlikely: all names used. Append a number.
        return $"{NamePool[0]}-{rng.Next(100, 999)}";
    }

    // ── Private: Race Selection ──────────────────────────────────────

    private static (string Race, string? Subrace) PickRace(Random rng, HashSet<int> usedIndices)
    {
        // Try to pick a race index not already used (for party diversity).
        for (int attempts = 0; attempts < 30; attempts++)
        {
            int idx = rng.Next(RacePool.Length);
            if (!usedIndices.Contains(idx))
            {
                usedIndices.Add(idx);
                return RacePool[idx];
            }
        }

        // Fallback: just pick any.
        int fallback = rng.Next(RacePool.Length);
        usedIndices.Add(fallback);
        return RacePool[fallback];
    }

    // ── Private: Ability Scores ──────────────────────────────────────

    /// <summary>
    /// Assigns standard array scores in priority order for the companion's class.
    /// </summary>
    private static void AssignAbilityScores(CharacterCreator creator, string className)
    {
        int[] array = CharacterCreator.GenerateStandardArray(); // [15, 14, 13, 12, 10, 8]

        if (!AbilityPriorities.TryGetValue(className, out var priorities))
            priorities = AbilityPriorities["Fighter"]; // Safe fallback.

        var scores = new AbilityScores();
        for (int i = 0; i < 6; i++)
        {
            scores.SetScore(priorities[i], array[i]);
        }

        creator.SetAbilityScores(scores);
    }

    // ── Private: Skill Selection ─────────────────────────────────────

    private static void AssignClassSkills(CharacterCreator creator, string className)
    {
        if (ClassSkills.TryGetValue(className, out var skills))
        {
            creator.SelectSkills(skills.ToList());
        }
        else
        {
            // Fallback: no extra class skills beyond background.
            creator.SelectSkills(new List<Skill>());
        }
    }

    // ── Private: Alignment ───────────────────────────────────────────

    private static Alignment PickAlignment(string className, Random rng)
    {
        // Lean toward alignments that fit the archetype, with some randomness.
        return className.ToLowerInvariant() switch
        {
            "fighter" => Pick(rng, Alignment.LawfulGood, Alignment.LawfulNeutral, Alignment.NeutralGood, Alignment.ChaoticGood),
            "cleric"  => Pick(rng, Alignment.LawfulGood, Alignment.NeutralGood, Alignment.LawfulNeutral),
            "rogue"   => Pick(rng, Alignment.ChaoticGood, Alignment.ChaoticNeutral, Alignment.TrueNeutral, Alignment.NeutralGood),
            "wizard"  => Pick(rng, Alignment.TrueNeutral, Alignment.LawfulNeutral, Alignment.NeutralGood, Alignment.ChaoticNeutral),
            "ranger"  => Pick(rng, Alignment.NeutralGood, Alignment.ChaoticGood, Alignment.TrueNeutral),
            _         => Pick(rng, Alignment.TrueNeutral, Alignment.NeutralGood, Alignment.ChaoticGood),
        };
    }

    private static T Pick<T>(Random rng, params T[] options) => options[rng.Next(options.Length)];

    // ── Private: AI Profile ──────────────────────────────────────────

    private static CompanionAIProfile BuildAIProfile(string className, Random rng)
    {
        var profile = className.ToLowerInvariant() switch
        {
            "fighter" => new CompanionAIProfile
            {
                Aggression = RandRange(rng, 7, 8),
                SelfPreservation = RandRange(rng, 6, 7),
                Teamwork = RandRange(rng, 5, 6),
                Creativity = RandRange(rng, 3, 5),
                PreferredTargets = TargetPreference.Nearest,
                TacticPreference = CombatTacticPreference.Aggressive,
            },
            "cleric" => new CompanionAIProfile
            {
                Aggression = RandRange(rng, 3, 4),
                SelfPreservation = RandRange(rng, 5, 6),
                Teamwork = RandRange(rng, 8, 9),
                Creativity = RandRange(rng, 4, 6),
                PreferredTargets = TargetPreference.Nearest,
                TacticPreference = CombatTacticPreference.Support,
            },
            "rogue" => new CompanionAIProfile
            {
                Aggression = RandRange(rng, 5, 6),
                SelfPreservation = RandRange(rng, 7, 8),
                Teamwork = RandRange(rng, 3, 4),
                Creativity = RandRange(rng, 7, 8),
                PreferredTargets = TargetPreference.Weakest,
                TacticPreference = CombatTacticPreference.Ranged,
            },
            "wizard" => new CompanionAIProfile
            {
                Aggression = RandRange(rng, 5, 6),
                SelfPreservation = RandRange(rng, 6, 7),
                Teamwork = RandRange(rng, 5, 6),
                Creativity = RandRange(rng, 8, 9),
                PreferredTargets = TargetPreference.Spellcasters,
                TacticPreference = CombatTacticPreference.Ranged,
            },
            "ranger" => new CompanionAIProfile
            {
                Aggression = RandRange(rng, 5, 7),
                SelfPreservation = RandRange(rng, 6, 7),
                Teamwork = RandRange(rng, 5, 6),
                Creativity = RandRange(rng, 5, 7),
                PreferredTargets = TargetPreference.Weakest,
                TacticPreference = CombatTacticPreference.Ranged,
            },
            _ => new CompanionAIProfile() // Defaults are balanced 5s.
        };

        profile.Autonomy = CompanionAutonomy.Suggest;
        profile.ClampScores();
        return profile;
    }

    /// <summary>
    /// Returns a random int in [min, max] inclusive.
    /// </summary>
    private static int RandRange(Random rng, int min, int max) => rng.Next(min, max + 1);

    // ── Private: Personality ─────────────────────────────────────────

    private static CompanionPersonality BuildPersonality(string className, string name, string race, Random rng)
    {
        PersonalityTemplate template = className.ToLowerInvariant() switch
        {
            "fighter" => Pick(rng, FighterPersonalities),
            "cleric"  => Pick(rng, ClericPersonalities),
            "rogue"   => Pick(rng, RoguePersonalities),
            "wizard"  => Pick(rng, WizardPersonalities),
            "ranger"  => Pick(rng, RangerPersonalities),
            _         => Pick(rng, FighterPersonalities), // Fallback.
        };

        return new CompanionPersonality
        {
            Name = name,
            Race = race,
            Class = className,
            PersonalityTraits = template.Traits.ToList(),
            Ideal = template.Ideal,
            Bond = template.Bond,
            Flaw = template.Flaw,
            SpeechPattern = template.SpeechPattern,
            VerbalTic = template.VerbalTic,
            CoreMotivation = template.CoreMotivation,
            Secret = template.Secret,
            AttitudeTowardPlayer = GeneratePlayerAttitude(className, rng),
            Backstory = $"{name} is a {race} {className}. {template.CoreMotivation}.",
        };
    }

    private static string GeneratePlayerAttitude(string className, Random rng)
    {
        string[][] attitudes = className.ToLowerInvariant() switch
        {
            "fighter" => new[]
            {
                new[] { "Respects the player's courage and wants to see if they're worthy of trust" },
                new[] { "Cautiously loyal, willing to follow if the player proves competent" },
                new[] { "Eager to fight alongside someone who seems capable" },
            },
            "cleric" => new[]
            {
                new[] { "Sees the player as someone worth guiding and protecting" },
                new[] { "Believes divine fate brought them together for a purpose" },
                new[] { "Warmly supportive but quietly evaluating the player's morality" },
            },
            "rogue" => new[]
            {
                new[] { "Sizing up the player, unsure if they're a mark or an ally" },
                new[] { "Amused by the player's earnestness, sticking around out of curiosity" },
                new[] { "Wary but sees an opportunity in traveling together" },
            },
            "wizard" => new[]
            {
                new[] { "Intellectually curious about the player's abilities" },
                new[] { "Tolerates the player as a useful travel companion" },
                new[] { "Interested in what adventures with the player might teach them" },
            },
            "ranger" => new[]
            {
                new[] { "Observing the player like a new species in the wild" },
                new[] { "Quietly protective, doesn't say much but watches out for the player" },
            },
            _ => new[]
            {
                new[] { "Cautiously optimistic about traveling together" },
            },
        };

        var options = attitudes[rng.Next(attitudes.Length)];
        return options[0];
    }

    // ── Private: Inter-Companion Attitudes ───────────────────────────

    private static readonly string[] PositiveAttitudes =
    {
        "Respects their dedication and skill",
        "Enjoys their company despite their differences",
        "Sees a kindred spirit in them",
        "Admires their courage, even if they'd never say it",
        "Finds their quirks endearing",
    };

    private static readonly string[] NeutralAttitudes =
    {
        "Tolerates them as a competent ally",
        "Doesn't fully trust them yet but acknowledges their usefulness",
        "Keeps them at arm's length for now",
        "Neither likes nor dislikes them — time will tell",
    };

    private static readonly string[] TenseAttitudes =
    {
        "Finds their methods distasteful but respects the results",
        "Clashes with their worldview but grudgingly cooperates",
        "Distrusts their motives but needs them for now",
    };

    private static void WireCompanionAttitudes(
        List<(Character Character, CompanionAIProfile AIProfile, CompanionPersonality Personality)> party,
        Random rng)
    {
        // Pool of attitudes weighted toward positive/neutral to keep party functional.
        var allAttitudes = new List<string>();
        allAttitudes.AddRange(PositiveAttitudes);
        allAttitudes.AddRange(PositiveAttitudes); // Double weight for positive.
        allAttitudes.AddRange(NeutralAttitudes);
        allAttitudes.AddRange(TenseAttitudes);

        for (int i = 0; i < party.Count; i++)
        {
            for (int j = 0; j < party.Count; j++)
            {
                if (i == j) continue;
                string otherName = party[j].Personality.Name;
                string attitude = allAttitudes[rng.Next(allAttitudes.Count)];
                party[i].Personality.AttitudeTowardCompanions[otherName] = attitude;
            }
        }
    }

    // ── Private: Utilities ───────────────────────────────────────────

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ── Personality Template ─────────────────────────────────────────

    /// <summary>
    /// Immutable bundle of personality data used as a template during generation.
    /// </summary>
    private sealed class PersonalityTemplate
    {
        public string CoreMotivation { get; }
        public string SpeechPattern { get; }
        public string VerbalTic { get; }
        public string Secret { get; }
        public string[] Traits { get; }
        public string Ideal { get; }
        public string Bond { get; }
        public string Flaw { get; }

        // NOTE: first param is a summary line (unused directly but kept as the template description).

        public PersonalityTemplate(
            string _summary,
            string speechPattern,
            string verbalTic,
            string coreMotivation,
            string secret,
            string[] traits,
            string ideal,
            string bond,
            string flaw)
        {
            SpeechPattern = speechPattern;
            VerbalTic = verbalTic;
            CoreMotivation = coreMotivation;
            Secret = secret;
            Traits = traits;
            Ideal = ideal;
            Bond = bond;
            Flaw = flaw;
        }
    }
}
