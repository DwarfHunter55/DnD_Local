using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ChroniclesUnbound.Data;
using ChroniclesUnbound.Journal;
using ChroniclesUnbound.Exploration;
using ChroniclesUnbound.Companions;

namespace ChroniclesUnbound.Tests.Data;

/// <summary>
/// Test suite for persistence and data loading in Chronicles Unbound.
/// Tests JSON serialization/deserialization round-trips and SRD data loading.
/// </summary>
[TestFixture]
public class DataTests
{
    private const string SRDDataPath = @"F:\Projects\DnD local\Data\SRD\";

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // CampaignSaveData Tests
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Test]
    public void CampaignSaveData_JsonRoundTrip_AllFieldsPreserved()
    {
        // Arrange: Create a fully populated CampaignSaveData object
        var original = new CampaignSaveData
        {
            ExplorationSnapshot = new ExplorationSnapshot
            {
                State = ExplorationState.Exploring,
                LocationId = "loc_silverwood_tavern",
                LocationName = "The Silver Hart Tavern",
                LocationType = "inn",
                LocationDescription = "A cozy establishment with a roaring fireplace."
            },
            QuestDataJson = "{\"activeQuests\":[\"quest_save_the_village\"]}",
            WorldDiscovery = new Dictionary<string, bool>
            {
                { "loc_silverwood", true },
                { "loc_dark_forest", false },
                { "loc_ancient_ruins", true }
            },
            RelationshipScores = new Dictionary<string, int>
            {
                { "companion_elara", 45 },
                { "companion_thorne", -12 },
                { "companion_zara", 78 }
            },
            RelationshipHistories = new Dictionary<string, List<RelationshipEvent>>
            {
                {
                    "companion_elara",
                    new List<RelationshipEvent>
                    {
                        new RelationshipEvent
                        {
                            EventType = RelationshipEventType.DialogueResponse,
                            ScoreChange = 10,
                            Description = "Agreed with her plan",
                            Timestamp = new DateTime(2024, 3, 15, 14, 30, 0, DateTimeKind.Utc)
                        }
                    }
                }
            },
            JournalDataJson = "{\"entries\":[],\"nextId\":1}",
            NarrativeHistory = new List<string>
            {
                "You enter the tavern.",
                "The innkeeper greets you warmly.",
                "A hooded figure watches from the corner."
            },
            CurrentScene = "Inside The Silver Hart Tavern",
            SuggestedActions = new List<string>
            {
                "Talk to the innkeeper",
                "Approach the hooded figure",
                "Order a drink",
                "Rest for the night"
            }
        };

        // Act: Serialize to JSON, then deserialize back
        string json = original.ToJson();
        var restored = CampaignSaveData.FromJson(json);

        // Assert: All fields match
        Assert.That(restored, Is.Not.Null, "Deserialization should succeed");

        // Exploration snapshot
        Assert.That(restored!.ExplorationSnapshot, Is.Not.Null);
        Assert.That(restored.ExplorationSnapshot!.State, Is.EqualTo(ExplorationState.Exploring));
        Assert.That(restored.ExplorationSnapshot.LocationId, Is.EqualTo("loc_silverwood_tavern"));
        Assert.That(restored.ExplorationSnapshot.LocationName, Is.EqualTo("The Silver Hart Tavern"));
        Assert.That(restored.ExplorationSnapshot.LocationType, Is.EqualTo("inn"));
        Assert.That(restored.ExplorationSnapshot.LocationDescription, Contains.Substring("cozy"));

        // Quest data
        Assert.That(restored.QuestDataJson, Is.EqualTo(original.QuestDataJson));

        // World discovery
        Assert.That(restored.WorldDiscovery, Is.Not.Null);
        Assert.That(restored.WorldDiscovery!.Count, Is.EqualTo(3));
        Assert.That(restored.WorldDiscovery["loc_silverwood"], Is.True);
        Assert.That(restored.WorldDiscovery["loc_dark_forest"], Is.False);

        // Relationships
        Assert.That(restored.RelationshipScores, Is.Not.Null);
        Assert.That(restored.RelationshipScores!["companion_elara"], Is.EqualTo(45));
        Assert.That(restored.RelationshipScores["companion_thorne"], Is.EqualTo(-12));
        Assert.That(restored.RelationshipScores["companion_zara"], Is.EqualTo(78));

        Assert.That(restored.RelationshipHistories, Is.Not.Null);
        Assert.That(restored.RelationshipHistories!["companion_elara"], Has.Count.EqualTo(1));
        Assert.That(restored.RelationshipHistories["companion_elara"][0].ScoreChange, Is.EqualTo(10));

        // Journal
        Assert.That(restored.JournalDataJson, Is.EqualTo(original.JournalDataJson));

        // Narrative
        Assert.That(restored.NarrativeHistory, Is.Not.Null);
        Assert.That(restored.NarrativeHistory!.Count, Is.EqualTo(3));
        Assert.That(restored.NarrativeHistory[2], Contains.Substring("hooded figure"));

        Assert.That(restored.CurrentScene, Is.EqualTo("Inside The Silver Hart Tavern"));

        Assert.That(restored.SuggestedActions, Is.Not.Null);
        Assert.That(restored.SuggestedActions!.Count, Is.EqualTo(4));
        Assert.That(restored.SuggestedActions, Contains.Item("Order a drink"));
    }

    [Test]
    public void CampaignSaveData_JsonRoundTrip_NullableFieldsHandled()
    {
        // Arrange: Create a minimal save with nulls
        var original = new CampaignSaveData
        {
            CurrentScene = "Starting area",
            NarrativeHistory = new List<string> { "The adventure begins..." }
        };

        // Act
        string json = original.ToJson();
        var restored = CampaignSaveData.FromJson(json);

        // Assert
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.CurrentScene, Is.EqualTo("Starting area"));
        Assert.That(restored.NarrativeHistory, Has.Count.EqualTo(1));
        Assert.That(restored.ExplorationSnapshot, Is.Null);
        Assert.That(restored.QuestDataJson, Is.Null);
        Assert.That(restored.WorldDiscovery, Is.Null);
    }

    [Test]
    public void CampaignSaveData_FromJson_InvalidInput_ReturnsNull()
    {
        // Act & Assert
        Assert.That(CampaignSaveData.FromJson(null), Is.Null);
        Assert.That(CampaignSaveData.FromJson(""), Is.Null);
        Assert.That(CampaignSaveData.FromJson("   "), Is.Null);
        Assert.That(CampaignSaveData.FromJson("{invalid json}"), Is.Null);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // JournalEntry Tests
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Test]
    public void JournalEntry_JsonRoundTrip_AllCategories()
    {
        // Test all journal categories
        var categories = new[]
        {
            JournalCategory.Quest,
            JournalCategory.NPC,
            JournalCategory.Location,
            JournalCategory.Combat,
            JournalCategory.Item,
            JournalCategory.LevelUp,
            JournalCategory.Trade,
            JournalCategory.Note
        };

        foreach (var category in categories)
        {
            // Arrange
            var original = new JournalEntry
            {
                Id = 42,
                Category = category,
                Title = $"Test {category} Entry",
                Description = $"This is a test entry for the {category} category.",
                Timestamp = "Day 5, 14:30",
                RealTimestamp = "2024-03-15T14:30:00Z",
                RelatedEntityId = "entity_123",
                LocationName = "Silverwood",
                IsRead = false,
                Tags = new List<string> { "important", "test", category.ToString().ToLower() }
            };

            // Act
            string json = JsonConvert.SerializeObject(original, Formatting.None);
            var restored = JsonConvert.DeserializeObject<JournalEntry>(json);

            // Assert
            Assert.That(restored, Is.Not.Null, $"Failed to deserialize {category}");
            Assert.That(restored!.Id, Is.EqualTo(42));
            Assert.That(restored.Category, Is.EqualTo(category));
            Assert.That(restored.Title, Is.EqualTo($"Test {category} Entry"));
            Assert.That(restored.Description, Contains.Substring(category.ToString()));
            Assert.That(restored.Timestamp, Is.EqualTo("Day 5, 14:30"));
            Assert.That(restored.RealTimestamp, Is.EqualTo("2024-03-15T14:30:00Z"));
            Assert.That(restored.RelatedEntityId, Is.EqualTo("entity_123"));
            Assert.That(restored.LocationName, Is.EqualTo("Silverwood"));
            Assert.That(restored.IsRead, Is.False);
            Assert.That(restored.Tags, Has.Count.EqualTo(3));
            Assert.That(restored.Tags, Contains.Item("important"));
        }
    }

    [Test]
    public void JournalEntry_JsonRoundTrip_WithTags()
    {
        // Arrange
        var original = new JournalEntry
        {
            Id = 1,
            Category = JournalCategory.Quest,
            Title = "The Lost Artifact",
            Description = "Find the ancient relic in the ruins.",
            Timestamp = "Day 1",
            RealTimestamp = DateTime.UtcNow.ToString("o"),
            Tags = new List<string> { "main_quest", "urgent", "silverwood_arc" }
        };

        // Act
        string json = JsonConvert.SerializeObject(original);
        var restored = JsonConvert.DeserializeObject<JournalEntry>(json);

        // Assert
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Tags, Has.Count.EqualTo(3));
        Assert.That(restored.Tags, Contains.Item("main_quest"));
        Assert.That(restored.Tags, Contains.Item("urgent"));
        Assert.That(restored.Tags, Contains.Item("silverwood_arc"));
    }

    [Test]
    public void JournalEntry_JsonRoundTrip_NullableFieldsOmitted()
    {
        // Arrange: Entry with no related entity or location
        var original = new JournalEntry
        {
            Id = 100,
            Category = JournalCategory.Note,
            Title = "Personal Note",
            Description = "Remember to check inventory.",
            Timestamp = "Day 10",
            RealTimestamp = "2024-03-20T10:00:00Z",
            IsRead = true
        };

        // Act
        string json = JsonConvert.SerializeObject(original);
        var restored = JsonConvert.DeserializeObject<JournalEntry>(json);

        // Assert
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.RelatedEntityId, Is.Null);
        Assert.That(restored.LocationName, Is.Null);
        Assert.That(restored.IsRead, Is.True);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // MagicEffect Tests
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Test]
    public void MagicEffect_JsonRoundTrip_PassiveBonus()
    {
        // Arrange: +1 weapon bonus
        var original = new MagicEffect
        {
            Type = MagicEffectType.Passive,
            Stat = MagicBonusStat.AttackRoll,
            Value = "+1",
            Description = "Grants a +1 bonus to attack rolls"
        };

        // Act
        string json = JsonConvert.SerializeObject(original);
        var restored = JsonConvert.DeserializeObject<MagicEffect>(json);

        // Assert
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Type, Is.EqualTo(MagicEffectType.Passive));
        Assert.That(restored.Stat, Is.EqualTo(MagicBonusStat.AttackRoll));
        Assert.That(restored.Value, Is.EqualTo("+1"));
        Assert.That(restored.Description, Contains.Substring("attack rolls"));
    }

    [Test]
    public void MagicEffect_JsonRoundTrip_Resistance()
    {
        // Arrange: Fire resistance
        var original = new MagicEffect
        {
            Type = MagicEffectType.Resistance,
            DamageType = "Fire",
            Description = "Grants resistance to fire damage"
        };

        // Act
        string json = JsonConvert.SerializeObject(original);
        var restored = JsonConvert.DeserializeObject<MagicEffect>(json);

        // Assert
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Type, Is.EqualTo(MagicEffectType.Resistance));
        Assert.That(restored.DamageType, Is.EqualTo("Fire"));
    }

    [Test]
    public void MagicEffect_JsonRoundTrip_ExtraDamage()
    {
        // Arrange: Flame Tongue extra damage
        var original = new MagicEffect
        {
            Type = MagicEffectType.ExtraDamage,
            DamageType = "Fire",
            Dice = "2d6",
            Condition = "",
            Description = "Deals an extra 2d6 fire damage"
        };

        // Act
        string json = JsonConvert.SerializeObject(original);
        var restored = JsonConvert.DeserializeObject<MagicEffect>(json);

        // Assert
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Type, Is.EqualTo(MagicEffectType.ExtraDamage));
        Assert.That(restored.Dice, Is.EqualTo("2d6"));
        Assert.That(restored.DamageType, Is.EqualTo("Fire"));
    }

    [Test]
    public void MagicEffect_JsonRoundTrip_ActiveAbility()
    {
        // Arrange: Staff of Fire's Fireball
        var original = new MagicEffect
        {
            Type = MagicEffectType.Active,
            Name = "Fireball",
            Description = "Cast Fireball (save DC 15) as an action",
            CostCharges = 3
        };

        // Act
        string json = JsonConvert.SerializeObject(original);
        var restored = JsonConvert.DeserializeObject<MagicEffect>(json);

        // Assert
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Type, Is.EqualTo(MagicEffectType.Active));
        Assert.That(restored.Name, Is.EqualTo("Fireball"));
        Assert.That(restored.CostCharges, Is.EqualTo(3));
    }

    [Test]
    public void MagicEffect_JsonRoundTrip_Immunity()
    {
        // Arrange: Poison immunity
        var original = new MagicEffect
        {
            Type = MagicEffectType.Immunity,
            DamageType = "Poison",
            Description = "Grants immunity to poison damage and the poisoned condition"
        };

        // Act
        string json = JsonConvert.SerializeObject(original);
        var restored = JsonConvert.DeserializeObject<MagicEffect>(json);

        // Assert
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Type, Is.EqualTo(MagicEffectType.Immunity));
        Assert.That(restored.DamageType, Is.EqualTo("Poison"));
    }

    [Test]
    public void MagicEffect_JsonRoundTrip_AllBonusStats()
    {
        // Test all MagicBonusStat enum values
        var stats = new[]
        {
            MagicBonusStat.AttackRoll,
            MagicBonusStat.DamageRoll,
            MagicBonusStat.AC,
            MagicBonusStat.SavingThrows,
            MagicBonusStat.AbilityScore,
            MagicBonusStat.SpellDC,
            MagicBonusStat.Speed,
            MagicBonusStat.HP
        };

        foreach (var stat in stats)
        {
            // Arrange
            var original = new MagicEffect
            {
                Type = MagicEffectType.Passive,
                Stat = stat,
                Value = "+2",
                Description = $"Grants +2 to {stat}"
            };

            // Act
            string json = JsonConvert.SerializeObject(original);
            var restored = JsonConvert.DeserializeObject<MagicEffect>(json);

            // Assert
            Assert.That(restored, Is.Not.Null, $"Failed for {stat}");
            Assert.That(restored!.Stat, Is.EqualTo(stat));
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // SRD JSON File Loading Tests
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Test]
    public void SRDData_EquipmentJson_LoadsWithoutErrors()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "equipment.json");

        // Act
        string jsonContent = File.ReadAllText(filePath);
        JArray? equipmentArray = null;

        Assert.DoesNotThrow(() =>
        {
            equipmentArray = JArray.Parse(jsonContent);
        }, "equipment.json should parse without errors");

        // Assert
        Assert.That(equipmentArray, Is.Not.Null);
        Assert.That(equipmentArray!.Count, Is.GreaterThan(0), "Equipment list should not be empty");
    }

    [Test]
    public void SRDData_EquipmentJson_ContainsLongsword()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "equipment.json");
        string jsonContent = File.ReadAllText(filePath);
        JArray equipmentArray = JArray.Parse(jsonContent);

        // Act
        var longsword = equipmentArray
            .FirstOrDefault(item => item["name"]?.ToString() == "Longsword");

        // Assert
        Assert.That(longsword, Is.Not.Null, "Longsword should exist in equipment.json");
        Assert.That(longsword!["description"]?.ToString(), Is.Not.Empty);
        Assert.That(longsword["properties"], Is.Not.Null);
        Assert.That(longsword["properties"]!["type"]?.ToString(), Is.EqualTo("Weapon"));
    }

    [Test]
    public void SRDData_SpellsJson_LoadsWithoutErrors()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "spells.json");

        // Act
        string jsonContent = File.ReadAllText(filePath);
        JArray? spellsArray = null;

        Assert.DoesNotThrow(() =>
        {
            spellsArray = JArray.Parse(jsonContent);
        }, "spells.json should parse without errors");

        // Assert
        Assert.That(spellsArray, Is.Not.Null);
        Assert.That(spellsArray!.Count, Is.GreaterThan(0), "Spells list should not be empty");
    }

    [Test]
    public void SRDData_SpellsJson_ContainsFireball()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "spells.json");
        string jsonContent = File.ReadAllText(filePath);
        JArray spellsArray = JArray.Parse(jsonContent);

        // Act
        var fireball = spellsArray
            .FirstOrDefault(spell => spell["name"]?.ToString() == "Fireball");

        // Assert
        Assert.That(fireball, Is.Not.Null, "Fireball should exist in spells.json");
        Assert.That(fireball!["level"]?.ToObject<int>(), Is.EqualTo(3));
        Assert.That(fireball["school"]?.ToString(), Is.EqualTo("Evocation"));
        Assert.That(fireball["range"]?.ToString(), Contains.Substring("150"));
    }

    [Test]
    public void SRDData_MonstersJson_LoadsWithoutErrors()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "monsters.json");

        // Act
        string jsonContent = File.ReadAllText(filePath);
        JArray? monstersArray = null;

        Assert.DoesNotThrow(() =>
        {
            monstersArray = JArray.Parse(jsonContent);
        }, "monsters.json should parse without errors");

        // Assert
        Assert.That(monstersArray, Is.Not.Null);
        Assert.That(monstersArray!.Count, Is.GreaterThan(0), "Monsters list should not be empty");
    }

    [Test]
    public void SRDData_ClassesJson_LoadsWithoutErrors()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "classes.json");

        // Act
        string jsonContent = File.ReadAllText(filePath);
        JArray? classesArray = null;

        Assert.DoesNotThrow(() =>
        {
            classesArray = JArray.Parse(jsonContent);
        }, "classes.json should parse without errors");

        // Assert
        Assert.That(classesArray, Is.Not.Null);
        Assert.That(classesArray!.Count, Is.GreaterThan(0), "Classes list should not be empty");
    }

    [Test]
    public void SRDData_RacesJson_LoadsWithoutErrors()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "races.json");

        // Act
        string jsonContent = File.ReadAllText(filePath);
        JArray? racesArray = null;

        Assert.DoesNotThrow(() =>
        {
            racesArray = JArray.Parse(jsonContent);
        }, "races.json should parse without errors");

        // Assert
        Assert.That(racesArray, Is.Not.Null);
        Assert.That(racesArray!.Count, Is.GreaterThan(0), "Races list should not be empty");
    }

    [Test]
    public void SRDData_BackgroundsJson_LoadsWithoutErrors()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "backgrounds.json");

        // Act
        string jsonContent = File.ReadAllText(filePath);
        JArray? backgroundsArray = null;

        Assert.DoesNotThrow(() =>
        {
            backgroundsArray = JArray.Parse(jsonContent);
        }, "backgrounds.json should parse without errors");

        // Assert
        Assert.That(backgroundsArray, Is.Not.Null);
        Assert.That(backgroundsArray!.Count, Is.GreaterThan(0), "Backgrounds list should not be empty");
    }

    [Test]
    public void SRDData_MagicItemsJson_LoadsWithoutErrors()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "magic_items.json");

        // Act
        string jsonContent = File.ReadAllText(filePath);
        JArray? magicItemsArray = null;

        Assert.DoesNotThrow(() =>
        {
            magicItemsArray = JArray.Parse(jsonContent);
        }, "magic_items.json should parse without errors");

        // Assert
        Assert.That(magicItemsArray, Is.Not.Null);
        Assert.That(magicItemsArray!.Count, Is.GreaterThan(0), "Magic items list should not be empty");
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Data Integrity Tests
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Test]
    public void SRDData_Equipment_NoDuplicateNames()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "equipment.json");
        string jsonContent = File.ReadAllText(filePath);
        JArray equipmentArray = JArray.Parse(jsonContent);

        // Act
        var names = equipmentArray
            .Select(item => item["name"]?.ToString())
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var duplicates = names
            .GroupBy(name => name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        // Assert
        Assert.That(duplicates, Is.Empty,
            $"Found duplicate equipment names: {string.Join(", ", duplicates)}");
    }

    [Test]
    public void SRDData_Spells_NoDuplicateNames()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "spells.json");
        string jsonContent = File.ReadAllText(filePath);
        JArray spellsArray = JArray.Parse(jsonContent);

        // Act
        var names = spellsArray
            .Select(spell => spell["name"]?.ToString())
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var duplicates = names
            .GroupBy(name => name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        // Assert
        Assert.That(duplicates, Is.Empty,
            $"Found duplicate spell names: {string.Join(", ", duplicates)}");
    }

    [Test]
    public void SRDData_Monsters_NoDuplicateNames()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "monsters.json");
        string jsonContent = File.ReadAllText(filePath);
        JArray monstersArray = JArray.Parse(jsonContent);

        // Act
        var names = monstersArray
            .Select(monster => monster["name"]?.ToString())
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var duplicates = names
            .GroupBy(name => name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        // Assert
        Assert.That(duplicates, Is.Empty,
            $"Found duplicate monster names: {string.Join(", ", duplicates)}");
    }

    [Test]
    public void SRDData_Classes_NoDuplicateNames()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "classes.json");
        string jsonContent = File.ReadAllText(filePath);
        JArray classesArray = JArray.Parse(jsonContent);

        // Act
        var names = classesArray
            .Select(cls => cls["name"]?.ToString())
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var duplicates = names
            .GroupBy(name => name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        // Assert
        Assert.That(duplicates, Is.Empty,
            $"Found duplicate class names: {string.Join(", ", duplicates)}");
    }

    [Test]
    public void SRDData_Races_NoDuplicateNames()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "races.json");
        string jsonContent = File.ReadAllText(filePath);
        JArray racesArray = JArray.Parse(jsonContent);

        // Act
        var names = racesArray
            .Select(race => race["name"]?.ToString())
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var duplicates = names
            .GroupBy(name => name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        // Assert
        Assert.That(duplicates, Is.Empty,
            $"Found duplicate race names: {string.Join(", ", duplicates)}");
    }

    [Test]
    public void SRDData_Equipment_AllItemsHaveRequiredFields()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "equipment.json");
        string jsonContent = File.ReadAllText(filePath);
        JArray equipmentArray = JArray.Parse(jsonContent);

        // Act & Assert
        foreach (var item in equipmentArray)
        {
            string itemName = item["name"]?.ToString() ?? "<unnamed>";

            Assert.That(item["name"], Is.Not.Null, $"Item missing name");
            Assert.That(item["name"]!.ToString(), Is.Not.Empty, $"Item has empty name");
            Assert.That(item["description"], Is.Not.Null, $"{itemName} missing description");
            Assert.That(item["rarity"], Is.Not.Null, $"{itemName} missing rarity");
            Assert.That(item["isEquippable"], Is.Not.Null, $"{itemName} missing isEquippable flag");
        }
    }

    [Test]
    public void SRDData_Spells_AllSpellsHaveRequiredFields()
    {
        // Arrange
        string filePath = Path.Combine(SRDDataPath, "spells.json");
        string jsonContent = File.ReadAllText(filePath);
        JArray spellsArray = JArray.Parse(jsonContent);

        // Act & Assert
        foreach (var spell in spellsArray)
        {
            string spellName = spell["name"]?.ToString() ?? "<unnamed>";

            Assert.That(spell["name"], Is.Not.Null, $"Spell missing name");
            Assert.That(spell["name"]!.ToString(), Is.Not.Empty, $"Spell has empty name");
            Assert.That(spell["level"], Is.Not.Null, $"{spellName} missing level");
            Assert.That(spell["school"], Is.Not.Null, $"{spellName} missing school");
            Assert.That(spell["casting_time"], Is.Not.Null, $"{spellName} missing casting_time");
        }
    }
}
