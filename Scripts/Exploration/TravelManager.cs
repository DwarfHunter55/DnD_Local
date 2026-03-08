using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ChroniclesUnbound.Data;
using ChroniclesUnbound.LLM;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Handles travel between locations: route validation, travel time calculation,
/// random event generation during each travel segment, and LLM narration.
///
/// This is a pure logic manager — it does not manage state transitions.
/// The ExplorationManager handles Traveling state entry/exit and calls into
/// this system to resolve the journey.
///
/// Usage:
///   var result = await travelManager.BeginTravel("millhaven", "crossroads_inn", party);
///   // Inspect result.Events for encounters, discoveries, etc.
///   // result.NarrativeDescription has the journey text.
///   // result.WasInterrupted indicates if combat stopped the journey.
/// </summary>
public class TravelManager
{
    /// <summary>Hours of travel per map hop. Each BFS hop ~ half a day.</summary>
    private const int HoursPerSegment = 4;

    /// <summary>
    /// Max segments to process before force-arriving. Prevents infinite loops
    /// on very long paths.
    /// </summary>
    private const int MaxSegments = 10;

    private readonly WorldMap _worldMap;
    private readonly EncounterGenerator _encounterGenerator;
    private readonly LLMService? _llmService;
    private readonly Random _rng;

    // -----------------------------------------------------------------
    // Weighted event tables indexed by danger bracket
    // -----------------------------------------------------------------

    /// <summary>
    /// Travel event probability tables. Each entry is (eventType, cumulativeWeight).
    /// Weights sum to 100 for readability. Indexed by danger bracket:
    ///   0 = danger 0-1 (safe roads)
    ///   1 = danger 2-3 (moderate wilderness)
    ///   2 = danger 4-5 (dangerous territory)
    /// </summary>
    private static readonly (TravelEventType Type, int Weight)[][] EventTables =
    {
        // Danger 0-1: mostly peaceful travel
        new[]
        {
            (TravelEventType.Nothing,          60),
            (TravelEventType.WeatherChange,    10),
            (TravelEventType.Merchant,         10),
            (TravelEventType.Shrine,           10),
            (TravelEventType.TravellerMeeting,  5),
            (TravelEventType.Discovery,         5),
        },
        // Danger 2-3: moderate risk
        new[]
        {
            (TravelEventType.Nothing,          30),
            (TravelEventType.RandomEncounter,  20),
            (TravelEventType.WeatherChange,    15),
            (TravelEventType.TerrainHazard,    10),
            (TravelEventType.Merchant,         10),
            (TravelEventType.TravellerMeeting, 10),
            (TravelEventType.Discovery,         5),
        },
        // Danger 4-5: dangerous territory
        new[]
        {
            (TravelEventType.Nothing,          15),
            (TravelEventType.RandomEncounter,  35),
            (TravelEventType.TerrainHazard,    15),
            (TravelEventType.WeatherChange,    15),
            (TravelEventType.TravellerMeeting, 10),
            (TravelEventType.Merchant,          5),
            (TravelEventType.Discovery,         5),
        },
    };

    // -----------------------------------------------------------------
    // Fallback text pools for when LLM is unavailable
    // -----------------------------------------------------------------

    private static readonly string[] FallbackWeather =
    {
        "Dark clouds gather overhead and a cold rain begins to fall.",
        "A thick fog rolls in, reducing visibility to only a few yards.",
        "The wind picks up sharply, howling through the trees.",
        "The sky clears and warm sunlight breaks through the clouds.",
        "A sudden downpour forces the party to seek temporary shelter.",
        "An unseasonable chill settles over the land, frosting the grass.",
    };

    private static readonly string[] FallbackHazards =
    {
        "A section of the path has collapsed into a muddy sinkhole. The party carefully navigates around it.",
        "Loose rocks tumble down a nearby slope, forcing a hasty detour.",
        "A fallen tree blocks the road. The party spends time clearing a path.",
        "The trail narrows along a crumbling cliff edge. One wrong step could be fatal.",
        "A swollen stream has flooded the ford. The party wades across chest-deep water.",
        "Thorny undergrowth chokes the trail, slowing progress to a crawl.",
    };

    private static readonly string[] FallbackMerchant =
    {
        "A traveling peddler with a laden mule offers wares from distant lands.",
        "A merchant caravan pauses alongside the road, guards eyeing the party warily.",
        "An eccentric tinker sits by the roadside, surrounded by curious trinkets and tools.",
    };

    private static readonly string[] FallbackShrine =
    {
        "A moss-covered shrine to a forgotten deity stands beside the path. Faded offerings sit at its base.",
        "A small wayside altar dedicated to the traveler's patron offers a moment of peace.",
        "Carved standing stones mark an ancient place of worship. The air feels still and reverent.",
    };

    private static readonly string[] FallbackTraveller =
    {
        "A group of pilgrims heading the opposite direction shares news of the road ahead.",
        "A lone ranger scouts the trail and warns of dangers further on.",
        "A bard traveling between towns offers a song in exchange for company.",
        "A patrol of guards passes by, nodding curtly to the party.",
        "A farmer with an oxcart plods along the road, happy for conversation.",
    };

    private static readonly string[] FallbackDiscovery =
    {
        "A faint trail branches off the main road, leading to a place the party had not noticed before.",
        "From a hilltop, the party spots a distant landmark not marked on any map.",
        "An old signpost, half-buried in overgrowth, points toward an unknown destination.",
    };

    private static readonly string[] FallbackNarrativeTemplates =
    {
        "The party sets out from {0} toward {1}. The journey takes roughly {2} hours along {3}. {4}",
        "Leaving {0} behind, the party travels {3} toward {1}. After about {2} hours of travel, {4}",
        "The road from {0} to {1} stretches ahead. Over the next {2} hours across {3}, {4}",
    };

    private static readonly string[] TerrainDescriptions =
    {
        "a well-worn dirt road",
        "winding forest paths",
        "rocky highland trails",
        "muddy wilderness tracks",
        "a cobblestone trade route",
        "overgrown footpaths through dense brush",
    };

    // -----------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------

    public TravelManager(WorldMap worldMap, EncounterGenerator encounterGenerator, LLMService? llmService = null)
        : this(worldMap, encounterGenerator, llmService, new Random())
    {
    }

    public TravelManager(WorldMap worldMap, EncounterGenerator encounterGenerator, LLMService? llmService, Random rng)
    {
        _worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));
        _encounterGenerator = encounterGenerator ?? throw new ArgumentNullException(nameof(encounterGenerator));
        _llmService = llmService;
        _rng = rng ?? new Random();
    }

    // -----------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------

    /// <summary>
    /// Check whether travel is possible between two locations.
    /// Requires a direct connection and the destination to be discovered.
    /// </summary>
    public bool CanTravel(string fromId, string toId)
    {
        if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
            return false;

        if (fromId == toId)
            return false;

        var from = _worldMap.GetLocation(fromId);
        var to = _worldMap.GetLocation(toId);

        if (from == null || to == null)
            return false;

        // Destination must be discovered
        if (!to.IsDiscovered)
            return false;

        // Must be directly connected
        return from.ConnectedLocationIds.Contains(toId);
    }

    /// <summary>
    /// Execute travel from one location to another. Generates events for each
    /// travel segment, rolls for encounters, and produces a narrative description.
    ///
    /// The party parameter is used for encounter scaling (level and size).
    /// If a combat encounter is rolled, the journey is interrupted — the caller
    /// should handle the combat and optionally resume travel afterward.
    /// </summary>
    /// <param name="fromId">Starting location ID.</param>
    /// <param name="toId">Destination location ID.</param>
    /// <param name="party">The traveling party, used for encounter scaling.</param>
    /// <param name="cancellationToken">Optional cancellation.</param>
    /// <returns>A TravelResult with all events, narration, and status.</returns>
    public async Task<TravelResult> BeginTravel(
        string fromId,
        string toId,
        List<Character> party,
        CancellationToken cancellationToken = default)
    {
        var result = new TravelResult
        {
            OriginId = fromId,
            DestinationId = toId,
        };

        // Validate the route
        var fromLoc = _worldMap.GetLocation(fromId);
        var toLoc = _worldMap.GetLocation(toId);

        if (fromLoc == null || toLoc == null)
        {
            GD.PrintErr($"[TravelManager] Invalid travel route: {fromId} -> {toId} (location not found).");
            result.NarrativeDescription = "The party cannot find a route to the destination.";
            return result;
        }

        if (!CanTravel(fromId, toId))
        {
            GD.PrintErr($"[TravelManager] Cannot travel: {fromId} -> {toId} (not connected or not discovered).");
            result.NarrativeDescription = $"There is no known path from {fromLoc.Name} to {toLoc.Name}.";
            return result;
        }

        // Calculate travel distance and time
        int hops = _worldMap.CalculateTravelDistance(fromId, toId);
        if (hops <= 0)
            hops = 1; // Direct connection is always at least 1 hop

        int segments = Math.Min(hops, MaxSegments);
        result.TravelTimeHours = segments * HoursPerSegment;

        GD.Print($"[TravelManager] Travel: {fromLoc.Name} -> {toLoc.Name}, " +
                 $"{segments} segment(s), ~{result.TravelTimeHours}h.");

        // Determine the route danger level: average of origin and destination,
        // biased toward the higher value (travel through dangerous areas is risky).
        int routeDanger = Math.Max(fromLoc.DangerLevel, toLoc.DangerLevel);

        // Calculate party stats for encounter generation
        int partyLevel = CalculateAverageLevel(party);
        int partySize = party?.Count ?? 1;

        // Process each travel segment
        for (int seg = 0; seg < segments; seg++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var travelEvent = RollTravelEvent(routeDanger, seg);

            // If we rolled an encounter, try to generate it
            if (travelEvent.Type == TravelEventType.RandomEncounter)
            {
                var encounter = TryGenerateEncounter(fromLoc, toLoc, routeDanger, partyLevel, partySize);
                if (encounter != null)
                {
                    travelEvent.EncounterData = encounter;
                    travelEvent.Description = BuildEncounterDescription(encounter);
                    result.Events.Add(travelEvent);
                    result.WasInterrupted = true;

                    GD.Print($"[TravelManager] Combat encounter in segment {seg}: " +
                             $"{encounter.TotalMonsterCount} monster(s), {encounter.Difficulty} difficulty.");
                    break; // Combat stops travel
                }
                else
                {
                    // Failed to generate encounter — downgrade to nothing
                    travelEvent.Type = TravelEventType.Nothing;
                    travelEvent.Description = "The road is quiet and uneventful.";
                }
            }

            // Handle discovery events — try to find an undiscovered connected location
            if (travelEvent.Type == TravelEventType.Discovery)
            {
                string discoveredId = TryFindDiscoverableLocation(fromId, toId);
                if (!string.IsNullOrEmpty(discoveredId))
                {
                    travelEvent.DiscoveredLocationId = discoveredId;
                    var discovered = _worldMap.GetLocation(discoveredId);
                    string name = discovered?.Name ?? discoveredId;
                    travelEvent.Description = $"The party spots signs of {name} nearby — a previously unknown location.";
                    // Actually mark it as discovered
                    _worldMap.DiscoverLocation(discoveredId);
                    GD.Print($"[TravelManager] Discovery in segment {seg}: {name}");
                }
                else
                {
                    // No discoverable locations — downgrade to nothing
                    travelEvent.Type = TravelEventType.Nothing;
                    travelEvent.Description = "The road stretches on without notable landmarks.";
                }
            }

            result.Events.Add(travelEvent);
        }

        // Generate narrative description
        result.NarrativeDescription = await GetTravelNarration(
            fromLoc.Name, toLoc.Name, result.Events, result.TravelTimeHours,
            routeDanger, result.WasInterrupted, cancellationToken);

        return result;
    }

    /// <summary>
    /// Roll a single travel event for one segment based on the route danger level.
    /// Uses the weighted event tables to select an event type, then generates
    /// a fallback description for non-encounter events.
    /// </summary>
    public TravelEvent RollTravelEvent(int dangerLevel, int segment = 0)
    {
        // Select the correct event table based on danger bracket
        int bracket = dangerLevel switch
        {
            <= 1 => 0,
            <= 3 => 1,
            _    => 2,
        };

        var table = EventTables[bracket];

        // Calculate total weight for this table
        int totalWeight = 0;
        foreach (var (_, weight) in table)
            totalWeight += weight;

        // Roll
        int roll = _rng.Next(totalWeight);
        TravelEventType selectedType = TravelEventType.Nothing;
        int cumulative = 0;

        foreach (var (type, weight) in table)
        {
            cumulative += weight;
            if (roll < cumulative)
            {
                selectedType = type;
                break;
            }
        }

        var travelEvent = new TravelEvent
        {
            Type = selectedType,
            Segment = segment,
            Description = GetFallbackEventDescription(selectedType),
        };

        return travelEvent;
    }

    /// <summary>
    /// Generate LLM narration for the travel journey. Falls back to template-based
    /// descriptions if the LLM is unavailable or fails.
    /// </summary>
    public async Task<string> GetTravelNarration(
        string fromName,
        string toName,
        List<TravelEvent> events,
        int travelTimeHours,
        int dangerLevel,
        bool wasInterrupted,
        CancellationToken cancellationToken = default)
    {
        if (_llmService == null)
            return BuildFallbackNarration(fromName, toName, events, travelTimeHours, wasInterrupted);

        var messages = BuildTravelNarrationPrompt(fromName, toName, events, travelTimeHours, dangerLevel, wasInterrupted);

        try
        {
            var response = await _llmService.GenerateAsync(messages, jsonMode: true, cancellationToken);

            if (response.IsSuccess && response.ParsedJson != null)
            {
                string narration = response.ParsedJson["narration"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(narration))
                    return narration;
            }

            // Fallback: use raw text if JSON parsing failed but we got text
            if (response.IsSuccess && !string.IsNullOrWhiteSpace(response.RawText))
                return response.RawText;

            GD.PrintErr($"[TravelManager] LLM narration failed: {response.ErrorMessage}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TravelManager] GetTravelNarration error: {ex.Message}");
        }

        return BuildFallbackNarration(fromName, toName, events, travelTimeHours, wasInterrupted);
    }

    // -----------------------------------------------------------------
    // Encounter generation
    // -----------------------------------------------------------------

    /// <summary>
    /// Attempts to generate a combat encounter for a travel segment.
    /// Uses the encounter table from the more dangerous of the two locations.
    /// Returns null if no encounter could be generated.
    /// </summary>
    private EncounterData? TryGenerateEncounter(
        Location from, Location to, int routeDanger, int partyLevel, int partySize)
    {
        // Prefer the encounter table from the more dangerous location along the route
        string tableId = !string.IsNullOrEmpty(to.EncounterTableId)
            ? to.EncounterTableId
            : from.EncounterTableId;

        if (!string.IsNullOrEmpty(tableId))
        {
            var encounter = _encounterGenerator.GenerateFromTable(tableId, partyLevel, partySize, routeDanger);
            if (encounter != null)
                return encounter;
        }

        // Fallback: generate a generic encounter scaled to the party
        var difficulty = routeDanger switch
        {
            <= 1 => EncounterDifficulty.Easy,
            2    => EncounterDifficulty.Easy,
            3    => EncounterDifficulty.Medium,
            4    => EncounterDifficulty.Hard,
            _    => EncounterDifficulty.Hard,
        };

        return _encounterGenerator.GenerateEncounter(partyLevel, partySize, difficulty);
    }

    // -----------------------------------------------------------------
    // Discovery
    // -----------------------------------------------------------------

    /// <summary>
    /// Attempts to find an undiscovered location connected to either the
    /// origin or destination that could be discovered during travel.
    /// Returns the location ID, or empty string if nothing is available.
    /// </summary>
    private string TryFindDiscoverableLocation(string fromId, string toId)
    {
        var candidates = new List<string>();

        // Check all locations connected to origin and destination
        CollectUndiscoveredNeighbors(fromId, candidates);
        CollectUndiscoveredNeighbors(toId, candidates);

        if (candidates.Count == 0)
            return string.Empty;

        // Pick one at random
        return candidates[_rng.Next(candidates.Count)];
    }

    private void CollectUndiscoveredNeighbors(string locationId, List<string> candidates)
    {
        var allConnected = _worldMap.GetAllConnectedLocations(locationId);
        foreach (var loc in allConnected)
        {
            if (!loc.IsDiscovered && !candidates.Contains(loc.Id))
            {
                // Only discover locations with no special requirements
                // (level/quest-gated locations should not be randomly found)
                if (string.IsNullOrWhiteSpace(loc.DiscoveryRequirement))
                    candidates.Add(loc.Id);
            }
        }
    }

    // -----------------------------------------------------------------
    // LLM prompt building
    // -----------------------------------------------------------------

    private static List<LLMMessage> BuildTravelNarrationPrompt(
        string fromName,
        string toName,
        List<TravelEvent> events,
        int travelTimeHours,
        int dangerLevel,
        bool wasInterrupted)
    {
        string dangerDesc = dangerLevel switch
        {
            0 => "safe, well-patrolled",
            1 => "relatively safe with occasional wildlife",
            2 => "moderately dangerous with bandits and predators",
            3 => "dangerous wild territory",
            4 => "very dangerous, few travel this way",
            _ => "deadly and forsaken by civilization",
        };

        // Build event summary for the LLM
        var eventLines = new List<string>();
        foreach (var evt in events)
        {
            if (evt.Type != TravelEventType.Nothing)
                eventLines.Add($"- {evt.Type}: {evt.Description}");
        }

        string eventSummary = eventLines.Count > 0
            ? string.Join("\n", eventLines)
            : "- No notable events occurred.";

        string systemPrompt =
            "You are a Dungeon Master narrating a travel sequence in a D&D 5e campaign. " +
            "Write an atmospheric, immersive description of the journey. Include sensory details " +
            "(weather, terrain, sounds, time of day progression). Weave the listed events naturally " +
            "into the narrative. Keep it to 2-3 paragraphs.\n\n" +
            "You MUST respond with ONLY a JSON object matching this exact schema:\n" +
            "{\n" +
            "  \"narration\": \"<travel narrative text>\",\n" +
            "  \"weather\": \"<one word: clear|cloudy|rainy|stormy|foggy|snowy>\",\n" +
            "  \"time_of_day\": \"<one word: dawn|morning|midday|afternoon|dusk|night>\"\n" +
            "}\n\n" +
            "Do not include any text outside the JSON object.";

        string interrupted = wasInterrupted
            ? "\nIMPORTANT: The journey was CUT SHORT by a hostile encounter. " +
              "End the narration at the moment of ambush, building tension."
            : "\nThe party arrived safely at their destination.";

        string userContent =
            $"=== TRAVEL ===\n" +
            $"From: {fromName}\n" +
            $"To: {toName}\n" +
            $"Travel time: approximately {travelTimeHours} hours\n" +
            $"Route danger: {dangerDesc}\n" +
            $"{interrupted}\n\n" +
            $"=== EVENTS DURING TRAVEL ===\n{eventSummary}";

        return new List<LLMMessage>
        {
            new LLMMessage("system", systemPrompt),
            new LLMMessage("user", userContent)
        };
    }

    // -----------------------------------------------------------------
    // Fallback narration (no LLM)
    // -----------------------------------------------------------------

    private string BuildFallbackNarration(
        string fromName,
        string toName,
        List<TravelEvent> events,
        int travelTimeHours,
        bool wasInterrupted)
    {
        // Pick a terrain description
        string terrain = TerrainDescriptions[_rng.Next(TerrainDescriptions.Length)];

        // Build event summary sentences
        var eventSentences = new List<string>();
        foreach (var evt in events)
        {
            if (evt.Type != TravelEventType.Nothing && !string.IsNullOrWhiteSpace(evt.Description))
                eventSentences.Add(evt.Description);
        }

        string eventText;
        if (wasInterrupted)
        {
            eventText = eventSentences.Count > 0
                ? string.Join(" ", eventSentences)
                : "Hostile creatures block the path ahead!";
        }
        else if (eventSentences.Count > 0)
        {
            eventText = string.Join(" ", eventSentences) + " Despite these events, the party arrives safely.";
        }
        else
        {
            eventText = "the party arrives without incident.";
        }

        // Pick a template and fill it
        string template = FallbackNarrativeTemplates[_rng.Next(FallbackNarrativeTemplates.Length)];
        return string.Format(template, fromName, toName, travelTimeHours, terrain, eventText);
    }

    // -----------------------------------------------------------------
    // Fallback event descriptions
    // -----------------------------------------------------------------

    private string GetFallbackEventDescription(TravelEventType type)
    {
        return type switch
        {
            TravelEventType.Nothing          => "The road is quiet and uneventful.",
            TravelEventType.WeatherChange    => PickRandom(FallbackWeather),
            TravelEventType.TerrainHazard    => PickRandom(FallbackHazards),
            TravelEventType.Merchant         => PickRandom(FallbackMerchant),
            TravelEventType.Shrine           => PickRandom(FallbackShrine),
            TravelEventType.TravellerMeeting => PickRandom(FallbackTraveller),
            TravelEventType.Discovery        => PickRandom(FallbackDiscovery),
            TravelEventType.RandomEncounter  => "Danger lurks on the road ahead.",
            _                                => "Something stirs in the distance.",
        };
    }

    private string PickRandom(string[] pool) => pool[_rng.Next(pool.Length)];

    private static string BuildEncounterDescription(EncounterData encounter)
    {
        if (encounter.Monsters.Count == 0)
            return "Hostile creatures block the path!";

        if (encounter.Monsters.Count == 1)
        {
            var m = encounter.Monsters[0];
            return m.Count == 1
                ? $"A {m.MonsterName} emerges from the shadows and attacks!"
                : $"{m.Count} {m.MonsterName}s block the road and prepare to attack!";
        }

        // Multiple monster types
        var parts = new List<string>();
        foreach (var m in encounter.Monsters)
        {
            parts.Add(m.Count == 1 ? $"a {m.MonsterName}" : $"{m.Count} {m.MonsterName}s");
        }

        return $"The party is ambushed by {string.Join(" and ", parts)}!";
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static int CalculateAverageLevel(List<Character>? party)
    {
        if (party == null || party.Count == 0)
            return 1;

        int totalLevel = 0;
        foreach (var c in party)
            totalLevel += c.Level;

        return Math.Max(1, totalLevel / party.Count);
    }
}
