using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ChroniclesUnbound.Core;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Combat;

/// <summary>
/// Main combat orchestrator for D&D 5e tactical combat. Manages initiative order,
/// turn flow, action validation/execution, death saves, opportunity attacks, and
/// victory/defeat detection. Emits signals for UI updates.
/// </summary>
public partial class CombatManager : Node
{
    private CombatState _state = CombatState.NotInCombat;
    private List<Combatant> _combatants = new();
    private int _currentTurnIndex;
    private int _roundNumber;
    private CombatGrid _grid;
    private SpellCastingManager _spellCastingManager;

    /// <summary>
    /// XP values for defeated enemies, accumulated during the encounter.
    /// Key = combatant ID, Value = XP reward.
    /// </summary>
    private Dictionary<string, int> _defeatedEnemyXP = new();

    /// <summary>
    /// Tracks which combatant IDs have advantage on their next attack against a specific target,
    /// granted by the Help action. Key = target combatant ID, Value = the ally who gets advantage.
    /// Consumed on the next attack roll against that target by the specified ally.
    /// </summary>
    private Dictionary<string, string> _helpAdvantage = new();

    // ── Signals ──────────────────────────────────────────────────────────

    [Signal] public delegate void CombatStartedEventHandler();
    [Signal] public delegate void TurnStartedEventHandler(string combatantId);
    [Signal] public delegate void ActionExecutedEventHandler(string description);
    [Signal] public delegate void TurnEndedEventHandler(string combatantId);
    [Signal] public delegate void CombatEndedEventHandler(bool playerVictory);
    [Signal] public delegate void CombatantDefeatedEventHandler(string combatantId);

    // ── Setup ────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes and starts a combat encounter. Creates Combatant wrappers from
    /// Character data, rolls initiative, sorts turn order, and places combatants on the grid.
    /// </summary>
    /// <param name="allies">Player character and companion characters.</param>
    /// <param name="enemies">Enemy characters (monsters converted to Character data).</param>
    /// <param name="grid">The combat grid to use for this encounter.</param>
    /// <param name="spellCastingManager">Optional spell casting manager for spell actions.</param>
    public void StartCombat(List<Character> allies, List<Character> enemies, CombatGrid grid,
        SpellCastingManager spellCastingManager = null)
    {
        if (_state == CombatState.InProgress)
            return;

        _grid = grid;
        _spellCastingManager = spellCastingManager;
        _combatants.Clear();
        _defeatedEnemyXP.Clear();
        _helpAdvantage.Clear();
        _roundNumber = 1;
        _currentTurnIndex = 0;

        _state = CombatState.RollingInitiative;

        // Create combatant wrappers for allies
        for (int i = 0; i < allies.Count; i++)
        {
            var character = allies[i];
            var combatant = CreateCombatant(character, isAlly: true, index: i);
            // First ally is the player character, rest are companions
            combatant.IsPlayerControlled = i == 0;
            _combatants.Add(combatant);
        }

        // Create combatant wrappers for enemies
        var enemyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < enemies.Count; i++)
        {
            var character = enemies[i];
            // Generate unique ID with suffix for duplicate monster names
            string baseName = character.Name.ToLowerInvariant().Replace(' ', '_');
            enemyCounts.TryGetValue(baseName, out int count);
            count++;
            enemyCounts[baseName] = count;
            string id = count > 1 ? $"{baseName}_{count}" : baseName;

            var combatant = new Combatant
            {
                Character = character,
                Id = id,
                IsAlly = false,
                IsPlayerControlled = false,
                HasExtraAttack = character.Features.Any(f =>
                    f.Equals("Extra Attack", StringComparison.OrdinalIgnoreCase)),
                Conditions = new ConditionManager()
            };
            _combatants.Add(combatant);
        }

        // Roll initiative for all combatants
        foreach (var combatant in _combatants)
        {
            int dexMod = combatant.Character.AbilityScores.GetModifier(Ability.Dexterity);
            combatant.Initiative = DiceRoller.RollInitiative(dexMod);
        }

        // Sort by initiative descending, break ties by higher DEX score
        _combatants.Sort((a, b) =>
        {
            int cmp = b.Initiative.CompareTo(a.Initiative);
            if (cmp != 0) return cmp;
            int dexA = a.Character.AbilityScores.Dexterity;
            int dexB = b.Character.AbilityScores.Dexterity;
            return dexB.CompareTo(dexA);
        });

        // Place combatants on the grid
        PlaceCombatantsOnGrid();

        _state = CombatState.InProgress;
        EmitSignal(SignalName.CombatStarted);

        // Start the first combatant's turn
        BeginCurrentTurn();
    }

    /// <summary>
    /// Assigns the SpellCastingManager after construction (e.g. if not available at StartCombat time).
    /// </summary>
    public void SetSpellCastingManager(SpellCastingManager manager)
    {
        _spellCastingManager = manager;
    }

    // ── Turn Management ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the combatant whose turn it currently is, or null if combat is not active.
    /// </summary>
    public Combatant GetCurrentCombatant()
    {
        if (_state != CombatState.InProgress || _combatants.Count == 0)
            return null;

        return _combatants[_currentTurnIndex];
    }

    /// <summary>
    /// Ends the current combatant's turn and advances to the next living combatant.
    /// Processes end-of-turn effects and checks for combat end.
    /// </summary>
    public void EndTurn()
    {
        if (_state != CombatState.InProgress)
            return;

        var current = GetCurrentCombatant();
        if (current == null)
            return;

        current.EndTurn();
        EmitSignal(SignalName.TurnEnded, current.Id);

        if (CheckCombatEnd())
            return;

        // Advance to next living combatant
        AdvanceToNextCombatant();
    }

    private void AdvanceToNextCombatant()
    {
        int count = _combatants.Count;
        bool newRoundTriggered = false;

        for (int i = 1; i <= count; i++)
        {
            int nextIndex = (_currentTurnIndex + i) % count;

            // When we wrap past the last combatant back to index 0, a new round begins
            if (nextIndex == 0 && !newRoundTriggered)
            {
                NextRound();
                newRoundTriggered = true;
            }

            var candidate = _combatants[nextIndex];

            // Skip dead combatants
            if (candidate.IsDead())
                continue;

            // Living, conscious combatants always get a turn
            if (candidate.IsConscious)
            {
                _currentTurnIndex = nextIndex;
                BeginCurrentTurn();
                return;
            }

            // Unconscious allies who aren't stabilized get a turn for death saves
            if (candidate.IsAlly && !candidate.IsStabilized())
            {
                _currentTurnIndex = nextIndex;
                BeginCurrentTurn();
                return;
            }
        }

        // No valid combatants found -- combat should have ended
        CheckCombatEnd();
    }

    private void BeginCurrentTurn()
    {
        var combatant = GetCurrentCombatant();
        if (combatant == null)
            return;

        // Process start-of-turn effects (hazard damage, condition ticks, etc.)
        ProcessStartOfTurnEffects(combatant);

        // Reset per-turn resources
        combatant.StartTurn();

        EmitSignal(SignalName.TurnStarted, combatant.Id);
    }

    private void NextRound()
    {
        _roundNumber++;

        // Clear Help advantage at the start of a new round (any unused Help expires)
        _helpAdvantage.Clear();
    }

    // ── Action Availability ──────────────────────────────────────────────

    /// <summary>
    /// Returns all valid actions the combatant can take given their remaining resources,
    /// conditions, and position on the grid.
    /// </summary>
    public List<CombatAction> GetAvailableActions(Combatant combatant)
    {
        var actions = new List<CombatAction>();

        if (combatant == null || combatant.IsDead())
            return actions;

        // Unconscious allies can only wait for death saves (processed automatically)
        if (!combatant.IsConscious)
            return actions;

        bool canAct = combatant.Conditions.CanTakeActions();
        bool canMove = combatant.Conditions.CanMove();

        // Movement actions
        if (canMove && combatant.RemainingMovement > 0)
        {
            var reachable = _grid.GetValidMovePositions(combatant.GridPosition, combatant.RemainingMovement);
            foreach (var pos in reachable)
            {
                actions.Add(new CombatAction
                {
                    Type = ActionType.Move,
                    Actor = combatant,
                    TargetPosition = pos
                });
            }
        }

        // Stand from prone
        if (canMove && combatant.Conditions.HasCondition(Condition.Prone))
        {
            int halfSpeed = combatant.Character.Speed / 2;
            if (combatant.RemainingMovement >= halfSpeed)
            {
                actions.Add(new CombatAction
                {
                    Type = ActionType.Stand,
                    Actor = combatant
                });
            }
        }

        // Standard actions
        if (canAct && !combatant.ActionUsed)
        {
            // Attack
            var attackTargets = GetValidAttackTargets(combatant);
            foreach (var target in attackTargets)
            {
                actions.Add(new CombatAction
                {
                    Type = ActionType.Attack,
                    Actor = combatant,
                    Target = target
                });
            }

            // Cast Spell
            if (_spellCastingManager != null)
            {
                foreach (string spellName in combatant.Character.SpellsKnown)
                {
                    for (int level = 0; level <= 9; level++)
                    {
                        if (_spellCastingManager.CanCastSpell(combatant.Character, spellName, level))
                        {
                            actions.Add(new CombatAction
                            {
                                Type = ActionType.CastSpell,
                                Actor = combatant,
                                SpellName = spellName,
                                SpellLevel = level
                            });
                            break; // Only list the lowest valid level to avoid flooding
                        }
                    }
                }
            }

            // Dash
            if (canMove)
            {
                actions.Add(new CombatAction
                {
                    Type = ActionType.Dash,
                    Actor = combatant
                });
            }

            // Dodge
            actions.Add(new CombatAction
            {
                Type = ActionType.Dodge,
                Actor = combatant
            });

            // Disengage
            actions.Add(new CombatAction
            {
                Type = ActionType.Disengage,
                Actor = combatant
            });

            // Help -- target an adjacent ally
            var adjacentAllies = GetAdjacentCombatants(combatant)
                .Where(c => c.IsAlly == combatant.IsAlly && c.IsConscious && c.Id != combatant.Id)
                .ToList();
            foreach (var ally in adjacentAllies)
            {
                actions.Add(new CombatAction
                {
                    Type = ActionType.Help,
                    Actor = combatant,
                    Target = ally
                });
            }

            // Hide -- requires Stealth check
            actions.Add(new CombatAction
            {
                Type = ActionType.Hide,
                Actor = combatant
            });
        }

        // Bonus actions
        if (!combatant.BonusActionUsed)
        {
            // Second Wind (Fighter feature, once per short rest)
            if (HasFeatureUses(combatant, "Second Wind"))
            {
                actions.Add(new CombatAction
                {
                    Type = ActionType.SecondWind,
                    Actor = combatant
                });
            }

            // Action Surge (Fighter feature, once per short rest)
            if (HasFeatureUses(combatant, "Action Surge") && combatant.ActionUsed)
            {
                actions.Add(new CombatAction
                {
                    Type = ActionType.ActionSurge,
                    Actor = combatant
                });
            }
        }

        return actions;
    }

    // ── Action Execution ─────────────────────────────────────────────────

    /// <summary>
    /// Validates and executes a combat action, applying all D&D 5e rules.
    /// Returns a result describing the outcome.
    /// </summary>
    public CombatActionResult ExecuteAction(CombatAction action)
    {
        if (_state != CombatState.InProgress)
            return CombatActionResult.Failure(action.Type, "Combat is not in progress.");

        if (action?.Actor == null)
            return CombatActionResult.Failure(action?.Type ?? ActionType.Attack, "Invalid action or actor.");

        if (action.Actor.IsDead())
            return CombatActionResult.Failure(action.Type, $"{action.Actor.Character.Name} is dead.");

        // Validate resource availability
        var validationError = ValidateActionResources(action);
        if (validationError != null)
            return validationError;

        CombatActionResult result = action.Type switch
        {
            ActionType.Move => ExecuteMove(action),
            ActionType.Attack => ExecuteAttack(action),
            ActionType.CastSpell => ExecuteCastSpell(action),
            ActionType.Dash => ExecuteDash(action),
            ActionType.Dodge => ExecuteDodge(action),
            ActionType.Disengage => ExecuteDisengage(action),
            ActionType.Help => ExecuteHelp(action),
            ActionType.Hide => ExecuteHide(action),
            ActionType.SecondWind => ExecuteSecondWind(action),
            ActionType.ActionSurge => ExecuteActionSurge(action),
            ActionType.OpportunityAttack => ExecuteOpportunityAttack(action),
            ActionType.Stand => ExecuteStand(action),
            _ => CombatActionResult.Failure(action.Type, $"Unhandled action type: {action.Type}")
        };

        EmitSignal(SignalName.ActionExecuted, result.Description);

        // Check for combat end after any action
        CheckCombatEnd();

        return result;
    }

    // ── Individual Action Implementations ────────────────────────────────

    private CombatActionResult ExecuteMove(CombatAction action)
    {
        var actor = action.Actor;
        var from = actor.GridPosition;
        var to = action.TargetPosition;

        // Validate the destination
        if (!_grid.CanMoveTo(to))
            return CombatActionResult.Failure(ActionType.Move, "Cannot move to that position.");

        // Calculate movement cost
        int costFeet = _grid.GetMovementCost(from, to);
        if (costFeet < 0)
            return CombatActionResult.Failure(ActionType.Move, "No valid path to that position.");

        if (costFeet > actor.RemainingMovement)
            return CombatActionResult.Failure(ActionType.Move,
                $"Not enough movement. Need {costFeet} ft, have {actor.RemainingMovement} ft.");

        // Get the path for step-by-step opportunity attack checking
        var path = _grid.FindPath(from, to);
        if (path.Count == 0)
            return CombatActionResult.Failure(ActionType.Move, "No valid path to that position.");

        var result = new CombatActionResult
        {
            ActionType = ActionType.Move,
            Success = true,
            Description = $"{actor.Character.Name} moves from ({from.X},{from.Y}) to ({to.X},{to.Y})."
        };

        // Walk the path step by step, checking for opportunity attacks
        for (int i = 1; i < path.Count; i++)
        {
            var stepFrom = path[i - 1];
            var stepTo = path[i];

            // Check for opportunity attacks when leaving a threatened square
            if (!actor.IsDisengaging)
            {
                var oaResult = CheckOpportunityAttack(actor, stepFrom, stepTo);
                if (oaResult != null)
                {
                    result.TriggeredOpportunityAttack = true;
                    result.OpportunityAttackResult = oaResult;

                    // If the mover was downed by the opportunity attack, stop moving
                    if (!actor.IsConscious)
                    {
                        // Update grid position to where they fell
                        _grid.MoveOccupant(actor.Id, stepFrom);
                        actor.GridPosition = stepFrom;
                        result.Description += $" Downed by opportunity attack at ({stepFrom.X},{stepFrom.Y}).";
                        return result;
                    }
                }
            }
        }

        // Complete the move
        _grid.MoveOccupant(actor.Id, to);
        actor.GridPosition = to;
        actor.RemainingMovement -= costFeet;

        return result;
    }

    private CombatActionResult ExecuteAttack(CombatAction action)
    {
        var actor = action.Actor;
        var target = action.Target;

        if (target == null)
            return CombatActionResult.Failure(ActionType.Attack, "No target specified.");

        if (target.IsDead())
            return CombatActionResult.Failure(ActionType.Attack, $"{target.Character.Name} is already dead.");

        // Check range -- melee is 5ft (adjacent), ranged uses weapon range
        float distance = _grid.GetDistance(actor.GridPosition, target.GridPosition);
        bool isMelee = distance <= 5f;

        if (!isMelee && distance > 120f) // Default max ranged attack range
            return CombatActionResult.Failure(ActionType.Attack, "Target is out of range.");

        // Check line of sight
        if (!_grid.HasLineOfSight(actor.GridPosition, target.GridPosition))
            return CombatActionResult.Failure(ActionType.Attack, "No line of sight to target.");

        // Calculate attack parameters
        int attackBonus = GetAttackBonus(actor, isMelee);
        string damageNotation = GetDamageNotation(actor);
        int damageBonus = GetDamageBonus(actor, isMelee);
        string damageType = GetDamageType(actor);

        // Determine advantage/disadvantage
        bool hasAdvantage = false;
        bool hasDisadvantage = false;

        // Attacker conditions
        if (actor.Conditions.HasDisadvantageOnAttacks())
            hasDisadvantage = true;

        if (actor.IsHidden || actor.Conditions.IsInvisible())
            hasAdvantage = true;

        // Target conditions
        if (target.Conditions.HasAdvantageOnAttacksAgainst())
            hasAdvantage = true;

        // Prone target: advantage for melee within 5ft, disadvantage for ranged
        if (target.Conditions.HasCondition(Condition.Prone))
        {
            if (isMelee)
                hasAdvantage = true;
            else
                hasDisadvantage = true;
        }

        // Invisible target gives disadvantage
        if (target.Conditions.IsInvisible() && !actor.Conditions.HasCondition(Condition.Blinded))
            hasDisadvantage = true;

        // Target is dodging -- attacks have disadvantage
        if (target.IsDodging)
            hasDisadvantage = true;

        // Help action advantage
        if (_helpAdvantage.TryGetValue(target.Id, out string helperId) &&
            helperId == actor.Id)
        {
            hasAdvantage = true;
            _helpAdvantage.Remove(target.Id); // Consumed
        }

        // Cover bonus to AC
        var cover = _grid.GetCover(actor.GridPosition, target.GridPosition);
        int coverBonus = cover switch
        {
            CoverType.Half => 2,
            CoverType.ThreeQuarters => 5,
            CoverType.Full => 999, // Can't target through full cover
            _ => 0
        };

        if (cover == CoverType.Full)
            return CombatActionResult.Failure(ActionType.Attack, "Target has full cover.");

        int effectiveAC = target.Character.ArmorClass + coverBonus;

        // Resolve the attack
        var attackResult = CombatResolver.ResolveAttack(
            attackBonus, effectiveAC, damageNotation, damageBonus, damageType,
            hasAdvantage, hasDisadvantage);

        var result = new CombatActionResult
        {
            ActionType = ActionType.Attack,
            Success = attackResult.Hits,
            AttackResult = attackResult,
            Description = FormatAttackDescription(actor, target, attackResult, isMelee)
        };

        if (attackResult.Hits)
        {
            int damage = attackResult.Damage;

            // Auto-crit on unconscious/paralyzed targets in melee
            if (isMelee && target.Conditions.MeleeHitsAutoCrit() && !attackResult.IsCritical)
            {
                // Already handled by the attack resolution if they have the condition,
                // but if target was unconscious, this counts as auto-crit for death saves
            }

            ApplyDamage(target, damage, isMelee);
            result.DamageDealt = damage;

            if (!target.IsConscious)
            {
                result.TargetDowned = true;
                if (target.IsDead())
                {
                    result.TargetKilled = true;
                    HandleCombatantDefeated(target);
                }
            }
        }

        // Consume attack from remaining attacks
        actor.AttacksRemaining--;
        if (actor.AttacksRemaining <= 0)
            actor.ActionUsed = true;

        // Attacking breaks hidden status
        actor.IsHidden = false;

        return result;
    }

    private CombatActionResult ExecuteCastSpell(CombatAction action)
    {
        var actor = action.Actor;

        if (_spellCastingManager == null)
            return CombatActionResult.Failure(ActionType.CastSpell, "Spell casting is not available.");

        var spellResult = _spellCastingManager.CastSpell(
            actor.Character, action.SpellName, action.SpellLevel);

        if (!spellResult.Success)
            return CombatActionResult.Failure(ActionType.CastSpell, spellResult.FailureReason);

        var result = new CombatActionResult
        {
            ActionType = ActionType.CastSpell,
            Success = true,
            SpellCastResult = spellResult,
            Description = $"{actor.Character.Name} casts {spellResult.SpellName}" +
                (spellResult.CastLevel > 0 ? $" at level {spellResult.CastLevel}" : "") + "."
        };

        // Apply damage if the spell deals damage and has a target
        if (spellResult.DamageRoll != null && action.Target != null)
        {
            int damage = Math.Max(0, spellResult.DamageRoll.Total);

            // Spell attack roll if applicable
            if (spellResult.SpellAttackBonus > 0 && action.Target != null)
            {
                float distance = _grid.GetDistance(actor.GridPosition, action.Target.GridPosition);
                bool isMelee = distance <= 5f;
                var attackResult = CombatResolver.ResolveAttack(
                    spellResult.SpellAttackBonus,
                    action.Target.Character.ArmorClass,
                    "0d1", 0, "Force", // Placeholder -- damage already rolled by spell system
                    false, false);

                if (!attackResult.Hits)
                {
                    result.Description += " The spell attack misses.";
                    result.DamageDealt = 0;
                    actor.ActionUsed = true;
                    return result;
                }
            }

            ApplyDamage(action.Target, damage, false);
            result.DamageDealt = damage;
            result.Description += $" Deals {damage} damage to {action.Target.Character.Name}.";

            if (!action.Target.IsConscious)
            {
                result.TargetDowned = true;
                if (action.Target.IsDead())
                {
                    result.TargetKilled = true;
                    HandleCombatantDefeated(action.Target);
                }
            }
        }

        // Healing spells
        if (spellResult.DamageRoll != null && action.Target != null &&
            spellResult.SpellName.Contains("Cure", StringComparison.OrdinalIgnoreCase))
        {
            int healing = Math.Max(0, spellResult.DamageRoll.Total);
            ApplyHealing(action.Target, healing);
            result.HealingDone = healing;
            result.Description += $" Heals {action.Target.Character.Name} for {healing} HP.";
        }

        actor.ActionUsed = true;
        return result;
    }

    private CombatActionResult ExecuteDash(CombatAction action)
    {
        var actor = action.Actor;

        // Dash doubles remaining movement for this turn
        float speedMultiplier = actor.Conditions.GetSpeedMultiplier();
        int baseSpeed = actor.Character.Speed;
        int dashMovement = actor.Conditions.CanMove()
            ? (int)(baseSpeed * speedMultiplier)
            : 0;

        actor.RemainingMovement += dashMovement;
        actor.ActionUsed = true;

        return CombatActionResult.SimpleSuccess(ActionType.Dash,
            $"{actor.Character.Name} dashes, gaining {dashMovement} ft of movement " +
            $"({actor.RemainingMovement} ft total remaining).");
    }

    private CombatActionResult ExecuteDodge(CombatAction action)
    {
        action.Actor.IsDodging = true;
        action.Actor.ActionUsed = true;

        return CombatActionResult.SimpleSuccess(ActionType.Dodge,
            $"{action.Actor.Character.Name} takes the Dodge action. " +
            "Attacks against them have disadvantage until next turn.");
    }

    private CombatActionResult ExecuteDisengage(CombatAction action)
    {
        action.Actor.IsDisengaging = true;
        action.Actor.ActionUsed = true;

        return CombatActionResult.SimpleSuccess(ActionType.Disengage,
            $"{action.Actor.Character.Name} takes the Disengage action. " +
            "Movement won't provoke opportunity attacks this turn.");
    }

    private CombatActionResult ExecuteHelp(CombatAction action)
    {
        var actor = action.Actor;
        var target = action.Target;

        if (target == null)
            return CombatActionResult.Failure(ActionType.Help, "No target specified for Help.");

        // Must be adjacent to the target (Help requires being within 5 ft of the enemy
        // you want to help against, but we simplify: you Help an ally, granting them
        // advantage on their next attack against any adjacent enemy)
        float distance = _grid.GetDistance(actor.GridPosition, target.GridPosition);
        if (distance > 5f)
            return CombatActionResult.Failure(ActionType.Help, "Target must be adjacent.");

        actor.IsHelping = true;
        actor.HelpTargetId = target.Id;
        actor.ActionUsed = true;

        // Find the nearest enemy to grant advantage against
        // D&D 5e Help: choose an enemy within 5ft of you. The next attack by an ally
        // against that enemy has advantage. We'll grant advantage to the helped ally
        // on their next attack against any valid target.
        var adjacentEnemies = GetAdjacentCombatants(actor)
            .Where(c => c.IsAlly != actor.IsAlly && c.IsConscious)
            .ToList();

        if (adjacentEnemies.Count > 0)
        {
            // Grant the helped ally advantage on their next attack against the first adjacent enemy
            foreach (var enemy in adjacentEnemies)
            {
                _helpAdvantage[enemy.Id] = target.Id;
            }
        }

        return CombatActionResult.SimpleSuccess(ActionType.Help,
            $"{actor.Character.Name} helps {target.Character.Name}. " +
            "Next attack by that ally against a nearby enemy has advantage.");
    }

    private CombatActionResult ExecuteHide(CombatAction action)
    {
        var actor = action.Actor;

        int stealthMod = actor.Character.GetSkillModifier(Skill.Stealth);
        bool hasDisadvantage = actor.Conditions.HasDisadvantageOnAbilityChecks();

        // Stealth check vs highest passive Perception of enemies that can see you
        int highestPP = 10; // Default DC
        foreach (var enemy in _combatants.Where(c => c.IsAlly != actor.IsAlly && c.IsConscious && !c.IsDead()))
        {
            if (_grid.HasLineOfSight(enemy.GridPosition, actor.GridPosition))
            {
                int pp = enemy.Character.PassivePerception;
                if (pp > highestPP)
                    highestPP = pp;
            }
        }

        var checkResult = CombatResolver.ResolveSkillCheck(
            "Stealth", stealthMod, highestPP,
            hasAdvantage: false, hasDisadvantage: hasDisadvantage);

        actor.ActionUsed = true;

        if (checkResult.Success)
        {
            actor.IsHidden = true;
            return CombatActionResult.SimpleSuccess(ActionType.Hide,
                $"{actor.Character.Name} hides successfully (Stealth {checkResult.Total} vs DC {highestPP}).");
        }

        return CombatActionResult.Failure(ActionType.Hide,
            $"{actor.Character.Name} fails to hide (Stealth {checkResult.Total} vs DC {highestPP}).");
    }

    private CombatActionResult ExecuteSecondWind(CombatAction action)
    {
        var actor = action.Actor;

        if (!HasFeatureUses(actor, "Second Wind"))
            return CombatActionResult.Failure(ActionType.SecondWind, "No uses of Second Wind remaining.");

        // Second Wind: regain 1d10 + fighter level HP as a bonus action
        int roll = DiceRoller.RollSingle(10);
        int healing = roll + actor.Character.Level;

        ApplyHealing(actor, healing);
        ConsumeFeatureUse(actor, "Second Wind");
        actor.BonusActionUsed = true;

        return new CombatActionResult
        {
            ActionType = ActionType.SecondWind,
            Success = true,
            HealingDone = healing,
            Description = $"{actor.Character.Name} uses Second Wind, healing for {healing} HP " +
                $"(1d10[{roll}] + {actor.Character.Level}). Now at {actor.Character.HitPoints}/{actor.Character.MaxHitPoints} HP."
        };
    }

    private CombatActionResult ExecuteActionSurge(CombatAction action)
    {
        var actor = action.Actor;

        if (!HasFeatureUses(actor, "Action Surge"))
            return CombatActionResult.Failure(ActionType.ActionSurge, "No uses of Action Surge remaining.");

        // Action Surge: reset the action for this turn
        actor.ActionUsed = false;
        actor.AttacksRemaining = actor.HasExtraAttack ? 2 : 1;

        ConsumeFeatureUse(actor, "Action Surge");
        actor.BonusActionUsed = true;

        return CombatActionResult.SimpleSuccess(ActionType.ActionSurge,
            $"{actor.Character.Name} uses Action Surge, gaining an additional action this turn!");
    }

    private CombatActionResult ExecuteOpportunityAttack(CombatAction action)
    {
        var actor = action.Actor;
        var target = action.Target;

        if (target == null)
            return CombatActionResult.Failure(ActionType.OpportunityAttack, "No target.");

        if (actor.ReactionUsed)
            return CombatActionResult.Failure(ActionType.OpportunityAttack, "Reaction already used.");

        if (!actor.Conditions.CanTakeReactions())
            return CombatActionResult.Failure(ActionType.OpportunityAttack, "Cannot take reactions.");

        // Resolve as a melee attack
        int attackBonus = GetAttackBonus(actor, isMelee: true);
        string damageNotation = GetDamageNotation(actor);
        int damageBonus = GetDamageBonus(actor, isMelee: true);
        string damageType = GetDamageType(actor);

        bool hasAdvantage = target.Conditions.HasAdvantageOnAttacksAgainst();
        bool hasDisadvantage = actor.Conditions.HasDisadvantageOnAttacks();

        var attackResult = CombatResolver.ResolveAttack(
            attackBonus,
            target.Character.ArmorClass,
            damageNotation,
            damageBonus,
            damageType,
            hasAdvantage,
            hasDisadvantage);

        actor.ReactionUsed = true;

        var result = new CombatActionResult
        {
            ActionType = ActionType.OpportunityAttack,
            Success = attackResult.Hits,
            AttackResult = attackResult,
            Description = $"{actor.Character.Name} makes an opportunity attack against {target.Character.Name}! " +
                (attackResult.Hits
                    ? $"Hit for {attackResult.Damage} {damageType} damage."
                    : "Miss!")
        };

        if (attackResult.Hits)
        {
            ApplyDamage(target, attackResult.Damage, isMelee: true);
            result.DamageDealt = attackResult.Damage;

            if (!target.IsConscious)
            {
                result.TargetDowned = true;
                if (target.IsDead())
                {
                    result.TargetKilled = true;
                    HandleCombatantDefeated(target);
                }
            }
        }

        return result;
    }

    private CombatActionResult ExecuteStand(CombatAction action)
    {
        var actor = action.Actor;

        if (!actor.Conditions.HasCondition(Condition.Prone))
            return CombatActionResult.Failure(ActionType.Stand, $"{actor.Character.Name} is not prone.");

        int halfSpeed = actor.Character.Speed / 2;
        if (actor.RemainingMovement < halfSpeed)
            return CombatActionResult.Failure(ActionType.Stand,
                $"Standing requires {halfSpeed} ft of movement, but only {actor.RemainingMovement} ft remaining.");

        actor.RemainingMovement -= halfSpeed;
        actor.Conditions.RemoveCondition(Condition.Prone);

        return CombatActionResult.SimpleSuccess(ActionType.Stand,
            $"{actor.Character.Name} stands up (costs {halfSpeed} ft of movement).");
    }

    // ── Death Saves ──────────────────────────────────────────────────────

    /// <summary>
    /// Processes a death saving throw for an unconscious ally.
    /// D&D 5e rules: d20, 10+ = success, nat 20 = regain 1 HP and become conscious,
    /// nat 1 = 2 failures. 3 successes = stabilized, 3 failures = dead.
    /// </summary>
    public (bool success, bool critSuccess, bool critFail) ProcessDeathSave(Combatant combatant)
    {
        if (combatant == null || combatant.IsConscious || combatant.IsDead() || combatant.IsStabilized())
            return (false, false, false);

        var (success, critSuccess, critFail) = CombatResolver.RollDeathSave();

        if (critSuccess)
        {
            // Nat 20: regain 1 HP, become conscious
            combatant.Character.HitPoints = 1;
            combatant.ResetDeathSaves();
            combatant.Conditions.RemoveCondition(Condition.Unconscious);

            string desc = $"{combatant.Character.Name} rolls a natural 20 on their death save! " +
                "They regain 1 HP and are conscious!";
            EmitSignal(SignalName.ActionExecuted, desc);
            return (true, true, false);
        }

        if (critFail)
        {
            // Nat 1: counts as 2 failures
            combatant.DeathSaveFailures += 2;
        }
        else if (success)
        {
            combatant.DeathSaveSuccesses++;
        }
        else
        {
            combatant.DeathSaveFailures++;
        }

        // Check for stabilization
        if (combatant.DeathSaveSuccesses >= 3)
        {
            string desc = $"{combatant.Character.Name} is stabilized with 3 successful death saves.";
            EmitSignal(SignalName.ActionExecuted, desc);
            return (success, false, false);
        }

        // Check for death
        if (combatant.DeathSaveFailures >= 3)
        {
            string desc = $"{combatant.Character.Name} has failed 3 death saves and is dead.";
            EmitSignal(SignalName.ActionExecuted, desc);
            HandleCombatantDefeated(combatant);
            return (false, false, critFail);
        }

        string saveDesc = $"{combatant.Character.Name} death save: " +
            (success ? "Success" : "Failure") +
            (critFail ? " (nat 1 = 2 failures!)" : "") +
            $" [{combatant.DeathSaveSuccesses} successes, {combatant.DeathSaveFailures} failures]";
        EmitSignal(SignalName.ActionExecuted, saveDesc);

        return (success, false, critFail);
    }

    // ── Opportunity Attack Check ─────────────────────────────────────────

    /// <summary>
    /// Checks if moving from one square to another triggers opportunity attacks from enemies.
    /// An opportunity attack is triggered when a creature leaves the reach of a hostile creature
    /// without using the Disengage action.
    /// </summary>
    private CombatActionResult CheckOpportunityAttack(Combatant mover, Vector2I from, Vector2I to)
    {
        // Get enemies adjacent to the 'from' position
        var adjacentIds = _grid.GetAdjacentOccupants(from);

        foreach (string id in adjacentIds)
        {
            var enemy = GetCombatantById(id);
            if (enemy == null || enemy.IsAlly == mover.IsAlly)
                continue; // Same team, no OA

            if (enemy.IsDead() || !enemy.IsConscious)
                continue;

            if (enemy.ReactionUsed)
                continue;

            if (!enemy.Conditions.CanTakeReactions())
                continue;

            // Check if the mover is leaving the enemy's reach (moving away from adjacency)
            float distAfter = _grid.GetDistance(enemy.GridPosition, to);
            if (distAfter <= 5f)
                continue; // Still adjacent, no OA triggered

            // Enemy makes an opportunity attack
            var oaAction = new CombatAction
            {
                Type = ActionType.OpportunityAttack,
                Actor = enemy,
                Target = mover
            };

            return ExecuteOpportunityAttack(oaAction);
        }

        return null;
    }

    // ── Combat End Detection ─────────────────────────────────────────────

    /// <summary>
    /// Checks if combat should end. All enemies dead = victory. All allies down/dead = defeat.
    /// </summary>
    private bool CheckCombatEnd()
    {
        if (_state != CombatState.InProgress)
            return _state == CombatState.CombatEnded;

        bool allEnemiesDead = _combatants
            .Where(c => !c.IsAlly)
            .All(c => c.IsDead());

        bool allAlliesDown = _combatants
            .Where(c => c.IsAlly)
            .All(c => c.IsDead() || !c.IsConscious);

        if (allEnemiesDead)
        {
            _state = CombatState.CombatEnded;
            EmitSignal(SignalName.CombatEnded, true);
            return true;
        }

        if (allAlliesDown)
        {
            _state = CombatState.CombatEnded;
            EmitSignal(SignalName.CombatEnded, false);
            return true;
        }

        return false;
    }

    // ── Queries ──────────────────────────────────────────────────────────

    public List<Combatant> GetAllCombatants() => new(_combatants);

    public List<Combatant> GetAllies() => _combatants.Where(c => c.IsAlly).ToList();

    public List<Combatant> GetEnemies() => _combatants.Where(c => !c.IsAlly).ToList();

    public Combatant GetCombatantById(string id)
    {
        return _combatants.FirstOrDefault(c =>
            string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public int GetRoundNumber() => _roundNumber;

    public CombatState GetState() => _state;

    /// <summary>
    /// Returns a TurnPhase snapshot for the current combatant.
    /// </summary>
    public TurnPhase GetCurrentTurnPhase()
    {
        var current = GetCurrentCombatant();
        return current != null ? TurnPhase.FromCombatant(current) : null;
    }

    // ── XP Calculation ───────────────────────────────────────────────────

    /// <summary>
    /// Calculates total XP earned from all defeated enemies in this encounter.
    /// </summary>
    public int CalculateEncounterXP()
    {
        int totalXP = 0;
        foreach (var kvp in _defeatedEnemyXP)
        {
            totalXP += kvp.Value;
        }
        return totalXP;
    }

    // ── Private Helpers ──────────────────────────────────────────────────

    private Combatant CreateCombatant(Character character, bool isAlly, int index)
    {
        string id = isAlly
            ? character.Name.ToLowerInvariant().Replace(' ', '_')
            : $"enemy_{index}";

        return new Combatant
        {
            Character = character,
            Id = id,
            IsAlly = isAlly,
            IsPlayerControlled = false,
            HasExtraAttack = character.CharacterClassName.Equals("Fighter", StringComparison.OrdinalIgnoreCase)
                && character.Level >= 5,
            Conditions = new ConditionManager()
        };
    }

    private void PlaceCombatantsOnGrid()
    {
        // Place allies on the left side of the grid, enemies on the right.
        // Exact placement depends on grid size; we use a simple column layout.
        int allyX = 1;
        int enemyX = Math.Max(_grid.GridWidth - 2, allyX + 4);

        int allyY = 1;
        int enemyY = 1;

        foreach (var combatant in _combatants)
        {
            Vector2I pos;
            if (combatant.IsAlly)
            {
                pos = FindOpenPosition(allyX, allyY, searchRight: false);
                allyY += 2;
                if (allyY >= _grid.GridHeight - 1)
                {
                    allyY = 1;
                    allyX++;
                }
            }
            else
            {
                pos = FindOpenPosition(enemyX, enemyY, searchRight: true);
                enemyY += 2;
                if (enemyY >= _grid.GridHeight - 1)
                {
                    enemyY = 1;
                    enemyX--;
                }
            }

            _grid.PlaceOccupant(combatant.Id, pos);
            combatant.GridPosition = pos;
        }
    }

    private Vector2I FindOpenPosition(int startX, int startY, bool searchRight)
    {
        // Search for an unoccupied, walkable cell near the starting position
        for (int radius = 0; radius < _grid.GridWidth; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                        continue; // Only check the outer ring

                    var pos = new Vector2I(startX + dx, startY + dy);
                    if (_grid.CanMoveTo(pos))
                        return pos;
                }
            }
        }

        // Fallback: scan entire grid
        for (int x = 0; x < _grid.GridWidth; x++)
        {
            for (int y = 0; y < _grid.GridHeight; y++)
            {
                var pos = new Vector2I(x, y);
                if (_grid.CanMoveTo(pos))
                    return pos;
            }
        }

        return Vector2I.Zero;
    }

    private CombatActionResult ValidateActionResources(CombatAction action)
    {
        var actor = action.Actor;

        if (action.IsStandardAction() && actor.ActionUsed)
            return CombatActionResult.Failure(action.Type, "Standard action already used this turn.");

        if (action.IsBonusAction() && actor.BonusActionUsed)
            return CombatActionResult.Failure(action.Type, "Bonus action already used this turn.");

        if (action.IsReaction() && actor.ReactionUsed)
            return CombatActionResult.Failure(action.Type, "Reaction already used.");

        // Standard action checks (attack uses AttacksRemaining instead of ActionUsed directly)
        if (action.Type == ActionType.Attack && actor.AttacksRemaining <= 0)
            return CombatActionResult.Failure(action.Type, "No attacks remaining this turn.");

        return null;
    }

    private void ApplyDamage(Combatant target, int damage, bool isMelee)
    {
        if (damage <= 0)
            return;

        // Temp HP absorbs damage first
        if (target.Character.TempHitPoints > 0)
        {
            if (target.Character.TempHitPoints >= damage)
            {
                target.Character.TempHitPoints -= damage;
                return;
            }
            damage -= target.Character.TempHitPoints;
            target.Character.TempHitPoints = 0;
        }

        // If target is already unconscious (at 0 HP), damage causes death save failures
        if (!target.IsConscious && target.IsAlly)
        {
            // Damage to unconscious = auto death save failure
            // Melee within 5ft = auto crit = 2 death save failures
            int failures = isMelee ? 2 : 1;
            target.DeathSaveFailures += failures;

            if (target.DeathSaveFailures >= 3)
            {
                HandleCombatantDefeated(target);
            }
            return;
        }

        target.Character.HitPoints -= damage;

        if (target.Character.HitPoints <= 0)
        {
            target.Character.HitPoints = 0;

            if (!target.IsAlly)
            {
                // Monsters die immediately at 0 HP
                HandleCombatantDefeated(target);
            }
            else
            {
                // Allies fall unconscious and start making death saves
                target.Conditions.ApplyCondition(Condition.Unconscious);
                target.Conditions.ApplyCondition(Condition.Prone);
            }
        }
    }

    private void ApplyHealing(Combatant target, int healing)
    {
        if (healing <= 0)
            return;

        bool wasUnconscious = !target.IsConscious;

        target.Character.HitPoints = Math.Min(
            target.Character.HitPoints + healing,
            target.Character.MaxHitPoints);

        // If healed from 0 HP, regain consciousness
        if (wasUnconscious && target.IsConscious)
        {
            target.ResetDeathSaves();
            target.Conditions.RemoveCondition(Condition.Unconscious);
            // Note: Prone remains -- they must use movement to stand
        }
    }

    private void HandleCombatantDefeated(Combatant combatant)
    {
        // Track XP for defeated enemies
        if (!combatant.IsAlly)
        {
            _defeatedEnemyXP[combatant.Id] = combatant.Character.ExperiencePoints;
        }

        _grid.RemoveOccupant(combatant.Id);
        EmitSignal(SignalName.CombatantDefeated, combatant.Id);
    }

    private void ProcessStartOfTurnEffects(Combatant combatant)
    {
        // Apply hazard damage if standing on a hazard tile
        var cell = _grid.GetCell(combatant.GridPosition);
        if (cell != null && cell.Type == TileType.Hazard && cell.HazardDamage > 0 && combatant.IsConscious)
        {
            ApplyDamage(combatant, cell.HazardDamage, isMelee: false);
            EmitSignal(SignalName.ActionExecuted,
                $"{combatant.Character.Name} takes {cell.HazardDamage} {cell.HazardDamageType ?? "hazard"} " +
                "damage from the terrain!");
        }
    }

    private List<Combatant> GetValidAttackTargets(Combatant attacker)
    {
        var targets = new List<Combatant>();

        foreach (var candidate in _combatants)
        {
            if (candidate.Id == attacker.Id)
                continue;
            if (candidate.IsAlly == attacker.IsAlly)
                continue; // Don't attack allies
            if (candidate.IsDead())
                continue;

            // Check line of sight
            if (!_grid.HasLineOfSight(attacker.GridPosition, candidate.GridPosition))
                continue;

            // Check if in range (melee = 5ft, ranged = 120ft default max)
            float distance = _grid.GetDistance(attacker.GridPosition, candidate.GridPosition);
            if (distance <= 120f)
                targets.Add(candidate);
        }

        return targets;
    }

    private List<Combatant> GetAdjacentCombatants(Combatant combatant)
    {
        var result = new List<Combatant>();
        var adjacentIds = _grid.GetAdjacentOccupants(combatant.GridPosition);

        foreach (string id in adjacentIds)
        {
            var adj = GetCombatantById(id);
            if (adj != null)
                result.Add(adj);
        }

        return result;
    }

    /// <summary>
    /// Calculates the attack bonus for a combatant.
    /// Melee: STR mod + proficiency (or DEX for finesse).
    /// Ranged: DEX mod + proficiency.
    /// </summary>
    private int GetAttackBonus(Combatant combatant, bool isMelee)
    {
        var scores = combatant.Character.AbilityScores;
        int profBonus = combatant.Character.ProficiencyBonus;

        int strMod = scores.GetModifier(Ability.Strength);
        int dexMod = scores.GetModifier(Ability.Dexterity);

        if (isMelee)
        {
            // Use higher of STR or DEX (assuming finesse is available as a simplification)
            // In a full implementation, we'd check the weapon properties
            return Math.Max(strMod, dexMod) + profBonus;
        }

        return dexMod + profBonus;
    }

    /// <summary>
    /// Returns the damage dice notation for the combatant's current weapon.
    /// Falls back to unarmed strike (1d1) if no weapon is equipped.
    /// </summary>
    private string GetDamageNotation(Combatant combatant)
    {
        // Check equipped main hand weapon
        if (combatant.Character.EquippedItems.TryGetValue("MainHand", out string weaponName))
        {
            var weapon = combatant.Character.InventoryItems
                .FirstOrDefault(item => string.Equals(item.Name, weaponName, StringComparison.OrdinalIgnoreCase));

            if (weapon != null && weapon.Properties.TryGetValue("damage", out string dmgNotation))
                return dmgNotation;
        }

        // Unarmed strike: 1 + STR modifier (represented as 1d1 for the roller, bonus added separately)
        return "1d1";
    }

    /// <summary>
    /// Returns the flat damage bonus (ability modifier) for attacks.
    /// </summary>
    private int GetDamageBonus(Combatant combatant, bool isMelee)
    {
        var scores = combatant.Character.AbilityScores;
        int strMod = scores.GetModifier(Ability.Strength);
        int dexMod = scores.GetModifier(Ability.Dexterity);

        if (isMelee)
            return Math.Max(strMod, dexMod);

        return dexMod;
    }

    /// <summary>
    /// Returns the damage type string for the combatant's current weapon.
    /// </summary>
    private string GetDamageType(Combatant combatant)
    {
        if (combatant.Character.EquippedItems.TryGetValue("MainHand", out string weaponName))
        {
            var weapon = combatant.Character.InventoryItems
                .FirstOrDefault(item => string.Equals(item.Name, weaponName, StringComparison.OrdinalIgnoreCase));

            if (weapon != null && weapon.Properties.TryGetValue("damageType", out string dmgType))
                return dmgType;
        }

        return "Bludgeoning"; // Unarmed strike
    }

    private bool HasFeatureUses(Combatant combatant, string featureName)
    {
        return combatant.Character.Features.Any(f =>
            f.Equals(featureName, StringComparison.OrdinalIgnoreCase))
            && combatant.Character.FeatureUsesRemaining.TryGetValue(featureName, out int uses)
            && uses > 0;
    }

    private void ConsumeFeatureUse(Combatant combatant, string featureName)
    {
        if (combatant.Character.FeatureUsesRemaining.TryGetValue(featureName, out int uses) && uses > 0)
        {
            combatant.Character.FeatureUsesRemaining[featureName] = uses - 1;
        }
    }

    private string FormatAttackDescription(Combatant attacker, Combatant target,
        AttackResult result, bool isMelee)
    {
        string attackType = isMelee ? "melee" : "ranged";
        string weaponName = "unarmed strike";

        if (attacker.Character.EquippedItems.TryGetValue("MainHand", out string wName))
            weaponName = wName;

        if (result.IsCritical)
        {
            return $"{attacker.Character.Name} scores a CRITICAL HIT on {target.Character.Name} " +
                $"with {weaponName}! {result.Damage} {result.DamageType} damage!";
        }

        if (result.IsFumble)
        {
            return $"{attacker.Character.Name} fumbles their {attackType} attack against " +
                $"{target.Character.Name} with {weaponName}!";
        }

        if (result.Hits)
        {
            return $"{attacker.Character.Name} hits {target.Character.Name} with {weaponName} " +
                $"({result.TotalAttack} vs AC {target.Character.ArmorClass}). " +
                $"{result.Damage} {result.DamageType} damage.";
        }

        return $"{attacker.Character.Name} misses {target.Character.Name} with {weaponName} " +
            $"({result.TotalAttack} vs AC {target.Character.ArmorClass}).";
    }
}
