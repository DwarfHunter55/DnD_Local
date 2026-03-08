namespace ChroniclesUnbound.Data;

/// <summary>
/// DTO for a tracked story thread used by the narrative coherence system.
/// </summary>
public class StoryThreadData
{
    public int Id { get; set; }
    public int SaveSlotId { get; set; }
    public string? ThreadType { get; set; }
    public string? Description { get; set; }
    public int IntroducedChapter { get; set; }
    public string Status { get; set; } = "active";
    public string Priority { get; set; } = "medium";
}
