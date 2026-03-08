using System.Collections.Generic;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Types of events that can occur during travel between locations.
/// </summary>
public enum TravelEventType
{
    /// <summary>Nothing eventful happens during this travel segment.</summary>
    Nothing,

    /// <summary>A hostile encounter occurs on the road.</summary>
    RandomEncounter,

    /// <summary>Weather shifts (storm, fog, downpour, etc.).</summary>
    WeatherChange,

    /// <summary>Environmental hazard (rockslide, flooding, sinkhole, etc.).</summary>
    TerrainHazard,

    /// <summary>A traveling merchant is encountered.</summary>
    Merchant,

    /// <summary>A roadside shrine offering a minor boon.</summary>
    Shrine,

    /// <summary>Another traveler or group is met on the road.</summary>
    TravellerMeeting,

    /// <summary>A hidden or previously unknown location is discovered nearby.</summary>
    Discovery
}

/// <summary>
/// A single event that occurred during one segment of travel.
/// </summary>
public class TravelEvent
{
    /// <summary>What type of event this is.</summary>
    public TravelEventType Type { get; set; } = TravelEventType.Nothing;

    /// <summary>Human-readable description of what happened.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// If this event is a RandomEncounter, contains the generated encounter data.
    /// Null for all other event types.
    /// </summary>
    public EncounterData? EncounterData { get; set; }

    /// <summary>
    /// If this event is a Discovery, contains the ID of the newly discovered location.
    /// Null/empty for all other event types.
    /// </summary>
    public string DiscoveredLocationId { get; set; } = string.Empty;

    /// <summary>
    /// Which travel segment (0-based) this event occurred in.
    /// Useful for ordering events in the narrative.
    /// </summary>
    public int Segment { get; set; }
}

/// <summary>
/// Complete result of a travel action between two locations.
/// Contains all events that occurred, the destination, and LLM narration.
/// </summary>
public class TravelResult
{
    /// <summary>All events that occurred during the journey, in order.</summary>
    public List<TravelEvent> Events { get; set; } = new();

    /// <summary>The location ID the party arrived at (or was heading to).</summary>
    public string DestinationId { get; set; } = string.Empty;

    /// <summary>The location ID the party departed from.</summary>
    public string OriginId { get; set; } = string.Empty;

    /// <summary>Total travel time in hours.</summary>
    public int TravelTimeHours { get; set; }

    /// <summary>
    /// LLM-generated or fallback narrative describing the journey.
    /// Includes weather, terrain, atmosphere, and event summaries.
    /// </summary>
    public string NarrativeDescription { get; set; } = string.Empty;

    /// <summary>Whether the journey was interrupted by a combat encounter.</summary>
    public bool WasInterrupted { get; set; }

    /// <summary>True if the party arrived at the destination without interruption.</summary>
    public bool Arrived => !WasInterrupted;

    /// <summary>Number of non-nothing events that occurred.</summary>
    public int EventCount
    {
        get
        {
            int count = 0;
            foreach (var e in Events)
            {
                if (e.Type != TravelEventType.Nothing)
                    count++;
            }
            return count;
        }
    }
}
