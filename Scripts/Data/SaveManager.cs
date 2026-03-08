using Godot;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace ChroniclesUnbound.Data;

/// <summary>
/// High-level save/load operations built on top of <see cref="DatabaseManager"/>.
/// All public methods use parameterized queries to prevent SQL injection.
/// </summary>
public class SaveManager
{
    private readonly DatabaseManager _db;

    public SaveManager(DatabaseManager db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Save Slots
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new save slot and returns its ID.
    /// </summary>
    public int CreateSaveSlot(string slotName)
    {
        if (string.IsNullOrWhiteSpace(slotName))
            throw new ArgumentException("Slot name cannot be empty.", nameof(slotName));

        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO SaveSlots (SlotName)
            VALUES ($slotName);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("$slotName", slotName);

        long id = (long)cmd.ExecuteScalar()!;
        GD.Print($"[SaveManager] Created save slot '{slotName}' (ID {id}).");
        return (int)id;
    }

    /// <summary>
    /// Returns metadata for all save slots, ordered by most recently updated first.
    /// </summary>
    public List<SaveSlotInfo> GetAllSaveSlots()
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, SlotName, CharacterName, CharacterLevel, PlayTimeSeconds, UpdatedAt
            FROM SaveSlots
            ORDER BY UpdatedAt DESC;
        ";

        var slots = new List<SaveSlotInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            slots.Add(new SaveSlotInfo
            {
                Id = reader.GetInt32(0),
                SlotName = reader.GetString(1),
                CharacterName = reader.IsDBNull(2) ? null : reader.GetString(2),
                CharacterLevel = reader.GetInt32(3),
                PlayTimeSeconds = reader.GetInt32(4),
                UpdatedAt = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return slots;
    }

    /// <summary>
    /// Returns true if at least one save slot exists in the database.
    /// </summary>
    public bool HasAnySaves()
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SaveSlots;";
        long count = (long)cmd.ExecuteScalar()!;
        return count > 0;
    }

    /// <summary>
    /// Returns the save slot with the most recent UpdatedAt timestamp,
    /// or null if no save slots exist.
    /// </summary>
    public SaveSlotInfo? GetLatestSaveSlot()
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, SlotName, CharacterName, CharacterLevel, PlayTimeSeconds, UpdatedAt
            FROM SaveSlots
            ORDER BY UpdatedAt DESC
            LIMIT 1;
        ";

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new SaveSlotInfo
        {
            Id = reader.GetInt32(0),
            SlotName = reader.GetString(1),
            CharacterName = reader.IsDBNull(2) ? null : reader.GetString(2),
            CharacterLevel = reader.GetInt32(3),
            PlayTimeSeconds = reader.GetInt32(4),
            UpdatedAt = reader.IsDBNull(5) ? null : reader.GetString(5)
        };
    }

    /// <summary>
    /// Deletes a save slot and all associated data (characters, game state,
    /// story threads, conversation memory).
    /// </summary>
    public void DeleteSaveSlot(int slotId)
    {
        var conn = _db.GetConnection();

        // Delete child rows first, then the slot itself.
        // Even with FK enforcement on, explicit deletes are clearer and don't
        // require ON DELETE CASCADE (which must be defined at table creation time).
        string[] childTables = { "Characters", "GameState", "StoryThreads", "ConversationMemory" };

        using var transaction = conn.BeginTransaction();
        try
        {
            foreach (string table in childTables)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                // Table names are hardcoded constants, not user input — safe to interpolate.
                cmd.CommandText = $"DELETE FROM {table} WHERE SaveSlotId = $slotId;";
                cmd.Parameters.AddWithValue("$slotId", slotId);
                cmd.ExecuteNonQuery();
            }

            using var deleteSlot = conn.CreateCommand();
            deleteSlot.Transaction = transaction;
            deleteSlot.CommandText = "DELETE FROM SaveSlots WHERE Id = $slotId;";
            deleteSlot.Parameters.AddWithValue("$slotId", slotId);
            deleteSlot.ExecuteNonQuery();

            transaction.Commit();
            GD.Print($"[SaveManager] Deleted save slot {slotId} and all associated data.");
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Characters
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the player character to the specified slot. Upserts: if a player
    /// character row already exists for the slot, it is updated.
    /// </summary>
    public void SavePlayerCharacter(int saveSlotId, string characterJson)
    {
        if (string.IsNullOrWhiteSpace(characterJson))
            throw new ArgumentException("Character JSON cannot be empty.", nameof(characterJson));

        SaveCharacterRow(saveSlotId, characterJson, isPlayer: true);
        UpdateSlotCharacterInfo(saveSlotId, characterJson);
        TouchSlotTimestamp(saveSlotId);
    }

    /// <summary>
    /// Saves all companion characters to the specified slot as a single JSON
    /// array in one database row. This avoids the old bug where multiple
    /// companions collapsed into one row because the upsert key was only
    /// (SaveSlotId, IsPlayerCharacter).
    ///
    /// Pass an empty list to clear companions from the slot.
    /// </summary>
    public void SaveCompanions(int saveSlotId, List<Character> companions)
    {
        string json = JsonConvert.SerializeObject(companions ?? new List<Character>(), Formatting.None);
        SaveCharacterRow(saveSlotId, json, isPlayer: false);
        TouchSlotTimestamp(saveSlotId);
    }

    /// <summary>
    /// Legacy method kept for backward compatibility. Prefer SavePlayerCharacter
    /// and SaveCompanions for new code.
    ///
    /// When isPlayer is false, the characterJson is expected to be a JSON array
    /// of all companions. When isPlayer is true, it is a single character object.
    /// </summary>
    public void SaveCharacter(int saveSlotId, string characterJson, bool isPlayer)
    {
        if (string.IsNullOrWhiteSpace(characterJson))
            throw new ArgumentException("Character JSON cannot be empty.", nameof(characterJson));

        SaveCharacterRow(saveSlotId, characterJson, isPlayer);

        if (isPlayer)
            UpdateSlotCharacterInfo(saveSlotId, characterJson);

        TouchSlotTimestamp(saveSlotId);
    }

    /// <summary>
    /// Loads the player character JSON for a save slot, or null if none exists.
    /// </summary>
    public string? LoadPlayerCharacter(int saveSlotId)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CharacterJson
            FROM Characters
            WHERE SaveSlotId = $slotId AND IsPlayerCharacter = 1
            LIMIT 1;
        ";
        cmd.Parameters.AddWithValue("$slotId", saveSlotId);

        var result = cmd.ExecuteScalar();
        return result is string s ? s : null;
    }

    /// <summary>
    /// Loads companion characters for a save slot. The companions are stored as
    /// a JSON array in a single row with IsPlayerCharacter = 0.
    /// Returns an empty list if no companion data exists.
    /// </summary>
    public List<Character> LoadCompanions(int saveSlotId)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CharacterJson
            FROM Characters
            WHERE SaveSlotId = $slotId AND IsPlayerCharacter = 0
            LIMIT 1;
        ";
        cmd.Parameters.AddWithValue("$slotId", saveSlotId);

        var result = cmd.ExecuteScalar();
        if (result is not string json || string.IsNullOrWhiteSpace(json))
            return new List<Character>();

        try
        {
            // Try to deserialize as an array first (new format).
            var companions = JsonConvert.DeserializeObject<List<Character>>(json);
            if (companions != null)
                return companions;
        }
        catch (JsonException)
        {
            // Fallback: old format stored a single companion object, not an array.
            try
            {
                var single = JsonConvert.DeserializeObject<Character>(json);
                if (single != null)
                    return new List<Character> { single };
            }
            catch (JsonException ex)
            {
                GD.PrintErr($"[SaveManager] Failed to deserialize companion data: {ex.Message}");
            }
        }

        return new List<Character>();
    }

    /// <summary>
    /// Loads all characters for a save slot (legacy method).
    /// Returns a list of tuples: (serialized JSON, whether it's the player character).
    /// Player character is returned first.
    /// </summary>
    public List<(string json, bool isPlayer)> LoadCharacters(int saveSlotId)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CharacterJson, IsPlayerCharacter
            FROM Characters
            WHERE SaveSlotId = $slotId
            ORDER BY IsPlayerCharacter DESC;
        ";
        cmd.Parameters.AddWithValue("$slotId", saveSlotId);

        var results = new List<(string json, bool isPlayer)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add((reader.GetString(0), reader.GetInt32(1) != 0));
        }

        return results;
    }

    /// <summary>
    /// Internal upsert for a character row. Uses (SaveSlotId, IsPlayerCharacter)
    /// as the compound key — there is exactly one player row and one companion
    /// row per save slot (companions are stored as a JSON array).
    /// </summary>
    private void SaveCharacterRow(int saveSlotId, string json, bool isPlayer)
    {
        var conn = _db.GetConnection();
        int isPlayerInt = isPlayer ? 1 : 0;

        using var check = conn.CreateCommand();
        check.CommandText = @"
            SELECT Id FROM Characters
            WHERE SaveSlotId = $slotId AND IsPlayerCharacter = $isPlayer
            LIMIT 1;
        ";
        check.Parameters.AddWithValue("$slotId", saveSlotId);
        check.Parameters.AddWithValue("$isPlayer", isPlayerInt);

        var existingId = check.ExecuteScalar();

        if (existingId is not null)
        {
            using var update = conn.CreateCommand();
            update.CommandText = @"
                UPDATE Characters SET CharacterJson = $json
                WHERE Id = $id;
            ";
            update.Parameters.AddWithValue("$json", json);
            update.Parameters.AddWithValue("$id", (long)existingId);
            update.ExecuteNonQuery();
        }
        else
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = @"
                INSERT INTO Characters (SaveSlotId, CharacterJson, IsPlayerCharacter)
                VALUES ($slotId, $json, $isPlayer);
            ";
            insert.Parameters.AddWithValue("$slotId", saveSlotId);
            insert.Parameters.AddWithValue("$json", json);
            insert.Parameters.AddWithValue("$isPlayer", isPlayerInt);
            insert.ExecuteNonQuery();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Game State
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the current game state for a slot. Upserts — one row per slot.
    /// </summary>
    public void SaveGameState(int saveSlotId, string phase, string location, string campaignJson)
    {
        var conn = _db.GetConnection();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT Id FROM GameState WHERE SaveSlotId = $slotId LIMIT 1;";
        check.Parameters.AddWithValue("$slotId", saveSlotId);
        var existingId = check.ExecuteScalar();

        if (existingId is not null)
        {
            using var update = conn.CreateCommand();
            update.CommandText = @"
                UPDATE GameState
                SET CurrentPhase = $phase, CurrentLocation = $location, CampaignDataJson = $campaign
                WHERE Id = $id;
            ";
            update.Parameters.AddWithValue("$phase", phase ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("$location", location ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("$campaign", campaignJson ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("$id", (long)existingId);
            update.ExecuteNonQuery();
        }
        else
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = @"
                INSERT INTO GameState (SaveSlotId, CurrentPhase, CurrentLocation, CampaignDataJson)
                VALUES ($slotId, $phase, $location, $campaign);
            ";
            insert.Parameters.AddWithValue("$slotId", saveSlotId);
            insert.Parameters.AddWithValue("$phase", phase ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("$location", location ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("$campaign", campaignJson ?? (object)DBNull.Value);
            insert.ExecuteNonQuery();
        }

        TouchSlotTimestamp(saveSlotId);
    }

    /// <summary>
    /// Loads the game state for a save slot. Returns null if none exists.
    /// </summary>
    public GameStateData? LoadGameState(int saveSlotId)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, SaveSlotId, CurrentPhase, CurrentLocation, CampaignDataJson
            FROM GameState
            WHERE SaveSlotId = $slotId
            LIMIT 1;
        ";
        cmd.Parameters.AddWithValue("$slotId", saveSlotId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new GameStateData
        {
            Id = reader.GetInt32(0),
            SaveSlotId = reader.GetInt32(1),
            CurrentPhase = reader.IsDBNull(2) ? null : reader.GetString(2),
            CurrentLocation = reader.IsDBNull(3) ? null : reader.GetString(3),
            CampaignDataJson = reader.IsDBNull(4) ? null : reader.GetString(4)
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Campaign Data (convenience wrappers around GameState.CampaignDataJson)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the CampaignSaveData JSON into the existing GameState row's
    /// CampaignDataJson column. If no GameState row exists for the slot yet,
    /// one is created with null phase/location.
    /// </summary>
    public void SaveCampaignData(int saveSlotId, string campaignDataJson)
    {
        var conn = _db.GetConnection();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT Id FROM GameState WHERE SaveSlotId = $slotId LIMIT 1;";
        check.Parameters.AddWithValue("$slotId", saveSlotId);
        var existingId = check.ExecuteScalar();

        if (existingId is not null)
        {
            using var update = conn.CreateCommand();
            update.CommandText = @"
                UPDATE GameState
                SET CampaignDataJson = $campaign
                WHERE Id = $id;
            ";
            update.Parameters.AddWithValue("$campaign", campaignDataJson ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("$id", (long)existingId);
            update.ExecuteNonQuery();
        }
        else
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = @"
                INSERT INTO GameState (SaveSlotId, CampaignDataJson)
                VALUES ($slotId, $campaign);
            ";
            insert.Parameters.AddWithValue("$slotId", saveSlotId);
            insert.Parameters.AddWithValue("$campaign", campaignDataJson ?? (object)DBNull.Value);
            insert.ExecuteNonQuery();
        }

        TouchSlotTimestamp(saveSlotId);
    }

    /// <summary>
    /// Loads the CampaignDataJson string from the GameState table for a slot.
    /// Returns null if no game state exists or the column is null.
    /// </summary>
    public string? LoadCampaignData(int saveSlotId)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CampaignDataJson
            FROM GameState
            WHERE SaveSlotId = $slotId
            LIMIT 1;
        ";
        cmd.Parameters.AddWithValue("$slotId", saveSlotId);

        var result = cmd.ExecuteScalar();
        return result is string s && !string.IsNullOrWhiteSpace(s) ? s : null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Story Threads
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves a new story thread for narrative coherence tracking.
    /// </summary>
    public void SaveStoryThread(int saveSlotId, string type, string description, int chapter, string priority = "medium")
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO StoryThreads (SaveSlotId, ThreadType, Description, IntroducedChapter, Priority)
            VALUES ($slotId, $type, $desc, $chapter, $priority);
        ";
        cmd.Parameters.AddWithValue("$slotId", saveSlotId);
        cmd.Parameters.AddWithValue("$type", type ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$desc", description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$chapter", chapter);
        cmd.Parameters.AddWithValue("$priority", priority ?? "medium");
        cmd.ExecuteNonQuery();

        TouchSlotTimestamp(saveSlotId);
    }

    /// <summary>
    /// Loads all story threads for a save slot, ordered by priority and chapter.
    /// </summary>
    public List<StoryThreadData> LoadStoryThreads(int saveSlotId)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, SaveSlotId, ThreadType, Description, IntroducedChapter, Status, Priority
            FROM StoryThreads
            WHERE SaveSlotId = $slotId
            ORDER BY
                CASE Priority WHEN 'high' THEN 0 WHEN 'medium' THEN 1 WHEN 'low' THEN 2 ELSE 3 END,
                IntroducedChapter ASC;
        ";
        cmd.Parameters.AddWithValue("$slotId", saveSlotId);

        var threads = new List<StoryThreadData>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            threads.Add(new StoryThreadData
            {
                Id = reader.GetInt32(0),
                SaveSlotId = reader.GetInt32(1),
                ThreadType = reader.IsDBNull(2) ? null : reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                IntroducedChapter = reader.GetInt32(4),
                Status = reader.GetString(5),
                Priority = reader.GetString(6)
            });
        }

        return threads;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Conversation Memory
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves a memory entry for the LLM context management system.
    /// </summary>
    /// <param name="tier">One of "session", "campaign", or "permanent".</param>
    public void SaveMemory(int saveSlotId, string tier, string content)
    {
        if (string.IsNullOrWhiteSpace(tier))
            throw new ArgumentException("Memory tier cannot be empty.", nameof(tier));
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Memory content cannot be empty.", nameof(content));

        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ConversationMemory (SaveSlotId, MemoryTier, Content)
            VALUES ($slotId, $tier, $content);
        ";
        cmd.Parameters.AddWithValue("$slotId", saveSlotId);
        cmd.Parameters.AddWithValue("$tier", tier);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Loads all memory entries for a save slot filtered by tier,
    /// ordered oldest-first so the context window is built chronologically.
    /// </summary>
    public List<string> LoadMemories(int saveSlotId, string tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
            throw new ArgumentException("Memory tier cannot be empty.", nameof(tier));

        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Content
            FROM ConversationMemory
            WHERE SaveSlotId = $slotId AND MemoryTier = $tier
            ORDER BY CreatedAt ASC;
        ";
        cmd.Parameters.AddWithValue("$slotId", saveSlotId);
        cmd.Parameters.AddWithValue("$tier", tier);

        var memories = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            memories.Add(reader.GetString(0));
        }

        return memories;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Play Time
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds elapsed seconds to a save slot's play time counter.
    /// </summary>
    public void UpdatePlayTime(int saveSlotId, int additionalSeconds)
    {
        if (additionalSeconds <= 0)
            return;

        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE SaveSlots
            SET PlayTimeSeconds = PlayTimeSeconds + $seconds,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = $slotId;
        ";
        cmd.Parameters.AddWithValue("$seconds", additionalSeconds);
        cmd.Parameters.AddWithValue("$slotId", saveSlotId);
        cmd.ExecuteNonQuery();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Internal Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates the save slot's UpdatedAt timestamp to now.
    /// </summary>
    private void TouchSlotTimestamp(int saveSlotId)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE SaveSlots SET UpdatedAt = CURRENT_TIMESTAMP WHERE Id = $slotId;";
        cmd.Parameters.AddWithValue("$slotId", saveSlotId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Extracts CharacterName and CharacterLevel from the player character JSON
    /// and updates the save slot metadata (used for the slot selection UI).
    /// Falls back silently on parse errors — save slot display is non-critical.
    /// </summary>
    private void UpdateSlotCharacterInfo(int saveSlotId, string characterJson)
    {
        try
        {
            var obj = Newtonsoft.Json.Linq.JObject.Parse(characterJson);
            string? name = obj.Value<string>("name");
            int? level = obj.Value<int?>("level");

            if (name is null && level is null)
                return;

            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE SaveSlots
                SET CharacterName = COALESCE($name, CharacterName),
                    CharacterLevel = COALESCE($level, CharacterLevel)
                WHERE Id = $slotId;
            ";
            cmd.Parameters.AddWithValue("$slotId", saveSlotId);
            cmd.Parameters.AddWithValue("$name", name ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$level", level.HasValue ? level.Value : DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            GD.PrintErr($"[SaveManager] Failed to parse character JSON for slot metadata: {ex.Message}");
        }
    }
}
