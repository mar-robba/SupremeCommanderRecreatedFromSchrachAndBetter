namespace Rts.Sim.Data;

/// <summary>
/// SupCom ACU Overcharge mode of a weapon (<see cref="WeaponBlueprint.Overcharge"/>; JSON <c>"Overcharge": {}</c> =
/// these defaults). Toggled per unit (<see cref="Orders.SetOverchargeCommand"/>, flag <see cref="World.UnitFlags.Overcharge"/>);
/// executed by <see cref="Combat.OverchargeSystem"/>: while armed and engaging with at least <see cref="MinEnergy"/>
/// stored, the next shot spends min(stored, <see cref="MaxEnergy"/>) energy and deals that × <see cref="DamagePerEnergy"/>
/// in a <see cref="DamageRadius"/> splash; then <see cref="Reload"/> seconds before the next one (normal shots go on).
/// </summary>
public sealed class OverchargeBlueprint
{
    /// <summary>Army stored energy needed to arm a shot.</summary>
    public float MinEnergy = 1000;
    /// <summary>Most energy one shot spends (taken from the army's storage).</summary>
    public float MaxEnergy = 5000;
    /// <summary>Damage per energy spent.</summary>
    public float DamagePerEnergy = 1;
    /// <summary>Splash radius of the shot (replaces the weapon's DamageRadius for OC shots).</summary>
    public float DamageRadius = 2.5f;
    /// <summary>Seconds between two Overcharge shots.</summary>
    public float Reload = 5f;
    /// <summary>Damage multiplier against structures.</summary>
    public float StructureDamageMult = 0.5f;
    /// <summary>Most damage one shot deals to a commander.</summary>
    public float CommanderMaxDamage = 400f;
}
