using System.Collections.Generic;
using Godot;
using ChroniclesUnbound.Combat;

namespace ChroniclesUnbound.Companions;

/// <summary>
/// The complete turn plan produced by the companion combat AI.
/// Contains movement, main action, bonus action, and a human-readable explanation.
/// </summary>
public class CombatAIPlan
{
    /// <summary>Destination cell to move to, or null to stay in place.</summary>
    public Vector2I? MoveTo { get; set; }

    /// <summary>Full path from current position to MoveTo, for visualization/animation.</summary>
    public List<Vector2I> MovePath { get; set; } = new();

    /// <summary>The main action to take this turn (Attack, CastSpell, Dodge, etc.).</summary>
    public CombatAction PlannedAction { get; set; }

    /// <summary>Bonus action to take this turn, or null if none.</summary>
    public CombatAction PlannedBonusAction { get; set; }

    /// <summary>Human-readable explanation of why the AI chose this plan.</summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>How confident the AI is in this plan, from 0 (desperate) to 1 (optimal).</summary>
    public float ConfidenceScore { get; set; }

    /// <summary>The strategic priority that drove this decision.</summary>
    public CombatPriority Priority { get; set; } = CombatPriority.Default;
}
