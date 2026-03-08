using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ChroniclesUnbound.LLM;

/// <summary>
/// Parsed output from the narrative DM prompt. Contains the story text,
/// mechanical game events, companion reactions, and suggested player actions.
/// </summary>
public sealed class NarrativeResult
{
    /// <summary>The narrative prose to display to the player.</summary>
    public string NarrativeText { get; init; } = string.Empty;

    /// <summary>Structured game events (e.g. skill checks, item pickups, state changes).</summary>
    public List<JObject> GameEvents { get; init; } = new();

    /// <summary>Per-companion reaction data keyed by companion name.</summary>
    public Dictionary<string, JObject> CompanionReactions { get; init; } = new();

    /// <summary>Suggested player actions for the next turn.</summary>
    public List<string> SuggestedActions { get; init; } = new();
}
