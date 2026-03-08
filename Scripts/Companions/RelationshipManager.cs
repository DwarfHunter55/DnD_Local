using Godot;

namespace ChroniclesUnbound.Companions;

/// <summary>
/// Core manager tracking relationship scores and history between the player
/// and each companion character. Emits signals on tier changes for UI notifications
/// and gameplay system reactions.
///
/// Scores are clamped to [-100, +100]. Each companion starts at 0 (Neutral tier).
/// </summary>
public partial class RelationshipManager : Node
{
    // ── Signals ──────────────────────────────────────────────────────────

    /// <summary>
    /// Emitted when a companion's relationship tier changes (up or down).
    /// Parameters: companionId, previousTier (int), newTier (int).
    /// Cast int values to <see cref="RelationshipTier"/>.
    /// </summary>
    [Signal]
    public delegate void TierChangedEventHandler(
        string companionId, int previousTier, int newTier);

    /// <summary>
    /// Emitted whenever a relationship event is recorded, regardless of tier change.
    /// Parameters: companionId, scoreChange, newScore.
    /// </summary>
    [Signal]
    public delegate void ScoreChangedEventHandler(
        string companionId, int scoreChange, int newScore);

    // ── Constants ────────────────────────────────────────────────────────

    private const int MinScore = -100;
    private const int MaxScore = 100;
    private const int DefaultScore = 0;

    // ── State ────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-companion relationship scores, keyed by companion ID.
    /// </summary>
    private readonly Dictionary<string, int> _scores = new();

    /// <summary>
    /// Per-companion event histories, keyed by companion ID.
    /// Ordered chronologically (oldest first).
    /// </summary>
    private readonly Dictionary<string, List<RelationshipEvent>> _histories = new();

    // ── Singleton ────────────────────────────────────────────────────────

    public static RelationshipManager Instance { get; private set; }

    public override void _Ready()
    {
        Instance = this;
    }

    // ── Registration ─────────────────────────────────────────────────────

    /// <summary>
    /// Registers a companion for tracking. Call when a companion joins the party.
    /// If the companion is already registered, this is a no-op.
    /// Optionally accepts a starting score (defaults to 0 / Neutral).
    /// </summary>
    public void RegisterCompanion(string companionId, int startingScore = DefaultScore)
    {
        if (string.IsNullOrEmpty(companionId))
            return;

        if (_scores.ContainsKey(companionId))
            return;

        _scores[companionId] = Math.Clamp(startingScore, MinScore, MaxScore);
        _histories[companionId] = new List<RelationshipEvent>();
    }

    /// <summary>
    /// Removes a companion from tracking. Call when a companion permanently leaves.
    /// </summary>
    public void UnregisterCompanion(string companionId)
    {
        _scores.Remove(companionId);
        _histories.Remove(companionId);
    }

    /// <summary>
    /// Returns true if the companion is currently being tracked.
    /// </summary>
    public bool IsRegistered(string companionId)
    {
        return !string.IsNullOrEmpty(companionId) && _scores.ContainsKey(companionId);
    }

    // ── Core API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Records a relationship event for a companion. Applies the score change,
    /// clamps the result, stores the event in history, and emits signals.
    /// If the companion is not registered, the event is silently ignored.
    /// </summary>
    public void RecordEvent(string companionId, RelationshipEvent evt)
    {
        if (evt == null || !IsRegistered(companionId))
            return;

        int previousScore = _scores[companionId];
        var previousTier = RelationshipTierExtensions.TierFromScore(previousScore);

        int newScore = Math.Clamp(previousScore + evt.ScoreChange, MinScore, MaxScore);
        _scores[companionId] = newScore;

        var newTier = RelationshipTierExtensions.TierFromScore(newScore);

        // Stamp the resulting score onto the event for history display.
        evt.ResultingScore = newScore;
        _histories[companionId].Add(evt);

        // Always emit score changed.
        EmitSignal(SignalName.ScoreChanged, companionId, evt.ScoreChange, newScore);

        // Emit tier changed only when the tier actually transitions.
        if (previousTier != newTier)
        {
            EmitSignal(SignalName.TierChanged, companionId, (int)previousTier, (int)newTier);

            GD.Print($"[Relationship] {companionId}: tier changed from {previousTier} to {newTier} " +
                     $"(score {previousScore} -> {newScore})");
        }
    }

    /// <summary>
    /// Convenience method for recording a quick event without constructing the object manually.
    /// </summary>
    public void RecordEvent(string companionId, RelationshipEventType type, int scoreChange, string description)
    {
        RecordEvent(companionId, new RelationshipEvent
        {
            EventType = type,
            ScoreChange = scoreChange,
            Description = description,
            Timestamp = DateTime.UtcNow
        });
    }

    // ── Queries ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current relationship score for a companion, or 0 if not registered.
    /// </summary>
    public int GetScore(string companionId)
    {
        return _scores.TryGetValue(companionId, out int score) ? score : DefaultScore;
    }

    /// <summary>
    /// Returns the current relationship tier for a companion.
    /// Returns Neutral if the companion is not registered.
    /// </summary>
    public RelationshipTier GetTier(string companionId)
    {
        return RelationshipTierExtensions.TierFromScore(GetScore(companionId));
    }

    /// <summary>
    /// Returns the gameplay bonuses active for a companion's current tier.
    /// </summary>
    public RelationshipBonus GetBonuses(string companionId)
    {
        return RelationshipBonuses.GetBonusesForTier(GetTier(companionId));
    }

    /// <summary>
    /// Returns the full event history for a companion, ordered chronologically.
    /// Returns an empty list if the companion is not registered.
    /// </summary>
    public IReadOnlyList<RelationshipEvent> GetHistory(string companionId)
    {
        return _histories.TryGetValue(companionId, out var history)
            ? history.AsReadOnly()
            : Array.Empty<RelationshipEvent>();
    }

    /// <summary>
    /// Returns the most recent N events for a companion.
    /// Useful for LLM context injection (recent relationship events inform dialogue tone).
    /// </summary>
    public List<RelationshipEvent> GetRecentHistory(string companionId, int count = 5)
    {
        if (!_histories.TryGetValue(companionId, out var history) || history.Count == 0)
            return new List<RelationshipEvent>();

        int start = Math.Max(0, history.Count - count);
        return history.GetRange(start, history.Count - start);
    }

    /// <summary>
    /// Returns all registered companion IDs.
    /// </summary>
    public IEnumerable<string> GetAllCompanionIds()
    {
        return _scores.Keys;
    }

    /// <summary>
    /// Returns all companions at or above the specified tier.
    /// Useful for checking "which companions are loyal enough for X".
    /// </summary>
    public List<string> GetCompanionsAtOrAboveTier(RelationshipTier minimumTier)
    {
        var result = new List<string>();
        foreach (var kvp in _scores)
        {
            if (RelationshipTierExtensions.TierFromScore(kvp.Value) >= minimumTier)
                result.Add(kvp.Key);
        }
        return result;
    }

    // ── Direct Score Manipulation ────────────────────────────────────────

    /// <summary>
    /// Sets a companion's score to an exact value. Use sparingly -- prefer RecordEvent
    /// for normal gameplay so that history is tracked. Useful for save/load or debug.
    /// Emits TierChanged if applicable, but does NOT record a history event.
    /// </summary>
    public void SetScore(string companionId, int score)
    {
        if (!IsRegistered(companionId))
            return;

        var previousTier = GetTier(companionId);
        _scores[companionId] = Math.Clamp(score, MinScore, MaxScore);
        var newTier = GetTier(companionId);

        if (previousTier != newTier)
            EmitSignal(SignalName.TierChanged, companionId, (int)previousTier, (int)newTier);
    }

    // ── Serialization Helpers ────────────────────────────────────────────

    /// <summary>
    /// Returns the full state as dictionaries for save/load serialization.
    /// </summary>
    public (Dictionary<string, int> Scores, Dictionary<string, List<RelationshipEvent>> Histories) GetState()
    {
        return (new Dictionary<string, int>(_scores),
                new Dictionary<string, List<RelationshipEvent>>(_histories));
    }

    /// <summary>
    /// Restores state from saved data. Clears existing state first.
    /// </summary>
    public void RestoreState(
        Dictionary<string, int> scores,
        Dictionary<string, List<RelationshipEvent>> histories)
    {
        _scores.Clear();
        _histories.Clear();

        if (scores != null)
        {
            foreach (var kvp in scores)
                _scores[kvp.Key] = Math.Clamp(kvp.Value, MinScore, MaxScore);
        }

        if (histories != null)
        {
            foreach (var kvp in histories)
                _histories[kvp.Key] = new List<RelationshipEvent>(kvp.Value);
        }

        // Ensure every scored companion has a history list.
        foreach (var key in _scores.Keys)
        {
            if (!_histories.ContainsKey(key))
                _histories[key] = new List<RelationshipEvent>();
        }
    }

    /// <summary>
    /// Clears all relationship data. Used when starting a new game.
    /// </summary>
    public void Reset()
    {
        _scores.Clear();
        _histories.Clear();
    }
}
