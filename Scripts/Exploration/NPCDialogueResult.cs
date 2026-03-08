namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Detected intent from an NPC dialogue response.
/// Used to trigger game systems (quest offers, shop transitions, etc.)
/// after the dialogue text is displayed.
/// </summary>
public enum DialogueIntent
{
    /// <summary>Normal conversation, no special action needed.</summary>
    Chat,
    /// <summary>NPC is offering or discussing a quest.</summary>
    QuestOffer,
    /// <summary>NPC is providing quest-related information or updates.</summary>
    QuestInfo,
    /// <summary>NPC is offering to open their shop.</summary>
    OpenShop,
    /// <summary>NPC is giving directions or information about a location.</summary>
    LocationHint,
    /// <summary>NPC is sharing a rumor or lore.</summary>
    Rumor,
    /// <summary>NPC is ending the conversation.</summary>
    Farewell,
    /// <summary>NPC is hostile or refuses to talk.</summary>
    Hostile
}

/// <summary>
/// Result of an NPC dialogue exchange. Contains the NPC's response text,
/// detected intent for game system routing, and optional quest linkage.
/// </summary>
public class NPCDialogueResult
{
    /// <summary>Display name of the speaking NPC.</summary>
    public string SpeakerName { get; set; } = string.Empty;

    /// <summary>The NPC's dialogue response text.</summary>
    public string ResponseText { get; set; } = string.Empty;

    /// <summary>
    /// Detected intent of the response. The caller can use this to
    /// trigger follow-up game actions (e.g. opening a quest log or shop).
    /// </summary>
    public DialogueIntent DetectedIntent { get; set; } = DialogueIntent.Chat;

    /// <summary>
    /// Quest ID related to this dialogue, if any. Non-null when the NPC
    /// is offering a quest or discussing quest objectives.
    /// </summary>
    public string? RelatedQuestId { get; set; }
}
