using Newtonsoft.Json;
using Godot;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Manages the world location graph: loading, querying, discovery state, and
/// travel distance calculations. Pure data manager — no Godot Node dependency.
/// Instantiate once at game start and cache it.
///
/// Locations form a bidirectional graph. Travel distance is computed as the
/// shortest path (BFS hop count) between two locations, measured in abstract
/// travel units (each hop = 1 unit, roughly half a day's travel).
/// </summary>
public class WorldMap
{
    private readonly Dictionary<string, Location> _locations = new();

    /// <summary>
    /// Number of locations loaded into the world map.
    /// </summary>
    public int LocationCount => _locations.Count;

    // -----------------------------------------------------------------
    // Loading
    // -----------------------------------------------------------------

    /// <summary>
    /// Loads all locations from a JSON file. Accepts Godot res:// paths or
    /// absolute filesystem paths.
    /// </summary>
    /// <param name="jsonPath">Path to locations.json.</param>
    /// <exception cref="FileNotFoundException">If the file does not exist.</exception>
    /// <exception cref="JsonException">If deserialization fails.</exception>
    public void LoadLocations(string jsonPath)
    {
        string resolvedPath = ResolvePath(jsonPath);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException(
                $"World locations file not found at: {resolvedPath}", resolvedPath);

        string json = File.ReadAllText(resolvedPath);

        var locations = JsonConvert.DeserializeObject<List<Location>>(json);
        if (locations == null)
            throw new JsonException($"Failed to deserialize locations from: {resolvedPath}");

        _locations.Clear();
        foreach (var loc in locations)
        {
            if (string.IsNullOrWhiteSpace(loc.Id))
            {
                GD.PrintErr("[WorldMap] Skipping location with empty ID.");
                continue;
            }

            if (_locations.ContainsKey(loc.Id))
            {
                GD.PrintErr($"[WorldMap] Duplicate location ID '{loc.Id}', skipping.");
                continue;
            }

            _locations[loc.Id] = loc;
        }

        GD.Print($"[WorldMap] Loaded {_locations.Count} locations from {resolvedPath}.");
        ValidateConnections();
    }

    // -----------------------------------------------------------------
    // Queries
    // -----------------------------------------------------------------

    /// <summary>
    /// Get a location by its ID. Returns null if not found.
    /// </summary>
    public Location? GetLocation(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return _locations.TryGetValue(id, out var loc) ? loc : null;
    }

    /// <summary>
    /// Get all locations connected to the given location that the player has
    /// already discovered. Useful for populating the travel UI.
    /// </summary>
    public List<Location> GetConnectedLocations(string locationId)
    {
        var result = new List<Location>();
        var source = GetLocation(locationId);
        if (source == null)
            return result;

        foreach (string connId in source.ConnectedLocationIds)
        {
            if (_locations.TryGetValue(connId, out var connected) && connected.IsDiscovered)
                result.Add(connected);
        }

        return result;
    }

    /// <summary>
    /// Get all locations connected to the given location, including undiscovered
    /// ones. Used internally for pathfinding and by systems that need the full graph.
    /// </summary>
    public List<Location> GetAllConnectedLocations(string locationId)
    {
        var result = new List<Location>();
        var source = GetLocation(locationId);
        if (source == null)
            return result;

        foreach (string connId in source.ConnectedLocationIds)
        {
            if (_locations.TryGetValue(connId, out var connected))
                result.Add(connected);
        }

        return result;
    }

    /// <summary>
    /// Get all locations the player has discovered.
    /// </summary>
    public List<Location> GetDiscoveredLocations()
    {
        var result = new List<Location>();
        foreach (var loc in _locations.Values)
        {
            if (loc.IsDiscovered)
                result.Add(loc);
        }
        return result;
    }

    /// <summary>
    /// Get all locations in the world map regardless of discovery state.
    /// </summary>
    public List<Location> GetAllLocations()
    {
        return new List<Location>(_locations.Values);
    }

    // -----------------------------------------------------------------
    // Discovery
    // -----------------------------------------------------------------

    /// <summary>
    /// Mark a location as discovered. Returns true if the location exists and
    /// was not already discovered. Returns false if already discovered or not found.
    /// </summary>
    public bool DiscoverLocation(string id)
    {
        var loc = GetLocation(id);
        if (loc == null)
        {
            GD.PrintErr($"[WorldMap] Cannot discover unknown location: {id}");
            return false;
        }

        if (loc.IsDiscovered)
            return false;

        loc.IsDiscovered = true;
        GD.Print($"[WorldMap] Discovered: {loc.Name}");
        return true;
    }

    // -----------------------------------------------------------------
    // Travel Distance
    // -----------------------------------------------------------------

    /// <summary>
    /// Calculate the shortest travel distance between two locations using BFS
    /// over the full connection graph (ignoring discovery state). Returns the
    /// number of hops, where each hop represents roughly half a day of travel.
    /// Returns -1 if no path exists.
    /// </summary>
    public int CalculateTravelDistance(string fromId, string toId)
    {
        if (fromId == toId)
            return 0;

        if (!_locations.ContainsKey(fromId) || !_locations.ContainsKey(toId))
            return -1;

        // BFS shortest path
        var visited = new HashSet<string> { fromId };
        var queue = new Queue<(string id, int distance)>();
        queue.Enqueue((fromId, 0));

        while (queue.Count > 0)
        {
            var (currentId, distance) = queue.Dequeue();

            if (!_locations.TryGetValue(currentId, out var current))
                continue;

            foreach (string connId in current.ConnectedLocationIds)
            {
                if (connId == toId)
                    return distance + 1;

                if (visited.Add(connId))
                    queue.Enqueue((connId, distance + 1));
            }
        }

        return -1; // no path found
    }

    // -----------------------------------------------------------------
    // Accessibility
    // -----------------------------------------------------------------

    /// <summary>
    /// Check whether a location is accessible to the player character.
    /// A location is accessible if it is discovered and its discovery
    /// requirements are met.
    ///
    /// Requirement format:
    ///   - "" or null: no requirement (always accessible if discovered)
    ///   - "quest:quest_id": requires the named quest to be completed
    ///   - "level:N": requires player character level >= N
    /// </summary>
    public bool IsLocationAccessible(string locationId, Character? playerCharacter)
    {
        var loc = GetLocation(locationId);
        if (loc == null)
            return false;

        if (!loc.IsDiscovered)
            return false;

        if (string.IsNullOrWhiteSpace(loc.DiscoveryRequirement))
            return true;

        return CheckRequirement(loc.DiscoveryRequirement, playerCharacter);
    }

    /// <summary>
    /// Evaluate a discovery requirement string against the player character.
    /// </summary>
    private static bool CheckRequirement(string requirement, Character? playerCharacter)
    {
        if (playerCharacter == null)
            return false;

        // "level:N" — requires player level >= N
        if (requirement.StartsWith("level:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(requirement.AsSpan(6), out int requiredLevel))
                return playerCharacter.Level >= requiredLevel;

            GD.PrintErr($"[WorldMap] Malformed level requirement: {requirement}");
            return false;
        }

        // "quest:quest_id" — requires quest completion.
        // Quest system integration is deferred; for now always return false
        // so gated content stays locked until the quest tracker is wired in.
        if (requirement.StartsWith("quest:", StringComparison.OrdinalIgnoreCase))
        {
            // TODO: Integrate with QuestTracker when it exists.
            // string questId = requirement.Substring(6);
            // return QuestTracker.Instance?.IsCompleted(questId) ?? false;
            GD.Print($"[WorldMap] Quest requirement '{requirement}' — quest system not yet integrated.");
            return false;
        }

        GD.PrintErr($"[WorldMap] Unknown requirement format: {requirement}");
        return false;
    }

    // -----------------------------------------------------------------
    // Save / Load State
    // -----------------------------------------------------------------

    /// <summary>
    /// Get the discovery state of all locations for serialization.
    /// Returns a dictionary of location ID to IsDiscovered.
    /// </summary>
    public Dictionary<string, bool> GetDiscoveryState()
    {
        var state = new Dictionary<string, bool>();
        foreach (var kvp in _locations)
            state[kvp.Key] = kvp.Value.IsDiscovered;
        return state;
    }

    /// <summary>
    /// Restore discovery state from a saved dictionary. Locations not present
    /// in the dictionary keep their default state from the JSON file.
    /// </summary>
    public void RestoreDiscoveryState(Dictionary<string, bool> state)
    {
        foreach (var kvp in state)
        {
            if (_locations.TryGetValue(kvp.Key, out var loc))
                loc.IsDiscovered = kvp.Value;
        }

        int discovered = state.Values.Count(v => v);
        GD.Print($"[WorldMap] Restored discovery state: {discovered} of {_locations.Count} discovered.");
    }

    // -----------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------

    /// <summary>
    /// Validate that all connection references point to existing locations
    /// and that connections are bidirectional. Logs warnings for issues.
    /// </summary>
    private void ValidateConnections()
    {
        foreach (var loc in _locations.Values)
        {
            foreach (string connId in loc.ConnectedLocationIds)
            {
                if (!_locations.ContainsKey(connId))
                {
                    GD.PrintErr($"[WorldMap] {loc.Id} references unknown location: {connId}");
                    continue;
                }

                var target = _locations[connId];
                if (!target.ConnectedLocationIds.Contains(loc.Id))
                {
                    GD.PrintErr($"[WorldMap] One-way connection: {loc.Id} -> {connId} (missing reverse).");
                }
            }
        }
    }

    // -----------------------------------------------------------------
    // Path resolution (matches SRDDataLoader pattern)
    // -----------------------------------------------------------------

    private static string ResolvePath(string path)
    {
        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Godot.ProjectSettings.GlobalizePath(path);
            }
            catch
            {
                return path.Replace("res://", "");
            }
        }

        return path;
    }
}
