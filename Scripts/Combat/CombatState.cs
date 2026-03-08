namespace ChroniclesUnbound.Combat;

/// <summary>
/// Tracks the overall phase of a combat encounter.
/// </summary>
public enum CombatState
{
    /// <summary>No combat is active.</summary>
    NotInCombat,

    /// <summary>Combat has started; initiative is being rolled and sorted.</summary>
    RollingInitiative,

    /// <summary>Combat is actively in progress with turns being taken.</summary>
    InProgress,

    /// <summary>Combat has ended (all enemies defeated or all allies down).</summary>
    CombatEnded
}
