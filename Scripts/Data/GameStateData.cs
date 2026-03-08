namespace ChroniclesUnbound.Data;

/// <summary>
/// DTO for persisted game state associated with a save slot.
/// </summary>
public class GameStateData
{
    public int Id { get; set; }
    public int SaveSlotId { get; set; }
    public string? CurrentPhase { get; set; }
    public string? CurrentLocation { get; set; }
    public string? CampaignDataJson { get; set; }
}
