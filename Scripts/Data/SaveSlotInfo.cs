namespace ChroniclesUnbound.Data;

/// <summary>
/// Lightweight DTO for displaying save slot metadata in the UI.
/// </summary>
public class SaveSlotInfo
{
    public int Id { get; set; }
    public string SlotName { get; set; } = string.Empty;
    public string? CharacterName { get; set; }
    public int CharacterLevel { get; set; } = 1;
    public int PlayTimeSeconds { get; set; }
    public string? UpdatedAt { get; set; }

    /// <summary>
    /// Returns play time formatted as "Xh Ym" for display.
    /// </summary>
    public string FormattedPlayTime
    {
        get
        {
            int hours = PlayTimeSeconds / 3600;
            int minutes = (PlayTimeSeconds % 3600) / 60;
            return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
        }
    }
}
