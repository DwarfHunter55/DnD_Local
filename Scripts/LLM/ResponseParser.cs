using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChroniclesUnbound.LLM;

/// <summary>
/// Validates and extracts structured data from LLM JSON responses.
/// Designed to be lenient — returns partial data or sensible defaults
/// rather than throwing on malformed output.
/// </summary>
public static class ResponseParser
{
    /// <summary>
    /// Parse a narrative DM response into a <see cref="NarrativeResult"/>.
    /// </summary>
    public static NarrativeResult ParseNarrativeResponse(LLMResponse response)
    {
        if (!response.IsSuccess)
        {
            GD.PrintErr($"[ResponseParser] Cannot parse failed response: {response.ErrorMessage}");
            return FallbackNarrative(response.ErrorMessage ?? "Unknown error");
        }

        var json = response.ParsedJson ?? TryExtractJson(response.RawText);
        if (json == null)
        {
            GD.PrintErr("[ResponseParser] No valid JSON found in narrative response.");
            return FallbackNarrative(response.RawText);
        }

        string narrativeText = json["narrative_text"]?.ToString() ?? response.RawText;

        var gameEvents = new List<JObject>();
        if (json["game_events"] is JArray eventsArray)
        {
            foreach (var item in eventsArray)
            {
                if (item is JObject eventObj)
                    gameEvents.Add(eventObj);
            }
        }

        var companionReactions = new Dictionary<string, JObject>();
        if (json["companion_reactions"] is JObject reactionsObj)
        {
            foreach (var prop in reactionsObj.Properties())
            {
                if (prop.Value is JObject reactionData)
                    companionReactions[prop.Name] = reactionData;
            }
        }

        var suggestedActions = new List<string>();
        if (json["suggested_actions"] is JArray actionsArray)
        {
            foreach (var item in actionsArray)
            {
                string? action = item?.ToString();
                if (!string.IsNullOrWhiteSpace(action))
                    suggestedActions.Add(action);
            }
        }

        return new NarrativeResult
        {
            NarrativeText = narrativeText,
            GameEvents = gameEvents,
            CompanionReactions = companionReactions,
            SuggestedActions = suggestedActions
        };
    }

    /// <summary>
    /// Parse a companion dialogue response into a <see cref="CompanionDialogueResult"/>.
    /// </summary>
    public static CompanionDialogueResult ParseCompanionResponse(LLMResponse response)
    {
        if (!response.IsSuccess)
        {
            GD.PrintErr($"[ResponseParser] Cannot parse failed response: {response.ErrorMessage}");
            return new CompanionDialogueResult
            {
                Dialogue = "...",
                Emotion = "neutral",
                Opinion = string.Empty
            };
        }

        var json = response.ParsedJson ?? TryExtractJson(response.RawText);
        if (json == null)
        {
            GD.PrintErr("[ResponseParser] No valid JSON found in companion response.");
            // Fall back to treating raw text as dialogue.
            return new CompanionDialogueResult
            {
                Dialogue = response.RawText.Trim(),
                Emotion = "neutral",
                Opinion = string.Empty
            };
        }

        return new CompanionDialogueResult
        {
            Dialogue = json["dialogue"]?.ToString() ?? response.RawText.Trim(),
            Emotion = json["emotion"]?.ToString() ?? "neutral",
            Opinion = json["opinion"]?.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Attempt to find and parse a JSON object in arbitrary text.
    /// Handles common LLM quirks: markdown code fences, leading/trailing prose, etc.
    /// </summary>
    public static JObject? TryExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Fast path: entire string is valid JSON.
        try
        {
            return JObject.Parse(text);
        }
        catch (JsonReaderException)
        {
            // Expected — fall through to extraction strategies.
        }

        // Strip markdown code fences (```json ... ``` or ``` ... ```).
        var fenceMatch = Regex.Match(text, @"```(?:json)?\s*\n?(.*?)\n?\s*```", RegexOptions.Singleline);
        if (fenceMatch.Success)
        {
            try
            {
                return JObject.Parse(fenceMatch.Groups[1].Value);
            }
            catch (JsonReaderException)
            {
                // Fence content wasn't valid JSON either — continue.
            }
        }

        // Find the outermost { ... } pair using brace counting.
        int depth = 0;
        int start = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{')
            {
                if (depth == 0)
                    start = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    string candidate = text.Substring(start, i - start + 1);
                    try
                    {
                        return JObject.Parse(candidate);
                    }
                    catch (JsonReaderException)
                    {
                        // Braces didn't contain valid JSON — reset and keep scanning.
                        start = -1;
                    }
                }
            }
        }

        return null;
    }

    // ----------------------------------------------------------------
    // Fallbacks
    // ----------------------------------------------------------------

    /// <summary>
    /// Create a minimal NarrativeResult when parsing fails, using whatever text is available.
    /// </summary>
    private static NarrativeResult FallbackNarrative(string rawText)
    {
        return new NarrativeResult
        {
            NarrativeText = string.IsNullOrWhiteSpace(rawText)
                ? "The Dungeon Master pauses, collecting their thoughts..."
                : rawText,
            GameEvents = new List<JObject>(),
            CompanionReactions = new Dictionary<string, JObject>(),
            SuggestedActions = new List<string> { "Look around", "Wait", "Continue forward" }
        };
    }
}
