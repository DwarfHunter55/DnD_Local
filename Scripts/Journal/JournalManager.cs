using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Newtonsoft.Json;

namespace ChroniclesUnbound.Journal;

/// <summary>
/// Manages all journal entries: adding, querying, read/unread tracking,
/// and save/load persistence. Entries are auto-assigned incrementing IDs
/// and timestamped on creation.
///
/// Singleton pattern matching other managers. Add to the scene tree under
/// Main or register as autoload; access via JournalManager.Instance.
/// </summary>
public partial class JournalManager : Node
{
    public static JournalManager? Instance { get; private set; }

    // -----------------------------------------------------------------
    // Signals
    // -----------------------------------------------------------------

    [Signal]
    public delegate void EntryAddedEventHandler(int entryId);

    [Signal]
    public delegate void EntryReadEventHandler(int entryId);

    // -----------------------------------------------------------------
    // State
    // -----------------------------------------------------------------

    private readonly List<JournalEntry> _entries = new();
    private int _nextId = 1;

    // -----------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[JournalManager] Initialized.");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    // -----------------------------------------------------------------
    // Core API
    // -----------------------------------------------------------------

    /// <summary>
    /// Creates a new journal entry and adds it to the log.
    /// Returns the auto-assigned entry ID.
    /// </summary>
    public int AddEntry(JournalCategory category, string title, string description,
                        string? relatedEntityId = null, string? locationName = null,
                        List<string>? tags = null)
    {
        if (string.IsNullOrEmpty(title))
        {
            GD.PrintErr("[JournalManager] AddEntry: title is null or empty.");
            return -1;
        }

        var entry = new JournalEntry
        {
            Id = _nextId++,
            Category = category,
            Title = title,
            Description = description ?? "",
            RealTimestamp = DateTime.UtcNow.ToString("o"),
            Timestamp = "", // No in-game time system yet
            RelatedEntityId = relatedEntityId,
            LocationName = locationName,
            IsRead = false,
            Tags = tags ?? new List<string>()
        };

        _entries.Add(entry);

        GD.Print($"[JournalManager] Entry added: [{category}] {title} (id:{entry.Id})");
        EmitSignal(SignalName.EntryAdded, entry.Id);

        return entry.Id;
    }

    /// <summary>
    /// Returns a journal entry by ID, or null if not found.
    /// </summary>
    public JournalEntry? GetEntry(int id)
    {
        return _entries.FirstOrDefault(e => e.Id == id);
    }

    /// <summary>
    /// Returns journal entries, optionally filtered by category,
    /// ordered by ID descending (newest first).
    /// </summary>
    public List<JournalEntry> GetEntries(JournalCategory? categoryFilter = null)
    {
        IEnumerable<JournalEntry> query = _entries;

        if (categoryFilter.HasValue)
            query = query.Where(e => e.Category == categoryFilter.Value);

        return query.OrderByDescending(e => e.Id).ToList();
    }

    /// <summary>
    /// Marks an entry as read. Emits EntryRead signal on success.
    /// </summary>
    public void MarkAsRead(int id)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == id);
        if (entry == null)
        {
            GD.PrintErr($"[JournalManager] MarkAsRead: unknown entry id {id}.");
            return;
        }

        if (entry.IsRead)
            return;

        entry.IsRead = true;
        GD.Print($"[JournalManager] Entry marked as read: {entry.Title} (id:{id})");
        EmitSignal(SignalName.EntryRead, id);
    }

    /// <summary>
    /// Returns the count of unread entries per category.
    /// Only categories with at least one unread entry are included.
    /// </summary>
    public Dictionary<JournalCategory, int> GetUnreadCounts()
    {
        return _entries
            .Where(e => !e.IsRead)
            .GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Returns the total number of unread entries across all categories.
    /// </summary>
    public int GetTotalUnreadCount()
    {
        return _entries.Count(e => !e.IsRead);
    }

    // -----------------------------------------------------------------
    // Save / Load
    // -----------------------------------------------------------------

    /// <summary>
    /// Serializes all journal state to JSON for persistence.
    /// </summary>
    public string GetSaveData()
    {
        var saveData = new JournalSaveData
        {
            Entries = _entries,
            NextId = _nextId
        };

        return JsonConvert.SerializeObject(saveData, Formatting.Indented);
    }

    /// <summary>
    /// Restores journal state from previously saved JSON. Replaces all current state.
    /// </summary>
    public void RestoreFromSaveData(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            GD.PrintErr("[JournalManager] RestoreFromSaveData: json is null or empty.");
            return;
        }

        try
        {
            var saveData = JsonConvert.DeserializeObject<JournalSaveData>(json);
            if (saveData == null)
            {
                GD.PrintErr("[JournalManager] RestoreFromSaveData: failed to deserialize.");
                return;
            }

            _entries.Clear();
            _entries.AddRange(saveData.Entries);
            _nextId = saveData.NextId;

            GD.Print($"[JournalManager] Restored {_entries.Count} journal entries from save data.");
        }
        catch (JsonException ex)
        {
            GD.PrintErr($"[JournalManager] RestoreFromSaveData: JSON error — {ex.Message}");
        }
    }

    /// <summary>
    /// Clears all journal state for a new game.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        _nextId = 1;
        GD.Print("[JournalManager] Cleared all entries.");
    }

    // -----------------------------------------------------------------
    // Internal save DTO
    // -----------------------------------------------------------------

    private class JournalSaveData
    {
        [JsonProperty("entries")]
        public List<JournalEntry> Entries { get; set; } = new();

        [JsonProperty("nextId")]
        public int NextId { get; set; } = 1;
    }
}
