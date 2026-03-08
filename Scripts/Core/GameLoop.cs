using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using ChroniclesUnbound.Data;
using ChroniclesUnbound.LLM;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Central game loop orchestrator. Bridges the C# backend (LLM, rules engine,
/// data models) with the GDScript NarrativeUI. Handles the core gameplay cycle:
/// player action -> LLM narrative -> parsed response -> UI display.
///
/// Added as a child of Main scene. Does NOT auto-start in _Ready().
/// SceneManager calls Initialize(narrativeUI) after loading the NarrativeUI scene,
/// which wires signals and starts the game loop.
/// </summary>
public partial class GameLoop : Node
{
    // -- Dependencies --
    private LLMService _llmService;
    private LLMConfig _llmConfig;
    private Node _narrativeUI;

    // -- Game state --
    private Character _playerCharacter;
    private List<Character> _companions;

    /// <summary>
    /// The player character, exposed read-only for other systems (e.g. ExplorationBridge).
    /// </summary>
    public Character? PlayerCharacter => _playerCharacter;

    /// <summary>
    /// The companion party members, exposed read-only for other systems (e.g. ExplorationBridge).
    /// </summary>
    public List<Character>? Companions => _companions;
    private List<string> _recentHistory;
    private List<string> _currentSuggestedActions;
    private string _currentScene;
    private bool _isProcessing;
    private bool _initialized;

    private const int MaxHistoryEntries = 10;

    private const string CampaignContext =
        "The Shattered Crown Campaign. The kingdom of Valdris is in turmoil " +
        "after the royal crown — an artifact of binding power — was shattered " +
        "into five fragments during an assassination. Each fragment holds a " +
        "portion of sovereign authority and has been seized by rival factions. " +
        "The party has been tasked by the remnants of the royal guard to " +
        "recover the fragments before the kingdom descends into civil war.";

    private const string InitialScene =
        "The party stands at the entrance to the Thornwood Inn, a weathered " +
        "waystation on the frontier road between Valdris and the Ashenmire. " +
        "Rain hammers the thatched roof. Inside, firelight flickers through " +
        "fogged windows. A faded notice board beside the door is covered in " +
        "bounty postings and rumors. The sound of laughter and clinking " +
        "mugs drifts from within. Somewhere nearby, a horse whinnies nervously.";

    // ----------------------------------------------------------------
    // Lifecycle
    // ----------------------------------------------------------------

    public override void _Ready()
    {
        GD.Print("[GameLoop] Ready. Waiting for Initialize() call from SceneManager.");

        _recentHistory = new List<string>();
        _currentSuggestedActions = new List<string>();
        _isProcessing = false;

        // Create LLM service eagerly — it has no scene dependencies.
        _llmConfig = new LLMConfig
        {
            ModelName = "llama3.1:8b",
            MaxTokens = 2048,
            Temperature = 0.7f,
            TimeoutSeconds = 60,
            MaxRetries = 2
        };
        _llmService = new LLMService(_llmConfig);
    }

    /// <summary>
    /// Deferred initialization called by SceneManager after the NarrativeUI scene
    /// has been instanced and added to the tree. Creates demo characters, wires
    /// signals, and starts the game loop.
    /// </summary>
    /// <param name="narrativeUI">The instanced NarrativeUI root node.</param>
    public void Initialize(Node narrativeUI)
    {
        if (_initialized)
        {
            GD.PrintErr("[GameLoop] Initialize() called more than once — ignoring.");
            return;
        }

        if (narrativeUI == null)
        {
            GD.PrintErr("[GameLoop] Initialize() received null NarrativeUI — aborting.");
            return;
        }

        _initialized = true;
        _narrativeUI = narrativeUI;

        GD.Print("[GameLoop] Initializing with NarrativeUI reference...");

        // Create demo characters
        _playerCharacter = CreateDemoPlayer();
        _companions = CreateDemoCompanions();

        // Connect GDScript signals to C# handlers.
        // NarrativeUI emits: action_submitted(text: String), suggested_action_selected(index: int),
        // inventory_requested(), journal_requested()
        _narrativeUI.Connect("action_submitted", Callable.From<string>(OnActionSubmitted));
        _narrativeUI.Connect("suggested_action_selected", Callable.From<int>(OnSuggestedActionSelected));
        _narrativeUI.Connect("inventory_requested", Callable.From(OnInventoryRequested));
        _narrativeUI.Connect("journal_requested", Callable.From(OnJournalRequested));
        _narrativeUI.Connect("save_requested", Callable.From(OnSaveRequested));

        // Set the current scene description
        _currentScene = InitialScene;

        // Populate party panel
        UpdatePartyPanel();

        // Register with GameStateManager and transition game state
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ActiveGameLoop = this;
            GameStateManager.Instance.SetPhase(GameStateManager.GamePhase.Exploration);
        }

        // Display the opening scene and check LLM availability (deferred to avoid blocking)
        CallDeferred(MethodName.StartGameAsync);
    }

    /// <summary>
    /// Async entry point called deferred from _Ready to display the opening and check Ollama.
    /// </summary>
    private async void StartGameAsync()
    {
        // Clear the default placeholder text and show our own intro
        _narrativeUI.Call("add_narrative_text",
            "\n[color=#b8b0a0]" +
            "The Shattered Crown — Act I\n" +
            "─────────────────────────────────[/color]\n\n");

        _narrativeUI.Call("set_status", "Checking Ollama connection...");

        bool ollamaAvailable = await _llmService.IsAvailableAsync();

        if (ollamaAvailable)
        {
            GD.Print("[GameLoop] Ollama is available. Generating opening scene...");
            _narrativeUI.Call("set_status", "DM is setting the scene...");

            // Try to generate an LLM opening scene
            await ProcessPlayerAction("Look around and take in the surroundings as the party arrives.");
        }
        else
        {
            GD.Print("[GameLoop] Ollama is not available. Showing hardcoded intro.");
            DisplayHardcodedOpening();
        }
    }

    public override void _ExitTree()
    {
        if (GameStateManager.Instance?.ActiveGameLoop == this)
            GameStateManager.Instance.ActiveGameLoop = null;

        _llmService?.Dispose();
    }

    // ----------------------------------------------------------------
    // Signal handlers (called from GDScript)
    // ----------------------------------------------------------------

    private void OnActionSubmitted(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (_isProcessing)
        {
            _narrativeUI.Call("set_status", "Please wait... the DM is still thinking.");
            return;
        }

        _ = ProcessPlayerAction(text);
    }

    private void OnSuggestedActionSelected(int index)
    {
        if (_isProcessing)
        {
            _narrativeUI.Call("set_status", "Please wait... the DM is still thinking.");
            return;
        }

        if (index < 0 || index >= _currentSuggestedActions.Count)
            return;

        string action = _currentSuggestedActions[index];
        _ = ProcessPlayerAction(action);
    }

    /// <summary>
    /// Called when the player clicks the Inventory button or presses I in NarrativeUI.
    /// Transitions to the Inventory phase, which triggers SceneManager to push
    /// the InventoryUI overlay.
    /// </summary>
    private void OnInventoryRequested()
    {
        GD.Print("[GameLoop] Inventory requested.");
        GameStateManager.Instance?.SetPhase(GameStateManager.GamePhase.Inventory);
    }

    /// <summary>
    /// Called when the player clicks the Journal button or presses J in NarrativeUI.
    /// Transitions to the Journal phase, which triggers SceneManager to push
    /// the JournalUI overlay.
    /// </summary>
    private void OnJournalRequested()
    {
        GD.Print("[GameLoop] Journal requested.");
        GameStateManager.Instance?.SetPhase(GameStateManager.GamePhase.Journal);
    }

    private void OnSaveRequested()
    {
        GD.Print("[GameLoop] Save requested.");
        var bridge = SaveLoadBridge.Instance;
        if (bridge == null)
        {
            GD.PrintErr("[GameLoop] SaveLoadBridge not available.");
            _narrativeUI?.Call("set_status", "Save system not available.");
            return;
        }

        var gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            _narrativeUI?.Call("set_status", "Cannot save — game state not initialized.");
            return;
        }

        int? slotId = gsm.ActiveSaveSlotId;
        int result;
        if (slotId.HasValue)
        {
            // Overwrite existing save slot
            result = bridge.SaveGame(slotId.Value);
        }
        else
        {
            // First save for this session — create a new slot
            string slotName = _playerCharacter?.Name ?? "Manual Save";
            result = bridge.SaveGameToNewSlot(slotName);
            if (result >= 0)
            {
                gsm.ActiveSaveSlotId = result;
            }
        }

        if (result >= 0)
        {
            _narrativeUI?.Call("set_status", "Game saved.");
        }
        else
        {
            _narrativeUI?.Call("set_status", "Save failed.");
        }
    }

    // ----------------------------------------------------------------
    // Core game loop
    // ----------------------------------------------------------------

    /// <summary>
    /// The main action processing pipeline:
    /// 1. Show thinking indicator
    /// 2. Build prompt with context
    /// 3. Send to LLM
    /// 4. Parse response
    /// 5. Display narrative, events, companion reactions, actions
    /// 6. Update history
    /// </summary>
    private async Task ProcessPlayerAction(string action)
    {
        _isProcessing = true;

        try
        {
            // 1. Show the player's action in the narrative and set thinking status
            _narrativeUI.Call("add_narrative_text",
                $"\n[color=#c9a227][b]> {EscapeBBCode(action)}[/b][/color]\n\n");
            _narrativeUI.Call("set_status", "The DM is thinking...");

            // 2. Build context strings
            string recentHistory = _recentHistory.Count > 0
                ? string.Join("\n", _recentHistory)
                : "(This is the beginning of the adventure.)";

            string companionStates = BuildCompanionStatesString();

            // 3. Build the narrative prompt
            var messages = PromptBuilder.BuildNarrativePrompt(
                CampaignContext,
                recentHistory,
                _currentScene,
                action,
                companionStates);

            // 4. Send to LLM
            var response = await _llmService.GenerateAsync(messages, jsonMode: true);

            // 5. Parse the response
            var result = ResponseParser.ParseNarrativeResponse(response);

            // 6. Display narrative text
            if (!string.IsNullOrWhiteSpace(result.NarrativeText))
            {
                _narrativeUI.Call("add_narrative_text",
                    $"[color=#eee8d5]{EscapeBBCode(result.NarrativeText)}[/color]\n\n");

                // Update the current scene to reflect what the LLM described
                _currentScene = result.NarrativeText;
            }

            // 7. Process game events (skill checks, item finds, etc.)
            ProcessGameEvents(result.GameEvents);

            // 8. Display companion reactions
            DisplayCompanionReactions(result.CompanionReactions);

            // 9. Update suggested actions
            _currentSuggestedActions = result.SuggestedActions;
            if (_currentSuggestedActions.Count > 0)
            {
                var actionsArray = new Godot.Collections.Array<string>();
                foreach (var a in _currentSuggestedActions.Take(4))
                    actionsArray.Add(a);
                _narrativeUI.Call("set_suggested_actions", actionsArray);
            }

            // 10. Update rolling history
            AddToHistory($"Player: {action}");
            if (!string.IsNullOrWhiteSpace(result.NarrativeText))
                AddToHistory($"DM: {TruncateForHistory(result.NarrativeText)}");

            // 11. Show generation time in status
            string statusText = response.IsSuccess
                ? $"Ready. (Response generated in {response.GenerationTimeMs}ms)"
                : $"Ready. (Used fallback — {response.ErrorMessage})";
            _narrativeUI.Call("set_status", statusText);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameLoop] Error processing action: {ex.Message}");
            _narrativeUI.Call("add_narrative_text",
                "[color=#e74c3c][i]An error occurred. The DM collects their notes...[/i][/color]\n\n");
            _narrativeUI.Call("set_status", $"Error: {ex.Message}");

            // Provide fallback actions so the player is never stuck
            _currentSuggestedActions = new List<string>
            {
                "Look around",
                "Talk to a nearby NPC",
                "Check inventory",
                "Rest"
            };
            var fallbackActions = new Godot.Collections.Array<string>();
            foreach (var a in _currentSuggestedActions)
                fallbackActions.Add(a);
            _narrativeUI.Call("set_suggested_actions", fallbackActions);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    // ----------------------------------------------------------------
    // Game event processing
    // ----------------------------------------------------------------

    /// <summary>
    /// Process structured game events from the LLM response.
    /// Handles skill checks (rolls dice via CombatResolver), item finds, and other events.
    /// </summary>
    private void ProcessGameEvents(List<JObject> events)
    {
        if (events == null || events.Count == 0)
            return;

        foreach (var evt in events)
        {
            string eventType = evt["type"]?.ToString() ?? "unknown";
            string description = evt["description"]?.ToString() ?? "";
            var details = evt["details"] as JObject;

            switch (eventType.ToLowerInvariant())
            {
                case "skill_check":
                    ProcessSkillCheck(description, details);
                    break;

                case "item_found":
                    _narrativeUI.Call("add_narrative_text",
                        $"[color=#c9a227][b]Item Found:[/b] {EscapeBBCode(description)}[/color]\n\n");
                    break;

                case "combat_start":
                    _narrativeUI.Call("add_narrative_text",
                        "[color=#e74c3c][b]-- COMBAT --[/b][/color]\n" +
                        $"[color=#eee8d5]{EscapeBBCode(description)}[/color]\n\n");
                    break;

                case "state_change":
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        _narrativeUI.Call("add_narrative_text",
                            $"[color=#b8b0a0][i]{EscapeBBCode(description)}[/i][/color]\n\n");
                    }
                    break;

                case "npc_action":
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        _narrativeUI.Call("add_narrative_text",
                            $"[color=#b8b0a0]{EscapeBBCode(description)}[/color]\n\n");
                    }
                    break;

                default:
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        _narrativeUI.Call("add_narrative_text",
                            $"[color=#b8b0a0][i]{EscapeBBCode(description)}[/i][/color]\n\n");
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Resolve a skill check event using the rules engine and display the result.
    /// The LLM specifies which skill and DC; we roll the dice deterministically.
    /// </summary>
    private void ProcessSkillCheck(string description, JObject details)
    {
        // Extract skill name and DC from the event details
        string skillName = details?["skill"]?.ToString() ?? "Perception";
        int dc = details?["dc"]?.ToObject<int>() ?? 12;
        string characterName = details?["character"]?.ToString() ?? _playerCharacter.Name;

        // Find the character performing the check
        Character character = _playerCharacter;
        if (!string.Equals(characterName, _playerCharacter.Name, StringComparison.OrdinalIgnoreCase))
        {
            var companion = _companions.FirstOrDefault(c =>
                c.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
            if (companion != null)
                character = companion;
        }

        // Map the skill name string to the Skill enum
        if (!TryParseSkill(skillName, out Skill skill))
        {
            // Fallback: show the description without a mechanical roll
            _narrativeUI.Call("add_dice_result", $"{description} (unrecognized skill: {skillName})");
            return;
        }

        // Roll using the rules engine
        int modifier = character.GetSkillModifier(skill);
        var result = CombatResolver.ResolveSkillCheck(skillName, modifier, dc);

        // Display the dice result
        _narrativeUI.Call("add_dice_result", result.ToString());

        // Add the outcome to history so the LLM knows what happened
        AddToHistory($"[Skill Check] {result}");
    }

    // ----------------------------------------------------------------
    // Companion reactions
    // ----------------------------------------------------------------

    /// <summary>
    /// Display companion dialogue and reactions from the LLM response.
    /// Each companion entry has dialogue, emotion, and optional action fields.
    /// </summary>
    private void DisplayCompanionReactions(Dictionary<string, JObject> reactions)
    {
        if (reactions == null || reactions.Count == 0)
            return;

        foreach (var (name, data) in reactions)
        {
            string dialogue = data["dialogue"]?.ToString() ?? "";
            string emotion = data["emotion"]?.ToString() ?? "";

            if (!string.IsNullOrWhiteSpace(dialogue))
            {
                _narrativeUI.Call("add_companion_dialogue", name, dialogue, emotion);
            }

            // If the companion took an action, note it
            string action = data["action"]?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(action))
            {
                _narrativeUI.Call("add_narrative_text",
                    $"[color=#b8b0a0][i]{EscapeBBCode(name)} {EscapeBBCode(action)}[/i][/color]\n");
            }
        }

        // Add a blank line after all companion reactions
        _narrativeUI.Call("add_narrative_text", "\n");
    }

    // ----------------------------------------------------------------
    // Hardcoded fallback (Ollama offline)
    // ----------------------------------------------------------------

    private void DisplayHardcodedOpening()
    {
        // Show the intro scene
        _narrativeUI.Call("add_narrative_text",
            $"[color=#eee8d5]{InitialScene}[/color]\n\n");

        // Companion reactions
        _narrativeUI.Call("add_companion_dialogue",
            "Theron", "Another night on the road. At least this inn still stands. " +
            "Let us find shelter before the storm worsens.", "weary");

        _narrativeUI.Call("add_companion_dialogue",
            "Lyra", "I smell fresh bread. That's all the scouting report I need. " +
            "Race you to the bar!", "cheerful");

        _narrativeUI.Call("add_narrative_text", "\n");

        // Suggested actions
        _currentSuggestedActions = new List<string>
        {
            "Enter the Thornwood Inn",
            "Check the notice board",
            "Scout the perimeter of the inn",
            "Talk to the stable hand"
        };

        var actionsArray = new Godot.Collections.Array<string>();
        foreach (var a in _currentSuggestedActions)
            actionsArray.Add(a);
        _narrativeUI.Call("set_suggested_actions", actionsArray);

        _narrativeUI.Call("set_status",
            "Ollama not detected. Showing demo scene. Start 'ollama serve' for LLM narrative.");
    }

    // ----------------------------------------------------------------
    // Party panel management
    // ----------------------------------------------------------------

    /// <summary>
    /// Push all character data to the NarrativeUI party slots.
    /// Slot 0 = player, slots 1+ = companions.
    /// </summary>
    private void UpdatePartyPanel()
    {
        // Slot 0: Player character
        _narrativeUI.Call("update_party_member",
            0,
            _playerCharacter.Name,
            _playerCharacter.CharacterClassName,
            _playerCharacter.Level,
            _playerCharacter.HitPoints,
            _playerCharacter.MaxHitPoints,
            0); // relationship score ignored for slot 0

        // Slots 1+: Companions
        for (int i = 0; i < _companions.Count && i < 4; i++)
        {
            var c = _companions[i];
            int relationship = c.Relationships.TryGetValue(_playerCharacter.Name, out int rel) ? rel : 0;

            _narrativeUI.Call("update_party_member",
                i + 1,
                c.Name,
                c.CharacterClassName,
                c.Level,
                c.HitPoints,
                c.MaxHitPoints,
                relationship);
        }
    }

    // ----------------------------------------------------------------
    // Demo character creation
    // ----------------------------------------------------------------

    /// <summary>
    /// Creates a Level 3 Human Fighter for the PoC.
    /// Stats are pre-rolled using standard array (15, 14, 13, 12, 10, 8).
    /// </summary>
    private Character CreateDemoPlayer()
    {
        return new Character
        {
            Name = "Aldric",
            Race = "Human",
            CharacterClassName = "Fighter",
            Level = 3,
            Background = "Soldier",
            Alignment = Alignment.NeutralGood,
            AbilityScores = new AbilityScores
            {
                Strength = 16,     // 15 + 1 Human
                Dexterity = 13,    // 12 + 1 Human
                Constitution = 15, // 14 + 1 Human
                Intelligence = 9,  // 8 + 1 Human
                Wisdom = 11,       // 10 + 1 Human
                Charisma = 14      // 13 + 1 Human
            },
            HitPoints = 28,        // 10 + 2*7 (avg d10) + 3*2 (CON mod)
            MaxHitPoints = 28,
            ArmorClass = 18,       // Chain mail (16) + Shield (2)
            Speed = 30,
            IsCompanion = false,
            Features = new List<string>
            {
                "Fighting Style: Defense",
                "Second Wind",
                "Action Surge",
                "Martial Archetype: Champion",
                "Improved Critical"
            },
            Skills = new Dictionary<Skill, ProficiencyLevel>
            {
                { Skill.Athletics, ProficiencyLevel.Proficient },
                { Skill.Intimidation, ProficiencyLevel.Proficient },
                { Skill.Perception, ProficiencyLevel.Proficient },
                { Skill.Survival, ProficiencyLevel.Proficient }
            },
            SavingThrowProficiencies = new HashSet<Ability>
            {
                Ability.Strength,
                Ability.Constitution
            },
            EquippedItems = new Dictionary<string, string>
            {
                { "MainHand", "Longsword" },
                { "OffHand", "Shield" },
                { "Armor", "Chain Mail" }
            },
            Languages = new List<string> { "Common", "Orcish" },
            PersonalityTraits = new List<string>
            {
                "I face problems head-on. A simple, direct approach is the best path.",
                "I can stare down a hell hound without flinching."
            },
            Ideal = "Greater Good. Our lot is to lay down our lives in defense of others.",
            Bond = "I fight for those who cannot fight for themselves.",
            Flaw = "I have little respect for anyone who is not a proven warrior."
        };
    }

    /// <summary>
    /// Creates two demo companions: an Elf Cleric and a Halfling Rogue.
    /// </summary>
    private List<Character> CreateDemoCompanions()
    {
        var theron = new Character
        {
            Name = "Theron",
            Race = "High Elf",
            CharacterClassName = "Cleric",
            Level = 3,
            Background = "Acolyte",
            Alignment = Alignment.LawfulGood,
            AbilityScores = new AbilityScores
            {
                Strength = 10,
                Dexterity = 14,  // +2 Elf
                Constitution = 13,
                Intelligence = 13, // +1 High Elf
                Wisdom = 16,
                Charisma = 10
            },
            HitPoints = 21,      // 8 + 2*5 (avg d8) + 3*1 (CON mod)
            MaxHitPoints = 21,
            ArmorClass = 16,     // Scale mail (14) + Shield (2)
            Speed = 30,
            IsCompanion = true,
            CombatTactic = CombatTacticPreference.Support,
            Features = new List<string>
            {
                "Spellcasting",
                "Divine Domain: Life",
                "Disciple of Life",
                "Channel Divinity: Preserve Life",
                "Darkvision",
                "Fey Ancestry"
            },
            SpellsKnown = new List<string>
            {
                "Sacred Flame", "Guidance", "Spare the Dying",
                "Cure Wounds", "Healing Word", "Bless", "Shield of Faith",
                "Lesser Restoration", "Spiritual Weapon"
            },
            SpellSlotsCurrent = new[] { 4, 2, 0, 0, 0, 0, 0, 0, 0 },
            SpellSlotsMax = new[] { 4, 2, 0, 0, 0, 0, 0, 0, 0 },
            Skills = new Dictionary<Skill, ProficiencyLevel>
            {
                { Skill.Medicine, ProficiencyLevel.Proficient },
                { Skill.Religion, ProficiencyLevel.Proficient },
                { Skill.Insight, ProficiencyLevel.Proficient },
                { Skill.Perception, ProficiencyLevel.Proficient }
            },
            SavingThrowProficiencies = new HashSet<Ability>
            {
                Ability.Wisdom,
                Ability.Charisma
            },
            EquippedItems = new Dictionary<string, string>
            {
                { "MainHand", "Mace" },
                { "OffHand", "Shield" },
                { "Armor", "Scale Mail" }
            },
            Languages = new List<string> { "Common", "Elvish", "Celestial" },
            PersonalityTraits = new List<string>
            {
                "I see omens in every event and action. The gods try to speak to us.",
                "I quote ancient texts and proverbs in almost every situation."
            },
            Ideal = "Faith. Trust in the divine plan, even when the path grows dark.",
            Bond = "I will do anything to protect the temple where I was raised.",
            Flaw = "My piety sometimes leads me to blindly trust those who profess faith.",
            Backstory = "A devoted cleric of Lathander raised in a border temple. " +
                        "Joined the party seeking to restore order to the kingdom.",
            Relationships = new Dictionary<string, int>
            {
                { "Aldric", 25 }  // Friendly — respects the fighter's sense of duty
            }
        };

        var lyra = new Character
        {
            Name = "Lyra",
            Race = "Lightfoot Halfling",
            CharacterClassName = "Rogue",
            Level = 3,
            Background = "Criminal",
            Alignment = Alignment.ChaoticGood,
            AbilityScores = new AbilityScores
            {
                Strength = 8,
                Dexterity = 17,  // 15 + 2 Halfling
                Constitution = 12,
                Intelligence = 14,
                Wisdom = 10,
                Charisma = 14    // 13 + 1 Lightfoot
            },
            HitPoints = 18,      // 8 + 2*4 (avg d8-1) + 3*1 (CON mod)
            MaxHitPoints = 18,
            ArmorClass = 14,     // Leather (11) + DEX mod (3)
            Speed = 25,
            IsCompanion = true,
            CombatTactic = CombatTacticPreference.Ranged,
            Features = new List<string>
            {
                "Sneak Attack (2d6)",
                "Thieves' Cant",
                "Cunning Action",
                "Roguish Archetype: Thief",
                "Fast Hands",
                "Lucky",
                "Brave",
                "Naturally Stealthy"
            },
            Skills = new Dictionary<Skill, ProficiencyLevel>
            {
                { Skill.Stealth, ProficiencyLevel.Expertise },
                { Skill.SleightOfHand, ProficiencyLevel.Expertise },
                { Skill.Acrobatics, ProficiencyLevel.Proficient },
                { Skill.Perception, ProficiencyLevel.Proficient },
                { Skill.Deception, ProficiencyLevel.Proficient },
                { Skill.Investigation, ProficiencyLevel.Proficient }
            },
            SavingThrowProficiencies = new HashSet<Ability>
            {
                Ability.Dexterity,
                Ability.Intelligence
            },
            EquippedItems = new Dictionary<string, string>
            {
                { "MainHand", "Shortsword" },
                { "OffHand", "Dagger" },
                { "Armor", "Leather Armor" },
                { "Ranged", "Shortbow" }
            },
            ToolProficiencies = new List<string> { "Thieves' Tools", "Playing Cards", "Disguise Kit" },
            Languages = new List<string> { "Common", "Halfling", "Thieves' Cant" },
            PersonalityTraits = new List<string>
            {
                "I always have a plan for what to do when things go wrong.",
                "I am incredibly slow to trust, but fiercely loyal to those who earn it."
            },
            Ideal = "Freedom. Chains are meant to be broken, as are those who forge them.",
            Bond = "I'm trying to pay off a debt I owe to a generous benefactor.",
            Flaw = "When I see something valuable, I can't think about anything but how to steal it.",
            Backstory = "A former street thief from the capital who turned to adventuring " +
                        "after her fence was killed during the crown's shattering.",
            Relationships = new Dictionary<string, int>
            {
                { "Aldric", 15 }  // Neutral-to-friendly — appreciates his straightforwardness
            }
        };

        return new List<Character> { theron, lyra };
    }

    // ----------------------------------------------------------------
    // Save / Load support
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns the recent narrative history for save persistence.
    /// </summary>
    public List<string> GetNarrativeHistory()
    {
        return new List<string>(_recentHistory);
    }

    /// <summary>
    /// Returns the current suggested actions for save persistence.
    /// </summary>
    public List<string> GetSuggestedActions()
    {
        return new List<string>(_currentSuggestedActions);
    }

    /// <summary>
    /// Returns the current scene description for save persistence.
    /// </summary>
    public string GetCurrentScene()
    {
        return _currentScene ?? "";
    }

    /// <summary>
    /// Restores the GameLoop state from saved data. Replaces the player character,
    /// companions, and narrative state. Called by SaveLoadBridge after a scene
    /// transition completes and Initialize() has been called by SceneManager.
    /// </summary>
    public void RestoreFromSave(
        Character playerCharacter,
        List<Character> companions,
        CampaignSaveData? campaignData)
    {
        // Replace characters
        _playerCharacter = playerCharacter ?? _playerCharacter;
        _companions = companions ?? _companions ?? new List<Character>();

        // Restore narrative state
        if (campaignData != null)
        {
            if (campaignData.NarrativeHistory != null)
            {
                _recentHistory.Clear();
                _recentHistory.AddRange(campaignData.NarrativeHistory);
            }

            if (campaignData.SuggestedActions != null)
            {
                _currentSuggestedActions = new List<string>(campaignData.SuggestedActions);
            }

            if (!string.IsNullOrWhiteSpace(campaignData.CurrentScene))
            {
                _currentScene = campaignData.CurrentScene;
            }
        }

        // Refresh UI if narrative UI is connected
        if (_narrativeUI != null)
        {
            UpdatePartyPanel();

            // Re-display the last scene description
            if (!string.IsNullOrWhiteSpace(_currentScene))
            {
                _narrativeUI.Call("add_narrative_text",
                    $"[color=#eee8d5]{EscapeBBCode(_currentScene)}[/color]\n\n");
            }

            // Restore suggested actions in the UI
            if (_currentSuggestedActions.Count > 0)
            {
                var actionsArray = new Godot.Collections.Array<string>();
                foreach (var a in _currentSuggestedActions)
                    actionsArray.Add(a);
                _narrativeUI.Call("set_suggested_actions", actionsArray);
            }

            _narrativeUI.Call("set_status", "Game loaded.");
        }

        GD.Print($"[GameLoop] Restored from save: {_playerCharacter.Name} (Lvl {_playerCharacter.Level}), " +
                 $"{_companions.Count} companions.");
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Build a text summary of companion states for the LLM context.
    /// Includes name, class, HP, relationship, and personality keywords.
    /// </summary>
    private string BuildCompanionStatesString()
    {
        var lines = new List<string>();

        foreach (var c in _companions)
        {
            int relationship = c.Relationships.TryGetValue(_playerCharacter.Name, out int rel) ? rel : 0;
            string tier = GetRelationshipTier(relationship);

            lines.Add(
                $"- {c.Name} ({c.Race} {c.CharacterClassName} Lvl {c.Level}): " +
                $"HP {c.HitPoints}/{c.MaxHitPoints}, " +
                $"Relationship with player: {tier} ({relationship}). " +
                $"Personality: {string.Join("; ", c.PersonalityTraits)}");
        }

        return string.Join("\n", lines);
    }

    private static string GetRelationshipTier(int score)
    {
        if (score <= -61) return "Hostile";
        if (score <= -21) return "Cold";
        if (score <= 20)  return "Neutral";
        if (score <= 60)  return "Friendly";
        if (score <= 85)  return "Close";
        return "Devoted";
    }

    /// <summary>
    /// Add an entry to the rolling history buffer, trimming old entries.
    /// </summary>
    private void AddToHistory(string entry)
    {
        _recentHistory.Add(entry);
        while (_recentHistory.Count > MaxHistoryEntries)
            _recentHistory.RemoveAt(0);
    }

    /// <summary>
    /// Truncate text for history storage to avoid bloating the context window.
    /// </summary>
    private static string TruncateForHistory(string text, int maxLength = 300)
    {
        if (text.Length <= maxLength)
            return text;
        return text.Substring(0, maxLength) + "...";
    }

    /// <summary>
    /// Attempt to parse a skill name string (from LLM) to the Skill enum.
    /// Handles common variations like "Sleight of Hand" vs "SleightOfHand".
    /// </summary>
    private static bool TryParseSkill(string skillName, out Skill skill)
    {
        // Normalize: remove spaces, lowercase for comparison
        string normalized = skillName.Replace(" ", "").Replace("_", "");

        foreach (Skill s in Enum.GetValues(typeof(Skill)))
        {
            if (string.Equals(s.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                skill = s;
                return true;
            }
        }

        // Common aliases
        switch (normalized.ToLowerInvariant())
        {
            case "sleightofhand":
                skill = Skill.SleightOfHand;
                return true;
            case "animalhandling":
                skill = Skill.AnimalHandling;
                return true;
            default:
                skill = default;
                return false;
        }
    }

    /// <summary>
    /// Escape BBCode brackets in user/LLM text to prevent injection into the RichTextLabel.
    /// Godot's RichTextLabel uses [tag] for formatting, so literal [ must be escaped.
    /// </summary>
    private static string EscapeBBCode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Replace [ with the BBCode escape sequence [lb]
        // But only if it's not already part of a known BBCode tag we're wrapping with
        return text.Replace("[", "[lb]");
    }
}
