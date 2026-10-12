using System.Numerics;
using Rts.Sim.Data;

namespace Rts.Sim.Snapshot;

/// <summary>A shield dome of a unit present in the snapshot (up or recharging).</summary>
public struct ShieldView
{
    public int Unit, Gen, Army, Bp;
    /// <summary>Dome center (y-up); horizontal radius and vertical semi-axis = Radius × HeightScale.</summary>
    public Vector3 Center;
    public float Radius, HeightScale;
    /// <summary>Shield HP / MaxHealth (0 while down).</summary>
    public float HpFrac;
    public bool Up;
    /// <summary>While down: 0..1 recharge progress.</summary>
    public float RechargeFrac;
    public int LastHitTick;
}

/// <summary>Missile silo of an own/allied unit: stockpile and build progress of the next missile.</summary>
public struct SiloView
{
    public int Unit, Gen;
    public SiloKind Kind;
    public int Stock, MaxStock;
    /// <summary>0..1 progress of the missile being built.</summary>
    public float Progress;
}

/// <summary>A strategic (nuclear) launch, sent to every player for the "Strategic launch detected" warning.</summary>
public struct LaunchView
{
    /// <summary>Increasing launch number: show each serial once.</summary>
    public int Serial;
    public int Tick;
    public int Army;
    /// <summary>Launch site and target: only for the view army's team (NaN for enemy launches under fog).</summary>
    public Vector2 From, Target;
}
