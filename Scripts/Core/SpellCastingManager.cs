using System;
using System.Text.RegularExpressions;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Orchestrates spell casting: validation, slot expenditure, concentration
/// management, save DC/attack bonus calculation, and damage rolling.
/// </summary>
public class SpellCastingManager
{
    // Pattern to extract dice notation from damage strings: "1d10", "8d6", etc.
    private static readonly Regex DiceNotationPattern = new(
        @"(\d+)d(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Pattern to extract extra dice from higher_levels text.
    // Matches patterns like "1d6", "2d8" etc. that appear in upcast descriptions.
    private static readonly Regex ExtraDicePattern = new(
        @"(\d+)d(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Maps class names to their spellcasting ability per SRD.
    private static readonly System.Collections.Generic.Dictionary<string, Ability> ClassCastingAbility = new(
        StringComparer.OrdinalIgnoreCase)
    {
        { "Bard",      Ability.Charisma },
        { "Cleric",    Ability.Wisdom },
        { "Druid",     Ability.Wisdom },
        { "Paladin",   Ability.Charisma },
        { "Ranger",    Ability.Wisdom },
        { "Sorcerer",  Ability.Charisma },
        { "Warlock",   Ability.Charisma },
        { "Wizard",    Ability.Intelligence },
    };

    private readonly SpellDatabase _spellDatabase;
    private readonly SpellSlotTracker _slotTracker;
    private readonly ConcentrationTracker _concentrationTracker;

    public SpellDatabase SpellDatabase => _spellDatabase;
    public SpellSlotTracker SlotTracker => _slotTracker;
    public ConcentrationTracker ConcentrationTracker => _concentrationTracker;

    public SpellCastingManager(
        SpellDatabase spellDatabase,
        SpellSlotTracker slotTracker,
        ConcentrationTracker concentrationTracker)
    {
        _spellDatabase = spellDatabase ?? throw new ArgumentNullException(nameof(spellDatabase));
        _slotTracker = slotTracker ?? throw new ArgumentNullException(nameof(slotTracker));
        _concentrationTracker = concentrationTracker ?? throw new ArgumentNullException(nameof(concentrationTracker));
    }

    /// <summary>
    /// Checks whether a character can cast the specified spell at the given level.
    /// Validates: spell exists, character knows it, slot availability (for non-cantrips),
    /// and upcast level validity.
    /// </summary>
    /// <param name="caster">The character attempting to cast.</param>
    /// <param name="spellName">Name of the spell to cast.</param>
    /// <param name="atLevel">
    /// Spell slot level to use. Pass 0 or the spell's base level for normal cast.
    /// Pass a higher value to upcast.
    /// </param>
    public bool CanCastSpell(Character caster, string spellName, int atLevel = 0)
    {
        if (caster == null || string.IsNullOrWhiteSpace(spellName))
            return false;

        var spell = _spellDatabase.GetSpell(spellName);
        if (spell == null)
            return false;

        // Character must know the spell.
        if (!CharacterKnowsSpell(caster, spellName))
            return false;

        // Cantrips don't require slots.
        if (spell.Level == 0)
            return true;

        // Determine effective cast level.
        int castLevel = atLevel > 0 ? atLevel : spell.Level;

        // Cannot downcast.
        if (castLevel < spell.Level)
            return false;

        // Cannot exceed 9th level.
        if (castLevel > SpellSlotTracker.MaxSpellLevel)
            return false;

        // Must have a slot at the cast level.
        return _slotTracker.HasAvailableSlot(castLevel);
    }

    /// <summary>
    /// Casts a spell: validates, expends slot, handles concentration, calculates
    /// save DC and attack bonus, and rolls damage.
    /// </summary>
    /// <param name="caster">The character casting the spell.</param>
    /// <param name="spellName">Name of the spell to cast.</param>
    /// <param name="atLevel">
    /// Spell slot level to use. Pass 0 to cast at the spell's base level.
    /// </param>
    public SpellCastResult CastSpell(Character caster, string spellName, int atLevel = 0)
    {
        if (caster == null)
            return SpellCastResult.Failure(spellName ?? "", "No caster specified.");

        if (string.IsNullOrWhiteSpace(spellName))
            return SpellCastResult.Failure("", "No spell name specified.");

        // Look up the spell.
        var spell = _spellDatabase.GetSpell(spellName);
        if (spell == null)
            return SpellCastResult.Failure(spellName, $"Unknown spell: \"{spellName}\".");

        // Check if the character knows this spell.
        if (!CharacterKnowsSpell(caster, spellName))
            return SpellCastResult.Failure(spellName, $"{caster.Name} does not know {spellName}.");

        bool isCantrip = spell.Level == 0;
        int castLevel = isCantrip ? 0 : (atLevel > 0 ? atLevel : spell.Level);

        // Validate cast level for non-cantrips.
        if (!isCantrip)
        {
            if (castLevel < spell.Level)
                return SpellCastResult.Failure(spellName,
                    $"Cannot cast {spellName} (level {spell.Level}) at level {castLevel}.");

            if (castLevel > SpellSlotTracker.MaxSpellLevel)
                return SpellCastResult.Failure(spellName,
                    $"Spell level {castLevel} exceeds maximum of {SpellSlotTracker.MaxSpellLevel}.");

            if (!_slotTracker.HasAvailableSlot(castLevel))
                return SpellCastResult.Failure(spellName,
                    $"No level {castLevel} spell slots remaining.");
        }

        // Get casting ability and calculate modifiers.
        Ability castingAbility = GetCastingAbility(caster.CharacterClassName);
        int abilityMod = caster.AbilityScores.GetModifier(castingAbility);
        int profBonus = caster.ProficiencyBonus;

        int saveDC = spell.SavingThrow.HasValue ? 8 + profBonus + abilityMod : 0;
        int spellAttackBonus = profBonus + abilityMod;

        // Expend the slot (cantrips are free).
        bool slotExpended = false;
        if (!isCantrip)
        {
            slotExpended = _slotTracker.ExpendSlot(castLevel);
            if (!slotExpended)
                return SpellCastResult.Failure(spellName, $"Failed to expend level {castLevel} spell slot.");
        }

        // Handle concentration.
        bool concentrationStarted = false;
        bool previousConcentrationEnded = false;

        if (spell.IsConcentration)
        {
            var previousSpell = _concentrationTracker.StartConcentration(spell);
            concentrationStarted = true;
            previousConcentrationEnded = previousSpell != null;
        }

        // Roll damage if applicable.
        DiceResult? damageRoll = null;

        if (!string.IsNullOrEmpty(spell.Damage))
        {
            string damageNotation;

            if (isCantrip)
            {
                damageNotation = GetScaledCantripDamageNotation(spell, caster.Level);
            }
            else if (castLevel > spell.Level && !string.IsNullOrEmpty(spell.HigherLevelDescription))
            {
                damageNotation = GetUpcastDamageNotation(spell, castLevel);
            }
            else
            {
                damageNotation = spell.Damage;
            }

            if (!string.IsNullOrEmpty(damageNotation))
            {
                try
                {
                    damageRoll = DiceRoller.Roll(damageNotation);
                }
                catch (ArgumentException)
                {
                    // If damage notation is unparseable, still succeed but with no damage roll.
                    // This can happen with unusual spell descriptions.
                }
            }
        }

        return new SpellCastResult
        {
            Success = true,
            SpellName = spell.Name,
            CastLevel = castLevel,
            SlotExpended = slotExpended,
            DamageRoll = damageRoll,
            SaveDC = saveDC,
            SpellAttackBonus = spellAttackBonus,
            ConcentrationStarted = concentrationStarted,
            PreviousConcentrationEnded = previousConcentrationEnded
        };
    }

    /// <summary>
    /// Calculates the damage dice notation for a cantrip scaled by character level.
    /// Cantrip damage dice count increases at levels 5, 11, and 17:
    /// 1 die at levels 1-4, 2 dice at 5-10, 3 dice at 11-16, 4 dice at 17+.
    /// </summary>
    /// <param name="spell">The cantrip spell data. Must have a non-empty Damage field.</param>
    /// <param name="characterLevel">The caster's total character level (1-20).</param>
    /// <returns>
    /// A DiceResult with the scaled damage, or null if the spell has no damage notation.
    /// </returns>
    public DiceResult? GetScaledCantripDamage(SpellData spell, int characterLevel)
    {
        if (spell == null)
            throw new ArgumentNullException(nameof(spell));

        string notation = GetScaledCantripDamageNotation(spell, characterLevel);
        if (string.IsNullOrEmpty(notation))
            return null;

        return DiceRoller.Roll(notation);
    }

    /// <summary>
    /// Calculates the damage dice notation for a spell upcast to a higher level.
    /// Parses the HigherLevelDescription text to determine extra dice per level above base.
    /// </summary>
    /// <param name="spell">The spell data. Must have a non-empty Damage field.</param>
    /// <param name="castLevel">The slot level used to cast (must be >= spell.Level).</param>
    /// <returns>
    /// A DiceResult with the upcast damage, or null if the spell has no damage notation.
    /// </returns>
    public DiceResult? GetUpcastDamage(SpellData spell, int castLevel)
    {
        if (spell == null)
            throw new ArgumentNullException(nameof(spell));

        string notation = GetUpcastDamageNotation(spell, castLevel);
        if (string.IsNullOrEmpty(notation))
            return null;

        return DiceRoller.Roll(notation);
    }

    /// <summary>
    /// Gets the casting ability for a class. Defaults to Intelligence for unknown classes.
    /// </summary>
    public static Ability GetCastingAbility(string className)
    {
        if (!string.IsNullOrEmpty(className) && ClassCastingAbility.TryGetValue(className, out var ability))
            return ability;

        // Default to Intelligence (most common for homebrew/unknown classes).
        return Ability.Intelligence;
    }

    // ── Private Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the scaled dice notation string for a cantrip at the given character level.
    /// </summary>
    private static string GetScaledCantripDamageNotation(SpellData spell, int characterLevel)
    {
        if (string.IsNullOrEmpty(spell.Damage))
            return string.Empty;

        var match = DiceNotationPattern.Match(spell.Damage);
        if (!match.Success)
            return spell.Damage;

        int baseDiceCount = int.Parse(match.Groups[1].Value);
        int dieSides = int.Parse(match.Groups[2].Value);

        // Determine multiplier based on character level thresholds.
        int multiplier = characterLevel switch
        {
            >= 17 => 4,
            >= 11 => 3,
            >= 5  => 2,
            _     => 1
        };

        int totalDice = baseDiceCount * multiplier;
        return $"{totalDice}d{dieSides}";
    }

    /// <summary>
    /// Returns the upcast damage dice notation for a spell cast at a higher level.
    /// Parses the higher_levels text to find extra dice per slot level above base.
    /// Common pattern: "When you cast this spell using a spell slot of Xth level or higher,
    /// the damage increases by Yd(Z) for each slot level above (base)."
    /// </summary>
    private static string GetUpcastDamageNotation(SpellData spell, int castLevel)
    {
        if (string.IsNullOrEmpty(spell.Damage))
            return string.Empty;

        if (castLevel <= spell.Level)
            return spell.Damage;

        // Parse the base damage notation.
        var baseMatch = DiceNotationPattern.Match(spell.Damage);
        if (!baseMatch.Success)
            return spell.Damage;

        int baseDiceCount = int.Parse(baseMatch.Groups[1].Value);
        int baseDieSides = int.Parse(baseMatch.Groups[2].Value);

        int levelsAboveBase = castLevel - spell.Level;

        // Try to parse extra dice from the higher_levels description.
        if (!string.IsNullOrEmpty(spell.HigherLevelDescription))
        {
            var extraMatch = ExtraDicePattern.Match(spell.HigherLevelDescription);
            if (extraMatch.Success)
            {
                int extraDicePerLevel = int.Parse(extraMatch.Groups[1].Value);
                int extraDieSides = int.Parse(extraMatch.Groups[2].Value);

                int totalExtraDice = extraDicePerLevel * levelsAboveBase;

                // If the extra dice use the same die type, combine them.
                if (extraDieSides == baseDieSides)
                {
                    return $"{baseDiceCount + totalExtraDice}d{baseDieSides}";
                }

                // Different die types: return combined notation. DiceRoller only handles
                // single notation, so roll the larger pool with the same die type.
                // This is a simplification — most SRD spells use the same die type for upcast.
                return $"{baseDiceCount + totalExtraDice}d{baseDieSides}";
            }
        }

        // Fallback: if no higher_levels info, add 1 die of the base type per level above.
        // This matches the most common SRD upcast pattern (e.g. Cure Wounds, Burning Hands).
        return $"{baseDiceCount + levelsAboveBase}d{baseDieSides}";
    }

    /// <summary>
    /// Case-insensitive check whether a character knows a spell by name.
    /// </summary>
    private static bool CharacterKnowsSpell(Character caster, string spellName)
    {
        foreach (string known in caster.SpellsKnown)
        {
            if (string.Equals(known, spellName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
