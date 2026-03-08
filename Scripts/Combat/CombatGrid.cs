using System;
using System.Collections.Generic;
using Godot;

namespace ChroniclesUnbound.Combat;

/// <summary>
/// Core combat grid logic for D&D 5e tactical combat. Manages a 2D grid of cells,
/// occupant placement, A* pathfinding, line-of-sight, cover calculations, and
/// area-of-effect templates. Each cell represents a 5ft square.
/// </summary>
public partial class CombatGrid : Node2D
{
    /// <summary>Pixels per grid cell (one 5ft square).</summary>
    [Export] public int CellSize { get; set; } = 64;

    /// <summary>Grid width in cells.</summary>
    public int GridWidth => _gridWidth;

    /// <summary>Grid height in cells.</summary>
    public int GridHeight => _gridHeight;

    private Dictionary<Vector2I, GridCell> _grid = new();
    private Dictionary<string, Vector2I> _occupantPositions = new();
    private int _gridWidth;
    private int _gridHeight;

    private const int FeetPerCell = 5;

    // 8-directional neighbors: cardinal + diagonal
    private static readonly Vector2I[] Directions = new Vector2I[]
    {
        new(0, -1),  // N
        new(1, -1),  // NE
        new(1, 0),   // E
        new(1, 1),   // SE
        new(0, 1),   // S
        new(-1, 1),  // SW
        new(-1, 0),  // W
        new(-1, -1), // NW
    };

    [Signal]
    public delegate void GridInitializedEventHandler(int width, int height);

    [Signal]
    public delegate void OccupantPlacedEventHandler(string id, Vector2I position);

    [Signal]
    public delegate void OccupantMovedEventHandler(string id, Vector2I from, Vector2I to);

    [Signal]
    public delegate void OccupantRemovedEventHandler(string id, Vector2I position);

    [Signal]
    public delegate void TileChangedEventHandler(Vector2I position, int tileType);

    // ========================================================================
    // Grid Management
    // ========================================================================

    /// <summary>
    /// Creates a new grid of the given dimensions, filled with Floor tiles.
    /// </summary>
    public void InitializeGrid(int width, int height)
    {
        _gridWidth = width;
        _gridHeight = height;
        _grid.Clear();
        _occupantPositions.Clear();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var pos = new Vector2I(x, y);
                _grid[pos] = new GridCell(pos, TileType.Floor);
            }
        }

        EmitSignal(SignalName.GridInitialized, width, height);
    }

    /// <summary>
    /// Sets the tile type at the given position and re-applies default properties.
    /// </summary>
    public void SetTile(Vector2I pos, TileType type)
    {
        if (!IsValidPosition(pos))
            return;

        var cell = _grid[pos];
        cell.ApplyTileDefaults(type);
        EmitSignal(SignalName.TileChanged, pos, (int)type);
    }

    /// <summary>
    /// Sets a tile as a hazard with specified damage and damage type.
    /// </summary>
    public void SetHazard(Vector2I pos, int damage, string damageType)
    {
        if (!IsValidPosition(pos))
            return;

        var cell = _grid[pos];
        cell.ApplyTileDefaults(TileType.Hazard);
        cell.HazardDamage = damage;
        cell.HazardDamageType = damageType;
        EmitSignal(SignalName.TileChanged, pos, (int)TileType.Hazard);
    }

    /// <summary>
    /// Returns the cell at the given position, or null if out of bounds.
    /// </summary>
    public GridCell GetCell(Vector2I pos)
    {
        return _grid.TryGetValue(pos, out var cell) ? cell : null;
    }

    /// <summary>
    /// Returns true if the position is within grid bounds.
    /// </summary>
    public bool IsValidPosition(Vector2I pos)
    {
        return pos.X >= 0 && pos.X < _gridWidth && pos.Y >= 0 && pos.Y < _gridHeight;
    }

    /// <summary>
    /// Converts a grid position to world pixel coordinates (top-left corner of cell).
    /// </summary>
    public Vector2 GridToWorld(Vector2I gridPos)
    {
        return new Vector2(gridPos.X * CellSize, gridPos.Y * CellSize);
    }

    /// <summary>
    /// Converts a grid position to the center of the cell in world coordinates.
    /// </summary>
    public Vector2 GridToWorldCenter(Vector2I gridPos)
    {
        float half = CellSize * 0.5f;
        return new Vector2(gridPos.X * CellSize + half, gridPos.Y * CellSize + half);
    }

    /// <summary>
    /// Converts a world pixel position to the grid cell it falls within.
    /// </summary>
    public Vector2I WorldToGrid(Vector2 worldPos)
    {
        return new Vector2I(
            Mathf.FloorToInt(worldPos.X / CellSize),
            Mathf.FloorToInt(worldPos.Y / CellSize)
        );
    }

    // ========================================================================
    // Movement
    // ========================================================================

    /// <summary>
    /// Returns all cells reachable from the given position within the speed budget (in feet).
    /// Uses Dijkstra flood fill respecting movement costs, walls, and occupied cells.
    /// </summary>
    public List<Vector2I> GetValidMovePositions(Vector2I from, int speedInFeet)
    {
        int speedInCells = speedInFeet / FeetPerCell;
        var result = new List<Vector2I>();
        var costSoFar = new Dictionary<Vector2I, int> { [from] = 0 };
        var frontier = new PriorityQueue<Vector2I, int>();
        frontier.Enqueue(from, 0);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            int currentCost = costSoFar[current];

            foreach (var dir in Directions)
            {
                var next = current + dir;

                if (!IsValidPosition(next))
                    continue;

                var cell = _grid[next];

                if (cell.BlocksMovement)
                    continue;

                // Can't move through occupied cells (but can stop adjacent)
                // Exception: you CAN move through an ally's space in D&D 5e,
                // but for simplicity we block all occupied cells as destinations
                if (cell.IsOccupied && next != from)
                    continue;

                // Diagonal movement costs the same as cardinal in our D&D simplification
                int moveCost = cell.MovementCost;

                // Diagonal through two blocking corners is not allowed
                if (dir.X != 0 && dir.Y != 0)
                {
                    var adjA = _grid.GetValueOrDefault(current + new Vector2I(dir.X, 0));
                    var adjB = _grid.GetValueOrDefault(current + new Vector2I(0, dir.Y));
                    if ((adjA != null && adjA.BlocksMovement) && (adjB != null && adjB.BlocksMovement))
                        continue;
                }

                int newCost = currentCost + moveCost;

                if (newCost > speedInCells)
                    continue;

                if (!costSoFar.ContainsKey(next) || newCost < costSoFar[next])
                {
                    costSoFar[next] = newCost;
                    frontier.Enqueue(next, newCost);
                }
            }
        }

        // Collect all reachable cells except the origin
        foreach (var kvp in costSoFar)
        {
            if (kvp.Key != from)
                result.Add(kvp.Key);
        }

        return result;
    }

    /// <summary>
    /// Finds the shortest path from start to goal using A* pathfinding.
    /// Returns an empty list if no path exists.
    /// </summary>
    public List<Vector2I> FindPath(Vector2I from, Vector2I to)
    {
        return AStarPath(from, to);
    }

    /// <summary>
    /// Returns the total movement cost in feet to travel from one cell to another
    /// along the optimal path. Returns -1 if no path exists.
    /// </summary>
    public int GetMovementCost(Vector2I from, Vector2I to)
    {
        var path = AStarPath(from, to);
        if (path.Count == 0)
            return -1;

        int totalCost = 0;
        for (int i = 1; i < path.Count; i++)
        {
            var cell = _grid[path[i]];
            totalCost += cell.MovementCost;
        }

        return totalCost * FeetPerCell;
    }

    /// <summary>
    /// Returns true if the target position can be moved into (not a wall and not occupied).
    /// </summary>
    public bool CanMoveTo(Vector2I pos)
    {
        if (!IsValidPosition(pos))
            return false;

        var cell = _grid[pos];
        return !cell.BlocksMovement && !cell.IsOccupied;
    }

    // ========================================================================
    // Combat Queries
    // ========================================================================

    /// <summary>
    /// Returns the distance between two cells in feet. Uses Chebyshev distance
    /// (diagonal = same cost as cardinal), which is the common D&D 5e simplification.
    /// </summary>
    public float GetDistance(Vector2I a, Vector2I b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        return Math.Max(dx, dy) * FeetPerCell;
    }

    /// <summary>
    /// Checks line of sight between two cells using Bresenham's line algorithm.
    /// Returns true if no cell along the line blocks line of sight.
    /// </summary>
    public bool HasLineOfSight(Vector2I from, Vector2I to)
    {
        var line = GetBresenhamLine(from, to);

        // Check every cell along the line except start and end
        for (int i = 1; i < line.Count - 1; i++)
        {
            var pos = line[i];
            if (!IsValidPosition(pos))
                return false;

            if (_grid[pos].BlocksLineOfSight)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Determines the cover type between an attacker and a target. Traces a line
    /// and checks for intervening obstacles. D&D 5e uses the best cover from any
    /// corner of the attacker's square — we simplify by checking center-to-center.
    /// </summary>
    public CoverType GetCover(Vector2I attacker, Vector2I target)
    {
        var line = GetBresenhamLine(attacker, target);

        int wallCount = 0;
        int obstacleCount = 0;

        for (int i = 1; i < line.Count - 1; i++)
        {
            var pos = line[i];
            if (!IsValidPosition(pos))
            {
                wallCount++;
                continue;
            }

            var cell = _grid[pos];

            if (cell.Type == TileType.Wall)
            {
                wallCount++;
            }
            else if (cell.IsOccupied || cell.Type == TileType.Interactable)
            {
                obstacleCount++;
            }
        }

        if (wallCount >= 2)
            return CoverType.Full;
        if (wallCount >= 1)
            return CoverType.ThreeQuarters;
        if (obstacleCount >= 1)
            return CoverType.Half;

        return CoverType.None;
    }

    /// <summary>
    /// Returns the 8 cells surrounding the given position (only valid in-bounds cells).
    /// </summary>
    public List<Vector2I> GetAdjacentPositions(Vector2I pos)
    {
        var result = new List<Vector2I>(8);

        foreach (var dir in Directions)
        {
            var adj = pos + dir;
            if (IsValidPosition(adj))
                result.Add(adj);
        }

        return result;
    }

    /// <summary>
    /// Returns all grid positions within the given range (in feet) from center,
    /// using Chebyshev distance.
    /// </summary>
    public List<Vector2I> GetPositionsInRange(Vector2I center, int rangeFeet)
    {
        int rangeInCells = rangeFeet / FeetPerCell;
        var result = new List<Vector2I>();

        for (int x = center.X - rangeInCells; x <= center.X + rangeInCells; x++)
        {
            for (int y = center.Y - rangeInCells; y <= center.Y + rangeInCells; y++)
            {
                var pos = new Vector2I(x, y);
                if (IsValidPosition(pos) && pos != center)
                {
                    if (Math.Max(Math.Abs(x - center.X), Math.Abs(y - center.Y)) <= rangeInCells)
                        result.Add(pos);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the IDs of all occupants in the 8 cells adjacent to the given position.
    /// Used for opportunity attack checks.
    /// </summary>
    public List<string> GetAdjacentOccupants(Vector2I pos)
    {
        var result = new List<string>();

        foreach (var adjPos in GetAdjacentPositions(pos))
        {
            var cell = _grid[adjPos];
            if (cell.IsOccupied && cell.OccupantId != null)
                result.Add(cell.OccupantId);
        }

        return result;
    }

    // ========================================================================
    // AoE Templates
    // ========================================================================

    /// <summary>
    /// Returns all cells in a sphere (circle on 2D grid) of the given radius in feet.
    /// </summary>
    public List<Vector2I> GetSphereArea(Vector2I center, int radiusFeet)
    {
        int radiusCells = radiusFeet / FeetPerCell;
        var result = new List<Vector2I>();

        for (int x = center.X - radiusCells; x <= center.X + radiusCells; x++)
        {
            for (int y = center.Y - radiusCells; y <= center.Y + radiusCells; y++)
            {
                var pos = new Vector2I(x, y);
                if (!IsValidPosition(pos))
                    continue;

                // Use Euclidean distance for circular AoE
                float dist = MathF.Sqrt((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y));
                if (dist <= radiusCells + 0.5f) // +0.5 to include cells whose center is at the edge
                    result.Add(pos);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns all cells in a cone originating from origin, pointing toward direction,
    /// with the given length in feet. The cone's width at its end equals its length (90 degree cone).
    /// </summary>
    public List<Vector2I> GetConeArea(Vector2I origin, Vector2I direction, int lengthFeet)
    {
        int lengthCells = lengthFeet / FeetPerCell;
        var result = new List<Vector2I>();

        // Normalize direction vector to a float direction
        var dir = new Vector2(direction.X - origin.X, direction.Y - origin.Y).Normalized();

        if (dir.LengthSquared() < 0.001f)
            return result;

        for (int x = origin.X - lengthCells; x <= origin.X + lengthCells; x++)
        {
            for (int y = origin.Y - lengthCells; y <= origin.Y + lengthCells; y++)
            {
                var pos = new Vector2I(x, y);
                if (!IsValidPosition(pos) || pos == origin)
                    continue;

                var toCell = new Vector2(x - origin.X, y - origin.Y);
                float dist = toCell.Length();

                if (dist > lengthCells + 0.5f)
                    continue;

                // Check angle — cone is 90 degrees (45 degrees to each side)
                float dot = toCell.Normalized().Dot(dir);
                if (dot >= MathF.Cos(Mathf.DegToRad(45.0f)))
                    result.Add(pos);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns all cells in a line from origin in the given direction, with specified
    /// length and width in feet.
    /// </summary>
    public List<Vector2I> GetLineArea(Vector2I origin, Vector2I direction, int lengthFeet, int widthFeet)
    {
        int lengthCells = lengthFeet / FeetPerCell;
        int halfWidth = Math.Max(widthFeet / FeetPerCell / 2, 0);
        var result = new List<Vector2I>();

        var dir = new Vector2(direction.X - origin.X, direction.Y - origin.Y).Normalized();
        if (dir.LengthSquared() < 0.001f)
            return result;

        // Perpendicular direction for width
        var perp = new Vector2(-dir.Y, dir.X);

        for (int l = 1; l <= lengthCells; l++)
        {
            for (int w = -halfWidth; w <= halfWidth; w++)
            {
                var worldPos = new Vector2(origin.X, origin.Y) + dir * l + perp * w;
                var gridPos = new Vector2I(Mathf.RoundToInt(worldPos.X), Mathf.RoundToInt(worldPos.Y));

                if (IsValidPosition(gridPos) && !result.Contains(gridPos))
                    result.Add(gridPos);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns all cells in a cube (square on 2D grid) starting from the given corner,
    /// with the given size in feet.
    /// </summary>
    public List<Vector2I> GetCubeArea(Vector2I corner, int sizeFeet)
    {
        int sizeCells = sizeFeet / FeetPerCell;
        var result = new List<Vector2I>();

        for (int x = corner.X; x < corner.X + sizeCells; x++)
        {
            for (int y = corner.Y; y < corner.Y + sizeCells; y++)
            {
                var pos = new Vector2I(x, y);
                if (IsValidPosition(pos))
                    result.Add(pos);
            }
        }

        return result;
    }

    // ========================================================================
    // Occupant Management
    // ========================================================================

    /// <summary>
    /// Places an occupant on the grid at the given position. Fails silently if the
    /// cell is already occupied or invalid.
    /// </summary>
    public void PlaceOccupant(string id, Vector2I pos)
    {
        if (!IsValidPosition(pos))
            return;

        var cell = _grid[pos];
        if (cell.IsOccupied || cell.BlocksMovement)
            return;

        // Remove from old position if already on the grid
        if (_occupantPositions.ContainsKey(id))
            RemoveOccupant(id);

        cell.IsOccupied = true;
        cell.OccupantId = id;
        _occupantPositions[id] = pos;

        EmitSignal(SignalName.OccupantPlaced, id, pos);
    }

    /// <summary>
    /// Moves an occupant to a new position. The old cell is freed.
    /// </summary>
    public void MoveOccupant(string id, Vector2I newPos)
    {
        if (!_occupantPositions.TryGetValue(id, out var oldPos))
            return;

        if (!IsValidPosition(newPos))
            return;

        var newCell = _grid[newPos];
        if (newCell.IsOccupied || newCell.BlocksMovement)
            return;

        // Clear old cell
        var oldCell = _grid[oldPos];
        oldCell.IsOccupied = false;
        oldCell.OccupantId = null;

        // Set new cell
        newCell.IsOccupied = true;
        newCell.OccupantId = id;
        _occupantPositions[id] = newPos;

        EmitSignal(SignalName.OccupantMoved, id, oldPos, newPos);
    }

    /// <summary>
    /// Removes an occupant from the grid entirely.
    /// </summary>
    public void RemoveOccupant(string id)
    {
        if (!_occupantPositions.TryGetValue(id, out var pos))
            return;

        var cell = _grid[pos];
        cell.IsOccupied = false;
        cell.OccupantId = null;
        _occupantPositions.Remove(id);

        EmitSignal(SignalName.OccupantRemoved, id, pos);
    }

    /// <summary>
    /// Returns the grid position of the given occupant, or null if not found.
    /// </summary>
    public Vector2I? GetOccupantPosition(string id)
    {
        return _occupantPositions.TryGetValue(id, out var pos) ? pos : null;
    }

    /// <summary>
    /// Returns all occupant IDs currently on the grid.
    /// </summary>
    public IReadOnlyCollection<string> GetAllOccupants()
    {
        return _occupantPositions.Keys;
    }

    // ========================================================================
    // A* Pathfinding
    // ========================================================================

    /// <summary>
    /// A* pathfinding from start to goal. Returns the path including start and goal.
    /// Uses Manhattan distance as heuristic. Respects walls, difficult terrain costs,
    /// and occupied cells (blocked for movement).
    /// </summary>
    private List<Vector2I> AStarPath(Vector2I start, Vector2I goal)
    {
        if (!IsValidPosition(start) || !IsValidPosition(goal))
            return new List<Vector2I>();

        if (start == goal)
            return new List<Vector2I> { start };

        var goalCell = _grid[goal];
        if (goalCell.BlocksMovement || goalCell.IsOccupied)
            return new List<Vector2I>();

        var openSet = new PriorityQueue<Vector2I, int>();
        var cameFrom = new Dictionary<Vector2I, Vector2I>();
        var gScore = new Dictionary<Vector2I, int> { [start] = 0 };

        openSet.Enqueue(start, Heuristic(start, goal));

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();

            if (current == goal)
                return ReconstructPath(cameFrom, current);

            foreach (var dir in Directions)
            {
                var neighbor = current + dir;

                if (!IsValidPosition(neighbor))
                    continue;

                var cell = _grid[neighbor];

                if (cell.BlocksMovement)
                    continue;

                // Can't path through occupied cells unless it's the goal
                // (in D&D you can move through ally spaces but not end there;
                //  for pathfinding we allow the destination)
                if (cell.IsOccupied && neighbor != goal)
                    continue;

                // Block diagonal movement through two adjacent walls (corner cutting)
                if (dir.X != 0 && dir.Y != 0)
                {
                    var adjA = _grid.GetValueOrDefault(current + new Vector2I(dir.X, 0));
                    var adjB = _grid.GetValueOrDefault(current + new Vector2I(0, dir.Y));
                    if ((adjA != null && adjA.BlocksMovement) && (adjB != null && adjB.BlocksMovement))
                        continue;
                }

                int tentativeG = gScore[current] + cell.MovementCost;

                if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    int fScore = tentativeG + Heuristic(neighbor, goal);
                    openSet.Enqueue(neighbor, fScore);
                }
            }
        }

        // No path found
        return new List<Vector2I>();
    }

    /// <summary>
    /// Manhattan distance heuristic for A*.
    /// </summary>
    private static int Heuristic(Vector2I a, Vector2I b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    /// <summary>
    /// Traces back through the cameFrom map to build the full path.
    /// </summary>
    private static List<Vector2I> ReconstructPath(Dictionary<Vector2I, Vector2I> cameFrom, Vector2I current)
    {
        var path = new List<Vector2I> { current };

        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    // ========================================================================
    // Bresenham's Line (for LOS and cover)
    // ========================================================================

    /// <summary>
    /// Returns all grid cells along a line from start to end using Bresenham's algorithm.
    /// </summary>
    private static List<Vector2I> GetBresenhamLine(Vector2I start, Vector2I end)
    {
        var result = new List<Vector2I>();

        int x0 = start.X, y0 = start.Y;
        int x1 = end.X, y1 = end.Y;

        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            result.Add(new Vector2I(x0, y0));

            if (x0 == x1 && y0 == y1)
                break;

            int e2 = 2 * err;

            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }

        return result;
    }
}
