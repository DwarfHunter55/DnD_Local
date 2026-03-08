using System.Collections.Generic;
using ChroniclesUnbound.LLM;

namespace ChroniclesUnbound.Companions;

/// <summary>
/// Builds LLM prompt message lists for companion interactions using YAML templates.
/// Each method loads the appropriate template, fills placeholders with companion data,
/// and returns a List&lt;LLMMessage&gt; ready to send to LLMService.
/// </summary>
public class CompanionPromptBuilder
{
    private readonly PromptTemplateLoader _loader;

    public CompanionPromptBuilder(PromptTemplateLoader loader = null)
    {
        _loader = loader ?? new PromptTemplateLoader();
    }

    // ------------------------------------------------------------------
    // Dialogue — player-initiated conversation with a companion
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds a prompt for exploration dialogue between the player and a companion.
    /// Uses companion_dialogue.yaml template.
    /// </summary>
    /// <param name="personality">The companion's personality data.</param>
    /// <param name="situation">Current scene/situation description.</param>
    /// <param name="playerInput">What the player said or did to initiate dialogue.</param>
    /// <param name="relationshipTier">Relationship tier name (e.g. "Friendly", "Hostile").</param>
    /// <param name="recentContext">Optional recent conversation context for continuity.</param>
    /// <returns>Message list for LLM, or null if template is missing.</returns>
    public List<LLMMessage> BuildDialoguePrompt(
        CompanionPersonality personality,
        string situation,
        string playerInput,
        string relationshipTier = "Neutral",
        string recentContext = "")
    {
        var template = _loader.LoadTemplate("companion_dialogue");
        if (template == null) return null;

        string system = FillCompanionPlaceholders(template.SystemPrompt, personality);
        system = system.Replace("{relationship_tier}", relationshipTier);
        system = system.Replace("{recent_context}", string.IsNullOrEmpty(recentContext)
            ? "No prior conversation."
            : recentContext);

        string user = template.UserPrompt
            .Replace("{situation}", $"{situation}\n\nPLAYER SAYS:\n{playerInput}");

        return new List<LLMMessage>
        {
            new LLMMessage("system", system),
            new LLMMessage("user", user)
        };
    }

    // ------------------------------------------------------------------
    // Combat reasoning — companion explains their chosen action
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds a prompt for a companion to explain their combat decision in character.
    /// Uses combat_reasoning.yaml template.
    /// </summary>
    /// <param name="personality">The companion's personality data.</param>
    /// <param name="combatSituation">Description of current battle state (positions, HP, threats).</param>
    /// <param name="chosenAction">The action the AI selected (e.g. "Cast Fireball at goblin cluster").</param>
    /// <param name="tacticalReasoning">The utility AI's reasoning for the choice.</param>
    /// <returns>Message list for LLM, or null if template is missing.</returns>
    public List<LLMMessage> BuildCombatReasoningPrompt(
        CompanionPersonality personality,
        string combatSituation,
        string chosenAction = "",
        string tacticalReasoning = "")
    {
        var template = _loader.LoadTemplate("combat_reasoning");
        if (template == null) return null;

        string system = FillCompanionPlaceholders(template.SystemPrompt, personality);

        string user = template.UserPrompt
            .Replace("{battle_state}", combatSituation)
            .Replace("{chosen_action}", string.IsNullOrEmpty(chosenAction)
                ? "Not yet decided"
                : chosenAction)
            .Replace("{reasoning}", string.IsNullOrEmpty(tacticalReasoning)
                ? "Standard tactical assessment"
                : tacticalReasoning);

        return new List<LLMMessage>
        {
            new LLMMessage("system", system),
            new LLMMessage("user", user)
        };
    }

    // ------------------------------------------------------------------
    // Reaction — companion reacts to a player action or event
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds a prompt for a companion to react to a player action or world event.
    /// Uses companion_reaction.yaml template.
    /// </summary>
    /// <param name="personality">The companion's personality data.</param>
    /// <param name="event">Description of what happened that the companion is reacting to.</param>
    /// <param name="relationshipTier">Relationship tier name (e.g. "Friendly", "Hostile").</param>
    /// <param name="situation">Current situation context.</param>
    /// <returns>Message list for LLM, or null if template is missing.</returns>
    public List<LLMMessage> BuildReactionPrompt(
        CompanionPersonality personality,
        string @event,
        string relationshipTier = "Neutral",
        string situation = "")
    {
        var template = _loader.LoadTemplate("companion_reaction");
        if (template == null) return null;

        string system = FillCompanionPlaceholders(template.SystemPrompt, personality);
        system = system.Replace("{player_action}", @event);
        system = system.Replace("{relationship_tier}", relationshipTier);
        system = system.Replace("{situation}", string.IsNullOrEmpty(situation)
            ? "General exploration"
            : situation);

        string user = template.UserPrompt;

        return new List<LLMMessage>
        {
            new LLMMessage("system", system),
            new LLMMessage("user", user)
        };
    }

    // ------------------------------------------------------------------
    // Banter — two companions talking to each other
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds a prompt for generating a short exchange between two companions.
    /// Uses companion_banter.yaml template.
    /// </summary>
    /// <param name="personality">First companion's personality data.</param>
    /// <param name="otherCompanion">Second companion's personality data.</param>
    /// <param name="situation">Current situation or trigger for the banter.</param>
    /// <returns>Message list for LLM, or null if template is missing.</returns>
    public List<LLMMessage> BuildBanterPrompt(
        CompanionPersonality personality,
        CompanionPersonality otherCompanion,
        string situation = "Idle moment during travel")
    {
        var template = _loader.LoadTemplate("companion_banter");
        if (template == null) return null;

        // Fill companion 1 placeholders
        string system = template.SystemPrompt
            .Replace("{name1}", personality.Name)
            .Replace("{race1}", personality.Race)
            .Replace("{class1}", personality.Class)
            .Replace("{personality_summary1}", personality.ToPromptSummary());

        // Fill companion 2 placeholders
        system = system
            .Replace("{name2}", otherCompanion.Name)
            .Replace("{race2}", otherCompanion.Race)
            .Replace("{class2}", otherCompanion.Class)
            .Replace("{personality_summary2}", otherCompanion.ToPromptSummary());

        // Fill inter-companion attitudes
        string attitude1To2 = personality.AttitudeTowardCompanions
            .TryGetValue(otherCompanion.Name, out string att1) ? att1 : "No strong feelings";
        string attitude2To1 = otherCompanion.AttitudeTowardCompanions
            .TryGetValue(personality.Name, out string att2) ? att2 : "No strong feelings";

        system = system
            .Replace("{attitude1_toward_2}", attitude1To2)
            .Replace("{attitude2_toward_1}", attitude2To1);

        string user = template.UserPrompt
            .Replace("{situation}", situation);

        return new List<LLMMessage>
        {
            new LLMMessage("system", system),
            new LLMMessage("user", user)
        };
    }

    // ------------------------------------------------------------------
    // Template metadata accessors
    // ------------------------------------------------------------------

    /// <summary>
    /// Gets the recommended temperature for a given template.
    /// Returns a sensible default (0.7) if the template is not found.
    /// </summary>
    public float GetTemperature(string templateName)
    {
        var template = _loader.LoadTemplate(templateName);
        return template?.Temperature ?? 0.7f;
    }

    /// <summary>
    /// Gets the recommended max tokens for a given template.
    /// Returns a sensible default (256) if the template is not found.
    /// </summary>
    public int GetMaxTokens(string templateName)
    {
        var template = _loader.LoadTemplate(templateName);
        return template?.MaxTokens ?? 256;
    }

    // ------------------------------------------------------------------
    // Placeholder helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Replaces common single-companion placeholders: {name}, {race}, {class}, {personality_summary}.
    /// </summary>
    private static string FillCompanionPlaceholders(string text, CompanionPersonality personality)
    {
        return text
            .Replace("{name}", personality.Name)
            .Replace("{race}", personality.Race)
            .Replace("{class}", personality.Class)
            .Replace("{personality_summary}", personality.ToPromptSummary());
    }
}
