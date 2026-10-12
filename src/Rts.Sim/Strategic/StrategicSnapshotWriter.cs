using System.Numerics;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Sim.Strategic;

/// <summary>
/// Writes the strategic part of a snapshot (called by SnapshotWriter after the units): shields of units present in
/// the snapshot (so fog of war applies), silos of the view army's team (everything without fog), and the recent
/// strategic launches (global: every player is warned; enemy launch sites/targets are hidden under fog).
/// </summary>
internal static class StrategicSnapshotWriter
{
    public static void Write(SimWorld w, SimSnapshot snap, int viewArmy)
    {
        var s = StrategicState.Of(w);
        var u = w.Units;
        snap.ShieldCount = 0;
        snap.SiloCount = 0;
        snap.Launches.Clear();
        snap.LaunchSerial = s.LaunchSerial;
        bool fog = snap.ViewArmy >= 0;
        foreach (var h in s.Units)
        {
            if (!u.IsActive(h)) continue;
            int i = h.Index;
            if ((uint)i >= (uint)snap.SlotToView.Length || snap.SlotToView[i] < 0) continue;
            var bp = w.BpOf(i);
            if (bp.Shield is { } sb)
            {
                ref var sh = ref s.Shield[i];
                Ensure(ref snap.Shields, snap.ShieldCount + 1);
                snap.Shields[snap.ShieldCount++] = new ShieldView
                {
                    Unit = i, Gen = u.Gen[i], Army = u.Army[i], Bp = bp.Index, Center = ShieldSystem.CenterOf(w, i, sb),
                    Radius = sb.Radius, HeightScale = sb.HeightScale, HpFrac = sh.Up ? sh.Hp / sb.MaxHealth : 0, Up = sh.Up,
                    RechargeFrac = sh.Up || sb.RechargeTime <= 0 ? 1 : Math.Clamp(1f - sh.RechargeLeft / sb.RechargeTime, 0, 1),
                    LastHitTick = sh.LastHitTick,
                };
            }
            if (bp.Silo is { } silo && (!fog || !w.IsEnemy(viewArmy, u.Army[i])))
            {
                ref var st = ref s.Silo[i];
                Ensure(ref snap.Silos, snap.SiloCount + 1);
                snap.Silos[snap.SiloCount++] = new SiloView
                {
                    Unit = i, Gen = u.Gen[i], Kind = silo.Kind, Stock = st.Stock, MaxStock = silo.MaxStorage, Progress = st.Progress,
                };
            }
        }
        foreach (var l in s.Launches)
        {
            bool known = !fog || !w.IsEnemy(viewArmy, l.Army);
            snap.Launches.Add(new LaunchView
            {
                Serial = l.Serial, Tick = l.Tick, Army = l.Army,
                From = known ? l.From : new Vector2(float.NaN), Target = known ? l.Target : new Vector2(float.NaN),
            });
        }
    }

    private static void Ensure<T>(ref T[] arr, int size)
    {
        if (arr.Length < size) Array.Resize(ref arr, Math.Max(size, arr.Length * 2));
    }
}
