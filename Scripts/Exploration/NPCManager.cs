using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ChroniclesUnbound.LLM;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// NPC interaction manager. Loads NPC data, tracks conversation history,
/// and generates personality-aware dialogue via the LLM with graceful fallbacks.
///
/// Singleton pattern matching other managers. Register as autoload or add to
/// scene tree; access via NPCManager.Instance.
/// </summary>
public partial class NPCManager : Node
{
    public static NPCManager? Instance { get; private set; }

    // -----------------------------------------------------------------
    // Signals
    // -----------------------------------------------------------------

    [Signal]
    public delegate void DialogueReceivedEventHandler(string npcId, string speakerName, string responseText);

    [Signal]
    public delegate void QuestDialogueDetectedEventHandler(string npcId, string questId);

    [Signal]
    public delegate void ShopDialogueDetectedEventHandler(string npcId, string shopId);

    // -----------------------------------------------------------------
    // Configuration
    // -----------------------------------------------------------------

    /// <summary>Maximum conversation history entries kept per NPC.</summary>
    private const int MaxConversationHistory = 5;

    // -----------------------------------------------------------------
    // State
    // -----------------------------------------------------------------

    /// <summary>All known NPCs keyed by npc_id.</summary>
    private readonly Dictionary<string, NPCData> _npcs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-NPC conversation history. Each entry is a (playerInput, npcResponse) pair.
    /// Capped at MaxConversationHistory entries per NPC for context window management.
    /// </summary>
    private readonly Dictionary<string, List<ConversationExchange>> _conversationHistory = new();

    /// <summary>The NPC currently being spoken to, or null.</summary>
    public NPCData? ActiveNPC { get; private set; }

    // -----------------------------------------------------------------
    // Dependencies
    // -----------------------------------------------------------------

    private LLMService? _llmService;

    // -----------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[NPCManager] Initialized.");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Inject the LLM service for dialogue generation.
    /// Call after the node enters the tree and the LLM service is available.
    /// </summary>
    public void Initialize(LLMService llmService)
    {
        _llmService = llmService;
        GD.Print("[NPCManager] LLM service injected.");
    }

    // -----------------------------------------------------------------
    // Data loading
    // -----------------------------------------------------------------

    /// <summary>
    /// Load NPC definitions from a JSON file. Call once during game initialization.
    /// </summary>
    public void LoadNPCs(string npcsJsonPath = "res://Data/World/npcs.json")
    {
        string resolvedPath = ResolveResPath(npcsJsonPath);

        if (!Godot.FileAccess.FileExists(resolvedPath) && !System.IO.File.Exists(resolvedPath))
        {
            GD.PrintErr($"[NPCManager] NPCs file not found: {resolvedPath}");
            return;
        }

        string json;
        if (resolvedPath.StartsWith("res://"))
        {
            using var file = Godot.FileAccess.Open(resolvedPath, Godot.FileAccess.ModeFlags.Read);
            json = file.GetAsText();
        }
        else
        {
            json = System.IO.File.ReadAllText(resolvedPath);
        }

        var npcs = JsonConvert.DeserializeObject<List<NPCData>>(json);
        if (npcs == null)
        {
            GD.PrintErr($"[NPCManager] Failed to deserialize NPCs from: {resolvedPath}");
            return;
        }

        _npcs.Clear();
        foreach (var npc in npcs)
        {
            _npcs[npc.NpcId] = npc;
        }

        GD.Print($"[NPCManager] Loaded {_npcs.Count} NPCs.");
    }

    // -----------------------------------------------------------------
    // NPC access
    // -----------------------------------------------------------------

    /// <summary>
    /// Returns the NPC data for the given ID, or null if not found.
    /// </summary>
    public NPCData? GetNPC(string npcId)
    {
        if (string.IsNullOrEmpty(npcId))
            return null;

        _npcs.TryGetValue(npcId, out var npc);
        return npc;
    }

    /// <summary>
    /// Returns all NPCs at the specified location.
    /// </summary>
    public List<NPCData> GetNPCsAtLocation(string locationId)
    {
        if (string.IsNullOrEmpty(locationId))
            return new List<NPCData>();

        return _npcs.Values
            .Where(n => string.Equals(n.LocationId, locationId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Returns all loaded NPCs.
    /// </summary>
    public IReadOnlyDictionary<string, NPCData> GetAllNPCs() => _npcs;

    // -----------------------------------------------------------------
    // Dialogue session management
    // -----------------------------------------------------------------

    /// <summary>
    /// Begin a dialogue session with an NPC. Sets them as the active NPC
    /// and returns their greeting as an initial result.
    /// </summary>
    public NPCDialogueResult? BeginDialogue(string npcId)
    {
        var npc = GetNPC(npcId);
        if (npc == null)
        {
            GD.PrintErr($"[NPCManager] Cannot begin dialogue: NPC '{npcId}' not found.");
            return null;
        }

        ActiveNPC = npc;
        GD.Print($"[NPCManager] Dialogue started with {npc.DisplayName}.");

        return new NPCDialogueResult
        {
            SpeakerName = npc.DisplayName,
            ResponseText = npc.Greeting,
            DetectedIntent = DetectGreetingIntent(npc)
        };
    }

    /// <summary>
    /// End the current dialogue session.
    /// </summary>
    public void EndDialogue()
    {
        if (ActiveNPC != null)
        {
            GD.Print($"[NPCManager] Dialogue ended with {ActiveNPC.DisplayName}.");
            ActiveNPC = null;
        }
    }

    // -----------------------------------------------------------------
    // Core dialogue generation
    // -----------------------------------------------------------------

    /// <summary>
    /// Generate an NPC dialogue response to player input. Uses the LLM with
    /// personality-aware prompting and conversation history. Falls back to
    /// scripted responses when the LLM is unavailable.
    /// </summary>
    /// <param name="npcId">The NPC to talk to.</param>
    /// <param name="playerInput">What the player said or asked.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Dialogue result with NPC response and detected intent.</returns>
    public async Task<NPCDialogueResult> TalkToNPC(
        string npcId,
        string playerInput,
        CancellationToken cancellationToken = default)
    {
        var npc = GetNPC(npcId);
        if (npc == null)
        {
            return new NPCDialogueResult
            {
                SpeakerName = "Unknown",
                ResponseText = "There is no one here by that name.",
                DetectedIntent = DialogueIntent.Chat
            };
        }

        // Set as active if not already
        if (ActiveNPC?.NpcId != npcId)
            ActiveNPC = npc;

        // Try LLM-powered dialogue first
        if (_llmService != null)
        {
            var llmResult = await GenerateLLMDialogue(npc, playerInput, cancellationToken);
            if (llmResult != null)
            {
                RecordExchange(npcId, playerInput, llmResult.ResponseText);
                EmitDialogueSignals(npcId, llmResult);
                return llmResult;
            }
        }

        // Fallback to scripted response
        var fallback = GenerateFallbackDialogue(npc, playerInput);
        RecordExchange(npcId, playerInput, fallback.ResponseText);
        EmitDialogueSignals(npcId, fallback);
        return fallback;
    }

    // -----------------------------------------------------------------
    // LLM dialogue generation
    // -----------------------------------------------------------------

    private async Task<NPCDialogueResult?> GenerateLLMDialogue(
        NPCData npc,
        string playerInput,
        CancellationToken cancellationToken)
    {
        var messages = BuildNPCDialoguePrompt(npc, playerInput);

        try
        {
            var response = await _llmService!.GenerateAsync(messages, jsonMode: true, cancellationToken);

            if (!response.IsSuccess)
            {
                GD.PrintErr($"[NPCManager] LLM dialogue failed: {response.ErrorMessage}");
                return null;
            }

            return ParseLLMResponse(npc, response);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NPCManager] LLM dialogue error: {ex.Message}");
            return null;
        }
    }

    private NPCDialogueResult? ParseLLMResponse(NPCData npc, LLMResponse response)
    {
        // Try structured JSON first
        if (response.ParsedJson != null)
        {
            string dialogue = response.ParsedJson["dialogue"]?.ToString() ?? string.Empty;
            string intentStr = response.ParsedJson["intent"]?.ToString() ?? "chat";
            string? questId = response.ParsedJson["related_quest_id"]?.ToString();

            if (!string.IsNullOrWhiteSpace(dialogue))
            {
                return new NPCDialogueResult
                {
                    SpeakerName = npc.DisplayName,
                    ResponseText = dialogue,
                    DetectedIntent = ParseIntent(intentStr),
                    RelatedQuestId = string.IsNullOrEmpty(questId) ? null : questId
                };
            }
        }

        // Fallback: use raw text if we got something
        if (!string.IsNullOrWhiteSpace(response.RawText))
        {
            // Try to extract JSON from raw text
            string rawText = response.RawText.Trim();
            try
            {
                var parsed = JObject.Parse(rawText);
                string dialogue = parsed["dialogue"]?.ToString() ?? rawText;
                string intentStr = parsed["intent"]?.ToString() ?? "chat";
                string? questId = parsed["related_quest_id"]?.ToString();

                return new NPCDialogueResult
                {
                    SpeakerName = npc.DisplayName,
                    ResponseText = dialogue,
                    DetectedIntent = ParseIntent(intentStr),
                    RelatedQuestId = string.IsNullOrEmpty(questId) ? null : questId
                };
            }
            catch (JsonReaderException)
            {
                // Raw text is not JSON — use it as plain dialogue
                return new NPCDialogueResult
                {
                    SpeakerName = npc.DisplayName,
                    ResponseText = rawText,
                    DetectedIntent = DialogueIntent.Chat
                };
            }
        }

        return null;
    }

    // -----------------------------------------------------------------
    // Prompt building
    // -----------------------------------------------------------------

    private List<LLMMessage> BuildNPCDialoguePrompt(NPCData npc, string playerInput)
    {
        string questContext = BuildQuestContext(npc);
        string historyContext = BuildHistoryContext(npc.NpcId);
        string hooksContext = npc.DialogueHooks.Count > 0
            ? "TOPICS THIS NPC KNOWS ABOUT:\n- " + string.Join("\n- ", npc.DialogueHooks)
            : "";

        string systemPrompt =
            $"You are {npc.DisplayName}, a {npc.Role} in a D&D 5e campaign world.\n\n" +
            $"PERSONALITY:\n{npc.Personality}\n\n" +
            (string.IsNullOrEmpty(hooksContext) ? "" : $"{hooksContext}\n\n") +
            (string.IsNullOrEmpty(questContext) ? "" : $"QUEST INFORMATION:\n{questContext}\n\n") +
            "RULES:\n" +
            "- Stay fully in character at all times.\n" +
            "- Your dialogue should reflect your personality, role, and knowledge.\n" +
            "- You only know what your character would reasonably know.\n" +
            "- Keep responses concise (1-3 sentences of dialogue, not paragraphs).\n" +
            "- If the player asks about something you don't know, say so in character.\n" +
            "- If you have a quest to offer, work it into the conversation naturally.\n" +
            "- If you run a shop, you can offer to show your wares.\n\n" +
            "You MUST respond with ONLY a JSON object matching this exact schema:\n" +
            "{\n" +
            "  \"dialogue\": \"<what you say, in character>\",\n" +
            "  \"intent\": \"<chat|quest_offer|quest_info|open_shop|location_hint|rumor|farewell|hostile>\",\n" +
            "  \"related_quest_id\": \"<quest_id or null>\"\n" +
            "}\n\n" +
            "Do not include any text outside the JSON object.";

        var messages = new List<LLMMessage>
        {
            new LLMMessage("system", systemPrompt)
        };

        // Inject conversation history as prior turns
        if (_conversationHistory.TryGetValue(npc.NpcId, out var history))
        {
            foreach (var exchange in history)
            {
                messages.Add(new LLMMessage("user", exchange.PlayerInput));
                messages.Add(new LLMMessage("assistant",
                    $"{{\"dialogue\": \"{EscapeJsonString(exchange.NPCResponse)}\", \"intent\": \"chat\", \"related_quest_id\": null}}"));
            }
        }

        messages.Add(new LLMMessage("user", playerInput));

        return messages;
    }

    private string BuildQuestContext(NPCData npc)
    {
        if (npc.AssociatedQuestIds.Count == 0)
            return string.Empty;

        var questManager = QuestManager.Instance;
        if (questManager == null)
            return $"Associated quests: {string.Join(", ", npc.AssociatedQuestIds)}";

        var parts = new List<string>();
        foreach (string questId in npc.AssociatedQuestIds)
        {
            var quest = questManager.GetQuest(questId);
            if (quest == null) continue;

            string status = quest.Status.ToString().ToLowerInvariant();
            parts.Add($"- {quest.Title} ({status}): {quest.Description}");
        }

        return parts.Count > 0 ? string.Join("\n", parts) : string.Empty;
    }

    private string BuildHistoryContext(string npcId)
    {
        if (!_conversationHistory.TryGetValue(npcId, out var history) || history.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var exchange in history)
        {
            parts.Add($"Player: {exchange.PlayerInput}");
            parts.Add($"NPC: {exchange.NPCResponse}");
        }

        return string.Join("\n", parts);
    }

    // -----------------------------------------------------------------
    // Fallback dialogue (no LLM available)
    // -----------------------------------------------------------------

    private NPCDialogueResult GenerateFallbackDialogue(NPCData npc, string playerInput)
    {
        string inputLower = playerInput.Trim().ToLowerInvariant();

        // Check for quest-related keywords
        if (npc.AssociatedQuestIds.Count > 0 &&
            (inputLower.Contains("quest") || inputLower.Contains("job") ||
             inputLower.Contains("work") || inputLower.Contains("help") ||
             inputLower.Contains("task") || inputLower.Contains("problem")))
        {
            string questId = npc.AssociatedQuestIds[0];
            string questResponse = GetFallbackQuestResponse(npc, questId);

            return new NPCDialogueResult
            {
                SpeakerName = npc.DisplayName,
                ResponseText = questResponse,
                DetectedIntent = DialogueIntent.QuestOffer,
                RelatedQuestId = questId
            };
        }

        // Check for shop-related keywords
        if (!string.IsNullOrEmpty(npc.AssociatedShopId) &&
            (inputLower.Contains("shop") || inputLower.Contains("buy") ||
             inputLower.Contains("sell") || inputLower.Contains("wares") ||
             inputLower.Contains("trade") || inputLower.Contains("browse")))
        {
            return new NPCDialogueResult
            {
                SpeakerName = npc.DisplayName,
                ResponseText = GetFallbackShopResponse(npc),
                DetectedIntent = DialogueIntent.OpenShop
            };
        }

        // Check for farewell
        if (inputLower.Contains("goodbye") || inputLower.Contains("farewell") ||
            inputLower.Contains("bye") || inputLower == "leave" || inputLower == "go")
        {
            return new NPCDialogueResult
            {
                SpeakerName = npc.DisplayName,
                ResponseText = GetFallbackFarewellResponse(npc),
                DetectedIntent = DialogueIntent.Farewell
            };
        }

        // Check for rumor/gossip keywords
        if (inputLower.Contains("rumor") || inputLower.Contains("gossip") ||
            inputLower.Contains("news") || inputLower.Contains("heard"))
        {
            return new NPCDialogueResult
            {
                SpeakerName = npc.DisplayName,
                ResponseText = GetFallbackRumorResponse(npc),
                DetectedIntent = DialogueIntent.Rumor
            };
        }

        // Default: pick a dialogue hook or give a generic response
        return new NPCDialogueResult
        {
            SpeakerName = npc.DisplayName,
            ResponseText = GetFallbackGenericResponse(npc),
            DetectedIntent = DialogueIntent.Chat
        };
    }

    private string GetFallbackQuestResponse(NPCData npc, string questId)
    {
        var quest = QuestManager.Instance?.GetQuest(questId);
        if (quest == null)
            return $"I might have something for you, adventurer. Come speak with me when you have a moment.";

        return quest.Status switch
        {
            QuestStatus.Available =>
                $"I have a task that needs doing. {quest.Description} Are you interested?",
            QuestStatus.Active =>
                $"How goes your progress? Remember, {quest.Objectives.FirstOrDefault()?.Description ?? "the task awaits."}",
            QuestStatus.Completed =>
                $"You've done well, adventurer. I won't forget your help.",
            QuestStatus.Failed =>
                $"A shame things didn't work out. Perhaps another time.",
            _ => $"We should talk about what needs doing around here."
        };
    }

    private string GetFallbackShopResponse(NPCData npc)
    {
        return npc.Role switch
        {
            NPCRole.Blacksmith => "Take a look at what I've forged. Fine steel, all of it.",
            NPCRole.Merchant => "Welcome! I've got goods from across the region. Care to browse?",
            NPCRole.Innkeeper => "Need supplies for the road? I've got provisions and a few useful odds and ends.",
            NPCRole.Priest => "We have remedies and blessed items. All proceeds support the temple.",
            _ => "I have some wares you might find useful. Want to take a look?"
        };
    }

    private string GetFallbackFarewellResponse(NPCData npc)
    {
        return npc.Role switch
        {
            NPCRole.Guard => "Stay out of trouble.",
            NPCRole.Priest => "May the light guide your path.",
            NPCRole.Innkeeper => "Safe travels. You're always welcome here.",
            _ => "Farewell, adventurer. Stay safe out there."
        };
    }

    private string GetFallbackRumorResponse(NPCData npc)
    {
        if (npc.DialogueHooks.Count > 0)
        {
            // Pick a dialogue hook as a rumor
            int index = (int)(Time.GetTicksMsec() % (ulong)npc.DialogueHooks.Count);
            return npc.DialogueHooks[index];
        }

        return "I haven't heard anything of note lately. But you might ask around town.";
    }

    private string GetFallbackGenericResponse(NPCData npc)
    {
        if (npc.DialogueHooks.Count > 0)
        {
            int index = (int)(Time.GetTicksMsec() % (ulong)npc.DialogueHooks.Count);
            return npc.DialogueHooks[index];
        }

        return npc.Role switch
        {
            NPCRole.Guard => "Keep your weapons sheathed in town. That's all I ask.",
            NPCRole.Innkeeper => "Can I get you something? A meal, a room, or just a drink?",
            NPCRole.Blacksmith => "Need something repaired or forged? I'm your smith.",
            NPCRole.Priest => "Blessings upon you. How may I help?",
            NPCRole.Sage => "Knowledge is the greatest treasure. What would you learn?",
            NPCRole.Ranger => "These woods have eyes. Best to stay sharp.",
            NPCRole.Druid => "The forest speaks to those who listen.",
            NPCRole.Noble => "I have responsibilities to this community. Make it brief.",
            NPCRole.Merchant => "Looking to trade? I've got fair prices.",
            _ => "What can I do for you, stranger?"
        };
    }

    // -----------------------------------------------------------------
    // Conversation history
    // -----------------------------------------------------------------

    private void RecordExchange(string npcId, string playerInput, string npcResponse)
    {
        if (!_conversationHistory.TryGetValue(npcId, out var history))
        {
            history = new List<ConversationExchange>();
            _conversationHistory[npcId] = history;
        }

        history.Add(new ConversationExchange
        {
            PlayerInput = playerInput,
            NPCResponse = npcResponse,
            Timestamp = DateTime.UtcNow
        });

        // Trim to max history
        while (history.Count > MaxConversationHistory)
            history.RemoveAt(0);
    }

    /// <summary>
    /// Clears conversation history for a specific NPC.
    /// </summary>
    public void ClearHistory(string npcId)
    {
        _conversationHistory.Remove(npcId);
    }

    /// <summary>
    /// Clears all conversation history.
    /// </summary>
    public void ClearAllHistory()
    {
        _conversationHistory.Clear();
    }

    // -----------------------------------------------------------------
    // Signal emission helpers
    // -----------------------------------------------------------------

    private void EmitDialogueSignals(string npcId, NPCDialogueResult result)
    {
        EmitSignal(SignalName.DialogueReceived, npcId, result.SpeakerName, result.ResponseText);

        if (result.DetectedIntent == DialogueIntent.QuestOffer ||
            result.DetectedIntent == DialogueIntent.QuestInfo)
        {
            if (!string.IsNullOrEmpty(result.RelatedQuestId))
                EmitSignal(SignalName.QuestDialogueDetected, npcId, result.RelatedQuestId);
        }

        if (result.DetectedIntent == DialogueIntent.OpenShop)
        {
            var npc = GetNPC(npcId);
            if (npc != null && !string.IsNullOrEmpty(npc.AssociatedShopId))
                EmitSignal(SignalName.ShopDialogueDetected, npcId, npc.AssociatedShopId);
        }
    }

    // -----------------------------------------------------------------
    // Save / Load
    // -----------------------------------------------------------------

    /// <summary>
    /// Returns serializable conversation state for save games.
    /// </summary>
    public Dictionary<string, List<ConversationExchange>> GetSaveData()
    {
        return new Dictionary<string, List<ConversationExchange>>(_conversationHistory);
    }

    /// <summary>
    /// Restores conversation state from save data.
    /// </summary>
    public void RestoreFromSaveData(Dictionary<string, List<ConversationExchange>>? saveData)
    {
        _conversationHistory.Clear();
        if (saveData == null) return;

        foreach (var kvp in saveData)
        {
            _conversationHistory[kvp.Key] = new List<ConversationExchange>(kvp.Value);
        }

        GD.Print($"[NPCManager] Restored conversation history for {_conversationHistory.Count} NPCs.");
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static DialogueIntent DetectGreetingIntent(NPCData npc)
    {
        // If the NPC has a quest to give, their greeting hints at it
        if (npc.AssociatedQuestIds.Count > 0)
            return DialogueIntent.QuestInfo;

        if (!string.IsNullOrEmpty(npc.AssociatedShopId))
            return DialogueIntent.Chat;

        return DialogueIntent.Chat;
    }

    private static DialogueIntent ParseIntent(string intentStr)
    {
        return intentStr?.ToLowerInvariant() switch
        {
            "quest_offer" => DialogueIntent.QuestOffer,
            "quest_info" => DialogueIntent.QuestInfo,
            "open_shop" => DialogueIntent.OpenShop,
            "location_hint" => DialogueIntent.LocationHint,
            "rumor" => DialogueIntent.Rumor,
            "farewell" => DialogueIntent.Farewell,
            "hostile" => DialogueIntent.Hostile,
            _ => DialogueIntent.Chat
        };
    }

    private static string EscapeJsonString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return input
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private static string ResolveResPath(string path)
    {
        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Godot.ProjectSettings.GlobalizePath(path);
            }
            catch
            {
                return path.Replace("res://", "");
            }
        }
        return path;
    }
}

/// <summary>
/// A single player-NPC dialogue exchange for conversation history tracking.
/// </summary>
public class ConversationExchange
{
    public string PlayerInput { get; set; } = string.Empty;
    public string NPCResponse { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
