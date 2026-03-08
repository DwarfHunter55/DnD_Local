using ChroniclesUnbound.Data;
using Newtonsoft.Json;

namespace ChroniclesUnbound.Companions;

/// <summary>
/// AI personality and behavior profile attached to companion characters.
/// Drives the utility-based combat AI scoring and exploration behavior.
/// All personality scores use a 1-10 scale where 5 is baseline/average.
/// </summary>
public class CompanionAIProfile
{
    // ── Combat Personality Scores (1-10) ─────────────────────────────

    /// <summary>
    /// How eagerly the companion engages enemies.
    /// Low = hangs back, high = charges in first.
    /// </summary>
    [JsonProperty("aggression")]
    public int Aggression { get; set; } = 5;

    /// <summary>
    /// How cautious and defensive the companion plays.
    /// Low = reckless, high = retreats when wounded.
    /// </summary>
    [JsonProperty("selfPreservation")]
    public int SelfPreservation { get; set; } = 5;

    /// <summary>
    /// How often the companion supports allies over dealing damage.
    /// Low = selfish fighter, high = prioritizes healing/buffing/flanking.
    /// </summary>
    [JsonProperty("teamwork")]
    public int Teamwork { get; set; } = 5;

    /// <summary>
    /// Willingness to use the environment, improvised tactics, or unusual actions.
    /// Low = sticks to basic attacks, high = shoves enemies off ledges.
    /// </summary>
    [JsonProperty("creativity")]
    public int Creativity { get; set; } = 5;

    // ── Targeting ────────────────────────────────────────────────────

    /// <summary>
    /// Which enemies this companion prefers to target in combat.
    /// </summary>
    [JsonProperty("preferredTargets")]
    public TargetPreference PreferredTargets { get; set; } = TargetPreference.Nearest;

    /// <summary>
    /// Overall combat tactic from the D&D 5e tactic enum (Aggressive, Defensive, Support, Balanced, Ranged).
    /// </summary>
    [JsonProperty("tacticPreference")]
    public CombatTacticPreference TacticPreference { get; set; } = CombatTacticPreference.Balanced;

    // ── Autonomy ─────────────────────────────────────────────────────

    /// <summary>
    /// How much control the player has over this companion's actions.
    /// Defaults to Suggest: AI proposes, player approves.
    /// </summary>
    [JsonProperty("autonomy")]
    public CompanionAutonomy Autonomy { get; set; } = CompanionAutonomy.Suggest;

    // ── Validation ───────────────────────────────────────────────────

    /// <summary>
    /// Clamps all personality scores to the valid 1-10 range.
    /// Call after deserialization or manual edits.
    /// </summary>
    public void ClampScores()
    {
        Aggression = Math.Clamp(Aggression, 1, 10);
        SelfPreservation = Math.Clamp(SelfPreservation, 1, 10);
        Teamwork = Math.Clamp(Teamwork, 1, 10);
        Creativity = Math.Clamp(Creativity, 1, 10);
    }
}
