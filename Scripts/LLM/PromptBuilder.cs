using System.Collections.Generic;

namespace ChroniclesUnbound.LLM;

/// <summary>
/// Constructs structured prompt message lists for each LLM pipeline stage.
/// All prompts enforce JSON output schemas so ResponseParser can reliably extract data.
/// </summary>
public static class PromptBuilder
{
    // ------------------------------------------------------------------
    // Narrative DM prompt
    // ------------------------------------------------------------------

    /// <summary>
    /// Build the main narrative Dungeon Master prompt that drives story progression.
    /// </summary>
    public static List<LLMMessage> BuildNarrativePrompt(
        string campaignContext,
        string recentHistory,
        string currentScene,
        string playerAction,
        string companionStates)
    {
        string systemPrompt =
            "You are a Dungeon Master for a D&D 5e campaign. " +
            "You narrate the world, describe consequences of player actions, and control NPCs. " +
            "Stay faithful to the established campaign context and characters.\n\n" +
            "RULES:\n" +
            "- Never take actions on behalf of the player or their companions.\n" +
            "- Keep narrative text vivid but concise (2-4 paragraphs).\n" +
            "- Suggest 2-4 possible player actions that make sense in context.\n" +
            "- If the action triggers a skill check, spell, or combat, include it in game_events.\n" +
            "- Companion reactions should reflect their personality and relationship state.\n\n" +
            "You MUST respond with ONLY a JSON object matching this exact schema:\n" +
            "{\n" +
            "  \"narrative_text\": \"<story prose describing what happens>\",\n" +
            "  \"game_events\": [\n" +
            "    { \"type\": \"<skill_check|combat_start|item_found|state_change|npc_action>\", " +
            "\"description\": \"...\", \"details\": {} }\n" +
            "  ],\n" +
            "  \"companion_reactions\": {\n" +
            "    \"<companion_name>\": { \"dialogue\": \"...\", \"emotion\": \"...\", \"action\": \"...\" }\n" +
            "  },\n" +
            "  \"suggested_actions\": [\"action 1\", \"action 2\", \"action 3\"]\n" +
            "}\n\n" +
            "Do not include any text outside the JSON object.";

        string userContent =
            $"=== CAMPAIGN CONTEXT ===\n{campaignContext}\n\n" +
            $"=== RECENT HISTORY ===\n{recentHistory}\n\n" +
            $"=== CURRENT SCENE ===\n{currentScene}\n\n" +
            $"=== COMPANION STATES ===\n{companionStates}\n\n" +
            $"=== PLAYER ACTION ===\n{playerAction}";

        return new List<LLMMessage>
        {
            new LLMMessage("system", systemPrompt),
            new LLMMessage("user", userContent)
        };
    }

    // ------------------------------------------------------------------
    // Companion dialogue prompt
    // ------------------------------------------------------------------

    /// <summary>
    /// Build a prompt for generating in-character companion dialogue.
    /// </summary>
    public static List<LLMMessage> BuildCompanionDialoguePrompt(
        string companionName,
        string personality,
        string situation,
        string relationshipState)
    {
        string systemPrompt =
            $"You are {companionName}, a D&D 5e companion character.\n\n" +
            $"PERSONALITY:\n{personality}\n\n" +
            $"RELATIONSHIP WITH PLAYER: {relationshipState}\n\n" +
            "Stay fully in character. Your dialogue should reflect your personality, " +
            "current mood, and how you feel about the player based on your relationship.\n\n" +
            "You MUST respond with ONLY a JSON object matching this exact schema:\n" +
            "{\n" +
            "  \"dialogue\": \"<what you say, in character>\",\n" +
            "  \"emotion\": \"<your current emotional state, e.g. amused, worried, angry, hopeful>\",\n" +
            "  \"opinion\": \"<your private thoughts about this situation>\"\n" +
            "}\n\n" +
            "Do not include any text outside the JSON object.";

        string userContent = $"SITUATION:\n{situation}";

        return new List<LLMMessage>
        {
            new LLMMessage("system", systemPrompt),
            new LLMMessage("user", userContent)
        };
    }

    // ------------------------------------------------------------------
    // Combat narration prompt
    // ------------------------------------------------------------------

    /// <summary>
    /// Build a prompt for narrating a combat action with dramatic flavor text.
    /// </summary>
    public static List<LLMMessage> BuildCombatNarrationPrompt(
        string combatState,
        string actionTaken,
        string result)
    {
        string systemPrompt =
            "You are a combat narrator for a D&D 5e tactical battle. " +
            "Describe combat actions with vivid, concise prose (1-2 sentences). " +
            "Base your narration on the mechanical result provided — do not invent new mechanics.\n\n" +
            "You MUST respond with ONLY a JSON object matching this exact schema:\n" +
            "{\n" +
            "  \"narration\": \"<dramatic description of the action and its outcome>\",\n" +
            "  \"mood\": \"<tension|triumph|desperation|calm|chaos>\",\n" +
            "  \"highlight\": \"<optional short text for UI emphasis, e.g. 'Critical Hit!' or null>\"\n" +
            "}\n\n" +
            "Do not include any text outside the JSON object.";

        string userContent =
            $"=== COMBAT STATE ===\n{combatState}\n\n" +
            $"=== ACTION TAKEN ===\n{actionTaken}\n\n" +
            $"=== MECHANICAL RESULT ===\n{result}";

        return new List<LLMMessage>
        {
            new LLMMessage("system", systemPrompt),
            new LLMMessage("user", userContent)
        };
    }
}
