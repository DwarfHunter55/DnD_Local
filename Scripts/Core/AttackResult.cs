namespace ChroniclesUnbound.Core;

/// <summary>
/// Full result of an attack roll including hit determination and damage.
/// </summary>
public sealed class AttackResult
{
    /// <summary>Natural d20 roll before any bonuses.</summary>
    public int AttackRoll { get; }

    /// <summary>Final attack total (natural roll + attack bonus).</summary>
    public int TotalAttack { get; }

    /// <summary>Whether the attack meets or exceeds the target AC.</summary>
    public bool Hits { get; }

    /// <summary>Natural 20 — always hits, doubles damage dice.</summary>
    public bool IsCritical { get; }

    /// <summary>Natural 1 — always misses regardless of bonuses.</summary>
    public bool IsFumble { get; }

    /// <summary>Total damage dealt (0 if the attack missed).</summary>
    public int Damage { get; }

    /// <summary>Damage type string (e.g. "Slashing", "Fire").</summary>
    public string DamageType { get; }

    /// <summary>Detailed damage roll breakdown, if a damage roll was made.</summary>
    public DiceResult? DamageRoll { get; }

    public AttackResult(
        int attackRoll,
        int totalAttack,
        bool hits,
        bool isCritical,
        bool isFumble,
        int damage,
        string damageType,
        DiceResult? damageRoll = null)
    {
        AttackRoll = attackRoll;
        TotalAttack = totalAttack;
        Hits = hits;
        IsCritical = isCritical;
        IsFumble = isFumble;
        Damage = damage;
        DamageType = damageType;
        DamageRoll = damageRoll;
    }

    public override string ToString()
    {
        string hitText = IsCritical ? "CRITICAL HIT"
            : IsFumble ? "FUMBLE"
            : Hits ? "Hit" : "Miss";

        string result = $"Attack: d20({AttackRoll}) total {TotalAttack} — {hitText}";

        if (Hits)
            result += $" | Damage: {Damage} {DamageType}";

        if (DamageRoll != null)
            result += $" ({DamageRoll})";

        return result;
    }
}
