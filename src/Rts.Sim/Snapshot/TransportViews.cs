using System.Numerics;

namespace Rts.Sim.Snapshot;

/// <summary>A carrier in the snapshot (transport / air staging / Atlantis) and its cargo.</summary>
public struct CargoView
{
    /// <summary>Carrier unit slot / generation (look it up with SimSnapshot.TryGetUnit).</summary>
    public int Unit, Gen;
    /// <summary>0 = carries land units (transport), 1 = docks aircraft (air staging / carrier). See Data.CargoLayer.</summary>
    public byte Layer;
    /// <summary>Slots used / total (UnitBlueprint.CargoSize per unit).</summary>
    public int Used, Capacity;
    /// <summary>Range in SimSnapshot.CargoUnits, in boarding order.</summary>
    public int Start, Count;
    /// <summary>VTOL transport currently landing/landed to load or unload.</summary>
    public bool Landing;
}

/// <summary>A unit inside a carrier.</summary>
public struct CargoUnitView
{
    public int Bp;
    public float HealthFrac;
    /// <summary>Fuel 0..1, -1 = no fuel.</summary>
    public float Fuel;
    /// <summary>Slots it takes.</summary>
    public int Size;
}

/// <summary>A ferry route (from transports with a Ferry order): units ordered to the Pickup beacon get shuttled to Drop.</summary>
public struct FerryView
{
    public int Army;
    public Vector3 Pickup, Drop;
    /// <summary>Transports assigned to this route.</summary>
    public int Transports;
}
