using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Sim.Transport;

/// <summary>
/// Writes the transport part of a snapshot (called by SnapshotWriter after the units): fuel of every unit view, the
/// cargo of carriers present in the snapshot (so fog applies to the carrier) and the ferry routes of the view army's team.
/// </summary>
internal static class TransportSnapshotWriter
{
    public static void Write(SimWorld w, SimSnapshot s, int viewArmy)
    {
        var u = w.Units;
        var c = w.Cargo;
        s.CargoViewCount = 0;
        s.CargoUnitCount = 0;
        s.FerryCount = 0;
        for (int v = 0; v < s.UnitCount; v++)
        {
            ref var view = ref s.Units[v];
            int i = view.Index;
            var bp = w.Blueprints[view.Bp];
            float fuelMax = bp.FuelSeconds;
            view.Fuel = fuelMax > 0 ? Math.Clamp(c.Fuel[i] / fuelMax, 0f, 1f) : -1f;
            if (bp.Cargo != null)
            {
                var holds = c.Holds[i];
                Ensure(ref s.CargoViews, s.CargoViewCount + 1);
                ref var cv = ref s.CargoViews[s.CargoViewCount++];
                cv.Unit = i;
                cv.Gen = view.Gen;
                cv.Layer = (byte)bp.Cargo.Layer;
                cv.Capacity = bp.Cargo.Capacity;
                cv.Used = c.UsedSlots(w, i);
                cv.Start = s.CargoUnitCount;
                cv.Count = holds?.Count ?? 0;
                cv.Landing = c.AirMode[i] == AirModes.Land;
                if (holds != null)
                {
                    Ensure(ref s.CargoUnits, s.CargoUnitCount + holds.Count);
                    foreach (var h in holds)
                    {
                        var jbp = w.BpOf(h.Index);
                        float jf = jbp.FuelSeconds;
                        s.CargoUnits[s.CargoUnitCount++] = new CargoUnitView
                        {
                            Bp = jbp.Index,
                            HealthFrac = jbp.MaxHealth > 0 ? u.Health[h.Index] / Combat.Veterancy.MaxHealth(jbp, u.Kills[h.Index]) : 1f,
                            Fuel = jf > 0 ? Math.Clamp(c.Fuel[h.Index] / jf, 0f, 1f) : -1f,
                            Size = jbp.CargoSize,
                        };
                    }
                }
            }
            if (bp.IsVtol && (viewArmy < 0 || !w.IsEnemy(viewArmy, view.Army))) AddFerries(w, s, i);
        }
    }

    private static void AddFerries(SimWorld w, SimSnapshot s, int i)
    {
        foreach (var o in w.Units.Orders[i].AsSpan())
        {
            if (o.Type != OrderType.Ferry) continue;
            int army = w.Units.Army[i];
            var pick = DMath.To3(o.Pos, w.Terrain.GetSurfaceHeight(o.Pos));
            var drop = DMath.To3(o.Aux, w.Terrain.GetSurfaceHeight(o.Aux));
            bool merged = false;
            for (int f = 0; f < s.FerryCount; f++)
            {
                ref var fv = ref s.Ferries[f];
                if (fv.Army != army || Vector3.DistanceSquared(fv.Pickup, pick) > 1f || Vector3.DistanceSquared(fv.Drop, drop) > 1f) continue;
                fv.Transports++;
                merged = true;
                break;
            }
            if (merged) continue;
            Ensure(ref s.Ferries, s.FerryCount + 1);
            s.Ferries[s.FerryCount++] = new FerryView { Army = army, Pickup = pick, Drop = drop, Transports = 1 };
        }
    }

    private static void Ensure<T>(ref T[] arr, int size)
    {
        if (arr.Length < size) Array.Resize(ref arr, Math.Max(size, arr.Length * 2));
    }
}
