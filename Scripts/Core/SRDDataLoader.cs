using Newtonsoft.Json;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Loads SRD JSON data files into typed C# collections.
/// All methods are static and stateless -- they read from disk each call.
/// For runtime use, cache the results rather than calling repeatedly.
/// </summary>
public static class SRDDataLoader
{
    /// <summary>
    /// Loads all race definitions from the given JSON file path.
    /// </summary>
    /// <param name="jsonPath">Absolute or Godot res:// path to races.json.</param>
    /// <exception cref="FileNotFoundException">If the file does not exist.</exception>
    /// <exception cref="JsonException">If the JSON cannot be deserialized.</exception>
    public static List<RaceData> LoadRaces(string jsonPath)
    {
        return LoadFromFile<List<RaceData>>(jsonPath, "races");
    }

    /// <summary>
    /// Loads all class definitions from the given JSON file path.
    /// </summary>
    /// <param name="jsonPath">Absolute or Godot res:// path to classes.json.</param>
    public static List<ClassData> LoadClasses(string jsonPath)
    {
        return LoadFromFile<List<ClassData>>(jsonPath, "classes");
    }

    /// <summary>
    /// Loads all background definitions from the given JSON file path.
    /// </summary>
    /// <param name="jsonPath">Absolute or Godot res:// path to backgrounds.json.</param>
    public static List<BackgroundData> LoadBackgrounds(string jsonPath)
    {
        return LoadFromFile<List<BackgroundData>>(jsonPath, "backgrounds");
    }

    /// <summary>
    /// Loads all equipment definitions from the given JSON file path.
    /// </summary>
    /// <param name="jsonPath">Absolute or Godot res:// path to equipment.json.</param>
    public static List<InventoryItem> LoadEquipment(string jsonPath)
    {
        return LoadFromFile<List<InventoryItem>>(jsonPath, "equipment");
    }

    /// <summary>
    /// Loads all magic item definitions from the given JSON file path.
    /// Uses the same InventoryItem format as equipment.json.
    /// </summary>
    /// <param name="jsonPath">Absolute or Godot res:// path to magic_items.json.</param>
    public static List<InventoryItem> LoadMagicItems(string jsonPath)
    {
        return LoadFromFile<List<InventoryItem>>(jsonPath, "magic items");
    }

    // ── Internal ─────────────────────────────────────────────────────

    private static T LoadFromFile<T>(string jsonPath, string dataName) where T : new()
    {
        string resolvedPath = ResolvePath(jsonPath);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException(
                $"SRD {dataName} file not found at: {resolvedPath}", resolvedPath);

        string json = File.ReadAllText(resolvedPath);

        var result = JsonConvert.DeserializeObject<T>(json);
        if (result == null)
            throw new JsonException($"Failed to deserialize SRD {dataName} from: {resolvedPath}");

        return result;
    }

    /// <summary>
    /// Resolves a Godot res:// path to an absolute filesystem path, or returns
    /// the path as-is if it is already an absolute path.
    /// </summary>
    private static string ResolvePath(string path)
    {
        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            // Godot's ProjectSettings.GlobalizePath handles res:// -> filesystem.
            // If running outside Godot (unit tests), strip prefix and use working dir.
            try
            {
                return Godot.ProjectSettings.GlobalizePath(path);
            }
            catch
            {
                // Fallback for unit tests outside the Godot runtime.
                return path.Replace("res://", "");
            }
        }

        return path;
    }
}
