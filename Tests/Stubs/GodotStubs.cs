// Minimal stubs for Godot types referenced by pure-logic source files.
// This allows the test project to compile without the Godot.NET.Sdk.
// Only stub what is actually used by included source files.

using System;

namespace Godot;

/// <summary>
/// Stub for Godot.ProjectSettings. Only GlobalizePath is needed by SRDDataLoader.
/// In the test environment, this strips the "res://" prefix and returns a relative path.
/// </summary>
public static class ProjectSettings
{
    /// <summary>
    /// Converts a Godot res:// path to a filesystem path.
    /// In tests, this simply strips the "res://" prefix.
    /// </summary>
    public static string GlobalizePath(string path)
    {
        if (path.StartsWith("res://", System.StringComparison.OrdinalIgnoreCase))
            return path.Substring(6); // Strip "res://"

        return path;
    }
}

/// <summary>
/// Stub for Godot.Vector2I (2D integer vector).
/// Used by CombatGrid for grid positions.
/// </summary>
public struct Vector2I : IEquatable<Vector2I>
{
    public int X { get; set; }
    public int Y { get; set; }

    public Vector2I(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static Vector2I operator +(Vector2I a, Vector2I b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2I operator -(Vector2I a, Vector2I b) => new(a.X - b.X, a.Y - b.Y);
    public static bool operator ==(Vector2I a, Vector2I b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Vector2I a, Vector2I b) => !(a == b);

    public bool Equals(Vector2I other) => X == other.X && Y == other.Y;
    public override bool Equals(object obj) => obj is Vector2I other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// Stub for Godot.Vector2 (2D floating-point vector).
/// Used by CombatGrid for world-space coordinates and AoE calculations.
/// </summary>
public struct Vector2 : IEquatable<Vector2>
{
    public float X { get; set; }
    public float Y { get; set; }

    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator *(Vector2 a, float scalar) => new(a.X * scalar, a.Y * scalar);
    public static Vector2 operator /(Vector2 a, float scalar) => new(a.X / scalar, a.Y / scalar);

    public float Length() => MathF.Sqrt(X * X + Y * Y);
    public float LengthSquared() => X * X + Y * Y;

    public Vector2 Normalized()
    {
        float len = Length();
        return len > 0.0001f ? new Vector2(X / len, Y / len) : new Vector2(0, 0);
    }

    public float Dot(Vector2 other) => X * other.X + Y * other.Y;

    public bool Equals(Vector2 other) => MathF.Abs(X - other.X) < 0.0001f && MathF.Abs(Y - other.Y) < 0.0001f;
    public override bool Equals(object obj) => obj is Vector2 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X:F2}, {Y:F2})";
}

/// <summary>
/// Stub for Godot.Mathf (math utilities).
/// </summary>
public static class Mathf
{
    public const float Pi = MathF.PI;

    public static int FloorToInt(float value) => (int)MathF.Floor(value);
    public static int RoundToInt(float value) => (int)MathF.Round(value);
    public static float DegToRad(float degrees) => degrees * MathF.PI / 180f;
    public static float RadToDeg(float radians) => radians * 180f / MathF.PI;
    public static float Cos(float radians) => MathF.Cos(radians);
}

/// <summary>
/// Stub for Godot [Export] attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ExportAttribute : Attribute { }

/// <summary>
/// Stub for Godot [Signal] attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Delegate)]
public class SignalAttribute : Attribute { }

/// <summary>
/// Stub base class for Godot nodes. CombatGrid inherits from Node2D.
/// We only need the bare minimum to allow compilation.
/// </summary>
public class GodotObject
{
    public void EmitSignal(StringName signal, params object[] args) { }
}

public class Node : GodotObject
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Stub for Godot.StringName.
/// </summary>
public class StringName
{
    private readonly string _name;
    public StringName(string name) { _name = name; }
    public static implicit operator StringName(string s) => new(s);
    public override string ToString() => _name;
}

/// <summary>
/// Stub for Godot.Node2D (2D scene node).
/// </summary>
public class Node2D : Node
{
}

/// <summary>
/// Stub for Godot.GD (global Godot debug/logging utilities).
/// </summary>
public static class GD
{
    public static void Print(params object[] what)
    {
        Console.WriteLine(string.Join(" ", what));
    }

    public static void PrintErr(params object[] what)
    {
        Console.Error.WriteLine(string.Join(" ", what));
    }

    public static void PushWarning(params object[] what)
    {
        Console.WriteLine("[WARNING] " + string.Join(" ", what));
    }
}
