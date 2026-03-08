using System;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Tracks concentration state for a single caster. Only one concentration
/// spell can be active at a time per 5e rules.
/// </summary>
public class ConcentrationTracker
{
    /// <summary>The currently concentrated spell, or null if not concentrating.</summary>
    public SpellData? ActiveSpell { get; private set; }

    /// <summary>Whether the caster is currently concentrating on a spell.</summary>
    public bool IsConcentrating => ActiveSpell != null;

    /// <summary>
    /// Begins concentrating on a spell. If already concentrating on a different
    /// spell, the previous concentration ends automatically.
    /// </summary>
    /// <returns>
    /// The previously active spell if concentration was swapped, or null if
    /// no prior concentration was active.
    /// </returns>
    public SpellData? StartConcentration(SpellData spell)
    {
        if (spell == null)
            throw new ArgumentNullException(nameof(spell));

        var previous = ActiveSpell;
        ActiveSpell = spell;
        return previous;
    }

    /// <summary>
    /// Ends concentration on the current spell (if any).
    /// </summary>
    /// <returns>The spell that was being concentrated on, or null if none.</returns>
    public SpellData? EndConcentration()
    {
        var previous = ActiveSpell;
        ActiveSpell = null;
        return previous;
    }

    /// <summary>
    /// Makes a concentration saving throw when the caster takes damage.
    /// DC = max(10, damageTaken / 2), rounded down per 5e rules.
    /// Uses Constitution saving throw: d20 + Constitution modifier.
    /// </summary>
    /// <param name="damageTaken">Amount of damage taken in a single source.</param>
    /// <param name="constitutionModifier">Caster's Constitution modifier.</param>
    /// <returns>
    /// A tuple with:
    /// - maintained: true if the check was passed (roll + mod >= DC)
    /// - dc: the concentration DC that was set
    /// - roll: the natural d20 result
    /// - total: roll + constitutionModifier
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">If damageTaken is negative.</exception>
    public (bool maintained, int dc, int roll, int total) MakeConcentrationCheck(
        int damageTaken,
        int constitutionModifier)
    {
        if (damageTaken < 0)
            throw new ArgumentOutOfRangeException(nameof(damageTaken), "Damage cannot be negative.");

        // Per 5e SRD: DC = max(10, floor(damage / 2))
        int dc = Math.Max(10, damageTaken / 2);
        int roll = DiceRoller.RollSingle(20);
        int total = roll + constitutionModifier;
        bool maintained = total >= dc;

        // If the check fails, concentration ends.
        if (!maintained)
            EndConcentration();

        return (maintained, dc, roll, total);
    }
}
