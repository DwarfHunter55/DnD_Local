using Godot;
using Microsoft.Data.Sqlite;

namespace ChroniclesUnbound.Data;

/// <summary>
/// Manages the SQLite database connection and schema initialization.
/// Owns the connection lifecycle — consumers obtain connections via <see cref="GetConnection"/>.
/// Implements IDisposable for deterministic cleanup.
/// </summary>
public class DatabaseManager : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// Creates a new DatabaseManager targeting the specified database file.
    /// The path is relative to user:// (Godot's persistent data directory).
    /// </summary>
    /// <param name="dbPath">
    /// Relative path for the database file. Defaults to "saves/game.db".
    /// The directory will be created if it does not exist.
    /// </param>
    public DatabaseManager(string dbPath = "saves/game.db")
    {
        // Resolve to an absolute path inside Godot's user data directory.
        string userDir = ProjectSettings.GlobalizePath("user://");
        string fullPath = System.IO.Path.Combine(userDir, dbPath);

        // Ensure the directory exists before SQLite tries to create the file.
        string? directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
            GD.Print($"[DatabaseManager] Created directory: {directory}");
        }

        _connectionString = $"Data Source={fullPath}";
        GD.Print($"[DatabaseManager] Database path: {fullPath}");
    }

    /// <summary>
    /// Returns an open connection to the database.
    /// Reuses a single connection for the lifetime of this manager
    /// (SQLite is single-writer anyway; one connection is sufficient for a single-player game).
    /// </summary>
    public SqliteConnection GetConnection()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DatabaseManager));

        if (_connection is not null)
            return _connection;

        _connection = new SqliteConnection(_connectionString);
        _connection.Open();

        // Enable WAL mode for better read performance and crash resilience.
        using var walCmd = _connection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
        walCmd.ExecuteNonQuery();

        // Enable foreign key enforcement (off by default in SQLite).
        using var fkCmd = _connection.CreateCommand();
        fkCmd.CommandText = "PRAGMA foreign_keys=ON;";
        fkCmd.ExecuteNonQuery();

        return _connection;
    }

    /// <summary>
    /// Creates all required tables if they do not already exist.
    /// Safe to call multiple times.
    /// </summary>
    public void InitializeDatabase()
    {
        var conn = GetConnection();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS SaveSlots (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SlotName        TEXT    NOT NULL,
                CharacterName   TEXT,
                CharacterLevel  INTEGER DEFAULT 1,
                PlayTimeSeconds INTEGER DEFAULT 0,
                CreatedAt       TEXT    DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt       TEXT    DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS Characters (
                Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                SaveSlotId         INTEGER NOT NULL,
                CharacterJson      TEXT    NOT NULL,
                IsPlayerCharacter  INTEGER DEFAULT 0,
                FOREIGN KEY (SaveSlotId) REFERENCES SaveSlots(Id)
            );

            CREATE TABLE IF NOT EXISTS GameState (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                SaveSlotId       INTEGER NOT NULL,
                CurrentPhase     TEXT,
                CurrentLocation  TEXT,
                CampaignDataJson TEXT,
                FOREIGN KEY (SaveSlotId) REFERENCES SaveSlots(Id)
            );

            CREATE TABLE IF NOT EXISTS StoryThreads (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                SaveSlotId        INTEGER NOT NULL,
                ThreadType        TEXT,
                Description       TEXT,
                IntroducedChapter INTEGER,
                Status            TEXT DEFAULT 'active',
                Priority          TEXT DEFAULT 'medium',
                FOREIGN KEY (SaveSlotId) REFERENCES SaveSlots(Id)
            );

            CREATE TABLE IF NOT EXISTS ConversationMemory (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                SaveSlotId INTEGER NOT NULL,
                MemoryTier TEXT    NOT NULL,
                Content    TEXT    NOT NULL,
                CreatedAt  TEXT    DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SaveSlotId) REFERENCES SaveSlots(Id)
            );
        ";
        cmd.ExecuteNonQuery();

        GD.Print("[DatabaseManager] Database schema initialized.");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }

        _disposed = true;
        GD.Print("[DatabaseManager] Disposed.");
    }

    ~DatabaseManager()
    {
        Dispose(false);
    }
}
