using Rts.Sim.Core;

namespace Rts.Sim.Transport;

/// <summary>
/// Per-world transport/cargo state (<c>SimWorld.Cargo</c>), indexed by unit slot. Plain arrays and lists only (save/load
/// friendly). Owned by <see cref="TransportSystem"/>; written in SEQUENTIAL code only (MovementSystem reads
/// <see cref="AirMode"/> and <see cref="Fuel"/> in its parallel phase).
/// </summary>
public sealed class CargoState
{
    /// <summary>Slot of the carrier holding this unit, -1 = not carried. Carried units have UnitFlags.Carried.</summary>
    public readonly int[] Carrier;
    /// <summary>Units inside this carrier, in boarding order (null until the carrier first loads).</summary>
    public readonly List<UnitHandle>?[] Holds;
    /// <summary>Remaining flight fuel in seconds (aircraft with UnitBlueprint.FuelSeconds &gt; 0).</summary>
    public readonly float[] Fuel;
    /// <summary>VTOL flight mode for this tick, see <see cref="AirModes"/>.</summary>
    public readonly byte[] AirMode;
    /// <summary>Last tick a unit asked this transport to come down (a boarder walking up to it).</summary>
    public readonly int[] WantLandTick;
    /// <summary>Last tick a unit boarded this carrier (ferry departure delay).</summary>
    public readonly int[] LastBoardTick;
    /// <summary>No automatic return to an air carrier before this tick (just launched / undocked).</summary>
    public readonly int[] NoAutoDockUntil;

    /// <summary>Units that boarded this tick (references to them are purged from other units' targets).</summary>
    internal readonly List<int> LoadedThisTick = new();

    /// <summary>Scratch list for grid queries (ferry waiters); never carries data between ticks.</summary>
    [Rts.Sim.Persistence.NoSave] internal readonly List<int> Near = new();

    public CargoState(int capacity)
    {
        Carrier = new int[capacity];
        Array.Fill(Carrier, -1);
        Holds = new List<UnitHandle>?[capacity];
        Fuel = new float[capacity];
        AirMode = new byte[capacity];
        WantLandTick = new int[capacity];
        LastBoardTick = new int[capacity];
        NoAutoDockUntil = new int[capacity];
    }

    /// <summary>Slots used inside carrier <paramref name="i"/>.</summary>
    public int UsedSlots(Rts.Sim.World.SimWorld w, int i)
    {
        var h = Holds[i];
        if (h == null) return 0;
        int n = 0;
        foreach (var c in h) n += w.Blueprints[w.Units.Bp[c.Index]].CargoSize;
        return n;
    }

    public int Count(int i) => Holds[i]?.Count ?? 0;
}

/// <summary>Values of <see cref="CargoState.AirMode"/>.</summary>
public static class AirModes
{
    /// <summary>Normal flight (VTOL: hover in place when there is no goal).</summary>
    public const byte Fly = 0;
    /// <summary>VTOL: descend to the carrier's HoverHeight (loading / unloading).</summary>
    public const byte Land = 1;
}
