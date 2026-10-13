using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Sim.Experimental;

/// <summary>A delayed experimental death explosion (applied by <see cref="ExperimentalSystem.Run"/> at DueTick).</summary>
public struct PendingBlast
{
    public Vector2 Center;
    public float Elevation;
    public float Radius;
    public float Damage;
    public int Army;
    public int DueTick;
    /// <summary>Blueprint of the unit that died (for effects).</summary>
    public int Bp;
}

/// <summary>
/// Per-world state of <see cref="ExperimentalSystem"/>, attached to the world like StrategicState. Plain data only
/// (lists/arrays of structs), so it can be saved/loaded.
/// </summary>
public sealed class ExperimentalState
{
    private static readonly ConditionalWeakTable<SimWorld, ExperimentalState> Table = new();

    public static ExperimentalState Of(SimWorld w) => Table.GetValue(w, static x => new ExperimentalState(x));
    public static bool TryGet(SimWorld w, [NotNullWhen(true)] out ExperimentalState? s) => Table.TryGetValue(w, out s);

    /// <summary>Registered units (spawn order): blueprints with Experimental data, mobile factories, enhanceable units
    /// and units with energy-per-shot weapons. Dead units are pruned in Run.</summary>
    public readonly List<UnitHandle> Units = new();
    /// <summary>Death explosions waiting for their delay (in scheduling order).</summary>
    public readonly List<PendingBlast> Blasts = new();
    /// <summary>Energy stored for the next shot, per unit slot × weapon (<see cref="UnitStore.MaxWeapons"/>).</summary>
    public readonly float[] Charge;
    /// <summary>Energy/s requested last tick for each weapon's charge (paid by EconomySystem this tick).</summary>
    public readonly float[] ChargeRequest;

    /// <summary>Scratch buffer for spatial queries (not state).</summary>
    internal readonly List<int> Query = new();

    private ExperimentalState(SimWorld w)
    {
        Charge = new float[w.Units.Capacity * UnitStore.MaxWeapons];
        ChargeRequest = new float[w.Units.Capacity * UnitStore.MaxWeapons];
    }
}
