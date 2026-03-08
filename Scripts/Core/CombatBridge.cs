using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ChroniclesUnbound.Combat;
using ChroniclesUnbound.Companions;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Core;

/// <summary>
/// Bridges the GDScript CombatUI with the C# CombatManager, CombatGrid, and
/// CombatGridRenderer. Translates GDScript signals into C# method calls and
/// pushes C# state changes back to the UI via Node.Call().
///
/// Follows the same pattern as ExplorationBridge: created as a child of Main,
/// initialized by SceneManager after the CombatUI scene is instanced.
/// </summary>
public partial class CombatBridge : Node
{
    public static CombatBridge? Instance { get; private set; }

    // -----------------------------------------------------------------
    // Dependencies
    // -----------------------------------------------------------------

    private Node? _combatUI;
    private CombatManager? _combatManager;
    private CombatGrid? _combatGrid;
    private CombatGridRenderer? _combatGridRenderer;

    // -----------------------------------------------------------------
    // Pending action state (two-step: select action -> select target)
    // -----------------------------------------------------------------

    private enum PendingActionType
    {
        None,
        Move,
        Attack,
        CastSpell,
        Help
    }

    private PendingActionType _pendingAction = PendingActionType.None;
    private string? _pendingSpellName;
    private int _pendingSpellLevel;

    /// <summary>
    /// Snapshot of the combatant's position before the current move,
    /// for undo support.
    /// </summary>
    private Vector2I? _preMovePosition;

    /// <summary>
    /// The movement the combatant had before the current move, for undo.
    /// </summary>
    private int _preMoveRemainingMovement;

    // -----------------------------------------------------------------
    // Companion AI state
    // -----------------------------------------------------------------

    /// <summary>
    /// Utility-based combat AI for companion turn decisions.
    /// Created once during Initialize and reused across turns.
    /// </summary>
    private CompanionCombatAI? _companionAI;

    /// <summary>
    /// The current AI plan awaiting player review (Suggest mode).
    /// Null when no review is pending.
    /// </summary>
    private CombatAIPlan? _pendingAIPlan;

    /// <summary>
    /// The combatant whose AI plan is currently pending review.
    /// </summary>
    private Combatant? _pendingReviewCombatant;

    /// <summary>
    /// The game phase that was active before combat was entered,
    /// so we can restore it when combat ends.
    /// </summary>
    private GameStateManager.GamePhase _previousPhase = GameStateManager.GamePhase.Exploration;

    /// <summary>
    /// Tracks the last round number we pushed to the UI, so we can detect round changes
    /// (CombatManager does not emit a RoundChanged signal).
    /// </summary>
    private int _lastPushedRound;

    private bool _initialized;

    // -----------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[CombatBridge] Ready.");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    // -----------------------------------------------------------------
    // Initialization (called by SceneManager after CombatUI is loaded)
    // -----------------------------------------------------------------

    /// <summary>
    /// Wire the bridge to a CombatUI instance. Finds or creates the
    /// CombatManager, connects all cross-language signals.
    /// Does NOT start combat — call StartCombat() separately.
    /// </summary>
    public void Initialize(Node combatUI)
    {
        if (_initialized)
        {
            GD.Print("[CombatBridge] Already initialized -- reconnecting UI.");
            _combatUI = combatUI;
            ConnectUISignals();
            // If combat is already in progress, refresh everything.
            if (_combatManager != null && _combatManager.GetState() == CombatState.InProgress)
            {
                RefreshAll();
            }
            return;
        }

        if (combatUI == null)
        {
            GD.PrintErr("[CombatBridge] Initialize() received null combatUI -- aborting.");
            return;
        }

        _combatUI = combatUI;

        // --- CombatManager ---
        var main = GetTree().Root.GetNode("Main");
        _combatManager = main.GetNodeOrNull<CombatManager>("CombatManager");
        if (_combatManager == null)
        {
            _combatManager = new CombatManager();
            _combatManager.Name = "CombatManager";
            main.AddChild(_combatManager);
        }

        // --- CompanionCombatAI ---
        _companionAI ??= new CompanionCombatAI();

        // --- Connect signals ---
        ConnectUISignals();
        ConnectManagerSignals();

        _initialized = true;
        GD.Print("[CombatBridge] Initialized.");
    }

    /// <summary>
    /// Allows SceneManager to inform us what phase was active before
    /// the Combat phase was entered, so we can restore it on end.
    /// </summary>
    public void SetPreviousPhase(GameStateManager.GamePhase phase)
    {
        _previousPhase = phase;
    }

    // -----------------------------------------------------------------
    // Signal wiring
    // -----------------------------------------------------------------

    private void ConnectUISignals()
    {
        if (_combatUI == null)
            return;

        // Disconnect first to avoid double-connections on re-initialization.
        TryDisconnect(_combatUI, "action_selected");
        TryDisconnect(_combatUI, "end_turn_pressed");
        TryDisconnect(_combatUI, "undo_move_pressed");
        TryDisconnect(_combatUI, "grid_cell_clicked");
        TryDisconnect(_combatUI, "companion_action_approved");
        TryDisconnect(_combatUI, "companion_action_modified");
        TryDisconnect(_combatUI, "companion_action_overridden");

        _combatUI.Connect("action_selected",
            Callable.From<string, string>(OnActionSelected));
        _combatUI.Connect("end_turn_pressed",
            Callable.From(OnEndTurnPressed));
        _combatUI.Connect("undo_move_pressed",
            Callable.From(OnUndoMovePressed));

        // Grid cell click — bridges the two-step action flow (select action, then click target).
        _combatUI.Connect("grid_cell_clicked",
            Callable.From<Vector2I>(OnCellClicked));

        // Companion action review panel signals.
        _combatUI.Connect("companion_action_approved",
            Callable.From<string, Godot.Collections.Dictionary>(OnCompanionActionApproved));
        _combatUI.Connect("companion_action_modified",
            Callable.From<string, Godot.Collections.Dictionary>(OnCompanionActionModified));
        _combatUI.Connect("companion_action_overridden",
            Callable.From<string>(OnCompanionActionOverridden));
    }

    private void ConnectManagerSignals()
    {
        if (_combatManager == null)
            return;

        _combatManager.CombatStarted += OnCombatStarted;
        _combatManager.TurnStarted += OnTurnStarted;
        _combatManager.ActionExecuted += OnActionExecuted;
        _combatManager.TurnEnded += OnTurnEnded;
        _combatManager.CombatEnded += OnCombatEnded;
        _combatManager.CombatantDefeated += OnCombatantDefeated;
    }

    /// <summary>
    /// Safely disconnect a signal if it is currently connected to this bridge.
    /// </summary>
    private void TryDisconnect(Node source, string signalName)
    {
        if (!source.HasSignal(signalName))
            return;

        try
        {
            var connections = source.GetSignalConnectionList(signalName);
            foreach (var conn in connections)
            {
                if (conn.TryGetValue("callable", out var callableVariant))
                {
                    var callable = callableVariant.AsCallable();
                    if (callable.Target == this)
                    {
                        source.Disconnect(signalName, callable);
                    }
                }
            }
        }
        catch
        {
            // Signal was not connected -- that's fine.
        }
    }

    // -----------------------------------------------------------------
    // Combat lifecycle
    // -----------------------------------------------------------------

    /// <summary>
    /// Starts a combat encounter. Creates the grid, places combatants, and
    /// kicks off the CombatManager turn loop.
    /// </summary>
    /// <param name="allies">Player + companion characters.</param>
    /// <param name="enemies">Enemy characters.</param>
    /// <param name="gridWidth">Grid width in cells.</param>
    /// <param name="gridHeight">Grid height in cells.</param>
    /// <param name="grid">Optional pre-built CombatGrid. If null, a flat floor grid is created.</param>
    /// <param name="renderer">Optional pre-built CombatGridRenderer.</param>
    public void StartCombat(
        List<Character> allies,
        List<Character> enemies,
        int gridWidth,
        int gridHeight,
        CombatGrid? grid = null,
        CombatGridRenderer? renderer = null)
    {
        if (_combatManager == null)
        {
            GD.PrintErr("[CombatBridge] Cannot start combat -- CombatManager is null.");
            return;
        }

        if (allies == null || allies.Count == 0)
        {
            GD.PrintErr("[CombatBridge] Cannot start combat -- no allies provided.");
            return;
        }

        if (enemies == null || enemies.Count == 0)
        {
            GD.PrintErr("[CombatBridge] Cannot start combat -- no enemies provided.");
            return;
        }

        // --- CombatGrid ---
        if (grid != null)
        {
            _combatGrid = grid;
        }
        else
        {
            // Create a simple flat floor grid.
            var main = GetTree().Root.GetNode("Main");
            _combatGrid = main.GetNodeOrNull<CombatGrid>("CombatGrid");
            if (_combatGrid == null)
            {
                _combatGrid = new CombatGrid();
                _combatGrid.Name = "CombatGrid";
                main.AddChild(_combatGrid);
            }
            _combatGrid.InitializeGrid(gridWidth, gridHeight);
        }

        // --- CombatGridRenderer ---
        if (renderer != null)
        {
            _combatGridRenderer = renderer;
        }
        else
        {
            var main = GetTree().Root.GetNode("Main");
            _combatGridRenderer = main.GetNodeOrNull<CombatGridRenderer>("CombatGridRenderer");
            // Renderer is optional -- grid still functions without visual rendering.
        }

        // Reset bridge state.
        ClearPendingAction();
        _preMovePosition = null;
        _lastPushedRound = 1;

        // Start the encounter. CombatManager handles initiative, placement, and first turn.
        _combatManager.StartCombat(allies, enemies, _combatGrid);

        GD.Print($"[CombatBridge] Combat started: {allies.Count} allies vs {enemies.Count} enemies on {gridWidth}x{gridHeight} grid.");
    }

    /// <summary>
    /// Handles the end of combat. Awards XP to party members, restores
    /// the previous game phase, and returns to exploration.
    /// </summary>
    public void EndCombat(bool playerVictory)
    {
        if (_combatManager == null)
            return;

        if (playerVictory)
        {
            // Distribute XP to surviving party members.
            int totalXP = _combatManager.CalculateEncounterXP();
            DistributeXP(totalXP);

            PushCombatLogEntry(
                $"[color=#2ecc71][b]Victory! Party earned {totalXP} XP.[/b][/color]\n",
                "victory");
        }
        else
        {
            PushCombatLogEntry(
                "[color=#e74c3c][b]Defeat... The party has fallen.[/b][/color]\n",
                "defeat");
        }

        // Clean up grid and renderer state.
        ClearPendingAction();
        _combatGridRenderer?.ClearHighlights();

        // Restore previous phase (usually Exploration).
        GameStateManager.Instance?.SetPhase(_previousPhase);

        GD.Print($"[CombatBridge] Combat ended. Victory: {playerVictory}");
    }

    // -----------------------------------------------------------------
    // GDScript signal handlers
    // -----------------------------------------------------------------

    /// <summary>
    /// Called when the player selects a combat action from the UI.
    /// For actions that need a target (Attack, CastSpell, Help, Move),
    /// enters a pending state waiting for a grid click. For self-only
    /// actions (Dash, Dodge, Disengage, Hide, etc.), executes immediately.
    /// </summary>
    private void OnActionSelected(string actionType, string targetId)
    {
        if (_combatManager == null)
            return;

        var current = _combatManager.GetCurrentCombatant();
        if (current == null || !current.IsPlayerControlled)
            return;

        GD.Print($"[CombatBridge] Action selected: {actionType}, target: '{targetId}'");

        switch (actionType)
        {
            case "Move":
                EnterMoveMode(current);
                break;

            case "Attack":
                EnterAttackMode(current);
                break;

            case "CastSpell":
                // For now, enter targeting mode. A full implementation would show
                // a spell selection list first; targetId can carry spell info.
                EnterSpellMode(current, targetId);
                break;

            case "Help":
                EnterHelpMode(current);
                break;

            // Self-only actions -- execute immediately.
            case "Dash":
                ExecuteImmediate(current, ActionType.Dash);
                break;
            case "Dodge":
                ExecuteImmediate(current, ActionType.Dodge);
                break;
            case "Disengage":
                ExecuteImmediate(current, ActionType.Disengage);
                break;
            case "Hide":
                ExecuteImmediate(current, ActionType.Hide);
                break;
            case "SecondWind":
                ExecuteImmediate(current, ActionType.SecondWind);
                break;
            case "ActionSurge":
                ExecuteImmediate(current, ActionType.ActionSurge);
                break;

            default:
                GD.Print($"[CombatBridge] Unhandled action type: {actionType}");
                break;
        }
    }

    /// <summary>
    /// Called when the player clicks End Turn in the UI.
    /// </summary>
    private void OnEndTurnPressed()
    {
        if (_combatManager == null)
            return;

        var current = _combatManager.GetCurrentCombatant();
        if (current == null || !current.IsPlayerControlled)
            return;

        GD.Print("[CombatBridge] End turn pressed.");

        // If this was a companion under manual Override, restore their IsPlayerControlled flag.
        RestoreCompanionControlFlag(current);

        ClearPendingAction();
        _preMovePosition = null;

        int roundBefore = _combatManager.GetRoundNumber();
        _combatManager.EndTurn();
        CheckRoundChange(roundBefore);
    }

    /// <summary>
    /// Called when the player clicks Undo Move in the UI.
    /// Restores the combatant to their pre-move position.
    /// </summary>
    private void OnUndoMovePressed()
    {
        if (_combatManager == null || _combatGrid == null)
            return;

        var current = _combatManager.GetCurrentCombatant();
        if (current == null || !current.IsPlayerControlled)
            return;

        if (_preMovePosition == null)
        {
            GD.Print("[CombatBridge] No move to undo.");
            return;
        }

        GD.Print($"[CombatBridge] Undoing move: returning to ({_preMovePosition.Value.X},{_preMovePosition.Value.Y}).");

        // Restore position on grid.
        _combatGrid.MoveOccupant(current.Id, _preMovePosition.Value);
        current.GridPosition = _preMovePosition.Value;
        current.RemainingMovement = _preMoveRemainingMovement;

        _preMovePosition = null;

        PushCombatLogEntry(
            $"[color=#b8b0a0][i]{current.Character.Name} retraces their steps.[/i][/color]\n",
            "info");

        // Update renderer and UI.
        RefreshGrid();
        RefreshMovementRemaining(current);
        ClearPendingAction();
    }

    // -----------------------------------------------------------------
    // Grid click handling (called by the combat grid scene)
    // -----------------------------------------------------------------

    /// <summary>
    /// Called when the player clicks a cell on the combat grid.
    /// Executes the pending action at that position if valid.
    /// The CombatGrid or a parent scene should connect its input_event
    /// to call this method with the grid position.
    /// </summary>
    public void OnCellClicked(Vector2I gridPos)
    {
        if (_combatManager == null || _combatGrid == null)
            return;

        var current = _combatManager.GetCurrentCombatant();
        if (current == null || !current.IsPlayerControlled)
            return;

        switch (_pendingAction)
        {
            case PendingActionType.Move:
                ExecuteMove(current, gridPos);
                break;

            case PendingActionType.Attack:
                ExecuteAttackAtCell(current, gridPos);
                break;

            case PendingActionType.CastSpell:
                ExecuteSpellAtCell(current, gridPos);
                break;

            case PendingActionType.Help:
                ExecuteHelpAtCell(current, gridPos);
                break;

            case PendingActionType.None:
                // No pending action -- clicking the grid does nothing.
                break;
        }
    }

    // -----------------------------------------------------------------
    // Pending action modes
    // -----------------------------------------------------------------

    private void EnterMoveMode(Combatant combatant)
    {
        ClearPendingAction();
        _pendingAction = PendingActionType.Move;

        // Highlight reachable cells.
        if (_combatGrid != null && _combatGridRenderer != null)
        {
            var reachable = _combatGrid.GetValidMovePositions(
                combatant.GridPosition, combatant.RemainingMovement);
            _combatGridRenderer.HighlightMovementRange(reachable);
        }

        GD.Print("[CombatBridge] Move mode entered.");
    }

    private void EnterAttackMode(Combatant combatant)
    {
        ClearPendingAction();
        _pendingAction = PendingActionType.Attack;

        // Highlight valid targets on the grid.
        if (_combatManager != null && _combatGridRenderer != null)
        {
            var actions = _combatManager.GetAvailableActions(combatant);
            var targetPositions = actions
                .Where(a => a.Type == ActionType.Attack && a.Target != null)
                .Select(a => a.Target.GridPosition)
                .Distinct()
                .ToList();
            _combatGridRenderer.HighlightAttackRange(targetPositions);
        }

        GD.Print("[CombatBridge] Attack mode entered.");
    }

    private void EnterSpellMode(Combatant combatant, string spellInfo)
    {
        ClearPendingAction();
        _pendingAction = PendingActionType.CastSpell;

        // Parse spell info from targetId if provided (format: "SpellName:Level").
        _pendingSpellName = null;
        _pendingSpellLevel = 0;

        if (!string.IsNullOrEmpty(spellInfo) && spellInfo.Contains(':'))
        {
            var parts = spellInfo.Split(':');
            _pendingSpellName = parts[0];
            if (parts.Length > 1)
                int.TryParse(parts[1], out _pendingSpellLevel);
        }
        else if (!string.IsNullOrEmpty(spellInfo))
        {
            _pendingSpellName = spellInfo;
        }

        // Highlight potential AoE or targets.
        // For targeted spells, highlight all enemy positions within range.
        if (_combatManager != null && _combatGridRenderer != null && _combatGrid != null)
        {
            var enemies = _combatManager.GetEnemies()
                .Where(e => !e.IsDead() &&
                    _combatGrid.HasLineOfSight(combatant.GridPosition, e.GridPosition))
                .Select(e => e.GridPosition)
                .ToList();
            _combatGridRenderer.HighlightAttackRange(enemies);
        }

        GD.Print($"[CombatBridge] Spell mode entered: {_pendingSpellName ?? "(select)"}.");
    }

    private void EnterHelpMode(Combatant combatant)
    {
        ClearPendingAction();
        _pendingAction = PendingActionType.Help;

        // Highlight adjacent allies.
        if (_combatManager != null && _combatGridRenderer != null && _combatGrid != null)
        {
            var adjacentAllyPositions = new List<Vector2I>();
            var adjacentIds = _combatGrid.GetAdjacentOccupants(combatant.GridPosition);
            foreach (string id in adjacentIds)
            {
                var adj = _combatManager.GetCombatantById(id);
                if (adj != null && adj.IsAlly == combatant.IsAlly && adj.IsConscious && adj.Id != combatant.Id)
                {
                    adjacentAllyPositions.Add(adj.GridPosition);
                }
            }
            _combatGridRenderer.HighlightMovementRange(adjacentAllyPositions);
        }

        GD.Print("[CombatBridge] Help mode entered.");
    }

    private void ClearPendingAction()
    {
        _pendingAction = PendingActionType.None;
        _pendingSpellName = null;
        _pendingSpellLevel = 0;
        _combatGridRenderer?.ClearHighlights();
    }

    // -----------------------------------------------------------------
    // Action execution
    // -----------------------------------------------------------------

    /// <summary>
    /// Executes a self-only action that does not require targeting.
    /// </summary>
    private void ExecuteImmediate(Combatant actor, ActionType actionType)
    {
        if (_combatManager == null)
            return;

        ClearPendingAction();

        var action = new CombatAction
        {
            Type = actionType,
            Actor = actor
        };

        var result = _combatManager.ExecuteAction(action);

        // Notify UI that the action has been used.
        _combatUI?.Call("show_action_used", actionType.ToString());

        // Refresh available actions (some may now be disabled).
        RefreshAvailableActions(actor);
        RefreshMovementRemaining(actor);
        CheckAllActionsSpent(actor);
    }

    private void ExecuteMove(Combatant actor, Vector2I targetPos)
    {
        if (_combatManager == null || _combatGrid == null)
            return;

        // Validate the position is reachable.
        var reachable = _combatGrid.GetValidMovePositions(
            actor.GridPosition, actor.RemainingMovement);
        if (!reachable.Contains(targetPos))
        {
            GD.Print($"[CombatBridge] Invalid move target: ({targetPos.X},{targetPos.Y})");
            return;
        }

        // Save pre-move state for undo.
        if (_preMovePosition == null)
        {
            _preMovePosition = actor.GridPosition;
            _preMoveRemainingMovement = actor.RemainingMovement;
        }

        var action = new CombatAction
        {
            Type = ActionType.Move,
            Actor = actor,
            TargetPosition = targetPos
        };

        int roundBefore = _combatManager.GetRoundNumber();
        var result = _combatManager.ExecuteAction(action);
        CheckRoundChange(roundBefore);

        ClearPendingAction();
        RefreshGrid();
        RefreshMovementRemaining(actor);

        // If the move triggered an opportunity attack, report it.
        if (result.TriggeredOpportunityAttack && result.OpportunityAttackResult != null)
        {
            PushCombatLogEntry(
                $"[color=#e74c3c]{result.OpportunityAttackResult.Description}[/color]\n",
                "opportunity_attack");

            // Actor may have been downed by the OA.
            if (!actor.IsConscious)
            {
                _preMovePosition = null;
                RefreshCurrentCombatant();
                RefreshInitiativeOrder();
                return;
            }
        }

        // Re-check available actions after moving.
        RefreshAvailableActions(actor);
        CheckAllActionsSpent(actor);
    }

    private void ExecuteAttackAtCell(Combatant actor, Vector2I cellPos)
    {
        if (_combatManager == null || _combatGrid == null)
            return;

        // Find the combatant at the clicked cell.
        var cell = _combatGrid.GetCell(cellPos);
        if (cell == null || !cell.IsOccupied || cell.OccupantId == null)
        {
            GD.Print("[CombatBridge] No target at clicked cell.");
            return;
        }

        var target = _combatManager.GetCombatantById(cell.OccupantId);
        if (target == null || target.IsAlly == actor.IsAlly || target.IsDead())
        {
            GD.Print("[CombatBridge] Invalid attack target.");
            return;
        }

        var action = new CombatAction
        {
            Type = ActionType.Attack,
            Actor = actor,
            Target = target
        };

        int roundBefore = _combatManager.GetRoundNumber();
        var result = _combatManager.ExecuteAction(action);
        CheckRoundChange(roundBefore);

        ClearPendingAction();

        // Invalidate undo after attacking (can't undo moves after taking other actions).
        _preMovePosition = null;

        _combatUI?.Call("show_action_used", "Attack");
        RefreshAvailableActions(actor);
        RefreshGrid();
        RefreshInitiativeOrder();
        CheckAllActionsSpent(actor);
    }

    private void ExecuteSpellAtCell(Combatant actor, Vector2I cellPos)
    {
        if (_combatManager == null || _combatGrid == null)
            return;

        // Find target at cell (for targeted spells).
        Combatant? target = null;
        var cell = _combatGrid.GetCell(cellPos);
        if (cell != null && cell.IsOccupied && cell.OccupantId != null)
        {
            target = _combatManager.GetCombatantById(cell.OccupantId);
        }

        var action = new CombatAction
        {
            Type = ActionType.CastSpell,
            Actor = actor,
            Target = target,
            TargetPosition = cellPos,
            SpellName = _pendingSpellName ?? "",
            SpellLevel = _pendingSpellLevel
        };

        int roundBefore = _combatManager.GetRoundNumber();
        var result = _combatManager.ExecuteAction(action);
        CheckRoundChange(roundBefore);

        ClearPendingAction();
        _preMovePosition = null;

        _combatUI?.Call("show_action_used", "CastSpell");
        RefreshAvailableActions(actor);
        RefreshGrid();
        RefreshInitiativeOrder();
        CheckAllActionsSpent(actor);
    }

    private void ExecuteHelpAtCell(Combatant actor, Vector2I cellPos)
    {
        if (_combatManager == null || _combatGrid == null)
            return;

        var cell = _combatGrid.GetCell(cellPos);
        if (cell == null || !cell.IsOccupied || cell.OccupantId == null)
        {
            GD.Print("[CombatBridge] No ally at clicked cell for Help.");
            return;
        }

        var target = _combatManager.GetCombatantById(cell.OccupantId);
        if (target == null || target.IsAlly != actor.IsAlly || !target.IsConscious || target.Id == actor.Id)
        {
            GD.Print("[CombatBridge] Invalid Help target.");
            return;
        }

        var action = new CombatAction
        {
            Type = ActionType.Help,
            Actor = actor,
            Target = target
        };

        _combatManager.ExecuteAction(action);

        ClearPendingAction();
        _preMovePosition = null;

        _combatUI?.Call("show_action_used", "Help");
        RefreshAvailableActions(actor);
        CheckAllActionsSpent(actor);
    }

    // -----------------------------------------------------------------
    // CombatManager signal handlers (C# events via [Signal])
    // -----------------------------------------------------------------

    /// <summary>
    /// Fired when CombatManager.StartCombat() finishes setup.
    /// Push the initial state to the UI.
    /// </summary>
    private void OnCombatStarted()
    {
        GD.Print("[CombatBridge] CombatStarted signal received.");

        _lastPushedRound = 1;
        _combatUI?.Call("set_round", 1);

        // Log initiative rolls so the player sees the turn order.
        if (_combatManager != null)
        {
            PushCombatLogEntry(
                "[color=#c9a227][b]--- Combat Begins! ---[/b][/color]\n",
                "round");
            PushCombatLogEntry(
                "[color=#c9a227]Initiative Order:[/color]\n",
                "info");

            foreach (var c in _combatManager.GetAllCombatants())
            {
                string faction = c.IsPlayerControlled ? "[color=#3498db]" :
                    c.IsAlly ? "[color=#27ae60]" : "[color=#e74c3c]";
                PushCombatLogEntry(
                    $"  {faction}{c.Character.Name}[/color] — Initiative {c.Initiative}\n",
                    "initiative");
            }

            PushCombatLogEntry(
                "\n[color=#c9a227][b]--- Round 1 ---[/b][/color]\n",
                "round");
        }

        RefreshInitiativeOrder();
        RefreshGrid();
        SetupRendererColors();

        // The first TurnStarted signal will follow immediately from BeginCurrentTurn.
    }

    /// <summary>
    /// Fired when a combatant's turn begins.
    /// Updates the active character panel and available actions.
    /// For companions, routes through the autonomy system (Suggest/Trusted/FullAuto/FullControl).
    /// For enemies, executes AI immediately. For the player, shows the normal action UI.
    /// </summary>
    private void OnTurnStarted(string combatantId)
    {
        GD.Print($"[CombatBridge] TurnStarted: {combatantId}");

        ClearPendingAction();
        _preMovePosition = null;
        _pendingAIPlan = null;
        _pendingReviewCombatant = null;

        // Hide any lingering action review panel from a previous turn.
        _combatUI?.Call("hide_action_review");

        RefreshInitiativeOrder();
        RefreshCurrentCombatant();

        var combatant = _combatManager?.GetCombatantById(combatantId);
        if (combatant == null)
            return;

        // ── Unconscious ally — auto-process death saves ─────────────
        // Unconscious combatants can't take actions; CombatManager gives them
        // a turn solely for death saving throws. Process and end turn.
        if (!combatant.IsConscious && combatant.IsAlly && !combatant.IsDead())
        {
            PushCombatLogEntry(
                $"[color=#e74c3c][i]{combatant.Character.Name} is unconscious...[/i][/color]\n",
                "death_save");

            Callable.From(() =>
            {
                if (_combatManager == null) return;
                _combatManager.ProcessDeathSave(combatant);
                RefreshInitiativeOrder();
                RefreshCurrentCombatant();
                RefreshGrid();
                EndCompanionTurn(combatant);
            }).CallDeferred();
            return;
        }

        RefreshAvailableActions(combatant);
        RefreshMovementRemaining(combatant);

        // Reset End Turn style for new turn.
        _combatUI?.Call("reset_end_turn_style");
        _combatUI?.Call("reset_reaction_status");

        // ── Player character turn ──────────────────────────────────────
        if (combatant.IsPlayerControlled)
        {
            PushCombatLogEntry(
                $"[color=#c9a227][b]Your turn — {combatant.Character.Name}[/b][/color]\n",
                "turn_start");
            return;
        }

        // ── Enemy turn (not ally, not player) ──────────────────────────
        if (!combatant.IsAlly)
        {
            PushCombatLogEntry(
                $"[color=#b8b0a0][i]{combatant.Character.Name}'s turn.[/i][/color]\n",
                "turn_start");
            // Enemy AI uses the same utility system, executes immediately.
            Callable.From(() => ExecuteCompanionTurnAuto(combatant)).CallDeferred();
            return;
        }

        // ── Companion turn ─────────────────────────────────────────────
        var autonomy = combatant.Character.CompanionAutonomy;

        PushCombatLogEntry(
            $"[color=#27ae60][i]{combatant.Character.Name}'s turn.[/i][/color]\n",
            "turn_start");

        switch (autonomy)
        {
            case CompanionAutonomy.FullControl:
                // Player controls manually -- temporarily mark as player-controlled
                // so the existing action button handlers accept input.
                combatant.IsPlayerControlled = true;
                PushCombatLogEntry(
                    $"[color=#b8b0a0](Full Control — you decide {combatant.Character.Name}'s actions.)[/color]\n",
                    "info");
                break;

            case CompanionAutonomy.Suggest:
                // AI suggests, player reviews via the CompanionActionPanel.
                Callable.From(() => ShowCompanionSuggestion(combatant)).CallDeferred();
                break;

            case CompanionAutonomy.Trusted:
                // AI acts automatically, log shows what happened.
                PushCombatLogEntry(
                    $"[color=#b8b0a0](Trusted — {combatant.Character.Name} acts on their own.)[/color]\n",
                    "info");
                Callable.From(() => ExecuteCompanionTurnAuto(combatant)).CallDeferred();
                break;

            case CompanionAutonomy.FullAuto:
                // AI acts silently, minimal feedback.
                Callable.From(() => ExecuteCompanionTurnAuto(combatant)).CallDeferred();
                break;
        }
    }

    /// <summary>
    /// Fired when any action is executed during combat.
    /// Pushes the result description to the combat log.
    /// </summary>
    private void OnActionExecuted(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return;

        PushCombatLogEntry($"[color=#eee8d5]{description}[/color]\n", "action");

        // Refresh the current combatant panel in case HP or conditions changed.
        RefreshCurrentCombatant();
        RefreshGrid();
    }

    /// <summary>
    /// Fired when a combatant's turn ends.
    /// Restores companion control flags if they were temporarily overridden.
    /// </summary>
    private void OnTurnEnded(string combatantId)
    {
        GD.Print($"[CombatBridge] TurnEnded: {combatantId}");

        // Restore IsPlayerControlled for any companion that was manually controlled.
        var combatant = _combatManager?.GetCombatantById(combatantId);
        if (combatant != null)
            RestoreCompanionControlFlag(combatant);

        // Turn transition is handled by TurnStarted for the next combatant.
    }

    /// <summary>
    /// Fired when combat ends (all enemies dead or all allies down).
    /// </summary>
    private void OnCombatEnded(bool playerVictory)
    {
        GD.Print($"[CombatBridge] CombatEnded: playerVictory={playerVictory}");
        EndCombat(playerVictory);
    }

    /// <summary>
    /// Fired when a combatant is defeated (killed or 3 death save failures).
    /// Updates the grid and initiative bar.
    /// </summary>
    private void OnCombatantDefeated(string combatantId)
    {
        GD.Print($"[CombatBridge] CombatantDefeated: {combatantId}");

        _combatGridRenderer?.RemoveOccupantDisplay(combatantId);
        RefreshInitiativeOrder();
        RefreshGrid();
    }

    // -----------------------------------------------------------------
    // Data push methods (C# -> GDScript)
    // -----------------------------------------------------------------

    /// <summary>
    /// Pushes the full initiative order to the CombatUI as an Array of Dictionary.
    /// Each entry: {id, name, is_player_controlled, is_ally}
    /// </summary>
    public void RefreshInitiativeOrder()
    {
        if (_combatUI == null || _combatManager == null)
            return;

        var combatants = _combatManager.GetAllCombatants();
        var current = _combatManager.GetCurrentCombatant();
        string activeId = current?.Id ?? "";

        var combatantsArray = new Godot.Collections.Array();

        foreach (var c in combatants)
        {
            // Skip dead combatants from the initiative display.
            if (c.IsDead())
                continue;

            combatantsArray.Add(new Godot.Collections.Dictionary
            {
                { "id", c.Id },
                { "name", c.Character.Name },
                { "is_player_controlled", c.IsPlayerControlled },
                { "is_ally", c.IsAlly }
            });
        }

        _combatUI.Call("update_initiative_bar", combatantsArray, activeId);
    }

    /// <summary>
    /// Pushes detailed stats for the current turn's combatant to the UI.
    /// </summary>
    public void RefreshCurrentCombatant()
    {
        if (_combatUI == null || _combatManager == null)
            return;

        var current = _combatManager.GetCurrentCombatant();
        if (current == null)
            return;

        var ch = current.Character;

        // Build conditions array.
        var conditionsArray = new Godot.Collections.Array();
        if (current.Conditions != null)
        {
            foreach (var condition in current.Conditions.GetActiveConditions())
            {
                conditionsArray.Add(condition.ToString());
            }
        }

        _combatUI.Call("update_active_character",
            ch.Name,
            ch.CharacterClassName,
            ch.Level,
            ch.HitPoints,
            ch.MaxHitPoints,
            ch.ArmorClass,
            ch.Speed,
            conditionsArray);
    }

    /// <summary>
    /// Pushes the list of valid actions for a combatant to the UI.
    /// Filters to unique action type names (the UI shows one button per type).
    /// </summary>
    public void RefreshAvailableActions(Combatant combatant)
    {
        if (_combatUI == null || _combatManager == null)
            return;

        var actions = _combatManager.GetAvailableActions(combatant);

        // Collect unique action type names, excluding Move (handled separately)
        // and OpportunityAttack/Stand (not player-initiated via button).
        var actionTypeNames = new HashSet<string>();
        foreach (var action in actions)
        {
            if (action.Type == ActionType.Move || action.Type == ActionType.Stand ||
                action.Type == ActionType.OpportunityAttack)
                continue;

            actionTypeNames.Add(action.Type.ToString());
        }

        var actionsArray = new Godot.Collections.Array();
        foreach (string name in actionTypeNames)
        {
            actionsArray.Add(name);
        }

        _combatUI.Call("update_available_actions", actionsArray);
    }

    /// <summary>
    /// Pushes movement remaining to the UI.
    /// </summary>
    public void RefreshMovementRemaining(Combatant combatant)
    {
        _combatUI?.Call("update_movement_remaining", combatant.RemainingMovement);
    }

    /// <summary>
    /// Appends a BBCode-formatted entry to the combat log.
    /// </summary>
    /// <param name="text">BBCode-formatted text.</param>
    /// <param name="type">Entry type for potential filtering (e.g. "action", "info", "victory").</param>
    public void PushCombatLogEntry(string text, string type)
    {
        _combatUI?.Call("add_combat_log", text);
    }

    /// <summary>
    /// Tells the renderer to redraw all occupant tokens with current data.
    /// Updates HP bars and condition indicators for all living combatants.
    /// </summary>
    public void RefreshGrid()
    {
        if (_combatGridRenderer == null || _combatManager == null)
            return;

        foreach (var c in _combatManager.GetAllCombatants())
        {
            if (c.IsDead())
                continue;

            _combatGridRenderer.SetOccupantHp(c.Id, c.Character.HitPoints, c.Character.MaxHitPoints);

            if (c.Conditions != null)
            {
                var conditionNames = c.Conditions.GetActiveConditions()
                    .Select(cond => cond.ToString())
                    .ToList();
                _combatGridRenderer.SetOccupantConditions(c.Id, conditionNames);
            }
        }

        _combatGridRenderer.QueueRedraw();
    }

    /// <summary>
    /// Refreshes all UI elements. Call after initialization or major state changes.
    /// </summary>
    public void RefreshAll()
    {
        RefreshInitiativeOrder();
        RefreshCurrentCombatant();
        RefreshGrid();

        var current = _combatManager?.GetCurrentCombatant();
        if (current != null)
        {
            RefreshAvailableActions(current);
            RefreshMovementRemaining(current);
        }

        if (_combatManager != null)
        {
            _combatUI?.Call("set_round", _combatManager.GetRoundNumber());
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Sets up token colors on the renderer for all combatants.
    /// Player = blue, allies = green, enemies = red.
    /// </summary>
    private void SetupRendererColors()
    {
        if (_combatGridRenderer == null || _combatManager == null)
            return;

        var playerColor = new Color(0.2f, 0.4f, 0.9f);
        var allyColor = new Color(0.2f, 0.8f, 0.3f);
        var enemyColor = new Color(0.9f, 0.2f, 0.15f);

        foreach (var c in _combatManager.GetAllCombatants())
        {
            Color color;
            if (c.IsPlayerControlled)
                color = playerColor;
            else if (c.IsAlly)
                color = allyColor;
            else
                color = enemyColor;

            _combatGridRenderer.SetOccupantColor(c.Id, color);
            _combatGridRenderer.SetOccupantHp(c.Id, c.Character.HitPoints, c.Character.MaxHitPoints);
        }
    }

    /// <summary>
    /// Distributes XP evenly among surviving party members.
    /// </summary>
    private void DistributeXP(int totalXP)
    {
        var gameLoop = GameStateManager.Instance?.ActiveGameLoop;
        if (gameLoop == null || totalXP <= 0)
            return;

        var survivors = new List<Character>();
        if (gameLoop.PlayerCharacter != null && gameLoop.PlayerCharacter.HitPoints > 0)
            survivors.Add(gameLoop.PlayerCharacter);

        if (gameLoop.Companions != null)
        {
            foreach (var companion in gameLoop.Companions)
            {
                if (companion.HitPoints > 0)
                    survivors.Add(companion);
            }
        }

        if (survivors.Count == 0)
            return;

        int xpEach = totalXP / survivors.Count;
        foreach (var survivor in survivors)
        {
            survivor.ExperiencePoints += xpEach;
        }

        GD.Print($"[CombatBridge] Distributed {xpEach} XP each to {survivors.Count} surviving party members.");
    }

    /// <summary>
    /// Checks if the round number changed after an action and pushes
    /// the update to the UI. CombatManager does not emit a RoundChanged
    /// signal, so we poll after EndTurn and actions.
    /// </summary>
    private void CheckRoundChange(int roundBefore)
    {
        if (_combatManager == null)
            return;

        int roundNow = _combatManager.GetRoundNumber();
        if (roundNow != _lastPushedRound)
        {
            _lastPushedRound = roundNow;
            _combatUI?.Call("set_round", roundNow);

            PushCombatLogEntry(
                $"\n[color=#c9a227][b]--- Round {roundNow} ---[/b][/color]\n",
                "round");
        }
    }

    /// <summary>
    /// Checks if the player has spent all actions and movement,
    /// and highlights the End Turn button if so.
    /// </summary>
    private void CheckAllActionsSpent(Combatant combatant)
    {
        if (_combatUI == null)
            return;

        bool movementDone = combatant.RemainingMovement <= 0;
        bool actionDone = combatant.ActionUsed;
        bool bonusDone = combatant.BonusActionUsed;

        if (movementDone && actionDone && bonusDone)
        {
            _combatUI.Call("highlight_end_turn");
        }
    }

    /// <summary>
    /// If the combatant is a companion that was temporarily set to IsPlayerControlled
    /// (via Override), restores them to non-player-controlled. The player character
    /// (first ally, index 0 in the initiative) is never a companion.
    /// </summary>
    private void RestoreCompanionControlFlag(Combatant combatant)
    {
        if (combatant.IsAlly && combatant.Character.IsCompanion && combatant.IsPlayerControlled)
        {
            combatant.IsPlayerControlled = false;
            GD.Print($"[CombatBridge] Restored {combatant.Character.Name} to AI-controlled.");
        }
    }

    // -----------------------------------------------------------------
    // Companion AI turn handling
    // -----------------------------------------------------------------

    /// <summary>
    /// Runs the companion AI, builds a display-friendly description, and pushes
    /// the suggestion to the CompanionActionPanel for player review (Suggest mode).
    /// </summary>
    private void ShowCompanionSuggestion(Combatant combatant)
    {
        if (_companionAI == null || _combatManager == null || _combatGrid == null || _combatUI == null)
            return;

        var plan = _companionAI.DecideTurn(combatant, _combatManager, _combatGrid);
        _pendingAIPlan = plan;
        _pendingReviewCombatant = combatant;

        // Build a human-readable description of the suggested action.
        string displayText = FormatPlanDisplayText(combatant, plan);

        // Build the suggested action dictionary for the UI.
        var suggestedAction = new Godot.Collections.Dictionary
        {
            { "display_text", displayText },
            { "action_type", plan.PlannedAction?.Type.ToString() ?? "None" }
        };

        // Build alternative actions: score all standard actions and pick the top 3
        // that are not the planned action, so the player has choices.
        var alternatives = BuildAlternatives(combatant, plan);

        _combatUI.Call("show_action_review",
            combatant.Id,
            combatant.Character.Name,
            suggestedAction,
            plan.Reasoning,
            alternatives);

        GD.Print($"[CombatBridge] Showing suggestion for {combatant.Character.Name}: {displayText}");
    }

    /// <summary>
    /// Executes a full companion turn automatically (Trusted, FullAuto, or enemy AI).
    /// Runs the AI, executes movement, then action, then bonus action, then ends turn.
    /// </summary>
    private void ExecuteCompanionTurnAuto(Combatant combatant)
    {
        if (_companionAI == null || _combatManager == null || _combatGrid == null)
            return;

        // Check that combat is still in progress and it's still this combatant's turn.
        if (_combatManager.GetState() != CombatState.InProgress)
            return;

        var current = _combatManager.GetCurrentCombatant();
        if (current == null || current.Id != combatant.Id)
            return;

        var plan = _companionAI.DecideTurn(combatant, _combatManager, _combatGrid);
        ExecuteAIPlan(combatant, plan);
    }

    /// <summary>
    /// Executes an AI plan: movement, then main action, then bonus action, then end turn.
    /// Used for auto-execution (Trusted/FullAuto/Enemy) and after player approval.
    /// </summary>
    private void ExecuteAIPlan(Combatant combatant, CombatAIPlan plan)
    {
        if (_combatManager == null || _combatGrid == null)
            return;

        // ── Movement ──────────────────────────────────────────────────
        if (plan.MoveTo.HasValue && plan.MoveTo.Value != combatant.GridPosition)
        {
            var moveAction = new CombatAction
            {
                Type = ActionType.Move,
                Actor = combatant,
                TargetPosition = plan.MoveTo.Value
            };

            int roundBefore = _combatManager.GetRoundNumber();
            var moveResult = _combatManager.ExecuteAction(moveAction);
            CheckRoundChange(roundBefore);

            RefreshGrid();
            RefreshMovementRemaining(combatant);

            // If downed by opportunity attack during move, end turn.
            if (!combatant.IsConscious)
            {
                EndCompanionTurn(combatant);
                return;
            }
        }

        // ── Bonus Action (before main action -- some bonus actions like SecondWind
        //    should happen first if HP is critical) ────────────────────
        if (plan.PlannedBonusAction != null)
        {
            int roundBefore = _combatManager.GetRoundNumber();
            _combatManager.ExecuteAction(plan.PlannedBonusAction);
            CheckRoundChange(roundBefore);

            RefreshCurrentCombatant();
            RefreshGrid();
        }

        // ── Main Action ───────────────────────────────────────────────
        if (plan.PlannedAction != null)
        {
            int roundBefore = _combatManager.GetRoundNumber();
            _combatManager.ExecuteAction(plan.PlannedAction);
            CheckRoundChange(roundBefore);

            RefreshCurrentCombatant();
            RefreshGrid();
            RefreshInitiativeOrder();
        }

        // ── End Turn ──────────────────────────────────────────────────
        EndCompanionTurn(combatant);
    }

    /// <summary>
    /// Ends a companion or enemy combatant's turn and advances to the next.
    /// </summary>
    private void EndCompanionTurn(Combatant combatant)
    {
        if (_combatManager == null)
            return;

        // Verify this is still the current combatant's turn.
        var current = _combatManager.GetCurrentCombatant();
        if (current == null || current.Id != combatant.Id)
            return;

        int roundBefore = _combatManager.GetRoundNumber();
        _combatManager.EndTurn();
        CheckRoundChange(roundBefore);
    }

    // -----------------------------------------------------------------
    // Companion action review signal handlers
    // -----------------------------------------------------------------

    /// <summary>
    /// Player approved the AI suggestion. Execute the pending plan as-is.
    /// </summary>
    private void OnCompanionActionApproved(string companionId, Godot.Collections.Dictionary actionData)
    {
        GD.Print($"[CombatBridge] Companion action approved for {companionId}.");

        if (_pendingAIPlan == null || _pendingReviewCombatant == null)
        {
            GD.PrintErr("[CombatBridge] No pending AI plan to approve.");
            return;
        }

        if (_pendingReviewCombatant.Id != companionId)
        {
            GD.PrintErr($"[CombatBridge] Approved companion '{companionId}' does not match pending '{_pendingReviewCombatant.Id}'.");
            return;
        }

        var plan = _pendingAIPlan;
        var combatant = _pendingReviewCombatant;
        _pendingAIPlan = null;
        _pendingReviewCombatant = null;

        ExecuteAIPlan(combatant, plan);
    }

    /// <summary>
    /// Player selected a different action from the alternatives list.
    /// Re-run the AI but force the selected action type instead.
    /// </summary>
    private void OnCompanionActionModified(string companionId, Godot.Collections.Dictionary newActionData)
    {
        GD.Print($"[CombatBridge] Companion action modified for {companionId}.");

        if (_pendingReviewCombatant == null || _combatManager == null || _combatGrid == null)
        {
            GD.PrintErr("[CombatBridge] No pending review state for modification.");
            return;
        }

        if (_pendingReviewCombatant.Id != companionId)
        {
            GD.PrintErr($"[CombatBridge] Modified companion '{companionId}' does not match pending '{_pendingReviewCombatant.Id}'.");
            return;
        }

        var combatant = _pendingReviewCombatant;
        _pendingAIPlan = null;
        _pendingReviewCombatant = null;

        // Reconstruct a CombatAction from the modified action data.
        string actionTypeStr = newActionData.TryGetValue("action_type", out var atVal) ? atVal.AsString() : "None";
        string targetId = newActionData.TryGetValue("target_id", out var tidVal) ? tidVal.AsString() : "";

        // Build a plan with the overridden action. Keep the AI's movement suggestion.
        var originalPlan = _companionAI?.DecideTurn(combatant, _combatManager, _combatGrid);
        if (originalPlan == null)
        {
            EndCompanionTurn(combatant);
            return;
        }

        // Find the matching action from available actions.
        var availableActions = _combatManager.GetAvailableActions(combatant);
        CombatAction? matchedAction = null;

        if (Enum.TryParse<ActionType>(actionTypeStr, out var parsedType))
        {
            matchedAction = availableActions.FirstOrDefault(a =>
                a.Type == parsedType &&
                (string.IsNullOrEmpty(targetId) || a.Target?.Id == targetId));

            // Fallback: just match by type.
            matchedAction ??= availableActions.FirstOrDefault(a => a.Type == parsedType);
        }

        var modifiedPlan = new CombatAIPlan
        {
            MoveTo = originalPlan.MoveTo,
            MovePath = originalPlan.MovePath,
            PlannedAction = matchedAction,
            PlannedBonusAction = originalPlan.PlannedBonusAction,
            Reasoning = $"Player chose: {actionTypeStr}",
            ConfidenceScore = 1f,
            Priority = originalPlan.Priority
        };

        ExecuteAIPlan(combatant, modifiedPlan);
    }

    /// <summary>
    /// Player clicked Override -- they want full manual control of this companion's turn.
    /// The companion is treated as player-controlled for the remainder of this turn.
    /// </summary>
    private void OnCompanionActionOverridden(string companionId)
    {
        GD.Print($"[CombatBridge] Companion action overridden for {companionId} — switching to manual control.");

        _pendingAIPlan = null;
        _pendingReviewCombatant = null;

        var combatant = _combatManager?.GetCombatantById(companionId);
        if (combatant == null)
            return;

        // Temporarily mark as player-controlled so the normal action UI works.
        combatant.IsPlayerControlled = true;

        PushCombatLogEntry(
            $"[color=#c9a227][b]Manual control — {combatant.Character.Name}[/b][/color]\n",
            "info");

        // Refresh UI to show the full player action buttons.
        RefreshAvailableActions(combatant);
        RefreshMovementRemaining(combatant);
    }

    // -----------------------------------------------------------------
    // Companion AI helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Builds a human-readable description of the AI plan for the review panel.
    /// </summary>
    private static string FormatPlanDisplayText(Combatant combatant, CombatAIPlan plan)
    {
        var parts = new List<string>();

        if (plan.MoveTo.HasValue && plan.MoveTo.Value != combatant.GridPosition)
        {
            parts.Add($"Move to ({plan.MoveTo.Value.X},{plan.MoveTo.Value.Y})");
        }

        if (plan.PlannedBonusAction != null)
        {
            string bonusDesc = plan.PlannedBonusAction.Type.ToString();
            parts.Add($"Bonus: {bonusDesc}");
        }

        if (plan.PlannedAction != null)
        {
            string actionDesc = plan.PlannedAction.Type.ToString();

            if (plan.PlannedAction.Target != null)
                actionDesc += $" {plan.PlannedAction.Target.Character.Name}";

            if (plan.PlannedAction.Type == ActionType.CastSpell &&
                !string.IsNullOrEmpty(plan.PlannedAction.SpellName))
            {
                actionDesc = $"Cast {plan.PlannedAction.SpellName}";
                if (plan.PlannedAction.Target != null)
                    actionDesc += $" on {plan.PlannedAction.Target.Character.Name}";
            }

            parts.Add(actionDesc);
        }

        if (parts.Count == 0)
            return "End turn (no viable actions).";

        return string.Join(", then ", parts) + ".";
    }

    /// <summary>
    /// Builds a list of alternative action Dictionaries for the CompanionActionPanel.
    /// Returns the top 4 non-primary unique action types from the available actions list.
    /// </summary>
    private Godot.Collections.Array BuildAlternatives(Combatant combatant, CombatAIPlan plan)
    {
        var alternatives = new Godot.Collections.Array();

        if (_combatManager == null)
            return alternatives;

        var available = _combatManager.GetAvailableActions(combatant);
        var plannedType = plan.PlannedAction?.Type;

        // Collect unique action types that differ from the planned action.
        var seen = new HashSet<string>();
        foreach (var action in available)
        {
            // Skip movement and non-player-initiated types.
            if (action.Type == ActionType.Move || action.Type == ActionType.Stand ||
                action.Type == ActionType.OpportunityAttack)
                continue;

            // Skip the already-planned type.
            if (action.Type == plannedType)
                continue;

            string key = action.Type.ToString();
            if (action.Target != null)
                key += $"_{action.Target.Id}";

            if (seen.Contains(key))
                continue;
            seen.Add(key);

            string displayText = action.Type.ToString();
            if (action.Target != null)
                displayText += $" {action.Target.Character.Name}";
            if (action.Type == ActionType.CastSpell && !string.IsNullOrEmpty(action.SpellName))
            {
                displayText = $"Cast {action.SpellName}";
                if (action.Target != null)
                    displayText += $" on {action.Target.Character.Name}";
            }

            var altDict = new Godot.Collections.Dictionary
            {
                { "display_text", displayText },
                { "action_type", action.Type.ToString() },
                { "target_id", action.Target?.Id ?? "" }
            };

            alternatives.Add(altDict);

            // Limit to 4 alternatives to keep the list manageable.
            if (alternatives.Count >= 4)
                break;
        }

        return alternatives;
    }
}
