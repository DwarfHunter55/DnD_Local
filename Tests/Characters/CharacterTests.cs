using NUnit.Framework;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Tests.Characters;

/// <summary>
/// Tests for the Character data model and AbilityScores calculations.
/// </summary>
[TestFixture]
public class CharacterTests
{
    // ── AbilityScores Modifier Calculation ───────────────────────────

    [TestCase(1, -5)]
    [TestCase(7, -2)]
    [TestCase(8, -1)]
    [TestCase(9, -1)]
    [TestCase(10, 0)]
    [TestCase(11, 0)]
    [TestCase(12, 1)]
    [TestCase(13, 1)]
    [TestCase(14, 2)]
    [TestCase(15, 2)]
    [TestCase(16, 3)]
    [TestCase(18, 4)]
    [TestCase(20, 5)]
    [TestCase(30, 10)]
    public void AbilityScores_GetModifier_CalculatesCorrectly(int score, int expectedMod)
    {
        var scores = new AbilityScores { Strength = score };
        Assert.That(scores.GetModifier(Ability.Strength), Is.EqualTo(expectedMod));
    }

    [Test]
    public void AbilityScores_GetAndSetScore_RoundTrips()
    {
        var scores = new AbilityScores();
        scores.SetScore(Ability.Dexterity, 16);
        Assert.That(scores.GetScore(Ability.Dexterity), Is.EqualTo(16));
    }

    // ── Proficiency Bonus ───────────────────────────────────────────

    [TestCase(1, 2)]
    [TestCase(4, 2)]
    [TestCase(5, 3)]
    [TestCase(8, 3)]
    [TestCase(9, 4)]
    [TestCase(12, 4)]
    [TestCase(13, 5)]
    [TestCase(16, 5)]
    [TestCase(17, 6)]
    [TestCase(20, 6)]
    public void ProficiencyBonus_ScalesWithLevel(int level, int expectedBonus)
    {
        var character = new Character { Level = level };
        Assert.That(character.ProficiencyBonus, Is.EqualTo(expectedBonus));
    }

    // ── Skill Modifier ──────────────────────────────────────────────

    [Test]
    public void GetSkillModifier_NoProficiency_ReturnsBaseAbilityMod()
    {
        var character = new Character
        {
            Level = 1,
            AbilityScores = new AbilityScores { Wisdom = 14 } // +2
        };

        // Perception uses Wisdom. No proficiency, so just +2.
        Assert.That(character.GetSkillModifier(Skill.Perception), Is.EqualTo(2));
    }

    [Test]
    public void GetSkillModifier_WithProficiency_AddsBonus()
    {
        var character = new Character
        {
            Level = 1,
            AbilityScores = new AbilityScores { Wisdom = 14 } // +2
        };
        character.Skills[Skill.Perception] = ProficiencyLevel.Proficient;

        // +2 (WIS) + 2 (prof at level 1) = +4
        Assert.That(character.GetSkillModifier(Skill.Perception), Is.EqualTo(4));
    }

    [Test]
    public void GetSkillModifier_WithExpertise_DoublesProficiency()
    {
        var character = new Character
        {
            Level = 5, // Prof bonus = 3
            AbilityScores = new AbilityScores { Dexterity = 16 } // +3
        };
        character.Skills[Skill.Stealth] = ProficiencyLevel.Expertise;

        // +3 (DEX) + 6 (expertise = 2x3) = +9
        Assert.That(character.GetSkillModifier(Skill.Stealth), Is.EqualTo(9));
    }

    // ── Passive Perception ──────────────────────────────────────────

    [Test]
    public void PassivePerception_Is10PlusPerceptionModifier()
    {
        var character = new Character
        {
            Level = 1,
            AbilityScores = new AbilityScores { Wisdom = 12 } // +1
        };
        character.Skills[Skill.Perception] = ProficiencyLevel.Proficient;

        // 10 + 1 (WIS) + 2 (prof) = 13
        Assert.That(character.PassivePerception, Is.EqualTo(13));
    }

    // ── GetAbilityForSkill ──────────────────────────────────────────

    [TestCase(Skill.Athletics, Ability.Strength)]
    [TestCase(Skill.Acrobatics, Ability.Dexterity)]
    [TestCase(Skill.Stealth, Ability.Dexterity)]
    [TestCase(Skill.Arcana, Ability.Intelligence)]
    [TestCase(Skill.Perception, Ability.Wisdom)]
    [TestCase(Skill.Persuasion, Ability.Charisma)]
    public void GetAbilityForSkill_MapsCorrectly(Skill skill, Ability expectedAbility)
    {
        Assert.That(Character.GetAbilityForSkill(skill), Is.EqualTo(expectedAbility));
    }

    // ── EquipmentSlotResolver ───────────────────────────────────────

    [Test]
    public void EquipmentSlotResolver_MeleeWeapon_GoesToMainHand()
    {
        var item = new InventoryItem
        {
            Name = "Longsword",
            IsEquippable = true,
            Properties = new Dictionary<string, string>
            {
                { "type", "Weapon" },
                { "weaponProperties", "Versatile" }
            }
        };

        var slots = EquipmentSlotResolver.GetValidSlots(item);
        Assert.That(slots, Does.Contain(EquipmentSlot.MainHand));
    }

    [Test]
    public void EquipmentSlotResolver_LightWeapon_CanDualWield()
    {
        var item = new InventoryItem
        {
            Name = "Dagger",
            IsEquippable = true,
            Properties = new Dictionary<string, string>
            {
                { "type", "Weapon" },
                { "weaponProperties", "Light, Finesse, Thrown" }
            }
        };

        var slots = EquipmentSlotResolver.GetValidSlots(item);
        Assert.That(slots, Does.Contain(EquipmentSlot.MainHand));
        Assert.That(slots, Does.Contain(EquipmentSlot.OffHand));
        Assert.That(slots, Does.Contain(EquipmentSlot.Ranged)); // Thrown
    }

    [Test]
    public void EquipmentSlotResolver_Shield_GoesToOffHand()
    {
        var item = new InventoryItem
        {
            Name = "Shield",
            IsEquippable = true,
            Properties = new Dictionary<string, string>
            {
                { "type", "Armor" },
                { "category", "Shield" }
            }
        };

        var slots = EquipmentSlotResolver.GetValidSlots(item);
        Assert.That(slots, Has.Exactly(1).Items);
        Assert.That(slots[0], Is.EqualTo(EquipmentSlot.OffHand));
    }

    [Test]
    public void EquipmentSlotResolver_NonEquippable_ReturnsEmpty()
    {
        var item = new InventoryItem { Name = "Rope", IsEquippable = false };
        var slots = EquipmentSlotResolver.GetValidSlots(item);
        Assert.That(slots, Is.Empty);
    }

    [Test]
    public void EquipmentSlotResolver_TwoHanded_DetectsCorrectly()
    {
        var item = new InventoryItem
        {
            Name = "Greatsword",
            IsEquippable = true,
            Properties = new Dictionary<string, string>
            {
                { "type", "Weapon" },
                { "weaponProperties", "Heavy, Two-Handed" }
            }
        };

        Assert.That(EquipmentSlotResolver.IsWeaponTwoHanded(item), Is.True);
    }
}
