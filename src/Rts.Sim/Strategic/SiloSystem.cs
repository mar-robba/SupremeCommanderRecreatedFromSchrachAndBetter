using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Strategic;

/// <summary>
/// Missile silos (TML, nuke launcher, anti-nuke). Sequential, after EconomySystem:
/// <list type="number">
/// <item>Stockpile (pay first, like construction): the work requested last tick (a fraction of a missile) is applied
///   scaled by the unit's economy efficiency; then, below MaxStorage, the silo requests
///   MissileCost × (dt / MissileBuildTime) for this tick. Paused / unfinished silos request nothing.</item>
/// <item>Launch orders (<see cref="OrderType.Launch"/>, front of the queue): with a missile in stock the silo
///   launches at Pos (TML: only within [MinRange, Range] of the silo's weapon, else the order is dropped) and the
///   order is popped; with an empty stockpile the order waits for the next missile. Anti-nuke silos drop Launch
///   orders (their stockpile feeds the missile defense).</item>
/// </list>
/// </summary>
public static class SiloSystem
{
    private const float CompleteEps = 1e-4f;

    internal static void InitUnit(StrategicState s, SimWorld w, int i, UnitBlueprint bp) => s.Silo[i] = default;

    /// <summary>Can this unit launch missiles at ground targets (TML / nuke)?</summary>
    public static bool IsLauncher(UnitBlueprint bp) => bp.Silo is { Kind: SiloKind.Tactical or SiloKind.Strategic };

    /// <summary>Is the ground point a valid launch target for the silo at <paramref name="from"/>?</summary>
    public static bool InRange(UnitBlueprint bp, Vector2 from, Vector2 target)
    {
        if (!IsLauncher(bp)) return false;
        var wb = bp.Weapons[bp.Silo!.WeaponIndex];
        float d2 = Vector2.DistanceSquared(from, target);
        return d2 <= wb.Range * wb.Range && d2 >= wb.MinRange * wb.MinRange;
    }

    internal static void Update(StrategicState s, SimWorld w)
    {
        var u = w.Units;
        const float dt = SimWorld.Dt;
        foreach (var h in s.Units)
        {
            if (!u.IsActive(h)) continue;
            int i = h.Index;
            var bp = w.BpOf(i);
            var sb = bp.Silo;
            if (sb == null) continue;
            ref var st = ref s.Silo[i];
            ref var e = ref u.Econ[i];
            // Mobile builders (commander with a missile pack): ConstructionSystem resets their requests every tick,
            // so the silo adds to them instead of owning them.
            bool builder = bp.IsMobile && bp.Economy.BuildRate > 0;

            // 1) Apply the work paid this tick (requests may have been cleared by a Stop: then nothing was paid).
            if (st.PendingWork > 0 && (builder || e.MassRequest > 0 || e.EnergyRequest > 0 || (sb.MissileCostMass <= 0 && sb.MissileCostEnergy <= 0)))
            {
                st.Progress += st.PendingWork * e.Efficiency;
                if (st.Progress >= 1f - CompleteEps)
                {
                    st.Stock++;
                    st.Progress = 0;
                }
            }
            st.PendingWork = 0;

            // 2) Request this tick's work.
            bool canBuild = u.IsComplete(i) && (u.Flags[i] & UnitFlags.Paused) == 0 && st.Stock < sb.MaxStorage;
            if (canBuild)
            {
                float work = MathF.Min(dt / MathF.Max(0.1f, sb.MissileBuildTime), 1f - st.Progress);
                st.PendingWork = work;
                if (builder)
                {
                    e.MassRequest += sb.MissileCostMass * work / dt;
                    e.EnergyRequest += sb.MissileCostEnergy * work / dt;
                }
                else
                {
                    e.MassRequest = sb.MissileCostMass * work / dt;
                    e.EnergyRequest = sb.MissileCostEnergy * work / dt;
                }
            }
            else if (!builder)
            {
                e.MassRequest = 0;
                e.EnergyRequest = 0;
            }

            // 3) Launch orders.
            if (!u.IsComplete(i)) continue;
            var q = u.Orders[i];
            if (q.IsEmpty || q.Current.Type != OrderType.Launch) continue;
            if (!IsLauncher(bp)) { q.Pop(); continue; }
            var target = w.Terrain.Clamp(q.Current.Pos);
            if (!InRange(bp, u.Pos[i], target)) { q.Pop(); continue; }
            if (st.Stock <= 0) continue; // wait for the missile being built
            if (MissileSystem.Launch(s, w, i, target) < 0) continue; // projectile store full: retry next tick
            st.Stock--;
            q.Pop();
        }
    }

    /// <summary>
    /// Sandbox instant build: every complete, unpaused silo of the armies with <paramref name="instant"/>[army] set
    /// finishes the missile it is building at once, free (one per tick, so a silo is full after MaxStorage ticks). The
    /// pending work is dropped and this tick's requests are cleared, so the economy charges nothing. Sequential, before
    /// EconomySystem (SandboxSystem.BeginTick).
    /// </summary>
    public static void InstantStockpile(SimWorld w, bool[] instant)
    {
        var s = StrategicState.Of(w);
        var u = w.Units;
        foreach (var h in s.Units)
        {
            if (!u.IsActive(h)) continue;
            int i = h.Index;
            if ((uint)u.Army[i] >= (uint)instant.Length || !instant[u.Army[i]]) continue;
            var sb = w.BpOf(i).Silo;
            if (sb == null || !u.IsComplete(i) || (u.Flags[i] & UnitFlags.Paused) != 0) continue;
            ref var st = ref s.Silo[i];
            if (st.Stock >= sb.MaxStorage) continue;
            st.Stock++;
            st.Progress = 0;
            st.PendingWork = 0;
            u.Econ[i].MassRequest = 0;
            u.Econ[i].EnergyRequest = 0;
        }
    }

    /// <summary>Debug/tests/scenes: sets the stockpile directly (clamped to MaxStorage). Sequential.</summary>
    public static void SetStock(SimWorld w, int unit, int stock)
    {
        var sb = w.BpOf(unit).Silo;
        if (sb == null) return;
        StrategicState.Of(w).Silo[unit].Stock = Math.Clamp(stock, 0, sb.MaxStorage);
    }
}
