namespace ChroniclesUnbound.Companions;

/// <summary>
/// Controls how much agency the companion AI has during gameplay.
/// Player-configurable per companion via the party management UI.
/// </summary>
public enum CompanionAutonomy
{
    /// <summary>
    /// Player controls the companion directly. No AI decisions are made.
    /// </summary>
    FullControl,

    /// <summary>
    /// AI suggests actions; player approves, modifies, or rejects before execution.
    /// This is the default mode.
    /// </summary>
    Suggest,

    /// <summary>
    /// AI acts autonomously but the player can interrupt and override at any time.
    /// </summary>
    Trusted,

    /// <summary>
    /// AI acts without any player review. Useful for low-stakes encounters.
    /// </summary>
    FullAuto
}
