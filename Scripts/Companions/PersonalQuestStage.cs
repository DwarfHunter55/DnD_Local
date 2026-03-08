using Newtonsoft.Json;

namespace ChroniclesUnbound.Companions;

/// <summary>
/// Tracks a companion's personal quest — a multi-stage narrative arc
/// tied to their backstory and unlocked through relationship progression.
/// </summary>
public class PersonalQuest
{
    [JsonProperty("questName")]
    public string QuestName { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Current stage of the quest. 0 = not started, 1 through MaxStages = in progress/complete.
    /// </summary>
    [JsonProperty("currentStage")]
    public int CurrentStage { get; set; }

    /// <summary>
    /// Total number of stages in this quest. Defaults to 3 for a standard three-act arc.
    /// </summary>
    [JsonProperty("maxStages")]
    public int MaxStages { get; set; } = 3;

    /// <summary>
    /// Description text for each stage, indexed 0 through MaxStages-1.
    /// </summary>
    [JsonProperty("stageDescriptions")]
    public List<string> StageDescriptions { get; set; } = new();

    /// <summary>
    /// True when the companion has completed all stages of their personal quest.
    /// </summary>
    [JsonIgnore]
    public bool IsComplete => CurrentStage >= MaxStages;

    /// <summary>
    /// How the quest ended — populated on completion. May vary based on player choices.
    /// </summary>
    [JsonProperty("resolution")]
    public string Resolution { get; set; } = string.Empty;

    // ── Trigger Conditions ───────────────────────────────────────────

    /// <summary>
    /// A description of what advances this quest to the next stage
    /// (e.g. "Visit the ruins of Thornfield", "Defeat the betrayer").
    /// Evaluated by the LLM narrative system or explicit game events.
    /// </summary>
    [JsonProperty("triggerCondition")]
    public string TriggerCondition { get; set; } = string.Empty;

    /// <summary>
    /// Minimum relationship tier required to unlock this quest.
    /// Maps to relationship tiers: 0=Hostile, 1=Cold, 2=Neutral, 3=Friendly, 4=Close, 5=Devoted.
    /// Defaults to 3 (Friendly) — the companion must trust the player enough to share.
    /// </summary>
    [JsonProperty("requiredRelationshipTier")]
    public int RequiredRelationshipTier { get; set; } = 3;

    // ── Methods ──────────────────────────────────────────────────────

    /// <summary>
    /// Advances the quest to the next stage if not already complete.
    /// Returns true if the quest advanced, false if already at max.
    /// </summary>
    public bool AdvanceStage()
    {
        if (IsComplete)
            return false;

        CurrentStage++;
        return true;
    }

    /// <summary>
    /// Returns the description for the current stage, or empty string if out of range.
    /// </summary>
    [JsonIgnore]
    public string CurrentStageDescription
    {
        get
        {
            int index = CurrentStage - 1;
            if (index >= 0 && index < StageDescriptions.Count)
                return StageDescriptions[index];
            return string.Empty;
        }
    }
}
