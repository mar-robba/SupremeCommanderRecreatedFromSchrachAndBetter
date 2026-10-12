namespace Rts.Sim.World;

/// <summary>
/// In-place structure upgrade in progress (SupCom T1→T2→T3 factories/mexes/radars), owned by
/// <see cref="Systems.UpgradeSystem"/>. Default = not upgrading. The structure keeps working as its current blueprint
/// (factories pause production) until <see cref="Fraction"/> reaches 1; then it becomes <see cref="TargetBp"/>.
/// </summary>
public struct UpgradeState
{
    public bool Active;
    /// <summary>Blueprint the structure upgrades into (its blueprint's UpgradesToIndex when started).</summary>
    public int TargetBp;
    /// <summary>0..1 progress, in fractions of the target blueprint's BuildTime (costs scale the same way).</summary>
    public float Fraction;
    /// <summary>Upgrade work already requested this tick by all builders (the structure and assisting engineers),
    /// so they never overshoot. Separate from BuilderState.ReservedWork (repair work on the same structure).</summary>
    public float ReservedWork;
    public int ReservedTick;
}
