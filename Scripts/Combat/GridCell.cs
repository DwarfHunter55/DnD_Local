using Godot;

namespace ChroniclesUnbound.Combat;

/// <summary>
/// Data for a single cell on the combat grid. Each cell represents a 5ft square.
/// </summary>
public class GridCell
{
    public Vector2I Position { get; set; }
    public TileType Type { get; set; }
    public bool IsOccupied { get; set; }
    public string OccupantId { get; set; }
    public int HazardDamage { get; set; }
    public string HazardDamageType { get; set; }
    public bool BlocksLineOfSight { get; set; }
    public bool BlocksMovement { get; set; }
    public int MovementCost { get; set; } // 1 = normal, 2 = difficult terrain

    public GridCell(Vector2I position, TileType type = TileType.Floor)
    {
        Position = position;
        Type = type;
        IsOccupied = false;
        OccupantId = null;
        HazardDamage = 0;
        HazardDamageType = null;
        ApplyTileDefaults(type);
    }

    /// <summary>
    /// Sets BlocksLineOfSight, BlocksMovement, and MovementCost based on tile type.
    /// </summary>
    public void ApplyTileDefaults(TileType type)
    {
        Type = type;

        switch (type)
        {
            case TileType.Floor:
            case TileType.Interactable:
                BlocksLineOfSight = false;
                BlocksMovement = false;
                MovementCost = 1;
                break;

            case TileType.Wall:
                BlocksLineOfSight = true;
                BlocksMovement = true;
                MovementCost = 1;
                break;

            case TileType.DifficultTerrain:
            case TileType.Water:
                BlocksLineOfSight = false;
                BlocksMovement = false;
                MovementCost = 2;
                break;

            case TileType.DeepWater:
                BlocksLineOfSight = false;
                BlocksMovement = false;
                MovementCost = 2; // Requires swimming but still traversable
                break;

            case TileType.Hazard:
                BlocksLineOfSight = false;
                BlocksMovement = false;
                MovementCost = 1;
                break;

            case TileType.Pit:
                BlocksLineOfSight = false;
                BlocksMovement = false; // Can enter, but takes fall damage
                MovementCost = 2;
                break;

            case TileType.Door:
                // Doors start closed — blocks LOS and movement until opened
                BlocksLineOfSight = true;
                BlocksMovement = true;
                MovementCost = 1;
                break;
        }
    }
}
