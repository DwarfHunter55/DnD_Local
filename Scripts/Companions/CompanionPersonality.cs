using System.Text;
using Newtonsoft.Json;

namespace ChroniclesUnbound.Companions;

/// <summary>
/// LLM-facing personality data for a companion character.
/// Used to build system prompts that drive the companion's dialogue,
/// reactions, and narrative behavior through the local LLM.
/// </summary>
public class CompanionPersonality
{
    // ── Identity ─────────────────────────────────────────────────────

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("race")]
    public string Race { get; set; } = string.Empty;

    [JsonProperty("class")]
    public string Class { get; set; } = string.Empty;

    // ── D&D Personality (mirrors Character sheet fields) ─────────────

    [JsonProperty("personalityTraits")]
    public List<string> PersonalityTraits { get; set; } = new();

    [JsonProperty("ideal")]
    public string Ideal { get; set; } = string.Empty;

    [JsonProperty("bond")]
    public string Bond { get; set; } = string.Empty;

    [JsonProperty("flaw")]
    public string Flaw { get; set; } = string.Empty;

    // ── LLM-Specific Personality ────────────────────────────────────

    /// <summary>
    /// Describes how the companion speaks (e.g. "Speaks formally, uses archaic words").
    /// </summary>
    [JsonProperty("speechPattern")]
    public string SpeechPattern { get; set; } = string.Empty;

    /// <summary>
    /// A recurring verbal quirk (e.g. "Ends sentences with 'indeed'").
    /// </summary>
    [JsonProperty("verbalTic")]
    public string VerbalTic { get; set; } = string.Empty;

    /// <summary>
    /// The companion's driving motivation (e.g. "Find her lost sister").
    /// </summary>
    [JsonProperty("coreMotivation")]
    public string CoreMotivation { get; set; } = string.Empty;

    /// <summary>
    /// A secret the companion keeps, typically tied to their personal quest.
    /// </summary>
    [JsonProperty("secret")]
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// How the companion initially feels about the player character.
    /// </summary>
    [JsonProperty("attitudeTowardPlayer")]
    public string AttitudeTowardPlayer { get; set; } = string.Empty;

    /// <summary>
    /// Full backstory text for deep LLM context when needed.
    /// </summary>
    [JsonProperty("backstory")]
    public string Backstory { get; set; } = string.Empty;

    // ── Inter-Companion Relationships ───────────────────────────────

    /// <summary>
    /// Attitudes toward other companions, keyed by companion name.
    /// Populated during party generation (e.g. "Thorin" -> "Distrusts him due to old grudge").
    /// </summary>
    [JsonProperty("attitudeTowardCompanions")]
    public Dictionary<string, string> AttitudeTowardCompanions { get; set; } = new();

    // ── Prompt Generation ───────────────────────────────────────────

    /// <summary>
    /// Generates a concise personality summary for injection into LLM system prompts.
    /// Keeps the output tight to conserve context window tokens.
    /// </summary>
    public string ToPromptSummary()
    {
        var sb = new StringBuilder();
        sb.Append($"Name: {Name} | Race: {Race} | Class: {Class}");

        if (PersonalityTraits.Count > 0)
            sb.Append($"\nTraits: {string.Join("; ", PersonalityTraits)}");

        if (!string.IsNullOrEmpty(Ideal))
            sb.Append($"\nIdeal: {Ideal}");

        if (!string.IsNullOrEmpty(Bond))
            sb.Append($"\nBond: {Bond}");

        if (!string.IsNullOrEmpty(Flaw))
            sb.Append($"\nFlaw: {Flaw}");

        if (!string.IsNullOrEmpty(CoreMotivation))
            sb.Append($"\nMotivation: {CoreMotivation}");

        if (!string.IsNullOrEmpty(SpeechPattern))
            sb.Append($"\nSpeech: {SpeechPattern}");

        if (!string.IsNullOrEmpty(VerbalTic))
            sb.Append($"\nVerbal tic: {VerbalTic}");

        if (!string.IsNullOrEmpty(AttitudeTowardPlayer))
            sb.Append($"\nAttitude toward player: {AttitudeTowardPlayer}");

        if (AttitudeTowardCompanions.Count > 0)
        {
            sb.Append("\nAttitudes toward companions:");
            foreach (var kvp in AttitudeTowardCompanions)
                sb.Append($"\n  - {kvp.Key}: {kvp.Value}");
        }

        return sb.ToString();
    }
}
