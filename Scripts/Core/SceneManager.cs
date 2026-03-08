using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Scene navigation singleton. Autoloaded via project.godot.
/// Manages scene transitions with fade in/out, scene caching, and a modal
/// scene stack for overlay screens.
///
/// Expects the Main scene to contain:
///   TransitionLayer (CanvasLayer with AnimationPlayer + ColorRect)
///   UILayer (CanvasLayer where content scenes are instanced)
///   GameLoop (Node)
/// </summary>
public partial class SceneManager : Node
{
    public static SceneManager? Instance { get; private set; }

    // ----------------------------------------------------------------
    // Scene path constants
    // ----------------------------------------------------------------

    public static class Scenes
    {
        public const string MainMenu          = "res://Scenes/UI/MainMenu.tscn";
        public const string CharacterCreation = "res://Scenes/UI/CharacterCreation.tscn";
        public const string LoadGame          = "res://Scenes/UI/LoadGame.tscn";
        public const string NarrativeUI       = "res://Scenes/UI/NarrativeUI.tscn";
        public const string ExplorationUI     = "res://Scenes/UI/ExplorationUI.tscn";
        public const string CombatUI          = "res://Scenes/Combat/CombatUI.tscn";
        public const string InventoryUI       = "res://Scenes/UI/InventoryUI.tscn";
        public const string JournalUI        = "res://Scenes/UI/JournalUI.tscn";
    }

    // ----------------------------------------------------------------
    // Signals
    // ----------------------------------------------------------------

    [Signal]
    public delegate void SceneChangedEventHandler(string previousScene, string newScene);

    // ----------------------------------------------------------------
    // Cached state
    // ----------------------------------------------------------------

    private readonly Dictionary<string, PackedScene> _sceneCache = new();
    private readonly Stack<string> _sceneStack = new();

    private CanvasLayer? _uiLayer;
    private AnimationPlayer? _animator;
    private Node? _currentSceneInstance;
    private string _currentScenePath = "";
    private bool _isTransitioning;

    private const float FadeDuration = 0.3f;

    // ----------------------------------------------------------------
    // Lifecycle
    // ----------------------------------------------------------------

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[SceneManager] Initialized.");

        // Defer finding nodes until the Main scene tree is fully built.
        CallDeferred(MethodName.InitializeFromMainScene);

        // Subscribe to phase changes from GameStateManager once it is available.
        CallDeferred(MethodName.SubscribeToPhaseChanges);
    }

    /// <summary>
    /// Deferred subscription to GameStateManager.PhaseChanged.
    /// Called after _Ready so the autoload is guaranteed to exist.
    /// </summary>
    private void SubscribeToPhaseChanges()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            GD.PrintErr("[SceneManager] GameStateManager not found — phase-based transitions disabled.");
            return;
        }

        gsm.PhaseChanged += OnPhaseChanged;
        GD.Print("[SceneManager] Subscribed to GameStateManager.PhaseChanged.");
    }

    /// <summary>
    /// Locate the UILayer and TransitionLayer from the Main scene.
    /// Then load the initial scene (MainMenu).
    /// </summary>
    private async void InitializeFromMainScene()
    {
        try
        {
            var root = GetTree().Root;

            // Main is the first child of root (the main scene).
            var main = root.GetNodeOrNull("Main");
            if (main == null)
            {
                GD.PrintErr("[SceneManager] Could not find Main node in scene tree.");
                return;
            }

            _uiLayer = main.GetNodeOrNull<CanvasLayer>("UILayer");
            if (_uiLayer == null)
            {
                GD.PrintErr("[SceneManager] Could not find UILayer in Main scene.");
                return;
            }

            // TransitionOverlay is instanced as a child of Main.
            // Its structure: TransitionLayer (CanvasLayer) > ColorRect + AnimationPlayer
            var transitionLayer = main.GetNodeOrNull("TransitionLayer");
            if (transitionLayer != null)
            {
                _animator = transitionLayer.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            }

            if (_animator == null)
            {
                GD.PrintErr("[SceneManager] TransitionOverlay AnimationPlayer not found. " +
                            "Transitions will be instant.");
            }

            GD.Print("[SceneManager] Main scene references acquired.");

            // Load the first scene with no fade at all — there is nothing to
            // fade from and the fade_in animation would snap the overlay to
            // opaque black on its first keyframe, hiding the UI if the signal
            // await stalls.
            await ChangeSceneInternal(Scenes.MainMenu, fadeOut: false, fadeIn: false);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[SceneManager] InitializeFromMainScene failed: {ex}");
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    // ----------------------------------------------------------------
    // Public API
    // ----------------------------------------------------------------

    /// <summary>
    /// Transition to a new scene with fade out/in. Replaces the current scene.
    /// Clears the scene stack.
    /// </summary>
    public async void ChangeScene(string scenePath)
    {
        if (_isTransitioning)
        {
            GD.PrintErr($"[SceneManager] Transition already in progress, ignoring ChangeScene({scenePath}).");
            return;
        }

        _sceneStack.Clear();
        await ChangeSceneInternal(scenePath, fadeOut: true);
    }

    /// <summary>
    /// Push a scene on top of the current one (for modal overlays).
    /// The current scene is hidden, not freed.
    /// </summary>
    public async void PushScene(string scenePath)
    {
        if (_isTransitioning)
        {
            GD.PrintErr($"[SceneManager] Transition already in progress, ignoring PushScene({scenePath}).");
            return;
        }

        if (!string.IsNullOrEmpty(_currentScenePath))
        {
            _sceneStack.Push(_currentScenePath);
        }

        // Hide the current scene instead of freeing it.
        if (_currentSceneInstance != null && IsInstanceValid(_currentSceneInstance))
        {
            _currentSceneInstance.Set("visible", false);
        }

        _isTransitioning = true;

        await FadeOut();

        var instance = InstantiateScene(scenePath);
        if (instance == null)
        {
            _isTransitioning = false;
            return;
        }

        string previousPath = _currentScenePath;
        _currentSceneInstance = instance;
        _currentScenePath = scenePath;
        _uiLayer!.AddChild(instance);

        ConnectSceneSignals(instance, scenePath);
        UpdateGamePhase(scenePath);

        await FadeIn();
        _isTransitioning = false;

        EmitSignal(SignalName.SceneChanged, previousPath, scenePath);
        GD.Print($"[SceneManager] Pushed scene: {scenePath}");
    }

    /// <summary>
    /// Pop the top scene and return to the previous one.
    /// </summary>
    public async void PopScene()
    {
        if (_isTransitioning)
        {
            GD.PrintErr("[SceneManager] Transition already in progress, ignoring PopScene().");
            return;
        }

        if (_sceneStack.Count == 0)
        {
            GD.PrintErr("[SceneManager] Scene stack is empty, cannot pop.");
            return;
        }

        _isTransitioning = true;

        await FadeOut();

        // Free the current overlay scene.
        if (_currentSceneInstance != null && IsInstanceValid(_currentSceneInstance))
        {
            _currentSceneInstance.QueueFree();
            _currentSceneInstance = null;
        }

        string previousPath = _currentScenePath;
        string restoredPath = _sceneStack.Pop();
        _currentScenePath = restoredPath;

        // Find the hidden scene instance in UILayer and show it.
        // It is the last child that was hidden.
        _currentSceneInstance = FindHiddenSceneInUILayer();
        if (_currentSceneInstance != null)
        {
            _currentSceneInstance.Set("visible", true);
        }

        UpdateGamePhase(restoredPath);

        await FadeIn();
        _isTransitioning = false;

        EmitSignal(SignalName.SceneChanged, previousPath, restoredPath);
        GD.Print($"[SceneManager] Popped to scene: {restoredPath}");
    }

    /// <summary>
    /// The resource path of the currently active scene.
    /// </summary>
    public string CurrentScenePath => _currentScenePath;

    /// <summary>
    /// Whether a transition is currently playing.
    /// </summary>
    public bool IsTransitioning => _isTransitioning;

    // ----------------------------------------------------------------
    // Internal transition logic
    // ----------------------------------------------------------------

    private async Task ChangeSceneInternal(string scenePath, bool fadeOut, bool fadeIn = true)
    {
        _isTransitioning = true;

        if (fadeOut)
        {
            await FadeOut();
        }

        // Free any existing scene(s) in UILayer.
        FreeAllScenesInUILayer();

        var instance = InstantiateScene(scenePath);
        if (instance == null)
        {
            _isTransitioning = false;
            return;
        }

        string previousPath = _currentScenePath;
        _currentSceneInstance = instance;
        _currentScenePath = scenePath;
        _uiLayer!.AddChild(instance);

        ConnectSceneSignals(instance, scenePath);
        UpdateGamePhase(scenePath);

        if (fadeIn)
        {
            await FadeIn();
        }
        _isTransitioning = false;

        EmitSignal(SignalName.SceneChanged, previousPath, scenePath);
        GD.Print($"[SceneManager] Scene changed: {previousPath} -> {scenePath}");
    }

    // ----------------------------------------------------------------
    // Scene caching and instantiation
    // ----------------------------------------------------------------

    /// <summary>
    /// Pre-load a scene into the cache. Useful for warming up scenes
    /// that will be needed soon (e.g. CombatUI when entering a dungeon).
    /// </summary>
    public void PreloadScene(string scenePath)
    {
        if (_sceneCache.ContainsKey(scenePath))
            return;

        var packed = GD.Load<PackedScene>(scenePath);
        if (packed != null)
        {
            _sceneCache[scenePath] = packed;
            GD.Print($"[SceneManager] Preloaded: {scenePath}");
        }
        else
        {
            GD.PrintErr($"[SceneManager] Failed to preload: {scenePath}");
        }
    }

    /// <summary>
    /// Remove a scene from the cache to free memory.
    /// </summary>
    public void EvictScene(string scenePath)
    {
        _sceneCache.Remove(scenePath);
    }

    private Node? InstantiateScene(string scenePath)
    {
        if (!_sceneCache.TryGetValue(scenePath, out var packed))
        {
            packed = GD.Load<PackedScene>(scenePath);
            if (packed == null)
            {
                GD.PrintErr($"[SceneManager] Failed to load scene: {scenePath}");
                return null;
            }
            _sceneCache[scenePath] = packed;
        }

        return packed.Instantiate();
    }

    // ----------------------------------------------------------------
    // Fade helpers
    // ----------------------------------------------------------------

    private async Task FadeOut()
    {
        if (_animator == null || !IsInstanceValid(_animator))
            return;

        _animator.Play("fade_out");
        await ToSignal(_animator, AnimationPlayer.SignalName.AnimationFinished);
    }

    private async Task FadeIn()
    {
        if (_animator == null || !IsInstanceValid(_animator))
            return;

        _animator.Play("fade_in");
        await ToSignal(_animator, AnimationPlayer.SignalName.AnimationFinished);
    }

    // ----------------------------------------------------------------
    // Scene signal wiring
    // ----------------------------------------------------------------

    /// <summary>
    /// Connect signals from instanced scenes to SceneManager handlers.
    /// This allows scenes to request navigation without knowing about SceneManager directly.
    /// Scenes emit signals like "start_new_game", "load_game", "return_to_menu", etc.
    /// </summary>
    private void ConnectSceneSignals(Node instance, string scenePath)
    {
        // MainMenu signals
        if (scenePath == Scenes.MainMenu)
        {
            TryConnect(instance, "new_game_pressed", Callable.From(OnNewGamePressed));
            TryConnect(instance, "continue_pressed", Callable.From(OnContinuePressed));
            TryConnect(instance, "load_game_pressed", Callable.From(OnLoadGamePressed));
            TryConnect(instance, "quit_pressed", Callable.From(OnQuitPressed));
        }
        // CharacterCreation signals
        else if (scenePath == Scenes.CharacterCreation)
        {
            TryConnect(instance, "character_created", Callable.From(OnCharacterCreated));
            TryConnect(instance, "creation_cancelled", Callable.From(OnCreationCancelled));
        }
        // LoadGame signals
        else if (scenePath == Scenes.LoadGame)
        {
            TryConnect(instance, "save_selected", Callable.From<int>(OnSaveSelected));
            TryConnect(instance, "back_pressed", Callable.From(OnBackToMenu));
        }
        // NarrativeUI — initialize GameLoop and listen for menu navigation.
        else if (scenePath == Scenes.NarrativeUI)
        {
            InitializeGameLoop(instance);
            TryConnect(instance, "menu_pressed", Callable.From(OnMenuPressedFromGame));
        }
        // ExplorationUI — initialize ExplorationBridge and listen for navigation.
        else if (scenePath == Scenes.ExplorationUI)
        {
            InitializeExplorationBridge(instance);
            TryConnect(instance, "menu_pressed", Callable.From(OnMenuPressedFromGame));
        }
        // CombatUI — initialize CombatBridge to manage the overlay.
        else if (scenePath == Scenes.CombatUI)
        {
            InitializeCombatBridge(instance);
        }
        // InventoryUI — initialize InventoryBridge to manage the overlay.
        else if (scenePath == Scenes.InventoryUI)
        {
            InitializeInventoryBridge(instance);
        }
        // JournalUI — initialize JournalBridge to manage the overlay.
        else if (scenePath == Scenes.JournalUI)
        {
            InitializeJournalBridge(instance);
        }
    }

    /// <summary>
    /// Safely attempt to connect a signal. Logs a warning if the signal doesn't exist
    /// (scene may not have that signal yet during development).
    /// </summary>
    private static void TryConnect(Node source, string signalName, Callable callable)
    {
        if (source.HasSignal(signalName))
        {
            source.Connect(signalName, callable);
        }
        else
        {
            GD.Print($"[SceneManager] Signal '{signalName}' not found on {source.Name} — skipping connection.");
        }
    }

    // ----------------------------------------------------------------
    // Navigation handlers (called from scene signals)
    // ----------------------------------------------------------------

    private void OnNewGamePressed()
    {
        GD.Print("[SceneManager] New Game requested.");
        ChangeScene(Scenes.CharacterCreation);
    }

    private void OnLoadGamePressed()
    {
        GD.Print("[SceneManager] Load Game requested.");
        ChangeScene(Scenes.LoadGame);
    }

    private void OnQuitPressed()
    {
        GD.Print("[SceneManager] Quit requested.");
        GetTree().Quit();
    }

    private void OnCharacterCreated()
    {
        GD.Print("[SceneManager] Character created. Starting game.");
        ChangeScene(Scenes.NarrativeUI);
    }

    private void OnCreationCancelled()
    {
        GD.Print("[SceneManager] Character creation cancelled.");
        ChangeScene(Scenes.MainMenu);
    }

    private void OnContinuePressed()
    {
        GD.Print("[SceneManager] Continue requested — loading latest save.");
        SaveLoadBridge.Instance?.LoadLatestSave();
    }

    private void OnSaveSelected(int slotId)
    {
        GD.Print($"[SceneManager] Loading save slot: {slotId}");
        SaveLoadBridge.Instance?.LoadGame(slotId);
    }

    private void OnBackToMenu()
    {
        GD.Print("[SceneManager] Returning to main menu.");
        ChangeScene(Scenes.MainMenu);
    }

    /// <summary>
    /// Called when the player clicks the Menu button during gameplay (NarrativeUI).
    /// Navigates back to the main menu, clearing the scene stack.
    /// </summary>
    private void OnMenuPressedFromGame()
    {
        GD.Print("[SceneManager] Menu pressed from gameplay — returning to main menu.");
        ChangeScene(Scenes.MainMenu);
    }

    /// <summary>
    /// Finds the GameLoop node under Main and calls Initialize() with the
    /// given NarrativeUI instance. Called after the NarrativeUI scene has been
    /// instanced and added to the tree.
    /// </summary>
    private static void InitializeGameLoop(Node narrativeUIInstance)
    {
        var gameLoop = ((SceneTree)Engine.GetMainLoop()).Root.GetNodeOrNull<GameLoop>("Main/GameLoop");
        if (gameLoop == null)
        {
            GD.PrintErr("[SceneManager] Could not find GameLoop at /root/Main/GameLoop.");
            return;
        }

        gameLoop.Initialize(narrativeUIInstance);
    }

    /// <summary>
    /// Finds or creates an ExplorationBridge node under /root/Main and calls
    /// Initialize() with the given ExplorationUI instance.
    /// </summary>
    private static void InitializeExplorationBridge(Node explorationUIInstance)
    {
        var main = ((SceneTree)Engine.GetMainLoop()).Root.GetNodeOrNull("Main");
        if (main == null)
        {
            GD.PrintErr("[SceneManager] Could not find Main node for ExplorationBridge.");
            return;
        }

        var bridge = main.GetNodeOrNull<ExplorationBridge>("ExplorationBridge");
        if (bridge == null)
        {
            bridge = new ExplorationBridge();
            bridge.Name = "ExplorationBridge";
            main.AddChild(bridge);
            GD.Print("[SceneManager] Created ExplorationBridge node under Main.");
        }

        bridge.Initialize(explorationUIInstance);
    }

    /// <summary>
    /// Finds or creates an InventoryBridge node under /root/Main and calls
    /// Initialize() with the given InventoryUI instance.
    /// </summary>
    private void InitializeInventoryBridge(Node inventoryUIInstance)
    {
        var main = ((SceneTree)Engine.GetMainLoop()).Root.GetNodeOrNull("Main");
        if (main == null)
        {
            GD.PrintErr("[SceneManager] Could not find Main node for InventoryBridge.");
            return;
        }

        var bridge = main.GetNodeOrNull<InventoryBridge>("InventoryBridge");
        if (bridge == null)
        {
            bridge = new InventoryBridge();
            bridge.Name = "InventoryBridge";
            main.AddChild(bridge);
            GD.Print("[SceneManager] Created InventoryBridge node under Main.");
        }

        bridge.Initialize(inventoryUIInstance);
    }

    /// <summary>
    /// Finds or creates a JournalBridge node under /root/Main and calls
    /// Initialize() with the given JournalUI instance.
    /// </summary>
    private void InitializeJournalBridge(Node journalUIInstance)
    {
        var main = ((SceneTree)Engine.GetMainLoop()).Root.GetNodeOrNull("Main");
        if (main == null)
        {
            GD.PrintErr("[SceneManager] Could not find Main node for JournalBridge.");
            return;
        }

        var bridge = main.GetNodeOrNull<JournalBridge>("JournalBridge");
        if (bridge == null)
        {
            bridge = new JournalBridge();
            bridge.Name = "JournalBridge";
            main.AddChild(bridge);
            GD.Print("[SceneManager] Created JournalBridge node under Main.");
        }

        bridge.Initialize(journalUIInstance);
    }

    /// <summary>
    /// Finds or creates a CombatBridge node under /root/Main and calls
    /// Initialize() with the given CombatUI instance.
    /// </summary>
    private void InitializeCombatBridge(Node combatUIInstance)
    {
        var main = ((SceneTree)Engine.GetMainLoop()).Root.GetNodeOrNull("Main");
        if (main == null)
        {
            GD.PrintErr("[SceneManager] Could not find Main node for CombatBridge.");
            return;
        }

        var bridge = main.GetNodeOrNull<CombatBridge>("CombatBridge");
        if (bridge == null)
        {
            bridge = new CombatBridge();
            bridge.Name = "CombatBridge";
            main.AddChild(bridge);
            GD.Print("[SceneManager] Created CombatBridge node under Main.");
        }

        bridge.Initialize(combatUIInstance);
    }

    // ----------------------------------------------------------------
    // Phase-driven transitions (reactive to GameStateManager)
    // ----------------------------------------------------------------

    /// <summary>
    /// Tracks the scene path that was active before combat was pushed,
    /// so we can guard against double-pop or mismatched transitions.
    /// </summary>
    private string _preCombatScenePath = "";

    /// <summary>
    /// Tracks the scene path that was active before inventory was pushed,
    /// so we can guard against double-pop or mismatched transitions.
    /// </summary>
    private string _preInventoryScenePath = "";

    /// <summary>
    /// Tracks the scene path that was active before journal was pushed,
    /// so we can guard against double-pop or mismatched transitions.
    /// </summary>
    private string _preJournalScenePath = "";

    /// <summary>
    /// Reacts to GameStateManager phase changes that require scene transitions.
    /// This is the reactive side — external systems call GameStateManager.SetPhase()
    /// and SceneManager responds by pushing/popping the appropriate UI.
    ///
    /// Note: UpdateGamePhase (below) is the proactive side — SceneManager sets the
    /// phase after its own ChangeScene/PushScene calls. To avoid infinite loops,
    /// OnPhaseChanged only acts on transitions that SceneManager did NOT initiate.
    /// </summary>
    private void OnPhaseChanged(int previousPhaseInt, int newPhaseInt)
    {
        var previousPhase = (GameStateManager.GamePhase)previousPhaseInt;
        var newPhase = (GameStateManager.GamePhase)newPhaseInt;

        // Combat entry: push CombatUI on top of the current scene.
        // Only push if the current scene is NOT already CombatUI (avoids double-push
        // when SceneManager itself triggered the phase change via UpdateGamePhase).
        if (newPhase == GameStateManager.GamePhase.Combat
            && _currentScenePath != Scenes.CombatUI)
        {
            GD.Print("[SceneManager] Phase -> Combat. Pushing CombatUI overlay.");
            _preCombatScenePath = _currentScenePath;

            // Tell CombatBridge what phase to restore on close.
            var bridge = ((SceneTree)Engine.GetMainLoop()).Root.GetNodeOrNull<CombatBridge>("Main/CombatBridge");
            bridge?.SetPreviousPhase(previousPhase);

            PushScene(Scenes.CombatUI);
            return;
        }

        // Combat exit: pop back to the exploration/narrative scene.
        // Only pop if we are currently showing CombatUI.
        if (previousPhase == GameStateManager.GamePhase.Combat
            && newPhase != GameStateManager.GamePhase.Combat
            && _currentScenePath == Scenes.CombatUI)
        {
            GD.Print($"[SceneManager] Phase <- Combat. Popping back to {_preCombatScenePath}.");
            PopScene();
            _preCombatScenePath = "";
            return;
        }

        // Inventory entry: push InventoryUI on top of the current scene.
        // Only push if the current scene is NOT already InventoryUI.
        if (newPhase == GameStateManager.GamePhase.Inventory
            && _currentScenePath != Scenes.InventoryUI)
        {
            GD.Print("[SceneManager] Phase -> Inventory. Pushing InventoryUI overlay.");
            _preInventoryScenePath = _currentScenePath;

            // Tell InventoryBridge what phase to restore on close.
            var bridge = ((SceneTree)Engine.GetMainLoop()).Root.GetNodeOrNull<InventoryBridge>("Main/InventoryBridge");
            bridge?.SetPreviousPhase(previousPhase);

            PushScene(Scenes.InventoryUI);
            return;
        }

        // Inventory exit: pop back to the underlying scene.
        // Only pop if we are currently showing InventoryUI.
        if (previousPhase == GameStateManager.GamePhase.Inventory
            && newPhase != GameStateManager.GamePhase.Inventory
            && _currentScenePath == Scenes.InventoryUI)
        {
            GD.Print($"[SceneManager] Phase <- Inventory. Popping back to {_preInventoryScenePath}.");
            PopScene();
            _preInventoryScenePath = "";
            return;
        }

        // Journal entry: push JournalUI on top of the current scene.
        // Only push if the current scene is NOT already JournalUI.
        if (newPhase == GameStateManager.GamePhase.Journal
            && _currentScenePath != Scenes.JournalUI)
        {
            GD.Print("[SceneManager] Phase -> Journal. Pushing JournalUI overlay.");
            _preJournalScenePath = _currentScenePath;

            // Tell JournalBridge what phase to restore on close.
            var bridge = ((SceneTree)Engine.GetMainLoop()).Root.GetNodeOrNull<JournalBridge>("Main/JournalBridge");
            bridge?.SetPreviousPhase(previousPhase);

            PushScene(Scenes.JournalUI);
            return;
        }

        // Journal exit: pop back to the underlying scene.
        // Only pop if we are currently showing JournalUI.
        if (previousPhase == GameStateManager.GamePhase.Journal
            && newPhase != GameStateManager.GamePhase.Journal
            && _currentScenePath == Scenes.JournalUI)
        {
            GD.Print($"[SceneManager] Phase <- Journal. Popping back to {_preJournalScenePath}.");
            PopScene();
            _preJournalScenePath = "";
            return;
        }
    }

    /// <summary>
    /// Public helper for external code to request returning to the main menu.
    /// Shows no confirmation — caller is responsible for any confirmation UI.
    /// </summary>
    public void NavigateToMainMenu()
    {
        GD.Print("[SceneManager] Navigating to main menu.");
        ChangeScene(Scenes.MainMenu);
    }

    // ----------------------------------------------------------------
    // Game phase integration
    // ----------------------------------------------------------------

    /// <summary>
    /// Set the GameStateManager phase based on which scene we're entering.
    /// </summary>
    private static void UpdateGamePhase(string scenePath)
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null)
            return;

        if (scenePath == Scenes.MainMenu || scenePath == Scenes.LoadGame)
            gsm.SetPhase(GameStateManager.GamePhase.MainMenu);
        else if (scenePath == Scenes.CharacterCreation)
            gsm.SetPhase(GameStateManager.GamePhase.CharacterCreation);
        else if (scenePath == Scenes.NarrativeUI || scenePath == Scenes.ExplorationUI)
            gsm.SetPhase(GameStateManager.GamePhase.Exploration);
        else if (scenePath == Scenes.CombatUI)
            gsm.SetPhase(GameStateManager.GamePhase.Combat);
        else if (scenePath == Scenes.InventoryUI)
            gsm.SetPhase(GameStateManager.GamePhase.Inventory);
        else if (scenePath == Scenes.JournalUI)
            gsm.SetPhase(GameStateManager.GamePhase.Journal);
    }

    // ----------------------------------------------------------------
    // UILayer helpers
    // ----------------------------------------------------------------

    private void FreeAllScenesInUILayer()
    {
        if (_uiLayer == null)
            return;

        foreach (var child in _uiLayer.GetChildren())
        {
            child.QueueFree();
        }

        _currentSceneInstance = null;
    }

    private Node? FindHiddenSceneInUILayer()
    {
        if (_uiLayer == null)
            return null;

        // Walk children in reverse to find the most recently added hidden node.
        var children = _uiLayer.GetChildren();
        for (int i = children.Count - 1; i >= 0; i--)
        {
            var child = children[i];
            if (child is CanvasItem ci && !ci.Visible)
                return child;
            // For non-CanvasItem nodes, check the "visible" property via metadata.
            if (!(bool)child.Get("visible"))
                return child;
        }

        return null;
    }
}
