using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ChroniclesUnbound.Exploration;

/// <summary>
/// Roles an NPC can fill in the world. Determines available interactions
/// and how the NPC is presented in the UI.
/// </summary>
public enum NPCRole
{
    Shopkeeper,
    QuestGiver,
    Innkeeper,
    Blacksmith,
    Sage,
    Guard,
    Villager,
    Priest,
    Ranger,
    Druid,
    Merchant,
    Noble
}

/// <summary>
/// Data class representing a single NPC in the world.
/// Loaded from npcs.json. All properties use [JsonProperty] for consistent
/// snake_case serialization matching the project's data conventions.
/// </summary>
public class NPCData
{
    [JsonProperty("npc_id")]
    public string NpcId { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Title or occupation displayed alongside the name (e.g. "Mayor", "Blacksmith").
    /// </summary>
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("role")]
    [JsonConverter(typeof(StringEnumConverter))]
    public NPCRole Role { get; set; } = NPCRole.Villager;

    /// <summary>
    /// ID of the location where this NPC resides.
    /// Must match a Location.Id in locations.json.
    /// </summary>
    [JsonProperty("location_id")]
    public string LocationId { get; set; } = string.Empty;

    /// <summary>
    /// Personality description used as context for LLM dialogue generation.
    /// Should capture speaking style, temperament, and motivations.
    /// </summary>
    [JsonProperty("personality")]
    public string Personality { get; set; } = string.Empty;

    /// <summary>
    /// Default greeting line spoken when the player first initiates dialogue.
    /// Used as a fallback when the LLM is unavailable.
    /// </summary>
    [JsonProperty("greeting")]
    public string Greeting { get; set; } = string.Empty;

    /// <summary>
    /// Topics this NPC can discuss. Fed to the LLM as conversation seeds
    /// and displayed as dialogue options in the UI.
    /// </summary>
    [JsonProperty("dialogue_hooks")]
    public List<string> DialogueHooks { get; set; } = new();

    /// <summary>
    /// IDs of quests this NPC can give or is involved in.
    /// Used to surface quest-related dialogue options.
    /// </summary>
    [JsonProperty("associated_quest_ids")]
    public List<string> AssociatedQuestIds { get; set; } = new();

    /// <summary>
    /// ID of the shop this NPC runs, if any. Null or empty for non-shopkeepers.
    /// Must match a ShopInventory.ShopId in shops.json.
    /// </summary>
    [JsonProperty("associated_shop_id")]
    public string? AssociatedShopId { get; set; }

    /// <summary>
    /// Display string combining name and title (e.g. "Mayor Harlan").
    /// </summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(Title) ? Name : $"{Title} {Name}";

    public override string ToString() => $"{DisplayName} ({Role}, at {LocationId})";
}
