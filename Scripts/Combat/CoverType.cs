namespace ChroniclesUnbound.Combat;

/// <summary>
/// Cover levels as defined in D&D 5e SRD. Cover provides bonuses to AC
/// and Dexterity saving throws.
/// </summary>
public enum CoverType
{
    None,           // No cover bonus
    Half,           // +2 AC, +2 DEX saves
    ThreeQuarters,  // +5 AC, +5 DEX saves
    Full            // Can't be targeted directly
}
