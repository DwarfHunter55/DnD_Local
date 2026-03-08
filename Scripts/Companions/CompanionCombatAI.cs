using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using ChroniclesUnbound.Combat;
using ChroniclesUnbound.Core;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Companions;

/// <summary>
/// Utility-based combat AI for companion characters. Evaluates all available actions,
/// scores them with weighted utility functions, and produces a complete turn plan.
/// Personality (CombatTacticPreference) biases scores without overriding tactical fundamentals.
/// Small random variance prevents perfectly predictable behavior.
/// </summary>
public class CompanionCombatAI
{
    // Dice notation pattern for estimating spell damage without rolling.
    private static readonly Regex DicePattern = new(
        @"(\d+)d(\d+)(?:\s*\+\s*(\d+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Random _rng;
    private readonly SpellCastingManager _spellCastingManager;

    // ── Tuning Constants ────────────────────────────────────────────────

    // Variance applied to all scores to prevent robotic play.
    private const float RandomVariance = 0.08f;

    // Emergency thresholds (fraction of max HP).
    private const float CriticalHpThreshold = 0.20f;
    private const float LowHpThreshold = 0.40f;

    // Spell slot conservation: penalty per slot level to discourage burning high slots early.
    private const float SlotConservationPenaltyPerLevel = 0.04f;

    // Score floors/ceilings.
    private const float EmergencyHealScore = 100f;
    private const float MaxActionScore = 50f;

    // Positioning weights.
    private const float MeleeIdealDistance = 5f;   // Adjacent (1 cell)
    private const float RangedIdealMinDistance = 20f; // 4 cells away minimum
    private const float RangedIdealMaxDistance = 50f; // 10 cells is comfortable max

    // ── Constructor ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new CompanionCombatAI.
    /// </summary>
    /// <param name="spellCastingManager">
    /// The spell casting manager for spell validation and data lookup. May be null
    /// if no spellcasting is available.
    /// </param>
    /// <param name="seed">Optional RNG seed for deterministic testing. -1 for random.</param>
    public CompanionCombatAI(SpellCastingManager spellCastingManager = null, int seed = -1)
    {
        _spellCastingManager = spellCastingManager;
        _rng = seed >= 0 ? new Random(seed) : new Random();
    }

    // ── Main Entry Point ────────────────────────────────────────────────

    /// <summary>
    /// Evaluates the combat state and produces the best turn plan for the companion.
    /// </summary>
    public CombatAIPlan DecideTurn(Combatant companion, CombatManager combatManager, CombatGrid grid)
    {
        if (companion == null || combatManager == null || grid == null)
        {
            return new CombatAIPlan
            {
                Reasoning = "Invalid combat state -- no action taken.",
                ConfidenceScore = 0f,
                Priority = CombatPriority.Default
            };
        }

        var allies = combatManager.GetAllies()
            .Where(a => a.Id != companion.Id && !a.IsDead())
            .ToList();
        var enemies = combatManager.GetEnemies()
            .Where(e => !e.IsDead())
            .ToList();

        if (enemies.Count == 0)
        {
            return new CombatAIPlan
            {
                Reasoning = "No enemies remaining.",
                ConfidenceScore = 1f,
                Priority = CombatPriority.Default
            };
        }

        var tactic = companion.Character.CombatTactic;
        var priority = AssessPriority(companion, allies, enemies);
        var reasoning = new StringBuilder();

        // Get all available actions from the combat manager.
        var availableActions = combatManager.GetAvailableActions(companion);

        // Separate actions by type.
        var moveActions = availableActions.Where(a => a.Type == ActionType.Move).ToList();
        var standardActions = availableActions.Where(a => a.IsStandardAction()).ToList();
        var bonusActions = availableActions.Where(a => a.IsBonusAction()).ToList();

        reasoning.AppendLine($"Priority: {priority}. Tactic: {tactic}.");

        // ── Step 1: Score all standard actions ──────────────────────────

        CombatAction bestAction = null;
        float bestActionScore = float.MinValue;
        string bestActionReason = string.Empty;

        foreach (var action in standardActions)
        {
            float score = 0f;
            string reason = string.Empty;

            switch (action.Type)
            {
                case ActionType.Attack:
                    score = ScoreAttack(companion, action.Target, grid, tactic);
                    reason = $"Attack {action.Target.Character.Name}";
                    break;

                case ActionType.CastSpell:
                    score = ScoreSpellCast(companion, action, grid, allies, enemies, tactic, priority);
                    reason = $"Cast {action.SpellName}";
                    if (action.Target != null)
                        reason += $" on {action.Target.Character.Name}";
                    break;

                case ActionType.Dodge:
                    score = ScoreDodge(companion, enemies, grid, tactic);
                    reason = "Dodge";
                    break;

                case ActionType.Disengage:
                    score = ScoreDisengage(companion, enemies, grid, tactic);
                    reason = "Disengage";
                    break;

                case ActionType.Dash:
                    score = ScoreDash(companion, enemies, allies, grid, tactic);
                    reason = "Dash";
                    break;

                case ActionType.Help:
                    score = ScoreHelp(companion, action.Target, tactic);
                    reason = $"Help {action.Target?.Character.Name}";
                    break;

                case ActionType.Hide:
                    score = ScoreHide(companion, tactic);
                    reason = "Hide";
                    break;

                default:
                    continue;
            }

            score = ApplyPersonalityWeights(score, action.Type, tactic);
            score += AddVariance();

            if (score > bestActionScore)
            {
                bestActionScore = score;
                bestAction = action;
                bestActionReason = reason;
            }
        }

        // ── Step 2: Score bonus actions ─────────────────────────────────

        CombatAction bestBonus = null;
        float bestBonusScore = 0f; // Must beat zero to be worth using.
        string bestBonusReason = string.Empty;

        foreach (var action in bonusActions)
        {
            float score = 0f;
            string reason = string.Empty;

            switch (action.Type)
            {
                case ActionType.SecondWind:
                    score = ScoreSecondWind(companion);
                    reason = "Second Wind";
                    break;

                case ActionType.ActionSurge:
                    // Action Surge is extremely valuable when there are good attacks available.
                    score = bestActionScore > 10f ? bestActionScore * 0.8f : 0f;
                    reason = "Action Surge";
                    break;

                default:
                    continue;
            }

            score += AddVariance();

            if (score > bestBonusScore)
            {
                bestBonusScore = score;
                bestBonus = action;
                bestBonusReason = reason;
            }
        }

        // ── Step 3: Select best movement position ───────────────────────

        Vector2I? bestMovePos = null;
        List<Vector2I> bestMovePath = new();
        float bestMoveScore = float.MinValue;

        // Score staying in place as the baseline.
        float stayScore = ScoreMovement(companion, companion.GridPosition, grid, enemies, allies, tactic);
        bestMoveScore = stayScore;

        // Only evaluate movement if there are move actions and it serves the plan.
        if (moveActions.Count > 0)
        {
            // Sample positions rather than scoring every single reachable cell.
            // For a typical 30ft speed, there can be 30+ reachable cells.
            var candidates = SelectMoveCandidates(companion, moveActions, enemies, allies, grid);

            foreach (var pos in candidates)
            {
                float moveScore = ScoreMovement(companion, pos, grid, enemies, allies, tactic);

                // If we have a ranged best action and we'd move adjacent to an enemy,
                // penalize it (provokes disadvantage on ranged attacks).
                if (bestAction != null && bestAction.Type == ActionType.CastSpell)
                {
                    bool wouldBeAdjacent = enemies.Any(e =>
                        grid.GetDistance(pos, e.GridPosition) <= 5f);
                    if (wouldBeAdjacent)
                        moveScore -= 5f;
                }

                moveScore += AddVariance();

                if (moveScore > bestMoveScore)
                {
                    bestMoveScore = moveScore;
                    bestMovePos = pos;
                }
            }

            // Build path for visualization if we're moving.
            if (bestMovePos.HasValue && bestMovePos.Value != companion.GridPosition)
            {
                bestMovePath = grid.FindPath(companion.GridPosition, bestMovePos.Value);
            }
            else
            {
                bestMovePos = null; // Stay in place.
            }
        }

        // ── Step 4: Re-evaluate action from the new position ────────────
        // If we're moving, some attack targets may become reachable/unreachable.
        // For simplicity, we keep the already-scored best action. The CombatManager
        // will validate it at execution time, and if invalid, the turn still uses
        // the movement which was scored independently.

        // ── Step 5: Build reasoning string ──────────────────────────────

        if (bestMovePos.HasValue)
            reasoning.AppendLine($"Move to ({bestMovePos.Value.X},{bestMovePos.Value.Y}).");

        if (bestAction != null)
            reasoning.AppendLine($"Action: {bestActionReason} (score: {bestActionScore:F1}).");

        if (bestBonus != null)
            reasoning.AppendLine($"Bonus: {bestBonusReason} (score: {bestBonusScore:F1}).");

        // Confidence: normalize best action score to 0-1 range.
        float confidence = Mathf.Clamp(bestActionScore / MaxActionScore, 0f, 1f);

        return new CombatAIPlan
        {
            MoveTo = bestMovePos,
            MovePath = bestMovePath,
            PlannedAction = bestAction,
            PlannedBonusAction = bestBonus,
            Reasoning = reasoning.ToString().TrimEnd(),
            ConfidenceScore = confidence,
            Priority = priority
        };
    }

    // ── Situation Assessment ────────────────────────────────────────────

    /// <summary>
    /// Determines the strategic priority for this turn based on the battlefield state.
    /// </summary>
    private CombatPriority AssessPriority(
        Combatant companion, List<Combatant> allies, List<Combatant> enemies)
    {
        // Emergency: any ally is unconscious and making death saves.
        bool allyDying = allies.Any(a => !a.IsConscious && !a.IsDead() && !a.IsStabilized());
        if (allyDying)
            return CombatPriority.Emergency;

        // Emergency: self is critically low.
        float selfHpRatio = GetHpRatio(companion);
        if (selfHpRatio <= CriticalHpThreshold)
            return CombatPriority.Emergency;

        // Offensive: an enemy is low enough to likely finish off this turn.
        bool canFinish = enemies.Any(e =>
            e.IsConscious && GetHpRatio(e) <= 0.25f);
        if (canFinish)
            return CombatPriority.Offensive;

        // Support: an ally is below half HP.
        bool allyHurt = allies.Any(a => a.IsConscious && GetHpRatio(a) <= 0.50f);
        if (allyHurt && companion.Character.SpellsKnown.Count > 0)
            return CombatPriority.Support;

        // Control: outnumbered (more conscious enemies than allies + self).
        int allyCount = allies.Count(a => a.IsConscious) + 1; // +1 for self
        int enemyCount = enemies.Count(e => e.IsConscious);
        if (enemyCount > allyCount + 1)
            return CombatPriority.Control;

        return CombatPriority.Default;
    }

    // ── Action Scoring ──────────────────────────────────────────────────

    /// <summary>
    /// Scores a melee or ranged attack against a target.
    /// Score = expected damage * hit probability, weighted by target threat.
    /// </summary>
    private float ScoreAttack(
        Combatant actor, Combatant target, CombatGrid grid, CombatTacticPreference tactic)
    {
        if (target == null || target.IsDead() || !target.IsConscious)
            return -1f;

        float distance = grid.GetDistance(actor.GridPosition, target.GridPosition);
        bool isMelee = distance <= 5f;

        // Can't attack if out of range.
        if (!isMelee && distance > 120f)
            return -1f;

        // Line of sight check.
        if (!grid.HasLineOfSight(actor.GridPosition, target.GridPosition))
            return -1f;

        // Estimate hit chance.
        int attackBonus = EstimateAttackBonus(actor, isMelee);
        int targetAC = target.Character.ArmorClass;

        // Cover bonus.
        var cover = grid.GetCover(actor.GridPosition, target.GridPosition);
        int coverBonus = cover switch
        {
            CoverType.Half => 2,
            CoverType.ThreeQuarters => 5,
            CoverType.Full => 999,
            _ => 0
        };

        if (cover == CoverType.Full)
            return -1f;

        int effectiveAC = targetAC + coverBonus;
        float hitChance = EstimateHitChance(attackBonus, effectiveAC);

        // Estimate damage.
        float avgDamage = EstimateWeaponDamage(actor, isMelee);

        // Expected damage.
        float expectedDamage = avgDamage * hitChance;

        // Bonus: target is low HP and could be killed.
        if (target.Character.HitPoints <= avgDamage)
            expectedDamage *= 1.5f;

        // Threat weighting: prioritize more dangerous enemies.
        float threatMultiplier = EstimateThreat(target);
        float score = expectedDamage * threatMultiplier;

        // Ranged attack in melee has disadvantage.
        if (!isMelee && IsAdjacentToEnemy(actor, grid))
            score *= 0.6f;

        return score;
    }

    /// <summary>
    /// Scores casting a spell. Considers damage/healing value, resource cost, and priority.
    /// </summary>
    private float ScoreSpellCast(
        Combatant actor, CombatAction action, CombatGrid grid,
        List<Combatant> allies, List<Combatant> enemies,
        CombatTacticPreference tactic, CombatPriority priority)
    {
        if (_spellCastingManager == null)
            return -1f;

        var spell = _spellCastingManager.SpellDatabase.GetSpell(action.SpellName);
        if (spell == null)
            return -1f;

        float score = 0f;

        // Healing spells.
        if (IsHealingSpell(spell))
        {
            // Find the best healing target among allies (including self).
            var healTargets = new List<Combatant> { actor };
            healTargets.AddRange(allies);

            float bestHealScore = -1f;
            foreach (var target in healTargets.Where(t => t.IsConscious || !t.IsDead()))
            {
                float healScore = ScoreHealing(actor, target, spell, priority);
                if (healScore > bestHealScore)
                {
                    bestHealScore = healScore;
                    action.Target = target;
                }
            }
            score = bestHealScore;
        }
        // Damage spells.
        else if (!string.IsNullOrEmpty(spell.Damage))
        {
            float avgDamage = EstimateSpellDamage(spell, actor.Character.Level, action.SpellLevel);

            // Pick best target for damage spells.
            float bestDmgScore = -1f;
            Combatant bestTarget = null;

            foreach (var enemy in enemies.Where(e => e.IsConscious))
            {
                float dist = grid.GetDistance(actor.GridPosition, enemy.GridPosition);
                if (!IsInSpellRange(spell, dist))
                    continue;

                if (!grid.HasLineOfSight(actor.GridPosition, enemy.GridPosition))
                    continue;

                float hitFactor = 1f;

                // Spell attack roll.
                if (spell.SavingThrow == null && !string.IsNullOrEmpty(spell.Damage))
                {
                    int spellAttackBonus = actor.Character.ProficiencyBonus +
                        actor.Character.AbilityScores.GetModifier(
                            SpellCastingManager.GetCastingAbility(actor.Character.CharacterClassName));
                    hitFactor = EstimateHitChance(spellAttackBonus, enemy.Character.ArmorClass);
                }
                // Save-based spells: estimate ~60% effectiveness on average.
                else if (spell.SavingThrow.HasValue)
                {
                    hitFactor = 0.6f;
                }

                float dmgScore = avgDamage * hitFactor * EstimateThreat(enemy);

                // Bonus for finishing off a target.
                if (enemy.Character.HitPoints <= avgDamage)
                    dmgScore *= 1.4f;

                if (dmgScore > bestDmgScore)
                {
                    bestDmgScore = dmgScore;
                    bestTarget = enemy;
                }
            }

            if (bestTarget != null)
            {
                action.Target = bestTarget;
                score = bestDmgScore;
            }
            else
            {
                return -1f; // No valid target in range.
            }
        }
        // Utility/buff spells (no damage, no healing -- Dodge-like or buff).
        else
        {
            // Generic utility score based on spell level (higher = presumably more impactful).
            score = 5f + spell.Level * 2f;

            // Concentration spells are worth more if we're not already concentrating.
            if (spell.IsConcentration)
                score += 3f;
        }

        // Slot conservation penalty for non-cantrips.
        if (action.SpellLevel > 0)
        {
            float conservationPenalty = action.SpellLevel * SlotConservationPenaltyPerLevel * score;
            score -= conservationPenalty;

            // Extra penalty if this is our last slot at this level.
            int remaining = 0;
            for (int i = 0; i < actor.Character.SpellSlotsCurrent.Length; i++)
                remaining += actor.Character.SpellSlotsCurrent[i];

            if (remaining <= 2)
                score *= 0.7f; // Conserve last slots.
        }

        return Math.Max(score, -1f);
    }

    /// <summary>
    /// Scores healing a specific target with a specific spell.
    /// Unconscious allies get emergency-level scores.
    /// </summary>
    private float ScoreHealing(
        Combatant actor, Combatant target, SpellData spell, CombatPriority priority)
    {
        if (target == null)
            return -1f;

        // Healing an unconscious (dying) ally is the highest priority action in the game.
        if (!target.IsConscious && !target.IsDead() && !target.IsStabilized())
            return EmergencyHealScore;

        // Don't heal targets at full HP.
        if (target.Character.HitPoints >= target.Character.MaxHitPoints)
            return -1f;

        float missingHp = target.Character.MaxHitPoints - target.Character.HitPoints;
        float hpRatio = GetHpRatio(target);
        float avgHealing = EstimateSpellDamage(spell, actor.Character.Level, spell.Level);

        // Don't overheal: cap effective healing at missing HP.
        float effectiveHealing = Math.Min(avgHealing, missingHp);

        // Base score from effective healing amount.
        float score = effectiveHealing;

        // Urgency multiplier based on how hurt the target is.
        if (hpRatio <= CriticalHpThreshold)
            score *= 2.5f;
        else if (hpRatio <= LowHpThreshold)
            score *= 1.8f;
        else if (hpRatio <= 0.60f)
            score *= 1.3f;

        // If priority is Emergency or Support, boost healing scores further.
        if (priority == CombatPriority.Emergency)
            score *= 1.5f;
        else if (priority == CombatPriority.Support)
            score *= 1.2f;

        return score;
    }

    /// <summary>
    /// Scores a movement position based on tactical value:
    /// distance to enemies, distance to allies, cover, and tactic preference.
    /// </summary>
    private float ScoreMovement(
        Combatant actor, Vector2I position, CombatGrid grid,
        List<Combatant> enemies, List<Combatant> allies,
        CombatTacticPreference tactic)
    {
        float score = 0f;

        // Penalize hazard tiles.
        var cell = grid.GetCell(position);
        if (cell != null && cell.Type == TileType.Hazard)
            score -= 15f;

        // Distance to nearest enemy.
        float nearestEnemyDist = float.MaxValue;
        foreach (var enemy in enemies.Where(e => e.IsConscious))
        {
            float dist = grid.GetDistance(position, enemy.GridPosition);
            if (dist < nearestEnemyDist)
                nearestEnemyDist = dist;
        }

        // Distance to nearest conscious ally.
        float nearestAllyDist = float.MaxValue;
        foreach (var ally in allies.Where(a => a.IsConscious))
        {
            float dist = grid.GetDistance(position, ally.GridPosition);
            if (dist < nearestAllyDist)
                nearestAllyDist = dist;
        }

        bool isMeleeOriented = tactic == CombatTacticPreference.Aggressive ||
                               tactic == CombatTacticPreference.Balanced;
        bool isRangedOriented = tactic == CombatTacticPreference.Ranged;
        bool isSupportOriented = tactic == CombatTacticPreference.Support;
        bool isDefensiveOriented = tactic == CombatTacticPreference.Defensive;

        if (isMeleeOriented)
        {
            // Melee fighters want to be adjacent to enemies.
            if (nearestEnemyDist <= MeleeIdealDistance)
                score += 10f;
            else
                score -= (nearestEnemyDist - MeleeIdealDistance) * 0.3f;
        }
        else if (isRangedOriented)
        {
            // Ranged attackers want distance from enemies but within weapon range.
            if (nearestEnemyDist >= RangedIdealMinDistance && nearestEnemyDist <= RangedIdealMaxDistance)
                score += 10f;
            else if (nearestEnemyDist < RangedIdealMinDistance)
                score -= (RangedIdealMinDistance - nearestEnemyDist) * 0.5f;
            else if (nearestEnemyDist > RangedIdealMaxDistance)
                score -= (nearestEnemyDist - RangedIdealMaxDistance) * 0.2f;
        }
        else if (isSupportOriented)
        {
            // Support wants to be near allies but not in melee with enemies.
            if (nearestAllyDist <= 15f)
                score += 6f;
            else
                score -= (nearestAllyDist - 15f) * 0.2f;

            // Mild avoidance of melee range.
            if (nearestEnemyDist <= MeleeIdealDistance)
                score -= 4f;
        }
        else if (isDefensiveOriented)
        {
            // Defensive: stay near allies, avoid being isolated.
            if (nearestAllyDist <= 10f)
                score += 5f;

            // Prefer moderate distance from enemies.
            if (nearestEnemyDist >= 10f && nearestEnemyDist <= 30f)
                score += 4f;
            else if (nearestEnemyDist < 10f)
                score -= 3f;
        }

        // Cover bonus: prefer positions with cover from enemies.
        foreach (var enemy in enemies.Where(e => e.IsConscious))
        {
            var cover = grid.GetCover(enemy.GridPosition, position);
            score += cover switch
            {
                CoverType.Half => 2f,
                CoverType.ThreeQuarters => 4f,
                CoverType.Full => 6f,
                _ => 0f
            };
        }

        // Mild penalty for being too far from any ally (avoid isolation).
        if (nearestAllyDist > 30f && allies.Count > 0)
            score -= 3f;

        return score;
    }

    /// <summary>
    /// Scores the Dodge action. Best when surrounded by enemies and low on HP.
    /// </summary>
    private float ScoreDodge(
        Combatant actor, List<Combatant> enemies, CombatGrid grid,
        CombatTacticPreference tactic)
    {
        float score = 3f; // Low baseline.

        // Higher value when adjacent to multiple enemies.
        int adjacentEnemies = enemies.Count(e =>
            e.IsConscious && grid.GetDistance(actor.GridPosition, e.GridPosition) <= 5f);
        score += adjacentEnemies * 3f;

        // Higher value when HP is low.
        float hpRatio = GetHpRatio(actor);
        if (hpRatio <= CriticalHpThreshold)
            score += 10f;
        else if (hpRatio <= LowHpThreshold)
            score += 5f;

        return score;
    }

    /// <summary>
    /// Scores the Disengage action. Useful for escaping melee when playing ranged/support.
    /// </summary>
    private float ScoreDisengage(
        Combatant actor, List<Combatant> enemies, CombatGrid grid,
        CombatTacticPreference tactic)
    {
        // Only valuable if currently adjacent to enemies.
        bool inMelee = enemies.Any(e =>
            e.IsConscious && grid.GetDistance(actor.GridPosition, e.GridPosition) <= 5f);

        if (!inMelee)
            return -1f;

        float score = 5f;

        // More valuable for ranged/support characters stuck in melee.
        if (tactic == CombatTacticPreference.Ranged || tactic == CombatTacticPreference.Support)
            score += 8f;

        // More valuable when low on HP.
        float hpRatio = GetHpRatio(actor);
        if (hpRatio <= CriticalHpThreshold)
            score += 8f;
        else if (hpRatio <= LowHpThreshold)
            score += 4f;

        return score;
    }

    /// <summary>
    /// Scores the Dash action. Useful when nothing is in range and we need to close distance.
    /// </summary>
    private float ScoreDash(
        Combatant actor, List<Combatant> enemies, List<Combatant> allies,
        CombatGrid grid, CombatTacticPreference tactic)
    {
        float nearestEnemy = float.MaxValue;
        foreach (var e in enemies.Where(e => e.IsConscious))
        {
            float dist = grid.GetDistance(actor.GridPosition, e.GridPosition);
            if (dist < nearestEnemy)
                nearestEnemy = dist;
        }

        // Dash is only worthwhile if enemies are far away and we can't do anything else useful.
        if (nearestEnemy <= (float)actor.Character.Speed)
            return 1f; // Can already reach, dash is wasteful.

        // More valuable for melee characters who need to close distance.
        float score = 4f;
        if (tactic == CombatTacticPreference.Aggressive)
            score += 3f;

        return score;
    }

    /// <summary>
    /// Scores the Help action on an adjacent ally.
    /// </summary>
    private float ScoreHelp(Combatant actor, Combatant target, CombatTacticPreference tactic)
    {
        if (target == null)
            return -1f;

        // Help is moderately useful -- grants advantage on next attack.
        // Better for support-oriented characters.
        float score = 6f;

        // Extra value if the ally is a high-damage dealer.
        if (target.HasExtraAttack)
            score += 3f;

        return score;
    }

    /// <summary>
    /// Scores the Hide action.
    /// </summary>
    private float ScoreHide(Combatant actor, CombatTacticPreference tactic)
    {
        // Hide gives advantage on next attack and makes enemies unable to target you.
        float score = 5f;

        // More valuable for Ranged fighters (hide + snipe pattern).
        if (tactic == CombatTacticPreference.Ranged)
            score += 4f;

        // Less valuable in melee (enemies know where you are).
        return score;
    }

    /// <summary>
    /// Scores using Second Wind (Fighter bonus action).
    /// </summary>
    private float ScoreSecondWind(Combatant actor)
    {
        float hpRatio = GetHpRatio(actor);

        // No point if at full HP.
        if (hpRatio >= 0.95f)
            return -1f;

        // Average healing: 1d10 + level, average = 5.5 + level.
        float avgHealing = 5.5f + actor.Character.Level;
        float missingHp = actor.Character.MaxHitPoints - actor.Character.HitPoints;
        float effectiveHealing = Math.Min(avgHealing, missingHp);

        float score = effectiveHealing;

        // Urgency scaling.
        if (hpRatio <= CriticalHpThreshold)
            score *= 2.0f;
        else if (hpRatio <= LowHpThreshold)
            score *= 1.5f;

        return score;
    }

    // ── Personality Weighting ───────────────────────────────────────────

    /// <summary>
    /// Applies personality-based score multipliers. Biases decisions without
    /// overriding fundamentally correct tactical choices.
    /// </summary>
    private float ApplyPersonalityWeights(float baseScore, ActionType type, CombatTacticPreference tactic)
    {
        // Negative scores stay negative -- personality doesn't make bad actions good.
        if (baseScore <= 0f)
            return baseScore;

        float multiplier = 1.0f;

        switch (tactic)
        {
            case CombatTacticPreference.Aggressive:
                if (type == ActionType.Attack)
                    multiplier = 1.3f;
                else if (type == ActionType.Dodge || type == ActionType.Disengage)
                    multiplier = 0.6f;
                else if (type == ActionType.CastSpell)
                    multiplier = 1.1f; // Slightly prefer damage spells (spell scoring handles this).
                break;

            case CombatTacticPreference.Defensive:
                if (type == ActionType.Dodge || type == ActionType.Disengage)
                    multiplier = 1.4f;
                else if (type == ActionType.Attack)
                    multiplier = 0.9f;
                else if (type == ActionType.Hide)
                    multiplier = 1.2f;
                break;

            case CombatTacticPreference.Support:
                if (type == ActionType.CastSpell)
                    multiplier = 1.25f; // Healing/buff spells score high inherently.
                else if (type == ActionType.Help)
                    multiplier = 1.3f;
                else if (type == ActionType.Attack)
                    multiplier = 0.85f;
                break;

            case CombatTacticPreference.Ranged:
                // Ranged preference is mostly handled by movement scoring.
                if (type == ActionType.Hide)
                    multiplier = 1.2f;
                else if (type == ActionType.Disengage)
                    multiplier = 1.15f;
                break;

            case CombatTacticPreference.Balanced:
                // No strong bias. Slight preference for attacks.
                if (type == ActionType.Attack)
                    multiplier = 1.05f;
                break;
        }

        return baseScore * multiplier;
    }

    // ── Movement Candidate Selection ────────────────────────────────────

    /// <summary>
    /// Selects a subset of reachable positions to evaluate, focusing on tactically
    /// interesting spots rather than exhaustively scoring every cell.
    /// </summary>
    private List<Vector2I> SelectMoveCandidates(
        Combatant actor, List<CombatAction> moveActions,
        List<Combatant> enemies, List<Combatant> allies, CombatGrid grid)
    {
        var candidates = new HashSet<Vector2I>();
        var allMovePositions = moveActions.Select(a => a.TargetPosition).ToList();

        // Always consider positions adjacent to enemies (for melee attacks).
        foreach (var enemy in enemies.Where(e => e.IsConscious))
        {
            var adjacent = grid.GetAdjacentPositions(enemy.GridPosition);
            foreach (var pos in adjacent)
            {
                if (allMovePositions.Contains(pos))
                    candidates.Add(pos);
            }
        }

        // Consider positions adjacent to hurt allies (for healing/support).
        foreach (var ally in allies.Where(a => !a.IsDead() && GetHpRatio(a) < 0.5f))
        {
            var adjacent = grid.GetAdjacentPositions(ally.GridPosition);
            foreach (var pos in adjacent)
            {
                if (allMovePositions.Contains(pos))
                    candidates.Add(pos);
            }
        }

        // For ranged characters: sample positions at moderate distance from enemies.
        if (actor.Character.CombatTactic == CombatTacticPreference.Ranged ||
            actor.Character.CombatTactic == CombatTacticPreference.Support)
        {
            foreach (var pos in allMovePositions)
            {
                float nearestDist = enemies
                    .Where(e => e.IsConscious)
                    .Select(e => grid.GetDistance(pos, e.GridPosition))
                    .DefaultIfEmpty(float.MaxValue)
                    .Min();

                if (nearestDist >= RangedIdealMinDistance && nearestDist <= RangedIdealMaxDistance)
                    candidates.Add(pos);
            }
        }

        // If we have very few candidates, add random samples from all reachable positions.
        if (candidates.Count < 5 && allMovePositions.Count > 0)
        {
            int samplesToAdd = Math.Min(8, allMovePositions.Count);
            for (int i = 0; i < samplesToAdd; i++)
            {
                int idx = _rng.Next(allMovePositions.Count);
                candidates.Add(allMovePositions[idx]);
            }
        }

        // Cap at a reasonable number to prevent excessive computation.
        if (candidates.Count > 20)
        {
            return candidates.OrderBy(_ => _rng.Next()).Take(20).ToList();
        }

        return candidates.ToList();
    }

    // ── Estimation Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Estimates hit probability as a value 0-1 given attack bonus and target AC.
    /// Accounts for nat 1 always missing and nat 20 always hitting.
    /// </summary>
    private static float EstimateHitChance(int attackBonus, int targetAC)
    {
        // Need to roll (AC - bonus) or higher on d20.
        int needed = targetAC - attackBonus;

        // Nat 1 always misses, nat 20 always hits, so clamp to [2, 20].
        needed = Math.Clamp(needed, 2, 20);

        // Number of faces that hit: 21 - needed (including the needed value).
        float chance = (21 - needed) / 20f;
        return Math.Clamp(chance, 0.05f, 0.95f); // Floor at 5% (nat 20), cap at 95%.
    }

    /// <summary>
    /// Estimates average weapon damage per hit for a combatant.
    /// </summary>
    private static float EstimateWeaponDamage(Combatant actor, bool isMelee)
    {
        float baseDamage = 4.5f; // Fallback: ~1d8 average.

        // Try to get actual weapon damage.
        if (actor.Character.EquippedItems.TryGetValue("MainHand", out string weaponName))
        {
            var weapon = actor.Character.InventoryItems
                .FirstOrDefault(item => string.Equals(item.Name, weaponName, StringComparison.OrdinalIgnoreCase));

            if (weapon != null && weapon.Properties.TryGetValue("damage", out string dmgNotation))
                baseDamage = EstimateAverageDice(dmgNotation);
        }

        // Add ability modifier.
        int strMod = actor.Character.AbilityScores.GetModifier(Ability.Strength);
        int dexMod = actor.Character.AbilityScores.GetModifier(Ability.Dexterity);
        int abilityBonus = isMelee ? Math.Max(strMod, dexMod) : dexMod;

        return baseDamage + abilityBonus;
    }

    /// <summary>
    /// Estimates attack bonus (ability mod + proficiency).
    /// </summary>
    private static int EstimateAttackBonus(Combatant actor, bool isMelee)
    {
        int strMod = actor.Character.AbilityScores.GetModifier(Ability.Strength);
        int dexMod = actor.Character.AbilityScores.GetModifier(Ability.Dexterity);
        int profBonus = actor.Character.ProficiencyBonus;

        return (isMelee ? Math.Max(strMod, dexMod) : dexMod) + profBonus;
    }

    /// <summary>
    /// Estimates average damage from a spell, accounting for cantrip scaling and upcasting.
    /// </summary>
    private static float EstimateSpellDamage(SpellData spell, int casterLevel, int castLevel)
    {
        if (string.IsNullOrEmpty(spell.Damage))
            return 0f;

        float avg = EstimateAverageDice(spell.Damage);

        // Cantrip scaling.
        if (spell.Level == 0)
        {
            int multiplier = casterLevel switch
            {
                >= 17 => 4,
                >= 11 => 3,
                >= 5 => 2,
                _ => 1
            };
            avg *= multiplier;
        }
        // Upcast scaling: estimate +1 die per level above base (common SRD pattern).
        else if (castLevel > spell.Level)
        {
            var match = DicePattern.Match(spell.Damage);
            if (match.Success)
            {
                int dieSides = int.Parse(match.Groups[2].Value);
                int levelsAbove = castLevel - spell.Level;
                avg += levelsAbove * (dieSides + 1) / 2f;
            }
        }

        return avg;
    }

    /// <summary>
    /// Parses dice notation and returns the statistical average.
    /// E.g., "2d6+3" returns 10 (avg 2d6 = 7, + 3 = 10).
    /// </summary>
    private static float EstimateAverageDice(string notation)
    {
        if (string.IsNullOrEmpty(notation))
            return 0f;

        var match = DicePattern.Match(notation);
        if (!match.Success)
            return 0f;

        int count = int.Parse(match.Groups[1].Value);
        int sides = int.Parse(match.Groups[2].Value);
        int bonus = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

        return count * (sides + 1) / 2f + bonus;
    }

    /// <summary>
    /// Estimates how threatening an enemy combatant is based on observable factors.
    /// Higher threat = higher priority target.
    /// </summary>
    private static float EstimateThreat(Combatant target)
    {
        float threat = 1.0f;

        // Higher level/CR enemies are more threatening.
        threat += target.Character.Level * 0.1f;

        // Spellcasters are high-value targets.
        if (target.Character.SpellsKnown.Count > 0)
            threat += 0.3f;

        // Extra Attack means higher damage output.
        if (target.HasExtraAttack)
            threat += 0.2f;

        // Low HP enemies are less threatening but also easier to finish off.
        float hpRatio = GetHpRatio(target);
        if (hpRatio <= 0.25f)
            threat += 0.3f; // Bonus for being finishable.

        return threat;
    }

    /// <summary>
    /// Returns the fraction of HP remaining (0.0 to 1.0).
    /// </summary>
    private static float GetHpRatio(Combatant combatant)
    {
        if (combatant.Character.MaxHitPoints <= 0)
            return 0f;

        return (float)combatant.Character.HitPoints / combatant.Character.MaxHitPoints;
    }

    /// <summary>
    /// Returns true if the actor is adjacent (within 5ft) to any conscious enemy.
    /// </summary>
    private static bool IsAdjacentToEnemy(Combatant actor, CombatGrid grid)
    {
        var adjacentIds = grid.GetAdjacentOccupants(actor.GridPosition);
        // We don't have the full combatant list here, so we check if any occupant
        // is adjacent. The caller context ensures enemies are checked.
        return adjacentIds.Count > 0;
    }

    /// <summary>
    /// Checks if a spell's range covers the given distance.
    /// Parses the range string for numeric feet values.
    /// </summary>
    private static bool IsInSpellRange(SpellData spell, float distanceFeet)
    {
        if (string.IsNullOrEmpty(spell.Range))
            return false;

        string range = spell.Range.ToLowerInvariant();

        if (range.Contains("self"))
            return true; // Self-targeted spells always work.

        if (range.Contains("touch"))
            return distanceFeet <= 5f;

        // Try to extract numeric range.
        var match = Regex.Match(range, @"(\d+)\s*(?:feet|ft\.?)");
        if (match.Success)
        {
            int rangeFeet = int.Parse(match.Groups[1].Value);
            return distanceFeet <= rangeFeet;
        }

        // If we can't parse the range, assume it works at typical combat distances.
        return distanceFeet <= 60f;
    }

    /// <summary>
    /// Returns true if the spell is a healing spell (heuristic based on name/description).
    /// </summary>
    private static bool IsHealingSpell(SpellData spell)
    {
        string name = spell.Name.ToLowerInvariant();
        return name.Contains("cure") ||
               name.Contains("heal") ||
               name.Contains("restoration") ||
               name.Contains("revivify") ||
               name.Contains("goodberry");
    }

    /// <summary>
    /// Adds small random variance to a score to prevent perfectly predictable behavior.
    /// </summary>
    private float AddVariance()
    {
        // Returns a value in [-variance, +variance].
        return ((float)_rng.NextDouble() * 2f - 1f) * RandomVariance * MaxActionScore;
    }
}
