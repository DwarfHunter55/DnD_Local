namespace ChroniclesUnbound.Combat;

/// <summary>
/// Types of terrain tiles on the combat grid. Each type has different
/// movement, line-of-sight, and interaction properties.
/// </summary>
public enum TileType
{
    Floor,            // Normal movement
    Wall,             // Blocks movement and line of sight
    DifficultTerrain, // Costs 2x movement
    Water,            // Shallow = difficult terrain
    DeepWater,        // Requires swimming
    Hazard,           // Damage on entry (fire, acid, spikes)
    Pit,              // Fall damage, requires climb to exit
    Door,             // Can be opened/closed
    Interactable      // Barrel, lever, etc.
}
