namespace Rts.Sim.Data;

/// <summary>
/// SupCom commander death explosion (<see cref="UnitBlueprint.DeathBlast"/>; JSON <c>"DeathBlast": {}</c> = these
/// defaults). <see cref="Delay"/> seconds after a complete unit with this block dies, a small nuke goes off where it
/// died (<see cref="Commander.CommanderDeathBlast"/>, through the strategic detonation queue): every army's land, naval
/// and air units are hurt (subs spared, shields ignored and knocked down) and wrecks in the inner ring vaporize.
/// Like nukes, the rings add up: the inner ring takes InnerDamage + OuterDamage, the outer ring OuterDamage.
/// </summary>
public sealed class DeathBlastBlueprint
{
    public float InnerRadius = 12;
    /// <summary>Extra damage inside InnerRadius (on top of OuterDamage).</summary>
    public float InnerDamage = 4000;
    public float OuterRadius = 26;
    /// <summary>Damage inside OuterRadius (the inner ring included).</summary>
    public float OuterDamage = 1500;
    /// <summary>Seconds between the death and the blast.</summary>
    public float Delay = 1.5f;
}
