using System.Collections.Generic;
using Godot;

namespace ChroniclesUnbound.Combat;

/// <summary>
/// Renders the combat grid visually using Godot's _Draw() API. Draws grid lines,
/// tile colors, occupant tokens, and overlay highlights for movement/attack ranges,
/// path previews, and AoE previews.
/// </summary>
public partial class CombatGridRenderer : Node2D
{
    /// <summary>
    /// Path to the CombatGrid node. Set in the editor or scene file.
    /// </summary>
    [Export] public NodePath GridPath { get; set; }

    /// <summary>
    /// Resolved reference to the CombatGrid node.
    /// </summary>
    public CombatGrid Grid { get; private set; }

    // Tile colors
    private static readonly Color FloorColor = new(0.25f, 0.2f, 0.15f);       // Dark brown
    private static readonly Color WallColor = new(0.15f, 0.15f, 0.15f);       // Dark gray
    private static readonly Color DifficultColor = new(0.35f, 0.28f, 0.15f);  // Brownish
    private static readonly Color WaterColor = new(0.2f, 0.3f, 0.5f);         // Blue-gray
    private static readonly Color DeepWaterColor = new(0.1f, 0.15f, 0.4f);    // Deep blue
    private static readonly Color HazardColor = new(0.5f, 0.15f, 0.1f);       // Red-brown
    private static readonly Color PitColor = new(0.08f, 0.08f, 0.08f);        // Near black
    private static readonly Color DoorColor = new(0.45f, 0.3f, 0.15f);        // Wood brown
    private static readonly Color InteractableColor = new(0.35f, 0.35f, 0.2f);// Olive

    private static readonly Color GridLineColor = new(0.3f, 0.3f, 0.3f, 0.5f);
    private static readonly Color GridLineBorderColor = new(0.4f, 0.4f, 0.4f, 0.8f);

    // Overlay colors
    private static readonly Color MoveHighlightColor = new(0.1f, 0.7f, 0.2f, 0.25f);
    private static readonly Color AttackHighlightColor = new(0.8f, 0.1f, 0.1f, 0.2f);
    private static readonly Color PathPreviewColor = new(0.9f, 0.9f, 0.2f, 0.6f);

    // Occupant default colors
    private static readonly Color PlayerColor = new(0.2f, 0.4f, 0.9f);   // Blue
    private static readonly Color AllyColor = new(0.2f, 0.8f, 0.3f);     // Green
    private static readonly Color EnemyColor = new(0.9f, 0.2f, 0.15f);   // Red
    private static readonly Color NeutralColor = new(0.7f, 0.7f, 0.2f);  // Yellow

    // Highlight state
    private readonly List<Vector2I> _moveHighlightCells = new();
    private readonly List<Vector2I> _attackHighlightCells = new();
    private readonly List<Vector2I> _pathPreviewCells = new();
    private List<Vector2I> _aoePreviewCells = new();
    private Color _aoePreviewColor = new(0.8f, 0.4f, 0.0f, 0.3f);

    // Per-occupant color overrides (id -> color)
    private readonly Dictionary<string, Color> _occupantColors = new();

    // HP display data (id -> current/max)
    private readonly Dictionary<string, (int Current, int Max)> _occupantHp = new();

    // Condition icons (id -> list of condition names)
    private readonly Dictionary<string, List<string>> _occupantConditions = new();

    // Hatching pattern parameters
    private const int HatchSpacing = 8;

    // ========================================================================
    // Lifecycle
    // ========================================================================

    public override void _Ready()
    {
        if (GridPath != null)
        {
            Grid = GetNode<CombatGrid>(GridPath);
        }

        if (Grid != null)
        {
            Grid.GridInitialized += OnGridChanged;
            Grid.TileChanged += OnTileChanged;
            Grid.OccupantPlaced += OnOccupantChanged;
            Grid.OccupantMoved += OnOccupantMovedChanged;
            Grid.OccupantRemoved += OnOccupantChanged;
        }
    }

    private void OnGridChanged(int _w, int _h) => QueueRedraw();
    private void OnTileChanged(Vector2I _pos, int _type) => QueueRedraw();
    private void OnOccupantChanged(string _id, Vector2I _pos) => QueueRedraw();
    private void OnOccupantMovedChanged(string _id, Vector2I _from, Vector2I _to) => QueueRedraw();

    // ========================================================================
    // Public API
    // ========================================================================

    /// <summary>
    /// Highlights cells as valid movement destinations (semi-transparent green).
    /// </summary>
    public void HighlightMovementRange(List<Vector2I> cells)
    {
        _moveHighlightCells.Clear();
        if (cells != null)
            _moveHighlightCells.AddRange(cells);
        QueueRedraw();
    }

    /// <summary>
    /// Highlights cells as valid attack targets (semi-transparent red).
    /// </summary>
    public void HighlightAttackRange(List<Vector2I> cells)
    {
        _attackHighlightCells.Clear();
        if (cells != null)
            _attackHighlightCells.AddRange(cells);
        QueueRedraw();
    }

    /// <summary>
    /// Shows a path preview as highlighted cells with directional dots.
    /// </summary>
    public void ShowPathPreview(List<Vector2I> path)
    {
        _pathPreviewCells.Clear();
        if (path != null)
            _pathPreviewCells.AddRange(path);
        QueueRedraw();
    }

    /// <summary>
    /// Shows an area-of-effect preview overlay.
    /// </summary>
    public void ShowAoEPreview(List<Vector2I> cells, Color color)
    {
        _aoePreviewCells = cells ?? new List<Vector2I>();
        _aoePreviewColor = color;
        QueueRedraw();
    }

    /// <summary>
    /// Clears all highlight overlays.
    /// </summary>
    public void ClearHighlights()
    {
        _moveHighlightCells.Clear();
        _attackHighlightCells.Clear();
        _pathPreviewCells.Clear();
        _aoePreviewCells.Clear();
        QueueRedraw();
    }

    /// <summary>
    /// Sets the display color for an occupant token.
    /// </summary>
    public void SetOccupantColor(string id, Color color)
    {
        _occupantColors[id] = color;
        QueueRedraw();
    }

    /// <summary>
    /// Sets the HP display for an occupant.
    /// </summary>
    public void SetOccupantHp(string id, int current, int max)
    {
        _occupantHp[id] = (current, max);
        QueueRedraw();
    }

    /// <summary>
    /// Sets the condition icons for an occupant.
    /// </summary>
    public void SetOccupantConditions(string id, List<string> conditions)
    {
        _occupantConditions[id] = conditions ?? new List<string>();
        QueueRedraw();
    }

    /// <summary>
    /// Removes all display data for an occupant (color, HP, conditions).
    /// </summary>
    public void RemoveOccupantDisplay(string id)
    {
        _occupantColors.Remove(id);
        _occupantHp.Remove(id);
        _occupantConditions.Remove(id);
        QueueRedraw();
    }

    // ========================================================================
    // Drawing
    // ========================================================================

    public override void _Draw()
    {
        if (Grid == null)
            return;

        int cellSize = Grid.CellSize;
        int width = Grid.GridWidth;
        int height = Grid.GridHeight;

        if (width == 0 || height == 0)
            return;

        // Layer 1: Tile backgrounds
        DrawTiles(cellSize, width, height);

        // Layer 2: Highlight overlays
        DrawHighlightCells(_moveHighlightCells, MoveHighlightColor, cellSize);
        DrawHighlightCells(_attackHighlightCells, AttackHighlightColor, cellSize);
        DrawHighlightCells(_aoePreviewCells, _aoePreviewColor, cellSize);

        // Layer 3: Path preview
        DrawPathPreview(cellSize);

        // Layer 4: Grid lines
        DrawGridLines(cellSize, width, height);

        // Layer 5: Occupant tokens
        DrawOccupants(cellSize);
    }

    private void DrawTiles(int cellSize, int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var pos = new Vector2I(x, y);
                var cell = Grid.GetCell(pos);
                if (cell == null)
                    continue;

                var rect = new Rect2(x * cellSize, y * cellSize, cellSize, cellSize);
                Color color = GetTileColor(cell.Type);

                DrawRect(rect, color);

                // Draw hatching for difficult terrain
                if (cell.Type == TileType.DifficultTerrain || cell.Type == TileType.Water)
                {
                    DrawHatching(rect, new Color(0.0f, 0.0f, 0.0f, 0.2f));
                }

                // Draw hazard icon (small triangle)
                if (cell.Type == TileType.Hazard)
                {
                    DrawHazardIcon(rect);
                }
            }
        }
    }

    private void DrawHatching(Rect2 rect, Color color)
    {
        // Diagonal lines from bottom-left to top-right
        for (float offset = -rect.Size.Y; offset < rect.Size.X; offset += HatchSpacing)
        {
            var start = new Vector2(
                Mathf.Max(rect.Position.X, rect.Position.X + offset),
                Mathf.Min(rect.Position.Y + rect.Size.Y, rect.Position.Y + rect.Size.Y - offset)
            );
            var end = new Vector2(
                Mathf.Min(rect.Position.X + rect.Size.X, rect.Position.X + offset + rect.Size.Y),
                Mathf.Max(rect.Position.Y, rect.Position.Y - offset)
            );

            // Clamp to rect bounds
            start.X = Mathf.Clamp(start.X, rect.Position.X, rect.Position.X + rect.Size.X);
            start.Y = Mathf.Clamp(start.Y, rect.Position.Y, rect.Position.Y + rect.Size.Y);
            end.X = Mathf.Clamp(end.X, rect.Position.X, rect.Position.X + rect.Size.X);
            end.Y = Mathf.Clamp(end.Y, rect.Position.Y, rect.Position.Y + rect.Size.Y);

            if (start.DistanceTo(end) > 1.0f)
                DrawLine(start, end, color, 1.0f);
        }
    }

    private void DrawHazardIcon(Rect2 rect)
    {
        // Small warning triangle in center
        var center = rect.GetCenter();
        float size = rect.Size.X * 0.2f;
        var color = new Color(1.0f, 0.8f, 0.0f, 0.7f); // Yellow warning

        var points = new Vector2[]
        {
            new(center.X, center.Y - size),
            new(center.X - size, center.Y + size),
            new(center.X + size, center.Y + size),
        };

        DrawColoredPolygon(points, color);
    }

    private void DrawHighlightCells(List<Vector2I> cells, Color color, int cellSize)
    {
        foreach (var pos in cells)
        {
            var rect = new Rect2(pos.X * cellSize, pos.Y * cellSize, cellSize, cellSize);
            DrawRect(rect, color);
        }
    }

    private void DrawPathPreview(int cellSize)
    {
        if (_pathPreviewCells.Count < 2)
            return;

        float half = cellSize * 0.5f;

        // Draw line segments connecting cell centers
        for (int i = 0; i < _pathPreviewCells.Count - 1; i++)
        {
            var from = new Vector2(
                _pathPreviewCells[i].X * cellSize + half,
                _pathPreviewCells[i].Y * cellSize + half
            );
            var to = new Vector2(
                _pathPreviewCells[i + 1].X * cellSize + half,
                _pathPreviewCells[i + 1].Y * cellSize + half
            );

            DrawLine(from, to, PathPreviewColor, 2.0f);
        }

        // Draw dots at each waypoint
        for (int i = 1; i < _pathPreviewCells.Count; i++)
        {
            var center = new Vector2(
                _pathPreviewCells[i].X * cellSize + half,
                _pathPreviewCells[i].Y * cellSize + half
            );
            DrawCircle(center, 3.0f, PathPreviewColor);
        }
    }

    private void DrawGridLines(int cellSize, int width, int height)
    {
        float totalWidth = width * cellSize;
        float totalHeight = height * cellSize;

        // Internal grid lines
        for (int x = 1; x < width; x++)
        {
            float px = x * cellSize;
            DrawLine(new Vector2(px, 0), new Vector2(px, totalHeight), GridLineColor, 1.0f);
        }

        for (int y = 1; y < height; y++)
        {
            float py = y * cellSize;
            DrawLine(new Vector2(0, py), new Vector2(totalWidth, py), GridLineColor, 1.0f);
        }

        // Outer border (thicker)
        DrawRect(new Rect2(0, 0, totalWidth, totalHeight), GridLineBorderColor, false, 2.0f);
    }

    private void DrawOccupants(int cellSize)
    {
        float half = cellSize * 0.5f;
        float tokenRadius = cellSize * 0.35f;
        float hpBarWidth = cellSize * 0.7f;
        float hpBarHeight = 4.0f;

        foreach (string id in Grid.GetAllOccupants())
        {
            var pos = Grid.GetOccupantPosition(id);
            if (pos == null)
                continue;

            var gridPos = pos.Value;
            var center = new Vector2(gridPos.X * cellSize + half, gridPos.Y * cellSize + half);

            // Get color
            Color tokenColor = _occupantColors.TryGetValue(id, out var customColor)
                ? customColor
                : NeutralColor;

            // Draw token shadow
            DrawCircle(center + new Vector2(2, 2), tokenRadius, new Color(0, 0, 0, 0.3f));

            // Draw token circle
            DrawCircle(center, tokenRadius, tokenColor);

            // Draw token border
            DrawArc(center, tokenRadius, 0, Mathf.Tau, 32,
                new Color(tokenColor.R * 0.6f, tokenColor.G * 0.6f, tokenColor.B * 0.6f), 2.0f);

            // Draw first letter of ID in center
            // (Using DrawChar is complex with fonts, so we draw a simple dot instead
            //  for the minimal implementation. A full version would use DrawString.)
            DrawCircle(center, 3.0f, new Color(1, 1, 1, 0.8f));

            // Draw HP bar below token
            if (_occupantHp.TryGetValue(id, out var hp) && hp.Max > 0)
            {
                float hpX = center.X - hpBarWidth * 0.5f;
                float hpY = center.Y + tokenRadius + 3.0f;
                float hpFraction = Mathf.Clamp((float)hp.Current / hp.Max, 0, 1);

                // Background
                DrawRect(new Rect2(hpX, hpY, hpBarWidth, hpBarHeight),
                    new Color(0.2f, 0.0f, 0.0f, 0.7f));

                // Fill
                Color hpColor = hpFraction > 0.5f
                    ? new Color(0.1f, 0.8f, 0.1f, 0.9f)
                    : hpFraction > 0.25f
                        ? new Color(0.9f, 0.7f, 0.0f, 0.9f)
                        : new Color(0.9f, 0.1f, 0.1f, 0.9f);

                DrawRect(new Rect2(hpX, hpY, hpBarWidth * hpFraction, hpBarHeight), hpColor);

                // Border
                DrawRect(new Rect2(hpX, hpY, hpBarWidth, hpBarHeight),
                    new Color(0.4f, 0.4f, 0.4f, 0.5f), false, 1.0f);
            }

            // Draw condition indicator dots above token
            if (_occupantConditions.TryGetValue(id, out var conditions) && conditions.Count > 0)
            {
                float dotY = center.Y - tokenRadius - 6.0f;
                float dotSpacing = 6.0f;
                float startX = center.X - (conditions.Count - 1) * dotSpacing * 0.5f;

                for (int i = 0; i < conditions.Count && i < 5; i++) // Max 5 dots shown
                {
                    var dotCenter = new Vector2(startX + i * dotSpacing, dotY);
                    DrawCircle(dotCenter, 2.5f, GetConditionColor(conditions[i]));
                }
            }
        }
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static Color GetTileColor(TileType type)
    {
        return type switch
        {
            TileType.Floor => FloorColor,
            TileType.Wall => WallColor,
            TileType.DifficultTerrain => DifficultColor,
            TileType.Water => WaterColor,
            TileType.DeepWater => DeepWaterColor,
            TileType.Hazard => HazardColor,
            TileType.Pit => PitColor,
            TileType.Door => DoorColor,
            TileType.Interactable => InteractableColor,
            _ => FloorColor,
        };
    }

    /// <summary>
    /// Maps condition names to indicator dot colors.
    /// </summary>
    private static Color GetConditionColor(string condition)
    {
        return condition?.ToLowerInvariant() switch
        {
            "poisoned" => new Color(0.4f, 0.8f, 0.1f),
            "burning" or "fire" => new Color(1.0f, 0.5f, 0.0f),
            "frozen" or "cold" => new Color(0.5f, 0.8f, 1.0f),
            "stunned" or "paralyzed" => new Color(1.0f, 1.0f, 0.2f),
            "blinded" => new Color(0.3f, 0.3f, 0.3f),
            "charmed" => new Color(1.0f, 0.4f, 0.7f),
            "frightened" => new Color(0.6f, 0.2f, 0.6f),
            "invisible" => new Color(0.8f, 0.8f, 0.8f, 0.4f),
            "prone" => new Color(0.6f, 0.4f, 0.2f),
            "restrained" => new Color(0.5f, 0.5f, 0.5f),
            "unconscious" => new Color(0.1f, 0.1f, 0.1f),
            _ => new Color(0.9f, 0.9f, 0.9f),
        };
    }
}
