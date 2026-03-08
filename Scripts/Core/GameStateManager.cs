using Godot;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Central game state coordinator. Autoloaded singleton accessible via
/// GetNode("/root/GameStateManager") or the typed accessor below.
/// Manages references to all major subsystem managers.
/// </summary>
public partial class GameStateManager : Node
{
    public static GameStateManager? Instance { get; private set; }

    /// <summary>
    /// Reference to the active GameLoop node, set by GameLoop._Ready().
    /// Null when no game session is running (e.g. still on main menu).
    /// </summary>
    public GameLoop? ActiveGameLoop { get; set; }

    /// <summary>
    /// The save slot ID for the currently active game session.
    /// Null when no save has been loaded or created yet (new game in progress).
    /// Set by SaveLoadBridge after a successful load or first save.
    /// </summary>
    public int? ActiveSaveSlotId { get; set; }

    // Future manager references — will be instantiated as development progresses.
    // public PartyManager? Party { get; private set; }
    // public CombatManager? Combat { get; private set; }
    // public CampaignManager? Campaign { get; private set; }
    // public LLMManager? LLM { get; private set; }
    // public CompanionManager? Companions { get; private set; }
    // public PersistenceManager? Persistence { get; private set; }

    public enum GamePhase
    {
        MainMenu,
        CharacterCreation,
        Exploration,
        Dialogue,
        Combat,
        Rest,
        LevelUp,
        Inventory,
        Journal
    }

    public GamePhase CurrentPhase { get; private set; } = GamePhase.MainMenu;

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[GameStateManager] Initialized.");
    }

    /// <summary>
    /// Transition to a new game phase. Emits PhaseChanged signal
    /// so UI and subsystems can react.
    /// </summary>
    public void SetPhase(GamePhase newPhase)
    {
        if (newPhase == CurrentPhase)
            return;

        var previous = CurrentPhase;
        CurrentPhase = newPhase;
        GD.Print($"[GameStateManager] Phase: {previous} -> {newPhase}");
        EmitSignal(SignalName.PhaseChanged, (int)previous, (int)newPhase);
    }

    [Signal]
    public delegate void PhaseChangedEventHandler(int previousPhase, int newPhase);

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }
}
