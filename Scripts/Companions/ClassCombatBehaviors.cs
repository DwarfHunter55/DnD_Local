using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ChroniclesUnbound.Combat;
using ChroniclesUnbound.Core;
using ChroniclesUnbound.Data;

namespace ChroniclesUnbound.Companions;

/// <summary>
/// Suggested combat action produced by a class behavior, with reasoning and score modifier.
/// </summary>
public class ClassActionSuggestion
{
    /// <summary>The suggested action, or null if no class-specific suggestion applies.</summary>
    public CombatAction Action { get; set; }

    /// <summary>Score bonus/penalty to apply to the base AI plan when this suggestion is chosen.</summary>
    public float ScoreModifier { get; set; }

    /// <summary>Human-readable explanation of why this class behavior recommends this action.</summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>Suggested movement target, or null to let the base AI decide.</summary>
    public Vector2I? PreferredPosition { get; set; }
}

/// <summary>
/// Abstract base for class-specific combat behavior modifiers. Each subclass encodes
/// the tactical identity of a D&D 5e class, adjusting the base utility AI's plan
/// to match how that class should fight.
/// </summary>
public abstract class ClassCombatBehavior
{
    // ── Shared Constants ─────────────────────────────────────────────────

    protected const float HpThresholdUrgent = 0.25f;
    protected const float HpThresholdLow = 0.40f;
    protected const float MeleeRange = 5f;
    protected const float MidRange = 20f;
    protected const float MaxRange = 60f;

    /// <summary>
    /// Adjusts an existing combat AI plan based on class-specific tactical priorities.
    /// Called after the base CompanionCombatAI has produced an initial plan, allowing
    /// class behaviors to override or bias the decision.
    /// </summary>
    /// <param name="plan">The base AI plan to modify in place.</param>
    /// <param name="self">The companion combatant.</param>
    /// <param name="allies">Living allied combatants (excluding self).</param>
    /// <param name="enemies">Living enemy combatants.</param>
    /// <param name="grid">The combat grid for distance/LOS queries.</param>
    public abstract void ModifyPriorities(
        CombatAIPlan plan, Combatant self,
        List<Combatant> allies, List<Combatant> enemies,
        CombatGrid grid);

    /// <summary>
    /// Suggests the optimal action for this class given the combat state.
    /// Returns null if the base AI's plan is already adequate.
    /// </summary>
    /// <param name="self">The companion combatant.</param>
    /// <param name="allies">Living allied combatants (excluding self).</param>
    /// <param name="enemies">Living enemy combatants.</param>
    /// <param name="grid">The combat grid for distance/LOS queries.</param>
    /// <param name="spellCastingManager">Spell system reference, may be null.</param>
    public abstract ClassActionSuggestion GetPreferredAction(
        Combatant self, List<Combatant> allies, List<Combatant> enemies,
        CombatGrid grid, SpellCastingManager spellCastingManager = null);

    /// <summary>
    /// Returns the appropriate class behavior for the given D&D class name.
    /// Falls back to null for unrecognized classes (base AI handles them fine).
    /// </summary>
    public static ClassCombatBehavior GetBehaviorForClass(string className)
    {
        if (string.IsNullOrEmpty(className))
            return null;

        return className.ToLowerInvariant() switch
        {
            "fighter" => new FighterBehavior(),
            "cleric" => new ClericBehavior(),
            "rogue" => new RogueBehavior(),
            "wizard" => new WizardBehavior(),
            _ => null
        };
    }

    // ── Shared Helpers ───────────────────────────────────────────────────

    protected static float GetHpRatio(Combatant c)
    {
        if (c.Character.MaxHitPoints <= 0) return 0f;
        return (float)c.Character.HitPoints / c.Character.MaxHitPoints;
    }

    protected static int CountAdjacentEnemies(Combatant self, List<Combatant> enemies, CombatGrid grid)
    {
        return enemies.Count(e =>
            e.IsConscious && grid.GetDistance(self.GridPosition, e.GridPosition) <= MeleeRange);
    }

    protected static int CountEnemiesInRadius(Vector2I center, List<Combatant> enemies, CombatGrid grid, float radius)
    {
        return enemies.Count(e =>
            e.IsConscious && grid.GetDistance(center, e.GridPosition) <= radius);
    }

    /// <summary>
    /// Finds the enemy that poses the highest threat to allies based on position and damage potential.
    /// </summary>
    protected static Combatant FindBiggestThreatToAllies(
        List<Combatant> allies, List<Combatant> enemies, CombatGrid grid)
    {
        Combatant biggestThreat = null;
        float highestThreatScore = float.MinValue;

        foreach (var enemy in enemies.Where(e => e.IsConscious))
        {
            float threat = 1f + enemy.Character.Level * 0.15f;

            // Spellcasters are more dangerous.
            if (enemy.Character.SpellsKnown.Count > 0)
                threat += 0.5f;

            // Extra Attack means higher sustained damage.
            if (enemy.HasExtraAttack)
                threat += 0.3f;

            // Enemies adjacent to squishy allies are immediate threats.
            foreach (var ally in allies.Where(a => a.IsConscious))
            {
                float dist = grid.GetDistance(enemy.GridPosition, ally.GridPosition);
                if (dist <= MeleeRange)
                {
                    float allyHpRatio = GetHpRatio(ally);
                    // Lower HP allies being threatened is more urgent.
                    threat += (1f - allyHpRatio) * 0.8f;
                }
            }

            if (threat > highestThreatScore)
            {
                highestThreatScore = threat;
                biggestThreat = enemy;
            }
        }

        return biggestThreat;
    }

    /// <summary>
    /// Finds the ally with the lowest HP ratio who is still conscious.
    /// </summary>
    protected static Combatant FindMostHurtAlly(List<Combatant> allies)
    {
        Combatant mostHurt = null;
        float lowestRatio = float.MaxValue;

        foreach (var ally in allies.Where(a => a.IsConscious))
        {
            float ratio = GetHpRatio(ally);
            if (ratio < lowestRatio)
            {
                lowestRatio = ratio;
                mostHurt = ally;
            }
        }

        return mostHurt;
    }

    /// <summary>
    /// Checks if a combatant likely has spells (based on SpellsKnown count).
    /// </summary>
    protected static bool IsSpellcaster(Combatant c)
    {
        return c.Character.SpellsKnown.Count > 0;
    }

    /// <summary>
    /// Checks if an enemy is likely undead based on its name containing common undead keywords.
    /// Since Character lacks a CreatureType field, this is a heuristic.
    /// </summary>
    protected static bool IsLikelyUndead(Combatant c)
    {
        string name = c.Character.Name.ToLowerInvariant();
        return name.Contains("skeleton") || name.Contains("zombie") || name.Contains("ghoul") ||
               name.Contains("ghost") || name.Contains("wight") || name.Contains("wraith") ||
               name.Contains("vampire") || name.Contains("lich") || name.Contains("mummy") ||
               name.Contains("specter") || name.Contains("shadow") || name.Contains("revenant");
    }

    /// <summary>
    /// Finds a position adjacent to the target from the set of reachable positions.
    /// Returns null if no adjacent position is reachable.
    /// </summary>
    protected static Vector2I? FindAdjacentPosition(
        Combatant self, Combatant target, CombatGrid grid)
    {
        var adjacent = grid.GetAdjacentPositions(target.GridPosition);
        Vector2I? bestPos = null;
        float bestDist = float.MaxValue;

        foreach (var pos in adjacent)
        {
            if (!grid.CanMoveTo(pos))
                continue;

            float dist = grid.GetDistance(self.GridPosition, pos);
            // Must be reachable within movement.
            if (dist > self.RemainingMovement)
                continue;

            if (dist < bestDist)
            {
                bestDist = dist;
                bestPos = pos;
            }
        }

        return bestPos;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// FIGHTER BEHAVIOR
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Fighter tactical behavior: frontline melee engagement, protecting allies,
/// using Action Surge for burst damage, and targeting the biggest threats.
/// </summary>
public class FighterBehavior : ClassCombatBehavior
{
    // Number of enemies that should be clustered to trigger Action Surge.
    private const int ActionSurgeClusterThreshold = 2;

    // Score boost for attacking the biggest threat to allies.
    private const float ThreatTargetBonus = 8f;

    // Score boost for positioning to protect squishy allies.
    private const float ProtectAllyBonus = 6f;

    public override void ModifyPriorities(
        CombatAIPlan plan, Combatant self,
        List<Combatant> allies, List<Combatant> enemies,
        CombatGrid grid)
    {
        if (plan == null) return;

        // Fighters shift toward Offensive priority more readily.
        if (plan.Priority == CombatPriority.Default && enemies.Count > 0)
        {
            var biggestThreat = FindBiggestThreatToAllies(allies, enemies, grid);
            if (biggestThreat != null)
            {
                // If the biggest threat is adjacent to a low-HP ally, bump to Offensive.
                bool threatEngagingAlly = allies.Any(a =>
                    a.IsConscious && GetHpRatio(a) < 0.6f &&
                    grid.GetDistance(biggestThreat.GridPosition, a.GridPosition) <= MeleeRange);

                if (threatEngagingAlly)
                    plan.Priority = CombatPriority.Offensive;
            }
        }

        // If the planned action is an Attack, check if we should retarget to the biggest threat.
        if (plan.PlannedAction?.Type == ActionType.Attack && plan.PlannedAction.Target != null)
        {
            var biggestThreat = FindBiggestThreatToAllies(allies, enemies, grid);
            if (biggestThreat != null && biggestThreat.Id != plan.PlannedAction.Target.Id)
            {
                // Only retarget if the threat is reachable.
                float dist = grid.GetDistance(self.GridPosition, biggestThreat.GridPosition);
                if (dist <= MeleeRange || dist <= 120f)
                {
                    plan.PlannedAction.Target = biggestThreat;
                    plan.Reasoning += $"\nFighter: Retargeting to {biggestThreat.Character.Name} (biggest threat to allies).";
                    plan.ConfidenceScore = Math.Min(plan.ConfidenceScore + 0.1f, 1f);
                }
            }
        }

        // Boost Action Surge usage when enemies are clustered around the fighter.
        if (plan.PlannedBonusAction?.Type == ActionType.ActionSurge)
        {
            int adjacentEnemies = CountAdjacentEnemies(self, enemies, grid);
            if (adjacentEnemies >= ActionSurgeClusterThreshold)
            {
                plan.ConfidenceScore = Math.Min(plan.ConfidenceScore + 0.15f, 1f);
                plan.Reasoning += $"\nFighter: Action Surge with {adjacentEnemies} enemies in melee.";
            }
        }

        // Prefer moving to protect squishy allies (position between ally and enemy).
        AdjustPositionToProtectAllies(plan, self, allies, enemies, grid);
    }

    public override ClassActionSuggestion GetPreferredAction(
        Combatant self, List<Combatant> allies, List<Combatant> enemies,
        CombatGrid grid, SpellCastingManager spellCastingManager = null)
    {
        if (enemies.Count == 0)
            return null;

        var biggestThreat = FindBiggestThreatToAllies(allies, enemies, grid);
        if (biggestThreat == null)
            return null;

        float dist = grid.GetDistance(self.GridPosition, biggestThreat.GridPosition);
        bool canMelee = dist <= MeleeRange;

        // Primary goal: melee attack the biggest threat.
        if (canMelee && grid.HasLineOfSight(self.GridPosition, biggestThreat.GridPosition))
        {
            var suggestion = new ClassActionSuggestion
            {
                Action = new CombatAction
                {
                    Type = ActionType.Attack,
                    Actor = self,
                    Target = biggestThreat
                },
                ScoreModifier = ThreatTargetBonus,
                Reasoning = $"Fighter: Engaging {biggestThreat.Character.Name} in melee (biggest threat to party)."
            };

            // Suggest Action Surge if multiple enemies clustered and we haven't used it.
            int adjacentEnemies = CountAdjacentEnemies(self, enemies, grid);
            if (adjacentEnemies >= ActionSurgeClusterThreshold &&
                self.Character.Features.Any(f => f.Equals("Action Surge", StringComparison.OrdinalIgnoreCase)) &&
                self.Character.FeatureUsesRemaining.TryGetValue("Action Surge", out int uses) && uses > 0)
            {
                suggestion.Reasoning += $" Action Surge available with {adjacentEnemies} clustered enemies.";
                suggestion.ScoreModifier += 5f;
            }

            return suggestion;
        }

        // If not in melee, move to intercept.
        var interceptPos = FindAdjacentPosition(self, biggestThreat, grid);
        if (interceptPos.HasValue)
        {
            return new ClassActionSuggestion
            {
                Action = new CombatAction
                {
                    Type = ActionType.Attack,
                    Actor = self,
                    Target = biggestThreat
                },
                ScoreModifier = ThreatTargetBonus,
                PreferredPosition = interceptPos.Value,
                Reasoning = $"Fighter: Moving to engage {biggestThreat.Character.Name} in melee."
            };
        }

        return null;
    }

    /// <summary>
    /// Adjusts the plan's movement to interpose between squishy allies and enemies.
    /// Fighters should stand between the party's backline and the enemy frontline.
    /// </summary>
    private static void AdjustPositionToProtectAllies(
        CombatAIPlan plan, Combatant self,
        List<Combatant> allies, List<Combatant> enemies,
        CombatGrid grid)
    {
        // Find the squishiest ally (lowest max HP, likely casters).
        var squishyAlly = allies
            .Where(a => a.IsConscious)
            .OrderBy(a => a.Character.MaxHitPoints)
            .FirstOrDefault();

        if (squishyAlly == null) return;

        // Find the nearest enemy to that squishy ally.
        var nearestEnemy = enemies
            .Where(e => e.IsConscious)
            .OrderBy(e => grid.GetDistance(e.GridPosition, squishyAlly.GridPosition))
            .FirstOrDefault();

        if (nearestEnemy == null) return;

        float enemyToAllyDist = grid.GetDistance(nearestEnemy.GridPosition, squishyAlly.GridPosition);

        // Only intervene if the enemy is within threatening distance of the squishy ally.
        if (enemyToAllyDist > 20f) return;

        // Try to find a position adjacent to the enemy that is between the enemy and the ally.
        var adjacent = grid.GetAdjacentPositions(nearestEnemy.GridPosition);
        Vector2I? bestProtectPos = null;
        float bestScore = float.MinValue;

        foreach (var pos in adjacent)
        {
            if (!grid.CanMoveTo(pos)) continue;

            float distFromSelf = grid.GetDistance(self.GridPosition, pos);
            if (distFromSelf > self.RemainingMovement) continue;

            // Score: prefer positions that are between the enemy and the ally.
            float distToAlly = grid.GetDistance(pos, squishyAlly.GridPosition);
            float score = ProtectAllyBonus - distToAlly * 0.2f;

            if (score > bestScore)
            {
                bestScore = score;
                bestProtectPos = pos;
            }
        }

        if (bestProtectPos.HasValue && plan.MoveTo != bestProtectPos)
        {
            // Only override movement if the protect position is significantly better.
            if (bestScore > 3f)
            {
                plan.MoveTo = bestProtectPos;
                plan.MovePath = grid.FindPath(self.GridPosition, bestProtectPos.Value);
                plan.Reasoning += $"\nFighter: Moving to protect {squishyAlly.Character.Name}.";
            }
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// CLERIC BEHAVIOR
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Cleric tactical behavior: party HP monitoring, prioritized healing, maintaining
/// concentration buffs, mid-range positioning, and Turn Undead when effective.
/// </summary>
public class ClericBehavior : ClassCombatBehavior
{
    // Thresholds for triggering healing actions.
    private const float HealingUrgentThreshold = 0.25f;
    private const float HealingNormalThreshold = 0.40f;

    // Number of undead required for Turn Undead to be worthwhile.
    private const int TurnUndeadThreshold = 3;

    // Ideal distance from enemies (between melee and full ranged).
    private const float IdealMinDistance = 15f;
    private const float IdealMaxDistance = 30f;

    // Score multipliers for healing urgency.
    private const float UrgentHealBoost = 20f;
    private const float NormalHealBoost = 10f;

    // Known buff spells that Clerics should try to maintain concentration on.
    private static readonly HashSet<string> BuffSpells = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bless", "Shield of Faith", "Spirit Guardians", "Spiritual Weapon",
        "Guardian of Faith", "Beacon of Hope", "Protection from Energy"
    };

    // Known healing spells for prioritization.
    private static readonly HashSet<string> HealingSpells = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cure Wounds", "Healing Word", "Prayer of Healing", "Mass Cure Wounds",
        "Heal", "Revivify", "Mass Healing Word"
    };

    public override void ModifyPriorities(
        CombatAIPlan plan, Combatant self,
        List<Combatant> allies, List<Combatant> enemies,
        CombatGrid grid)
    {
        if (plan == null) return;

        // Clerics are more sensitive to ally HP than the base AI.
        bool anyAllyUrgent = allies.Any(a => a.IsConscious && GetHpRatio(a) <= HealingUrgentThreshold);
        bool anyAllyLow = allies.Any(a => a.IsConscious && GetHpRatio(a) <= HealingNormalThreshold);
        bool selfLow = GetHpRatio(self) <= HealingNormalThreshold;

        // Override to Support/Emergency if an ally is hurting.
        if (anyAllyUrgent && plan.Priority != CombatPriority.Emergency)
        {
            plan.Priority = CombatPriority.Emergency;
            plan.Reasoning += "\nCleric: Ally critically wounded -- prioritizing healing.";
        }
        else if ((anyAllyLow || selfLow) && plan.Priority == CombatPriority.Default)
        {
            plan.Priority = CombatPriority.Support;
            plan.Reasoning += "\nCleric: Ally below 40% HP -- shifting to support.";
        }

        // If we're currently concentrating on a buff spell, avoid actions that would
        // put us in danger of losing concentration (e.g., moving into melee).
        if (IsConcentratingOnBuff(self))
        {
            if (plan.MoveTo.HasValue)
            {
                float nearestEnemyDist = enemies
                    .Where(e => e.IsConscious)
                    .Select(e => grid.GetDistance(plan.MoveTo.Value, e.GridPosition))
                    .DefaultIfEmpty(float.MaxValue)
                    .Min();

                if (nearestEnemyDist <= MeleeRange)
                {
                    plan.MoveTo = null;
                    plan.MovePath.Clear();
                    plan.Reasoning += "\nCleric: Avoiding melee to maintain concentration.";
                }
            }
        }

        // Check for Turn Undead opportunity.
        int nearbyUndead = enemies.Count(e =>
            e.IsConscious && IsLikelyUndead(e) &&
            grid.GetDistance(self.GridPosition, e.GridPosition) <= 30f);

        if (nearbyUndead >= TurnUndeadThreshold &&
            self.Character.Features.Any(f =>
                f.Equals("Turn Undead", StringComparison.OrdinalIgnoreCase) ||
                f.Equals("Channel Divinity: Turn Undead", StringComparison.OrdinalIgnoreCase)))
        {
            plan.Priority = CombatPriority.Control;
            plan.Reasoning += $"\nCleric: {nearbyUndead} undead nearby -- Turn Undead is effective.";
        }
    }

    public override ClassActionSuggestion GetPreferredAction(
        Combatant self, List<Combatant> allies, List<Combatant> enemies,
        CombatGrid grid, SpellCastingManager spellCastingManager = null)
    {
        // Priority 1: Heal an unconscious ally (emergency).
        var dyingAlly = allies.FirstOrDefault(a => !a.IsConscious && !a.IsDead() && !a.IsStabilized());
        if (dyingAlly != null)
        {
            var healAction = FindBestHealingSpell(self, dyingAlly, spellCastingManager);
            if (healAction != null)
            {
                return new ClassActionSuggestion
                {
                    Action = healAction,
                    ScoreModifier = UrgentHealBoost,
                    Reasoning = $"Cleric: Emergency heal on {dyingAlly.Character.Name} (dying)."
                };
            }
        }

        // Priority 2: Heal an ally below urgent threshold.
        var urgentAlly = allies
            .Where(a => a.IsConscious && GetHpRatio(a) <= HealingUrgentThreshold)
            .OrderBy(a => GetHpRatio(a))
            .FirstOrDefault();

        if (urgentAlly != null)
        {
            var healAction = FindBestHealingSpell(self, urgentAlly, spellCastingManager);
            if (healAction != null)
            {
                return new ClassActionSuggestion
                {
                    Action = healAction,
                    ScoreModifier = UrgentHealBoost,
                    Reasoning = $"Cleric: Urgent heal on {urgentAlly.Character.Name} ({GetHpRatio(urgentAlly) * 100:F0}% HP)."
                };
            }
        }

        // Priority 3: Turn Undead if many undead nearby.
        int nearbyUndead = enemies.Count(e =>
            e.IsConscious && IsLikelyUndead(e) &&
            grid.GetDistance(self.GridPosition, e.GridPosition) <= 30f);

        if (nearbyUndead >= TurnUndeadThreshold &&
            (HasFeatureAvailable(self, "Turn Undead") || HasFeatureAvailable(self, "Channel Divinity: Turn Undead")))
        {
            return new ClassActionSuggestion
            {
                Action = new CombatAction
                {
                    Type = ActionType.CastSpell,
                    Actor = self,
                    SpellName = "Turn Undead"
                },
                ScoreModifier = 15f,
                Reasoning = $"Cleric: Turn Undead against {nearbyUndead} undead."
            };
        }

        // Priority 4: Heal an ally below normal threshold.
        var lowAlly = allies
            .Where(a => a.IsConscious && GetHpRatio(a) <= HealingNormalThreshold)
            .OrderBy(a => GetHpRatio(a))
            .FirstOrDefault();

        if (lowAlly != null)
        {
            var healAction = FindBestHealingSpell(self, lowAlly, spellCastingManager);
            if (healAction != null)
            {
                return new ClassActionSuggestion
                {
                    Action = healAction,
                    ScoreModifier = NormalHealBoost,
                    Reasoning = $"Cleric: Healing {lowAlly.Character.Name} ({GetHpRatio(lowAlly) * 100:F0}% HP)."
                };
            }
        }

        // Priority 5: Cast a concentration buff if not already concentrating.
        if (!IsConcentratingOnBuff(self) && spellCastingManager != null)
        {
            var buffAction = FindBestBuffSpell(self, spellCastingManager);
            if (buffAction != null)
            {
                return new ClassActionSuggestion
                {
                    Action = buffAction,
                    ScoreModifier = 8f,
                    Reasoning = $"Cleric: Casting {buffAction.SpellName} to buff the party."
                };
            }
        }

        // Priority 6: Position at mid-range.
        var preferredPos = FindMidRangePosition(self, allies, enemies, grid);
        if (preferredPos.HasValue)
        {
            return new ClassActionSuggestion
            {
                PreferredPosition = preferredPos,
                ScoreModifier = 3f,
                Reasoning = "Cleric: Repositioning to mid-range for support."
            };
        }

        return null;
    }

    // ── Cleric Helpers ───────────────────────────────────────────────────

    private static CombatAction FindBestHealingSpell(
        Combatant self, Combatant target, SpellCastingManager spellCastingManager)
    {
        if (spellCastingManager == null) return null;

        CombatAction bestAction = null;
        float bestScore = float.MinValue;

        foreach (string spellName in self.Character.SpellsKnown)
        {
            if (!HealingSpells.Contains(spellName)) continue;

            // Find the lowest level at which we can cast this.
            for (int level = 0; level <= 9; level++)
            {
                if (!spellCastingManager.CanCastSpell(self.Character, spellName, level))
                    continue;

                var spellData = spellCastingManager.SpellDatabase.GetSpell(spellName);
                if (spellData == null) break;

                // Estimate healing value (higher is better, but don't waste high slots on minor healing).
                float missingHp = target.Character.MaxHitPoints - target.Character.HitPoints;
                float estimatedHealing = EstimateHealingAmount(spellData, level);
                float efficiency = Math.Min(estimatedHealing, missingHp);

                // Penalize using high-level slots for small amounts of healing.
                float slotPenalty = level * 1.5f;
                float score = efficiency - slotPenalty;

                // Big bonus for healing an unconscious target (any amount of healing works).
                if (!target.IsConscious)
                    score += 50f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestAction = new CombatAction
                    {
                        Type = ActionType.CastSpell,
                        Actor = self,
                        Target = target,
                        SpellName = spellName,
                        SpellLevel = level
                    };
                }

                break; // Only check the lowest valid level per spell.
            }
        }

        return bestAction;
    }

    private static CombatAction FindBestBuffSpell(
        Combatant self, SpellCastingManager spellCastingManager)
    {
        foreach (string spellName in self.Character.SpellsKnown)
        {
            if (!BuffSpells.Contains(spellName)) continue;

            for (int level = 0; level <= 9; level++)
            {
                if (!spellCastingManager.CanCastSpell(self.Character, spellName, level))
                    continue;

                return new CombatAction
                {
                    Type = ActionType.CastSpell,
                    Actor = self,
                    SpellName = spellName,
                    SpellLevel = level
                };
            }
        }

        return null;
    }

    private static bool IsConcentratingOnBuff(Combatant self)
    {
        // Check if the character's active concentration is a known buff spell.
        // Since ConcentrationTracker is on SpellCastingManager (not directly on Combatant),
        // we check the character's features/conditions as a proxy.
        // A more complete integration would pass ConcentrationTracker state per-combatant.
        // For now, check if the character has a concentration-related condition or flag.
        return self.Character.Features.Any(f => BuffSpells.Contains(f));
    }

    private static float EstimateHealingAmount(SpellData spell, int castLevel)
    {
        // Rough estimates based on SRD spell averages.
        string name = spell.Name.ToLowerInvariant();
        if (name.Contains("cure wounds"))
            return 4.5f + castLevel * 4.5f; // 1d8 per level + modifier
        if (name.Contains("healing word"))
            return 3.5f + castLevel * 3.5f; // 1d4 per level + modifier
        if (name.Contains("mass cure wounds"))
            return 14f; // 3d8 + modifier
        if (name.Contains("heal"))
            return 70f;

        // Generic: use the spell's damage dice as a proxy for healing.
        if (!string.IsNullOrEmpty(spell.Damage))
        {
            var match = System.Text.RegularExpressions.Regex.Match(spell.Damage, @"(\d+)d(\d+)");
            if (match.Success)
            {
                int count = int.Parse(match.Groups[1].Value);
                int sides = int.Parse(match.Groups[2].Value);
                return count * (sides + 1) / 2f;
            }
        }

        return 5f; // Fallback.
    }

    private static bool HasFeatureAvailable(Combatant self, string featureName)
    {
        return self.Character.Features.Any(f =>
            f.Equals(featureName, StringComparison.OrdinalIgnoreCase)) &&
            self.Character.FeatureUsesRemaining.TryGetValue(featureName, out int uses) &&
            uses > 0;
    }

    private static Vector2I? FindMidRangePosition(
        Combatant self, List<Combatant> allies, List<Combatant> enemies,
        CombatGrid grid)
    {
        // Find the centroid of allies (the Cleric wants to be near allies for healing range).
        if (allies.Count == 0 || enemies.Count == 0)
            return null;

        float avgAllyX = (float)allies.Where(a => a.IsConscious).Average(a => a.GridPosition.X);
        float avgAllyY = (float)allies.Where(a => a.IsConscious).Average(a => a.GridPosition.Y);

        // Check if our current position is already reasonable.
        float nearestEnemyDist = enemies
            .Where(e => e.IsConscious)
            .Select(e => grid.GetDistance(self.GridPosition, e.GridPosition))
            .DefaultIfEmpty(float.MaxValue)
            .Min();

        float nearestAllyDist = allies
            .Where(a => a.IsConscious)
            .Select(a => grid.GetDistance(self.GridPosition, a.GridPosition))
            .DefaultIfEmpty(float.MaxValue)
            .Min();

        // Already in a good spot: near allies, not in melee.
        if (nearestEnemyDist >= IdealMinDistance && nearestEnemyDist <= IdealMaxDistance &&
            nearestAllyDist <= 15f)
        {
            return null;
        }

        // Otherwise, look for a position near the ally centroid that avoids melee.
        var candidates = grid.GetAdjacentPositions(new Vector2I((int)avgAllyX, (int)avgAllyY));
        Vector2I? bestPos = null;
        float bestScore = float.MinValue;

        foreach (var pos in candidates)
        {
            if (!grid.CanMoveTo(pos)) continue;

            float distFromSelf = grid.GetDistance(self.GridPosition, pos);
            if (distFromSelf > self.RemainingMovement) continue;

            float enemyDist = enemies
                .Where(e => e.IsConscious)
                .Select(e => grid.GetDistance(pos, e.GridPosition))
                .DefaultIfEmpty(float.MaxValue)
                .Min();

            // Prefer mid-range, avoid melee.
            float score = 0f;
            if (enemyDist >= IdealMinDistance && enemyDist <= IdealMaxDistance)
                score += 5f;
            else if (enemyDist < IdealMinDistance)
                score -= (IdealMinDistance - enemyDist) * 0.5f;

            float allyDist = grid.GetDistance(pos, new Vector2I((int)avgAllyX, (int)avgAllyY));
            score -= allyDist * 0.2f; // Stay near allies.

            if (score > bestScore)
            {
                bestScore = score;
                bestPos = pos;
            }
        }

        return bestPos;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// ROGUE BEHAVIOR
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Rogue tactical behavior: seeks flanking for Sneak Attack, uses Hide/Cunning Action,
/// focuses high-value targets (spellcasters), and avoids direct confrontation.
/// </summary>
public class RogueBehavior : ClassCombatBehavior
{
    // Bonus score for attacking from a flanking position.
    private const float FlankingBonus = 12f;

    // Bonus score for targeting spellcasters.
    private const float SpellcasterTargetBonus = 6f;

    // Penalty for engaging when outnumbered in melee.
    private const float OutnumberedPenalty = -8f;

    // Hide bonus when Cunning Action is available.
    private const float CunningActionHideBonus = 10f;

    public override void ModifyPriorities(
        CombatAIPlan plan, Combatant self,
        List<Combatant> allies, List<Combatant> enemies,
        CombatGrid grid)
    {
        if (plan == null) return;

        int adjacentEnemies = CountAdjacentEnemies(self, enemies, grid);
        bool isOutnumbered = adjacentEnemies > 1;

        // Rogues should avoid staying in melee when outnumbered.
        if (isOutnumbered && plan.Priority != CombatPriority.Emergency)
        {
            // Prefer Disengage or repositioning.
            if (plan.PlannedAction?.Type == ActionType.Attack)
            {
                plan.Reasoning += "\nRogue: Outnumbered in melee -- considering repositioning.";
                plan.ConfidenceScore = Math.Max(plan.ConfidenceScore - 0.15f, 0f);
            }
        }

        // If the planned attack target is not a spellcaster, check if there's a spellcaster we can reach.
        if (plan.PlannedAction?.Type == ActionType.Attack && plan.PlannedAction.Target != null)
        {
            if (!IsSpellcaster(plan.PlannedAction.Target))
            {
                var spellcasterTarget = enemies
                    .Where(e => e.IsConscious && IsSpellcaster(e) &&
                        grid.HasLineOfSight(self.GridPosition, e.GridPosition))
                    .OrderBy(e => grid.GetDistance(self.GridPosition, e.GridPosition))
                    .FirstOrDefault();

                if (spellcasterTarget != null)
                {
                    float dist = grid.GetDistance(self.GridPosition, spellcasterTarget.GridPosition);
                    if (dist <= MeleeRange || dist <= 120f) // Melee or ranged
                    {
                        plan.PlannedAction.Target = spellcasterTarget;
                        plan.Reasoning += $"\nRogue: Retargeting to spellcaster {spellcasterTarget.Character.Name}.";
                    }
                }
            }
        }

        // Check if we can get Sneak Attack (flanking or advantage).
        if (plan.PlannedAction?.Type == ActionType.Attack && plan.PlannedAction.Target != null)
        {
            bool hasFlank = HasFlankingPosition(self, plan.PlannedAction.Target, allies, grid);
            if (hasFlank)
            {
                plan.ConfidenceScore = Math.Min(plan.ConfidenceScore + 0.2f, 1f);
                plan.Reasoning += "\nRogue: Sneak Attack available (flanking).";
            }
            else if (self.IsHidden)
            {
                plan.ConfidenceScore = Math.Min(plan.ConfidenceScore + 0.2f, 1f);
                plan.Reasoning += "\nRogue: Sneak Attack available (hidden).";
            }
        }
    }

    public override ClassActionSuggestion GetPreferredAction(
        Combatant self, List<Combatant> allies, List<Combatant> enemies,
        CombatGrid grid, SpellCastingManager spellCastingManager = null)
    {
        if (enemies.Count == 0)
            return null;

        int adjacentEnemies = CountAdjacentEnemies(self, enemies, grid);

        // If outnumbered in melee and not hidden, disengage and reposition.
        if (adjacentEnemies > 1 && !self.IsHidden)
        {
            return new ClassActionSuggestion
            {
                Action = new CombatAction
                {
                    Type = ActionType.Disengage,
                    Actor = self
                },
                ScoreModifier = Math.Abs(OutnumberedPenalty),
                Reasoning = $"Rogue: Disengaging -- outnumbered {adjacentEnemies}:1 in melee."
            };
        }

        // Priority: find a flankable spellcaster target.
        var preferredTarget = FindRogueTarget(self, allies, enemies, grid);
        if (preferredTarget == null)
            return null;

        // Check if we can flank the target from current or a nearby position.
        bool canFlank = HasFlankingPosition(self, preferredTarget, allies, grid);
        Vector2I? flankPos = null;

        if (!canFlank)
        {
            flankPos = FindFlankingPosition(self, preferredTarget, allies, grid);
            canFlank = flankPos.HasValue;
        }

        if (canFlank || self.IsHidden)
        {
            float bonus = FlankingBonus;
            string reason = "Rogue: ";

            if (IsSpellcaster(preferredTarget))
            {
                bonus += SpellcasterTargetBonus;
                reason += $"Targeting spellcaster {preferredTarget.Character.Name}";
            }
            else
            {
                reason += $"Attacking {preferredTarget.Character.Name}";
            }

            if (self.IsHidden)
                reason += " from stealth (Sneak Attack).";
            else if (flankPos.HasValue)
                reason += " from flanking position (Sneak Attack).";
            else
                reason += " with flanking (Sneak Attack).";

            return new ClassActionSuggestion
            {
                Action = new CombatAction
                {
                    Type = ActionType.Attack,
                    Actor = self,
                    Target = preferredTarget
                },
                ScoreModifier = bonus,
                PreferredPosition = flankPos,
                Reasoning = reason
            };
        }

        // No Sneak Attack available: consider Hiding as a setup for next turn.
        // Rogues can use Cunning Action to Hide as a bonus action at level 2+.
        bool hasCunningAction = self.Character.Features.Any(f =>
            f.Equals("Cunning Action", StringComparison.OrdinalIgnoreCase));

        if (hasCunningAction && !self.BonusActionUsed)
        {
            return new ClassActionSuggestion
            {
                Action = new CombatAction
                {
                    Type = ActionType.Hide,
                    Actor = self
                },
                ScoreModifier = CunningActionHideBonus,
                Reasoning = "Rogue: Using Cunning Action to Hide (bonus action) for Sneak Attack next turn."
            };
        }

        // Standard Hide if no bonus action version available.
        if (!self.ActionUsed && !self.IsHidden)
        {
            return new ClassActionSuggestion
            {
                Action = new CombatAction
                {
                    Type = ActionType.Hide,
                    Actor = self
                },
                ScoreModifier = 5f,
                Reasoning = "Rogue: Hiding to set up Sneak Attack."
            };
        }

        return null;
    }

    // ── Rogue Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Selects the best Rogue target: prefers spellcasters, then lowest HP, then nearest.
    /// </summary>
    private static Combatant FindRogueTarget(
        Combatant self, List<Combatant> allies, List<Combatant> enemies, CombatGrid grid)
    {
        Combatant bestTarget = null;
        float bestScore = float.MinValue;

        foreach (var enemy in enemies.Where(e => e.IsConscious))
        {
            float dist = grid.GetDistance(self.GridPosition, enemy.GridPosition);
            if (!grid.HasLineOfSight(self.GridPosition, enemy.GridPosition))
                continue;

            float score = 0f;

            // Spellcasters are high-priority targets for Rogues.
            if (IsSpellcaster(enemy))
                score += SpellcasterTargetBonus;

            // Low HP enemies are finishable.
            if (GetHpRatio(enemy) <= 0.30f)
                score += 4f;

            // Prefer targets we can flank.
            if (HasFlankingPosition(self, enemy, allies, grid))
                score += FlankingBonus * 0.5f;

            // Closer targets are more accessible.
            score -= dist * 0.1f;

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = enemy;
            }
        }

        return bestTarget;
    }

    /// <summary>
    /// Returns true if the Rogue (or the Rogue at its current position) and an ally
    /// are on opposite sides of an enemy, enabling Sneak Attack via flanking.
    /// </summary>
    private static bool HasFlankingPosition(
        Combatant self, Combatant target, List<Combatant> allies, CombatGrid grid)
    {
        if (grid.GetDistance(self.GridPosition, target.GridPosition) > MeleeRange)
            return false;

        // Check if any conscious ally is on the opposite side (also adjacent).
        foreach (var ally in allies.Where(a => a.IsConscious))
        {
            if (grid.GetDistance(ally.GridPosition, target.GridPosition) > MeleeRange)
                continue;

            // Simple flanking check: self and ally are on opposite sides of the target.
            // This means the target is roughly between the two.
            var dirSelf = self.GridPosition - target.GridPosition;
            var dirAlly = ally.GridPosition - target.GridPosition;

            // Opposite directions: dot product of direction vectors is negative.
            int dot = dirSelf.X * dirAlly.X + dirSelf.Y * dirAlly.Y;
            if (dot < 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Finds a reachable position that would give the Rogue flanking on the target.
    /// </summary>
    private static Vector2I? FindFlankingPosition(
        Combatant self, Combatant target, List<Combatant> allies, CombatGrid grid)
    {
        // For each ally adjacent to the target, find the opposite side.
        foreach (var ally in allies.Where(a =>
            a.IsConscious && grid.GetDistance(a.GridPosition, target.GridPosition) <= MeleeRange))
        {
            // The flanking position is on the opposite side of the target from the ally.
            var dir = target.GridPosition - ally.GridPosition;
            var flankPos = target.GridPosition + dir;

            if (!grid.CanMoveTo(flankPos))
                continue;

            float dist = grid.GetDistance(self.GridPosition, flankPos);
            if (dist <= self.RemainingMovement)
                return flankPos;
        }

        return null;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// WIZARD BEHAVIOR
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Wizard tactical behavior: maximum range positioning, AoE prioritization,
/// spell slot conservation, cantrip usage for chip damage, melee avoidance,
/// and concentration on control spells.
/// </summary>
public class WizardBehavior : ClassCombatBehavior
{
    // Number of grouped enemies to trigger AoE preference.
    private const int AoEClusterThreshold = 3;

    // AoE detection radius: most AoE spells cover 20ft.
    private const float AoEClusterRadius = 20f;

    // Ideal distance from nearest enemy.
    private const float IdealMinDistance = 30f;
    private const float IdealMaxDistance = 60f;

    // Score modifiers.
    private const float AoEGroupBonus = 15f;
    private const float CantripChipBonus = 3f;
    private const float HighSlotConservationPenalty = 5f;
    private const float MeleeEscapePriority = 12f;

    // Known control spells for concentration priority.
    private static readonly HashSet<string> ControlSpells = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hold Person", "Web", "Hypnotic Pattern", "Slow", "Banishment",
        "Wall of Force", "Hold Monster", "Confusion", "Entangle",
        "Grease", "Fog Cloud", "Stinking Cloud"
    };

    // Known AoE damage spells.
    private static readonly HashSet<string> AoESpells = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fireball", "Lightning Bolt", "Cone of Cold", "Burning Hands", "Thunderwave",
        "Shatter", "Ice Storm", "Cloud Kill", "Meteor Swarm", "Chain Lightning",
        "Synaptic Static"
    };

    public override void ModifyPriorities(
        CombatAIPlan plan, Combatant self,
        List<Combatant> allies, List<Combatant> enemies,
        CombatGrid grid)
    {
        if (plan == null) return;

        // Wizards shift to Control when enemies are clustered (AoE opportunity).
        if (plan.Priority == CombatPriority.Default)
        {
            var cluster = FindEnemyCluster(enemies, grid);
            if (cluster.count >= AoEClusterThreshold)
            {
                plan.Priority = CombatPriority.Control;
                plan.Reasoning += $"\nWizard: {cluster.count} enemies clustered -- AoE opportunity.";
            }
        }

        // If currently in melee, urgently escape.
        int adjacentEnemies = CountAdjacentEnemies(self, enemies, grid);
        if (adjacentEnemies > 0)
        {
            // Override movement to get out of melee.
            var escapePos = FindEscapePosition(self, enemies, grid);
            if (escapePos.HasValue)
            {
                plan.MoveTo = escapePos;
                plan.MovePath = grid.FindPath(self.GridPosition, escapePos.Value);
                plan.Reasoning += "\nWizard: Escaping melee range.";
            }

            // If we can Disengage, prefer it over eating opportunity attacks.
            if (!self.ActionUsed && plan.PlannedAction?.Type != ActionType.Disengage)
            {
                // Only Disengage if we don't have a very high-value spell to cast instead.
                if (plan.PlannedAction == null || plan.ConfidenceScore < 0.6f)
                {
                    plan.PlannedAction = new CombatAction
                    {
                        Type = ActionType.Disengage,
                        Actor = self
                    };
                    plan.Reasoning += "\nWizard: Disengaging to escape melee safely.";
                }
            }
        }

        // Discourage using high-level spell slots unless the situation warrants it.
        if (plan.PlannedAction?.Type == ActionType.CastSpell && plan.PlannedAction.SpellLevel >= 3)
        {
            bool isHighPriority = plan.Priority == CombatPriority.Emergency ||
                                  plan.Priority == CombatPriority.Control;
            if (!isHighPriority)
            {
                plan.ConfidenceScore = Math.Max(plan.ConfidenceScore - 0.1f, 0f);
                plan.Reasoning += $"\nWizard: Conserving level {plan.PlannedAction.SpellLevel} slot -- not an urgent situation.";
            }
        }
    }

    public override ClassActionSuggestion GetPreferredAction(
        Combatant self, List<Combatant> allies, List<Combatant> enemies,
        CombatGrid grid, SpellCastingManager spellCastingManager = null)
    {
        if (enemies.Count == 0)
            return null;

        int adjacentEnemies = CountAdjacentEnemies(self, enemies, grid);

        // Priority 0: If in melee, escape first.
        if (adjacentEnemies > 0)
        {
            return new ClassActionSuggestion
            {
                Action = new CombatAction
                {
                    Type = ActionType.Disengage,
                    Actor = self
                },
                ScoreModifier = MeleeEscapePriority,
                PreferredPosition = FindEscapePosition(self, enemies, grid),
                Reasoning = $"Wizard: Emergency disengage -- {adjacentEnemies} enemies in melee."
            };
        }

        // Priority 1: AoE when 3+ enemies are clustered.
        if (spellCastingManager != null)
        {
            var cluster = FindEnemyCluster(enemies, grid);
            if (cluster.count >= AoEClusterThreshold)
            {
                var aoeAction = FindBestAoESpell(self, cluster.center, enemies, spellCastingManager, grid);
                if (aoeAction != null)
                {
                    return new ClassActionSuggestion
                    {
                        Action = aoeAction,
                        ScoreModifier = AoEGroupBonus + (cluster.count - AoEClusterThreshold) * 3f,
                        Reasoning = $"Wizard: AoE {aoeAction.SpellName} targeting {cluster.count} enemies."
                    };
                }
            }
        }

        // Priority 2: Control spell if not already concentrating on one.
        if (spellCastingManager != null && !IsConcentratingOnControl(self))
        {
            var controlAction = FindBestControlSpell(self, enemies, spellCastingManager, grid);
            if (controlAction != null)
            {
                return new ClassActionSuggestion
                {
                    Action = controlAction,
                    ScoreModifier = 10f,
                    Reasoning = $"Wizard: Casting {controlAction.SpellName} for battlefield control."
                };
            }
        }

        // Priority 3: Cantrip for chip damage (conserve spell slots).
        if (spellCastingManager != null)
        {
            var cantripAction = FindBestCantrip(self, enemies, spellCastingManager, grid);
            if (cantripAction != null)
            {
                return new ClassActionSuggestion
                {
                    Action = cantripAction,
                    ScoreModifier = CantripChipBonus,
                    Reasoning = $"Wizard: {cantripAction.SpellName} for efficient chip damage."
                };
            }
        }

        // Priority 4: Reposition to max range.
        var safePos = FindMaxRangePosition(self, enemies, grid);
        if (safePos.HasValue)
        {
            return new ClassActionSuggestion
            {
                PreferredPosition = safePos,
                ScoreModifier = 2f,
                Reasoning = "Wizard: Repositioning to maximum range."
            };
        }

        return null;
    }

    // ── Wizard Helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Finds the densest cluster of enemies and returns its center and count.
    /// </summary>
    private static (Vector2I center, int count) FindEnemyCluster(
        List<Combatant> enemies, CombatGrid grid)
    {
        Vector2I bestCenter = Vector2I.Zero;
        int bestCount = 0;

        // Use each enemy position as a potential cluster center.
        foreach (var enemy in enemies.Where(e => e.IsConscious))
        {
            int count = CountEnemiesInRadius(enemy.GridPosition, enemies, grid, AoEClusterRadius);
            if (count > bestCount)
            {
                bestCount = count;
                bestCenter = enemy.GridPosition;
            }
        }

        return (bestCenter, bestCount);
    }

    private static CombatAction FindBestAoESpell(
        Combatant self, Vector2I clusterCenter, List<Combatant> enemies,
        SpellCastingManager spellCastingManager, CombatGrid grid)
    {
        CombatAction bestAction = null;
        float bestScore = float.MinValue;

        foreach (string spellName in self.Character.SpellsKnown)
        {
            if (!AoESpells.Contains(spellName)) continue;

            for (int level = 0; level <= 9; level++)
            {
                if (!spellCastingManager.CanCastSpell(self.Character, spellName, level))
                    continue;

                var spellData = spellCastingManager.SpellDatabase.GetSpell(spellName);
                if (spellData == null) break;

                // Check range from self to cluster center.
                float dist = grid.GetDistance(self.GridPosition, clusterCenter);
                if (!IsInRange(spellData, dist))
                    break;

                // Estimate total damage across all targets in the cluster.
                float avgDmg = EstimateAverageDamage(spellData, level, self.Character.Level);
                int targetsHit = CountEnemiesInRadius(clusterCenter, enemies, grid, AoEClusterRadius);
                float totalDmg = avgDmg * targetsHit;

                // Penalize higher spell slots.
                float slotCost = level * HighSlotConservationPenalty;
                float score = totalDmg - slotCost;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestAction = new CombatAction
                    {
                        Type = ActionType.CastSpell,
                        Actor = self,
                        SpellName = spellName,
                        SpellLevel = level,
                        TargetPosition = clusterCenter
                    };
                }

                break; // Only check lowest valid level per spell.
            }
        }

        return bestAction;
    }

    private static CombatAction FindBestControlSpell(
        Combatant self, List<Combatant> enemies,
        SpellCastingManager spellCastingManager, CombatGrid grid)
    {
        foreach (string spellName in self.Character.SpellsKnown)
        {
            if (!ControlSpells.Contains(spellName)) continue;

            for (int level = 0; level <= 9; level++)
            {
                if (!spellCastingManager.CanCastSpell(self.Character, spellName, level))
                    continue;

                var spellData = spellCastingManager.SpellDatabase.GetSpell(spellName);
                if (spellData == null) break;

                // Check if any enemy is in range.
                var target = enemies
                    .Where(e => e.IsConscious)
                    .Where(e => IsInRange(spellData, grid.GetDistance(self.GridPosition, e.GridPosition)))
                    .Where(e => grid.HasLineOfSight(self.GridPosition, e.GridPosition))
                    .OrderByDescending(e => e.Character.Level) // Target the strongest enemy.
                    .FirstOrDefault();

                if (target == null) break;

                return new CombatAction
                {
                    Type = ActionType.CastSpell,
                    Actor = self,
                    Target = target,
                    SpellName = spellName,
                    SpellLevel = level
                };
            }
        }

        return null;
    }

    private static CombatAction FindBestCantrip(
        Combatant self, List<Combatant> enemies,
        SpellCastingManager spellCastingManager, CombatGrid grid)
    {
        CombatAction bestAction = null;
        float bestDamage = float.MinValue;

        foreach (string spellName in self.Character.SpellsKnown)
        {
            if (!spellCastingManager.CanCastSpell(self.Character, spellName, 0))
                continue;

            var spellData = spellCastingManager.SpellDatabase.GetSpell(spellName);
            if (spellData == null || spellData.Level != 0)
                continue;

            if (string.IsNullOrEmpty(spellData.Damage))
                continue;

            float avgDmg = EstimateAverageDamage(spellData, 0, self.Character.Level);

            // Find the best target for this cantrip.
            var target = enemies
                .Where(e => e.IsConscious)
                .Where(e => IsInRange(spellData, grid.GetDistance(self.GridPosition, e.GridPosition)))
                .Where(e => grid.HasLineOfSight(self.GridPosition, e.GridPosition))
                .OrderBy(e => e.Character.HitPoints) // Prefer finishing off low HP targets.
                .FirstOrDefault();

            if (target == null) continue;

            if (avgDmg > bestDamage)
            {
                bestDamage = avgDmg;
                bestAction = new CombatAction
                {
                    Type = ActionType.CastSpell,
                    Actor = self,
                    Target = target,
                    SpellName = spellName,
                    SpellLevel = 0
                };
            }
        }

        return bestAction;
    }

    private static Vector2I? FindEscapePosition(
        Combatant self, List<Combatant> enemies, CombatGrid grid)
    {
        // Find the position within movement range that maximizes distance from all enemies.
        var adjacent = grid.GetAdjacentPositions(self.GridPosition);
        Vector2I? bestPos = null;
        float bestMinDist = float.MinValue;

        // Also check positions further away (up to full movement).
        var candidates = new List<Vector2I>(adjacent);

        // Add positions at 2-cell radius for better escape options.
        foreach (var pos in adjacent)
        {
            candidates.AddRange(grid.GetAdjacentPositions(pos));
        }

        foreach (var pos in candidates.Distinct())
        {
            if (!grid.CanMoveTo(pos)) continue;

            float distFromSelf = grid.GetDistance(self.GridPosition, pos);
            if (distFromSelf > self.RemainingMovement) continue;

            float minEnemyDist = enemies
                .Where(e => e.IsConscious)
                .Select(e => grid.GetDistance(pos, e.GridPosition))
                .DefaultIfEmpty(0f)
                .Min();

            if (minEnemyDist > bestMinDist)
            {
                bestMinDist = minEnemyDist;
                bestPos = pos;
            }
        }

        return bestPos;
    }

    private static Vector2I? FindMaxRangePosition(
        Combatant self, List<Combatant> enemies, CombatGrid grid)
    {
        float nearestEnemyDist = enemies
            .Where(e => e.IsConscious)
            .Select(e => grid.GetDistance(self.GridPosition, e.GridPosition))
            .DefaultIfEmpty(float.MaxValue)
            .Min();

        // Already at a good distance.
        if (nearestEnemyDist >= IdealMinDistance && nearestEnemyDist <= IdealMaxDistance)
            return null;

        // Otherwise, similar logic to escape but less urgent -- just find a better spot.
        return FindEscapePosition(self, enemies, grid);
    }

    private static bool IsConcentratingOnControl(Combatant self)
    {
        // Same proxy approach as ClericBehavior -- check features as a heuristic.
        return self.Character.Features.Any(f => ControlSpells.Contains(f));
    }

    private static bool IsInRange(SpellData spell, float distanceFeet)
    {
        if (string.IsNullOrEmpty(spell.Range)) return false;

        string range = spell.Range.ToLowerInvariant();
        if (range.Contains("self")) return true;
        if (range.Contains("touch")) return distanceFeet <= 5f;

        var match = System.Text.RegularExpressions.Regex.Match(range, @"(\d+)\s*(?:feet|ft\.?)");
        if (match.Success)
        {
            int rangeFeet = int.Parse(match.Groups[1].Value);
            return distanceFeet <= rangeFeet;
        }

        return distanceFeet <= 60f;
    }

    private static float EstimateAverageDamage(SpellData spell, int castLevel, int casterLevel)
    {
        if (string.IsNullOrEmpty(spell.Damage)) return 0f;

        var match = System.Text.RegularExpressions.Regex.Match(spell.Damage, @"(\d+)d(\d+)(?:\s*\+\s*(\d+))?");
        if (!match.Success) return 0f;

        int count = int.Parse(match.Groups[1].Value);
        int sides = int.Parse(match.Groups[2].Value);
        int bonus = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        float avg = count * (sides + 1) / 2f + bonus;

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
        // Upcast scaling.
        else if (castLevel > spell.Level)
        {
            int levelsAbove = castLevel - spell.Level;
            avg += levelsAbove * (sides + 1) / 2f;
        }

        return avg;
    }
}
