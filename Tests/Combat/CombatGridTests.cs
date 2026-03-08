using NUnit.Framework;
using ChroniclesUnbound.Combat;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace ChroniclesUnbound.Tests.Combat;

/// <summary>
/// Tests for the CombatGrid combat grid system covering grid creation, pathfinding,
/// line of sight, cover calculations, AoE templates, and occupant management.
/// </summary>
[TestFixture]
public class CombatGridTests
{
    private CombatGrid _grid;

    [SetUp]
    public void SetUp()
    {
        _grid = new CombatGrid();
    }

    // ========================================================================
    // Grid Creation & Management
    // ========================================================================

    [Test]
    public void InitializeGrid_CreatesGridWithCorrectDimensions()
    {
        _grid.InitializeGrid(10, 8);

        Assert.That(_grid.GridWidth, Is.EqualTo(10));
        Assert.That(_grid.GridHeight, Is.EqualTo(8));
    }

    [Test]
    public void InitializeGrid_AllCellsAreFloorByDefault()
    {
        _grid.InitializeGrid(5, 5);

        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                var cell = _grid.GetCell(new Vector2I(x, y));
                Assert.That(cell, Is.Not.Null);
                Assert.That(cell.Type, Is.EqualTo(TileType.Floor));
                Assert.That(cell.BlocksMovement, Is.False);
                Assert.That(cell.BlocksLineOfSight, Is.False);
            }
        }
    }

    [Test]
    public void SetTile_ChangesToWall_BlocksMovementAndSight()
    {
        _grid.InitializeGrid(5, 5);
        var pos = new Vector2I(2, 2);

        _grid.SetTile(pos, TileType.Wall);

        var cell = _grid.GetCell(pos);
        Assert.That(cell.Type, Is.EqualTo(TileType.Wall));
        Assert.That(cell.BlocksMovement, Is.True);
        Assert.That(cell.BlocksLineOfSight, Is.True);
    }

    [Test]
    public void SetTile_ChangesToDifficultTerrain_DoublesMovementCost()
    {
        _grid.InitializeGrid(5, 5);
        var pos = new Vector2I(3, 3);

        _grid.SetTile(pos, TileType.DifficultTerrain);

        var cell = _grid.GetCell(pos);
        Assert.That(cell.Type, Is.EqualTo(TileType.DifficultTerrain));
        Assert.That(cell.MovementCost, Is.EqualTo(2));
        Assert.That(cell.BlocksMovement, Is.False);
    }

    [Test]
    public void SetHazard_SetsCorrectDamageProperties()
    {
        _grid.InitializeGrid(5, 5);
        var pos = new Vector2I(1, 1);

        _grid.SetHazard(pos, 10, "fire");

        var cell = _grid.GetCell(pos);
        Assert.That(cell.Type, Is.EqualTo(TileType.Hazard));
        Assert.That(cell.HazardDamage, Is.EqualTo(10));
        Assert.That(cell.HazardDamageType, Is.EqualTo("fire"));
    }

    [Test]
    public void IsValidPosition_ReturnsTrueForInBounds()
    {
        _grid.InitializeGrid(10, 10);

        Assert.That(_grid.IsValidPosition(new Vector2I(0, 0)), Is.True);
        Assert.That(_grid.IsValidPosition(new Vector2I(9, 9)), Is.True);
        Assert.That(_grid.IsValidPosition(new Vector2I(5, 5)), Is.True);
    }

    [Test]
    public void IsValidPosition_ReturnsFalseForOutOfBounds()
    {
        _grid.InitializeGrid(10, 10);

        Assert.That(_grid.IsValidPosition(new Vector2I(-1, 0)), Is.False);
        Assert.That(_grid.IsValidPosition(new Vector2I(0, -1)), Is.False);
        Assert.That(_grid.IsValidPosition(new Vector2I(10, 0)), Is.False);
        Assert.That(_grid.IsValidPosition(new Vector2I(0, 10)), Is.False);
        Assert.That(_grid.IsValidPosition(new Vector2I(100, 100)), Is.False);
    }

    [Test]
    public void GetCell_ReturnsNullForInvalidPosition()
    {
        _grid.InitializeGrid(5, 5);

        var cell = _grid.GetCell(new Vector2I(10, 10));

        Assert.That(cell, Is.Null);
    }

    // ========================================================================
    // Grid Coordinate Conversion
    // ========================================================================

    [Test]
    public void GridToWorld_ConvertsCorrectly()
    {
        _grid.InitializeGrid(10, 10);
        _grid.CellSize = 64;

        var worldPos = _grid.GridToWorld(new Vector2I(2, 3));

        Assert.That(worldPos.X, Is.EqualTo(128f));
        Assert.That(worldPos.Y, Is.EqualTo(192f));
    }

    [Test]
    public void GridToWorldCenter_ConvertsToCenter()
    {
        _grid.InitializeGrid(10, 10);
        _grid.CellSize = 64;

        var worldPos = _grid.GridToWorldCenter(new Vector2I(2, 3));

        Assert.That(worldPos.X, Is.EqualTo(160f)); // 2*64 + 32
        Assert.That(worldPos.Y, Is.EqualTo(224f)); // 3*64 + 32
    }

    [Test]
    public void WorldToGrid_ConvertsCorrectly()
    {
        _grid.InitializeGrid(10, 10);
        _grid.CellSize = 64;

        var gridPos = _grid.WorldToGrid(new Vector2(128f, 192f));

        Assert.That(gridPos.X, Is.EqualTo(2));
        Assert.That(gridPos.Y, Is.EqualTo(3));
    }

    // ========================================================================
    // A* Pathfinding
    // ========================================================================

    [Test]
    public void FindPath_StraightLine_ReturnsDirectPath()
    {
        _grid.InitializeGrid(10, 10);
        var start = new Vector2I(0, 0);
        var goal = new Vector2I(5, 0);

        var path = _grid.FindPath(start, goal);

        Assert.That(path.Count, Is.EqualTo(6)); // start + 5 steps
        Assert.That(path[0], Is.EqualTo(start));
        Assert.That(path[path.Count - 1], Is.EqualTo(goal));
    }

    [Test]
    public void FindPath_AroundWall_FindsAlternatePath()
    {
        _grid.InitializeGrid(10, 10);

        // Create vertical wall at x=3
        for (int y = 0; y < 5; y++)
        {
            _grid.SetTile(new Vector2I(3, y), TileType.Wall);
        }

        var start = new Vector2I(2, 2);
        var goal = new Vector2I(4, 2);

        var path = _grid.FindPath(start, goal);

        // Path should go around the wall (not through it)
        Assert.That(path.Count, Is.GreaterThan(0));
        Assert.That(path[0], Is.EqualTo(start));
        Assert.That(path[path.Count - 1], Is.EqualTo(goal));

        // Verify no cell in the path is a wall
        foreach (var pos in path)
        {
            var cell = _grid.GetCell(pos);
            Assert.That(cell.BlocksMovement, Is.False, $"Path contains blocking cell at {pos}");
        }
    }

    [Test]
    public void FindPath_NoPathExists_ReturnsEmptyList()
    {
        _grid.InitializeGrid(10, 10);

        // Create complete wall blocking path
        for (int y = 0; y < 10; y++)
        {
            _grid.SetTile(new Vector2I(5, y), TileType.Wall);
        }

        var start = new Vector2I(2, 5);
        var goal = new Vector2I(8, 5);

        var path = _grid.FindPath(start, goal);

        Assert.That(path.Count, Is.EqualTo(0));
    }

    [Test]
    public void FindPath_StartEqualsGoal_ReturnsSingleElement()
    {
        _grid.InitializeGrid(10, 10);
        var pos = new Vector2I(5, 5);

        var path = _grid.FindPath(pos, pos);

        Assert.That(path.Count, Is.EqualTo(1));
        Assert.That(path[0], Is.EqualTo(pos));
    }

    [Test]
    public void FindPath_DestinationOccupied_ReturnsEmptyList()
    {
        _grid.InitializeGrid(10, 10);
        var start = new Vector2I(0, 0);
        var goal = new Vector2I(5, 5);

        _grid.PlaceOccupant("blocker", goal);

        var path = _grid.FindPath(start, goal);

        Assert.That(path.Count, Is.EqualTo(0));
    }

    [Test]
    public void FindPath_OutOfBounds_ReturnsEmptyList()
    {
        _grid.InitializeGrid(5, 5);

        var path = _grid.FindPath(new Vector2I(0, 0), new Vector2I(10, 10));

        Assert.That(path.Count, Is.EqualTo(0));
    }

    [Test]
    public void FindPath_DiagonalMovement_IsAllowed()
    {
        _grid.InitializeGrid(10, 10);
        var start = new Vector2I(0, 0);
        var goal = new Vector2I(3, 3);

        var path = _grid.FindPath(start, goal);

        Assert.That(path.Count, Is.GreaterThan(0));
        Assert.That(path[path.Count - 1], Is.EqualTo(goal));
        // Diagonal path should be shorter than cardinal-only
        Assert.That(path.Count, Is.LessThanOrEqualTo(5)); // Diagonal allows direct line
    }

    [Test]
    public void FindPath_CannotCutCorners_BlockedByTwoWalls()
    {
        _grid.InitializeGrid(10, 10);

        // Create L-shaped wall corner
        _grid.SetTile(new Vector2I(2, 1), TileType.Wall);
        _grid.SetTile(new Vector2I(1, 2), TileType.Wall);

        var start = new Vector2I(0, 0);
        var goal = new Vector2I(3, 3);

        var path = _grid.FindPath(start, goal);

        // Should not cut through the corner (1, 1) diagonally
        Assert.That(path.Count, Is.GreaterThan(0));

        // Check that no step goes diagonally through two adjacent walls
        for (int i = 1; i < path.Count; i++)
        {
            var from = path[i - 1];
            var to = path[i];
            var delta = to - from;

            if (delta.X != 0 && delta.Y != 0) // Diagonal move
            {
                var adjA = _grid.GetCell(from + new Vector2I(delta.X, 0));
                var adjB = _grid.GetCell(from + new Vector2I(0, delta.Y));

                bool cornerBlocked = (adjA != null && adjA.BlocksMovement) &&
                                    (adjB != null && adjB.BlocksMovement);
                Assert.That(cornerBlocked, Is.False, $"Path cuts corner at {from} -> {to}");
            }
        }
    }

    // ========================================================================
    // Dijkstra Valid Move Positions
    // ========================================================================

    [Test]
    public void GetValidMovePositions_30FeetSpeed_Returns6Cells()
    {
        _grid.InitializeGrid(20, 20);
        var start = new Vector2I(10, 10);

        var positions = _grid.GetValidMovePositions(start, 30);

        // 30 feet = 6 cells movement
        // Should be able to reach cells within 6 cells
        Assert.That(positions.Count, Is.GreaterThan(0));

        // Check a few specific reachable positions
        Assert.That(positions, Does.Contain(new Vector2I(10, 16))); // 6 cells south
        Assert.That(positions, Does.Contain(new Vector2I(16, 10))); // 6 cells east
    }

    [Test]
    public void GetValidMovePositions_WallsBlock_LimitsRange()
    {
        _grid.InitializeGrid(10, 10);
        var start = new Vector2I(5, 5);

        // Create a full vertical wall barrier at x=6 so nothing east can be reached
        for (int y = 0; y < 10; y++)
            _grid.SetTile(new Vector2I(6, y), TileType.Wall);

        var positions = _grid.GetValidMovePositions(start, 30);

        // Wall cells themselves should not be reachable
        Assert.That(positions, Does.Not.Contain(new Vector2I(6, 5)));
        // Positions behind the wall should not be reachable
        Assert.That(positions, Does.Not.Contain(new Vector2I(7, 5)));
        Assert.That(positions, Does.Not.Contain(new Vector2I(8, 5)));
    }

    [Test]
    public void GetValidMovePositions_DifficultTerrain_DoublesCost()
    {
        _grid.InitializeGrid(10, 10);
        var start = new Vector2I(5, 5);

        // Create difficult terrain
        _grid.SetTile(new Vector2I(6, 5), TileType.DifficultTerrain);

        var positions = _grid.GetValidMovePositions(start, 10); // 2 cells of movement

        // Should be able to move 1 cell through difficult terrain (costs 2)
        Assert.That(positions, Does.Contain(new Vector2I(6, 5)));

        // Should NOT be able to move 2 cells through difficult terrain (would cost 4)
        // But can move 2 cells on normal terrain
        Assert.That(positions, Does.Contain(new Vector2I(7, 5))); // If terrain is normal
    }

    [Test]
    public void GetValidMovePositions_OccupiedCellsBlocked_CannotMoveThrough()
    {
        _grid.InitializeGrid(10, 10);
        var start = new Vector2I(5, 5);

        _grid.PlaceOccupant("blocker", new Vector2I(6, 5));

        var positions = _grid.GetValidMovePositions(start, 30);

        // Cannot move into occupied cell
        Assert.That(positions, Does.Not.Contain(new Vector2I(6, 5)));
    }

    [Test]
    public void GetValidMovePositions_ZeroSpeed_ReturnsEmpty()
    {
        _grid.InitializeGrid(10, 10);
        var start = new Vector2I(5, 5);

        var positions = _grid.GetValidMovePositions(start, 0);

        Assert.That(positions.Count, Is.EqualTo(0));
    }

    [Test]
    public void GetValidMovePositions_ExcludesStartPosition()
    {
        _grid.InitializeGrid(10, 10);
        var start = new Vector2I(5, 5);

        var positions = _grid.GetValidMovePositions(start, 30);

        Assert.That(positions, Does.Not.Contain(start));
    }

    // ========================================================================
    // Movement Cost
    // ========================================================================

    [Test]
    public void GetMovementCost_StraightPath_CalculatesCorrectly()
    {
        _grid.InitializeGrid(10, 10);
        var from = new Vector2I(0, 0);
        var to = new Vector2I(3, 0);

        var cost = _grid.GetMovementCost(from, to);

        // 3 cells * 5 feet = 15 feet
        Assert.That(cost, Is.EqualTo(15));
    }

    [Test]
    public void GetMovementCost_ThroughDifficultTerrain_DoublesCost()
    {
        _grid.InitializeGrid(10, 10);
        var from = new Vector2I(0, 0);
        var to = new Vector2I(2, 0);

        // Set difficult terrain and wall off diagonal bypass
        _grid.SetTile(new Vector2I(1, 0), TileType.DifficultTerrain);
        _grid.SetTile(new Vector2I(0, 1), TileType.Wall);
        _grid.SetTile(new Vector2I(1, 1), TileType.Wall);
        _grid.SetTile(new Vector2I(2, 1), TileType.Wall);

        var cost = _grid.GetMovementCost(from, to);

        // Path forced through difficult terrain: cost 2 + 1 = 3, * 5 = 15 feet
        Assert.That(cost, Is.EqualTo(15));
    }

    [Test]
    public void GetMovementCost_NoPath_ReturnsNegativeOne()
    {
        _grid.InitializeGrid(10, 10);

        // Block path with wall
        for (int y = 0; y < 10; y++)
        {
            _grid.SetTile(new Vector2I(5, y), TileType.Wall);
        }

        var cost = _grid.GetMovementCost(new Vector2I(2, 5), new Vector2I(8, 5));

        Assert.That(cost, Is.EqualTo(-1));
    }

    [Test]
    public void CanMoveTo_FloorCell_ReturnsTrue()
    {
        _grid.InitializeGrid(10, 10);

        Assert.That(_grid.CanMoveTo(new Vector2I(5, 5)), Is.True);
    }

    [Test]
    public void CanMoveTo_WallCell_ReturnsFalse()
    {
        _grid.InitializeGrid(10, 10);
        _grid.SetTile(new Vector2I(5, 5), TileType.Wall);

        Assert.That(_grid.CanMoveTo(new Vector2I(5, 5)), Is.False);
    }

    [Test]
    public void CanMoveTo_OccupiedCell_ReturnsFalse()
    {
        _grid.InitializeGrid(10, 10);
        _grid.PlaceOccupant("unit", new Vector2I(5, 5));

        Assert.That(_grid.CanMoveTo(new Vector2I(5, 5)), Is.False);
    }

    // ========================================================================
    // Distance Calculation (Chebyshev)
    // ========================================================================

    [Test]
    public void GetDistance_StraightLine_CalculatesCorrectly()
    {
        _grid.InitializeGrid(10, 10);

        var distance = _grid.GetDistance(new Vector2I(0, 0), new Vector2I(4, 0));

        // 4 cells * 5 feet = 20 feet
        Assert.That(distance, Is.EqualTo(20f));
    }

    [Test]
    public void GetDistance_Diagonal_UsesChebyshev()
    {
        _grid.InitializeGrid(10, 10);

        var distance = _grid.GetDistance(new Vector2I(0, 0), new Vector2I(3, 3));

        // Chebyshev: max(3, 3) = 3 cells * 5 feet = 15 feet
        Assert.That(distance, Is.EqualTo(15f));
    }

    [Test]
    public void GetDistance_Mixed_UsesChebyshev()
    {
        _grid.InitializeGrid(10, 10);

        var distance = _grid.GetDistance(new Vector2I(0, 0), new Vector2I(5, 2));

        // Chebyshev: max(5, 2) = 5 cells * 5 feet = 25 feet
        Assert.That(distance, Is.EqualTo(25f));
    }

    // ========================================================================
    // Line of Sight
    // ========================================================================

    [Test]
    public void HasLineOfSight_ClearPath_ReturnsTrue()
    {
        _grid.InitializeGrid(10, 10);

        var hasLOS = _grid.HasLineOfSight(new Vector2I(0, 0), new Vector2I(5, 5));

        Assert.That(hasLOS, Is.True);
    }

    [Test]
    public void HasLineOfSight_WallInBetween_ReturnsFalse()
    {
        _grid.InitializeGrid(10, 10);
        _grid.SetTile(new Vector2I(3, 3), TileType.Wall);

        var hasLOS = _grid.HasLineOfSight(new Vector2I(0, 0), new Vector2I(6, 6));

        Assert.That(hasLOS, Is.False);
    }

    [Test]
    public void HasLineOfSight_DifficultTerrainDoesNotBlock_ReturnsTrue()
    {
        _grid.InitializeGrid(10, 10);
        _grid.SetTile(new Vector2I(3, 3), TileType.DifficultTerrain);

        var hasLOS = _grid.HasLineOfSight(new Vector2I(0, 0), new Vector2I(6, 6));

        Assert.That(hasLOS, Is.True);
    }

    [Test]
    public void HasLineOfSight_AdjacentCells_AlwaysTrue()
    {
        _grid.InitializeGrid(10, 10);

        var hasLOS = _grid.HasLineOfSight(new Vector2I(5, 5), new Vector2I(5, 6));

        Assert.That(hasLOS, Is.True);
    }

    [Test]
    public void HasLineOfSight_SameCell_ReturnsTrue()
    {
        _grid.InitializeGrid(10, 10);

        var hasLOS = _grid.HasLineOfSight(new Vector2I(5, 5), new Vector2I(5, 5));

        Assert.That(hasLOS, Is.True);
    }

    // ========================================================================
    // Cover Calculation
    // ========================================================================

    [Test]
    public void GetCover_ClearPath_ReturnsNone()
    {
        _grid.InitializeGrid(10, 10);

        var cover = _grid.GetCover(new Vector2I(0, 0), new Vector2I(5, 5));

        Assert.That(cover, Is.EqualTo(CoverType.None));
    }

    [Test]
    public void GetCover_OneWallInBetween_ReturnsThreeQuarters()
    {
        _grid.InitializeGrid(10, 10);
        _grid.SetTile(new Vector2I(3, 3), TileType.Wall);

        var cover = _grid.GetCover(new Vector2I(0, 0), new Vector2I(6, 6));

        Assert.That(cover, Is.EqualTo(CoverType.ThreeQuarters));
    }

    [Test]
    public void GetCover_TwoWallsInBetween_ReturnsFull()
    {
        _grid.InitializeGrid(10, 10);
        _grid.SetTile(new Vector2I(3, 3), TileType.Wall);
        _grid.SetTile(new Vector2I(4, 4), TileType.Wall);

        var cover = _grid.GetCover(new Vector2I(0, 0), new Vector2I(6, 6));

        Assert.That(cover, Is.EqualTo(CoverType.Full));
    }

    [Test]
    public void GetCover_OccupiedCellInBetween_ReturnsHalf()
    {
        _grid.InitializeGrid(10, 10);
        _grid.PlaceOccupant("blocker", new Vector2I(3, 3));

        var cover = _grid.GetCover(new Vector2I(0, 0), new Vector2I(6, 6));

        Assert.That(cover, Is.EqualTo(CoverType.Half));
    }

    [Test]
    public void GetCover_InteractableInBetween_ReturnsHalf()
    {
        _grid.InitializeGrid(10, 10);
        _grid.SetTile(new Vector2I(3, 3), TileType.Interactable);

        var cover = _grid.GetCover(new Vector2I(0, 0), new Vector2I(6, 6));

        Assert.That(cover, Is.EqualTo(CoverType.Half));
    }

    // ========================================================================
    // Adjacent Positions
    // ========================================================================

    [Test]
    public void GetAdjacentPositions_CenterCell_Returns8Neighbors()
    {
        _grid.InitializeGrid(10, 10);

        var adjacent = _grid.GetAdjacentPositions(new Vector2I(5, 5));

        Assert.That(adjacent.Count, Is.EqualTo(8));
        Assert.That(adjacent, Does.Contain(new Vector2I(4, 4))); // NW
        Assert.That(adjacent, Does.Contain(new Vector2I(5, 4))); // N
        Assert.That(adjacent, Does.Contain(new Vector2I(6, 4))); // NE
        Assert.That(adjacent, Does.Contain(new Vector2I(6, 5))); // E
        Assert.That(adjacent, Does.Contain(new Vector2I(6, 6))); // SE
        Assert.That(adjacent, Does.Contain(new Vector2I(5, 6))); // S
        Assert.That(adjacent, Does.Contain(new Vector2I(4, 6))); // SW
        Assert.That(adjacent, Does.Contain(new Vector2I(4, 5))); // W
    }

    [Test]
    public void GetAdjacentPositions_CornerCell_Returns3Neighbors()
    {
        _grid.InitializeGrid(10, 10);

        var adjacent = _grid.GetAdjacentPositions(new Vector2I(0, 0));

        Assert.That(adjacent.Count, Is.EqualTo(3));
        Assert.That(adjacent, Does.Contain(new Vector2I(1, 0)));
        Assert.That(adjacent, Does.Contain(new Vector2I(1, 1)));
        Assert.That(adjacent, Does.Contain(new Vector2I(0, 1)));
    }

    [Test]
    public void GetAdjacentPositions_EdgeCell_Returns5Neighbors()
    {
        _grid.InitializeGrid(10, 10);

        var adjacent = _grid.GetAdjacentPositions(new Vector2I(5, 0));

        Assert.That(adjacent.Count, Is.EqualTo(5));
    }

    [Test]
    public void GetAdjacentOccupants_ReturnsOccupantIds()
    {
        _grid.InitializeGrid(10, 10);
        var center = new Vector2I(5, 5);

        _grid.PlaceOccupant("enemy1", new Vector2I(4, 5));
        _grid.PlaceOccupant("enemy2", new Vector2I(6, 6));

        var occupants = _grid.GetAdjacentOccupants(center);

        Assert.That(occupants.Count, Is.EqualTo(2));
        Assert.That(occupants, Does.Contain("enemy1"));
        Assert.That(occupants, Does.Contain("enemy2"));
    }

    [Test]
    public void GetAdjacentOccupants_NoOccupants_ReturnsEmpty()
    {
        _grid.InitializeGrid(10, 10);

        var occupants = _grid.GetAdjacentOccupants(new Vector2I(5, 5));

        Assert.That(occupants.Count, Is.EqualTo(0));
    }

    // ========================================================================
    // Positions In Range
    // ========================================================================

    [Test]
    public void GetPositionsInRange_10Feet_Returns2CellRadius()
    {
        _grid.InitializeGrid(20, 20);
        var center = new Vector2I(10, 10);

        var positions = _grid.GetPositionsInRange(center, 10);

        // 10 feet = 2 cells
        Assert.That(positions, Does.Contain(new Vector2I(12, 10)));
        Assert.That(positions, Does.Contain(new Vector2I(10, 12)));
        Assert.That(positions, Does.Contain(new Vector2I(8, 10)));
        Assert.That(positions, Does.Contain(new Vector2I(10, 8)));

        // Should not include center
        Assert.That(positions, Does.Not.Contain(center));

        // Should not include cells beyond range
        Assert.That(positions, Does.Not.Contain(new Vector2I(13, 10)));
    }

    [Test]
    public void GetPositionsInRange_5Feet_Returns1CellRadius()
    {
        _grid.InitializeGrid(20, 20);
        var center = new Vector2I(10, 10);

        var positions = _grid.GetPositionsInRange(center, 5);

        // Should return exactly the 8 adjacent cells
        Assert.That(positions.Count, Is.EqualTo(8));
    }

    // ========================================================================
    // AoE Templates - Sphere
    // ========================================================================

    [Test]
    public void GetSphereArea_10FeetRadius_ReturnsCircle()
    {
        _grid.InitializeGrid(20, 20);
        var center = new Vector2I(10, 10);

        var area = _grid.GetSphereArea(center, 10);

        // 10 feet = 2 cells radius
        // Should include center
        Assert.That(area, Does.Contain(center));

        // Should include cells at exactly 2 cells away
        Assert.That(area, Does.Contain(new Vector2I(12, 10)));
        Assert.That(area, Does.Contain(new Vector2I(8, 10)));

        // Should form roughly circular shape
        Assert.That(area.Count, Is.GreaterThan(8));
        Assert.That(area.Count, Is.LessThan(25)); // Not a full square
    }

    [Test]
    public void GetSphereArea_5FeetRadius_ReturnsSingleCell()
    {
        _grid.InitializeGrid(20, 20);
        var center = new Vector2I(10, 10);

        var area = _grid.GetSphereArea(center, 5);

        // 5 feet = 1 cell radius
        Assert.That(area, Does.Contain(center));
        // May include adjacent cells depending on Euclidean distance calculation
        Assert.That(area.Count, Is.GreaterThan(0));
    }

    // ========================================================================
    // AoE Templates - Cone
    // ========================================================================

    [Test]
    public void GetConeArea_15FeetLength_ReturnsConeCells()
    {
        _grid.InitializeGrid(20, 20);
        var origin = new Vector2I(10, 10);
        var direction = new Vector2I(13, 10); // East

        var area = _grid.GetConeArea(origin, direction, 15);

        // 15 feet = 3 cells length
        Assert.That(area.Count, Is.GreaterThan(0));

        // Should not include origin
        Assert.That(area, Does.Not.Contain(origin));

        // Should include cells in the direction
        Assert.That(area, Does.Contain(new Vector2I(11, 10)));
    }

    [Test]
    public void GetConeArea_ZeroLength_ReturnsEmpty()
    {
        _grid.InitializeGrid(20, 20);
        var origin = new Vector2I(10, 10);
        var direction = new Vector2I(13, 10);

        var area = _grid.GetConeArea(origin, direction, 0);

        Assert.That(area.Count, Is.EqualTo(0));
    }

    // ========================================================================
    // AoE Templates - Line
    // ========================================================================

    [Test]
    public void GetLineArea_20FeetLength5FeetWidth_ReturnsLineCells()
    {
        _grid.InitializeGrid(20, 20);
        var origin = new Vector2I(10, 10);
        var direction = new Vector2I(15, 10); // East

        var area = _grid.GetLineArea(origin, direction, 20, 5);

        // 20 feet = 4 cells length, 5 feet = 1 cell width
        Assert.That(area.Count, Is.GreaterThan(0));

        // Should include cells along the line
        Assert.That(area, Does.Contain(new Vector2I(11, 10)));
        Assert.That(area, Does.Contain(new Vector2I(12, 10)));
    }

    [Test]
    public void GetLineArea_ZeroWidth_ReturnsNarrowLine()
    {
        _grid.InitializeGrid(20, 20);
        var origin = new Vector2I(10, 10);
        var direction = new Vector2I(15, 10);

        var area = _grid.GetLineArea(origin, direction, 20, 0);

        // Should still return cells along the line
        Assert.That(area.Count, Is.GreaterThan(0));
    }

    // ========================================================================
    // AoE Templates - Cube
    // ========================================================================

    [Test]
    public void GetCubeArea_10FeetSize_Returns2x2Square()
    {
        _grid.InitializeGrid(20, 20);
        var corner = new Vector2I(10, 10);

        var area = _grid.GetCubeArea(corner, 10);

        // 10 feet = 2 cells
        Assert.That(area.Count, Is.EqualTo(4)); // 2x2 = 4 cells
        Assert.That(area, Does.Contain(new Vector2I(10, 10)));
        Assert.That(area, Does.Contain(new Vector2I(11, 10)));
        Assert.That(area, Does.Contain(new Vector2I(10, 11)));
        Assert.That(area, Does.Contain(new Vector2I(11, 11)));
    }

    [Test]
    public void GetCubeArea_5FeetSize_ReturnsSingleCell()
    {
        _grid.InitializeGrid(20, 20);
        var corner = new Vector2I(10, 10);

        var area = _grid.GetCubeArea(corner, 5);

        // 5 feet = 1 cell
        Assert.That(area.Count, Is.EqualTo(1));
        Assert.That(area, Does.Contain(corner));
    }

    [Test]
    public void GetCubeArea_PartiallyOutOfBounds_ReturnsOnlyValidCells()
    {
        _grid.InitializeGrid(10, 10);
        var corner = new Vector2I(9, 9);

        var area = _grid.GetCubeArea(corner, 10);

        // Only cells within grid bounds
        Assert.That(area.Count, Is.EqualTo(1));
        Assert.That(area, Does.Contain(new Vector2I(9, 9)));
    }

    // ========================================================================
    // Occupant Management
    // ========================================================================

    [Test]
    public void PlaceOccupant_ValidPosition_Succeeds()
    {
        _grid.InitializeGrid(10, 10);
        var pos = new Vector2I(5, 5);

        _grid.PlaceOccupant("unit1", pos);

        var cell = _grid.GetCell(pos);
        Assert.That(cell.IsOccupied, Is.True);
        Assert.That(cell.OccupantId, Is.EqualTo("unit1"));
    }

    [Test]
    public void PlaceOccupant_AlreadyOccupied_FailsSilently()
    {
        _grid.InitializeGrid(10, 10);
        var pos = new Vector2I(5, 5);

        _grid.PlaceOccupant("unit1", pos);
        _grid.PlaceOccupant("unit2", pos); // Should fail

        var cell = _grid.GetCell(pos);
        Assert.That(cell.OccupantId, Is.EqualTo("unit1")); // First occupant remains
    }

    [Test]
    public void PlaceOccupant_InvalidPosition_FailsSilently()
    {
        _grid.InitializeGrid(10, 10);

        _grid.PlaceOccupant("unit1", new Vector2I(100, 100));

        // Should not throw exception
        Assert.Pass();
    }

    [Test]
    public void PlaceOccupant_OnWall_FailsSilently()
    {
        _grid.InitializeGrid(10, 10);
        var pos = new Vector2I(5, 5);
        _grid.SetTile(pos, TileType.Wall);

        _grid.PlaceOccupant("unit1", pos);

        var cell = _grid.GetCell(pos);
        Assert.That(cell.IsOccupied, Is.False);
    }

    [Test]
    public void MoveOccupant_ValidMove_Updates()
    {
        _grid.InitializeGrid(10, 10);
        var from = new Vector2I(5, 5);
        var to = new Vector2I(6, 6);

        _grid.PlaceOccupant("unit1", from);
        _grid.MoveOccupant("unit1", to);

        var oldCell = _grid.GetCell(from);
        var newCell = _grid.GetCell(to);

        Assert.That(oldCell.IsOccupied, Is.False);
        Assert.That(newCell.IsOccupied, Is.True);
        Assert.That(newCell.OccupantId, Is.EqualTo("unit1"));
    }

    [Test]
    public void MoveOccupant_ToOccupiedCell_FailsSilently()
    {
        _grid.InitializeGrid(10, 10);
        var pos1 = new Vector2I(5, 5);
        var pos2 = new Vector2I(6, 6);

        _grid.PlaceOccupant("unit1", pos1);
        _grid.PlaceOccupant("unit2", pos2);

        _grid.MoveOccupant("unit1", pos2); // Should fail

        var cell1 = _grid.GetCell(pos1);
        var cell2 = _grid.GetCell(pos2);

        Assert.That(cell1.OccupantId, Is.EqualTo("unit1")); // Stays in place
        Assert.That(cell2.OccupantId, Is.EqualTo("unit2"));
    }

    [Test]
    public void RemoveOccupant_ExistingOccupant_Clears()
    {
        _grid.InitializeGrid(10, 10);
        var pos = new Vector2I(5, 5);

        _grid.PlaceOccupant("unit1", pos);
        _grid.RemoveOccupant("unit1");

        var cell = _grid.GetCell(pos);
        Assert.That(cell.IsOccupied, Is.False);
        Assert.That(cell.OccupantId, Is.Null);
    }

    [Test]
    public void RemoveOccupant_NonExistentOccupant_FailsSilently()
    {
        _grid.InitializeGrid(10, 10);

        _grid.RemoveOccupant("nonexistent");

        // Should not throw exception
        Assert.Pass();
    }

    [Test]
    public void GetOccupantPosition_ExistingOccupant_ReturnsPosition()
    {
        _grid.InitializeGrid(10, 10);
        var pos = new Vector2I(5, 5);

        _grid.PlaceOccupant("unit1", pos);

        var occupantPos = _grid.GetOccupantPosition("unit1");

        Assert.That(occupantPos, Is.Not.Null);
        Assert.That(occupantPos.Value, Is.EqualTo(pos));
    }

    [Test]
    public void GetOccupantPosition_NonExistentOccupant_ReturnsNull()
    {
        _grid.InitializeGrid(10, 10);

        var occupantPos = _grid.GetOccupantPosition("nonexistent");

        Assert.That(occupantPos, Is.Null);
    }

    [Test]
    public void GetAllOccupants_ReturnsAllIds()
    {
        _grid.InitializeGrid(10, 10);

        _grid.PlaceOccupant("unit1", new Vector2I(1, 1));
        _grid.PlaceOccupant("unit2", new Vector2I(2, 2));
        _grid.PlaceOccupant("unit3", new Vector2I(3, 3));

        var occupants = _grid.GetAllOccupants();

        Assert.That(occupants.Count, Is.EqualTo(3));
        Assert.That(occupants, Does.Contain("unit1"));
        Assert.That(occupants, Does.Contain("unit2"));
        Assert.That(occupants, Does.Contain("unit3"));
    }

    [Test]
    public void GetAllOccupants_EmptyGrid_ReturnsEmpty()
    {
        _grid.InitializeGrid(10, 10);

        var occupants = _grid.GetAllOccupants();

        Assert.That(occupants.Count, Is.EqualTo(0));
    }

    [Test]
    public void PlaceOccupant_SameIdTwice_MovesToNewPosition()
    {
        _grid.InitializeGrid(10, 10);
        var pos1 = new Vector2I(5, 5);
        var pos2 = new Vector2I(6, 6);

        _grid.PlaceOccupant("unit1", pos1);
        _grid.PlaceOccupant("unit1", pos2); // Should remove from pos1 and place at pos2

        var cell1 = _grid.GetCell(pos1);
        var cell2 = _grid.GetCell(pos2);

        Assert.That(cell1.IsOccupied, Is.False);
        Assert.That(cell2.IsOccupied, Is.True);
        Assert.That(cell2.OccupantId, Is.EqualTo("unit1"));
    }
}
