using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Factory build queues (SEQUENTIAL, runs right after ConstructionSystem). The front of
/// <see cref="FactoryState.Queue"/> is built inside the factory as an UnderConstruction unit (it can't move or
/// shoot), progressing with the factory's BuildRate × economy efficiency (via <see cref="ConstructionSystem.Work"/>,
/// so assisting engineers add their build power). When complete the unit drives out of the factory to the exit
/// (<see cref="FactoryExitMotion"/>; aircraft take off) and receives a copy of the factory's own order queue (rally
/// orders), or a short move out of the exit. With Repeat on, the
/// finished item is re-appended to the queue. A destroyed factory kills its unfinished unit.
/// </summary>
public static class FactorySystem
{
    public const int MaxQueueLength = 500;
    /// <summary>Distance beyond the exit that units without rally orders drive to.</summary>
    public const float ExitMoveDistance = 4f;

    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        if (bp.Has(UnitCategory.Factory)) w.Units.Factory[i].Queue ??= new List<int>();
    }

    public static void OnUnitDeath(SimWorld w, int i, UnitBlueprint bp)
    {
        if (!bp.Has(UnitCategory.Factory)) return;
        var u = w.Units;
        var cur = u.Factory[i].Current;
        if (u.IsActive(cur) && !u.IsComplete(cur.Index)) w.KillUnit(cur.Index, UnitHandle.None);
        u.Factory[i].Current = UnitHandle.None;
    }

    public static void Enqueue(SimWorld w, FactoryEnqueueCommand c)
    {
        if ((uint)c.BlueprintIndex >= (uint)w.Blueprints.Count || c.Count <= 0) return;
        int count = Math.Min(c.Count, MaxQueueLength);
        foreach (var h in c.Factories)
        {
            if (!CommandSystem.Owns(w, c.Army, h)) continue;
            var bp = w.BpOf(h.Index);
            if (!bp.Has(UnitCategory.Factory) || Array.IndexOf(bp.BuildIndices, c.BlueprintIndex) < 0) continue;
            var q = w.Units.Factory[h.Index].Queue ??= new List<int>();
            for (int n = 0; n < count && q.Count < MaxQueueLength; n++) q.Add(c.BlueprintIndex);
        }
    }

    /// <summary>Removes up to Count entries of the blueprint from the back of each queue. Removing the entry
    /// being built cancels it and refunds what was spent.</summary>
    public static void Dequeue(SimWorld w, FactoryDequeueCommand c)
    {
        int count = Math.Max(1, c.Count);
        foreach (var h in c.Factories)
        {
            if (!CommandSystem.Owns(w, c.Army, h)) continue;
            var q = w.Units.Factory[h.Index].Queue;
            if (q == null) continue;
            int removed = 0;
            for (int k = q.Count - 1; k >= 0 && removed < count; k--)
            {
                if (q[k] != c.BlueprintIndex) continue;
                if (k == 0) CancelCurrent(w, h.Index);
                q.RemoveAt(k);
                removed++;
            }
        }
    }

    private static void CancelCurrent(SimWorld w, int f)
    {
        var u = w.Units;
        ref var fs = ref u.Factory[f];
        var cur = fs.Current;
        fs.Current = UnitHandle.None;
        ConstructionSystem.StopBuilder(w, f);
        if (!u.IsActive(cur) || u.IsComplete(cur.Index)) return;
        var cbp = w.BpOf(cur.Index);
        var eco = w.Armies[u.Army[f]].Eco;
        float frac = u.BuildFraction[cur.Index];
        eco.Mass = MathF.Min(eco.MassMax, eco.Mass + cbp.Economy.BuildCostMass * frac);
        eco.Energy = MathF.Min(eco.EnergyMax, eco.Energy + cbp.Economy.BuildCostEnergy * frac);
        ConstructionSystem.RemoveUnitSilently(w, cur.Index);
    }

    public static void Run(SimWorld w)
    {
        var u = w.Units;
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            if (!u.IsActive(i)) continue;
            var bp = w.BpOf(i);
            if (!bp.Has(UnitCategory.Factory)) continue;
            ref var e = ref u.Econ[i];
            e.MassRequest = 0;
            e.EnergyRequest = 0;
            ref var f = ref u.Factory[i];
            if (!u.IsComplete(i) || (u.Flags[i] & UnitFlags.Paused) != 0)
            {
                ConstructionSystem.Idle(w, i);
                continue;
            }

            if (!f.Current.IsNone)
            {
                if (!u.IsActive(f.Current))
                {
                    f.Current = UnitHandle.None; // destroyed inside the factory: the queue front is restarted
                }
                else if (u.IsComplete(f.Current.Index))
                {
                    Release(w, i, bp, f.Current.Index);
                    f.Current = UnitHandle.None;
                    if (f.Queue is { Count: > 0 })
                    {
                        int done = f.Queue[0];
                        f.Queue.RemoveAt(0);
                        if (f.Repeat) f.Queue.Add(done);
                    }
                }
            }

            // In-place upgrade: production pauses (queue and the unit inside are kept) while the factory upgrades.
            if (u.Upgrade[i].Active) { UpgradeSystem.WorkOnSelf(w, i); continue; }

            if (f.Current.IsNone && f.Queue is { Count: > 0 }) StartNext(w, i, bp, ref f);

            if (!f.Current.IsNone) ConstructionSystem.Work(w, i, f.Current, BuilderMode.Build);
            else ConstructionSystem.Idle(w, i);
        }
    }

    private static void StartNext(SimWorld w, int i, UnitBlueprint bp, ref FactoryState f)
    {
        var u = w.Units;
        int b = f.Queue![0];
        if ((uint)b >= (uint)w.Blueprints.Count || Array.IndexOf(bp.BuildIndices, b) < 0)
        {
            f.Queue.RemoveAt(0);
            return;
        }
        int army = u.Army[i];
        var a = w.Armies[army];
        if (a.UnitCount >= a.UnitCap) return; // wait for room
        var h = w.SpawnUnit(b, army, u.Pos[i], u.Heading[i], complete: false);
        if (h.IsNone) return;
        f.Current = h;
        var ubp = w.Blueprints[b];
        w.Events.Effects.Add(new EffectEvent
        {
            Kind = EffectKind.BuildStarted,
            Pos = DMath.To3(u.Pos[h.Index], u.Elevation[h.Index]),
            Size = MathF.Max(ubp.Visual.Size.X, ubp.Visual.Size.Z),
            Army = army,
            BlueprintIndex = b,
        });
    }

    /// <summary>Moves a finished unit to the factory exit and gives it the rally orders.</summary>
    private static void Release(SimWorld w, int f, UnitBlueprint fbp, int unit)
    {
        var u = w.Units;
        var ubp = w.BpOf(unit);
        var layer = ubp.Movement.Layer;
        // Mobile factories (Fatboy) let units out at the back and don't hand their own move orders over as rally.
        float half = fbp.IsMobile ? fbp.Visual.Size.Z * 0.5f : ConstructionSystem.HalfSize(fbp);
        float dist = half + ubp.Radius + 0.3f;
        float heading = u.Heading[f] + (fbp.IsMobile ? DMath.Pi : 0f);
        var exit = u.Pos[f] + DMath.Dir(heading) * dist;
        if (layer != MotionLayer.Air)
        {
            // Front exit first; if blocked try the sides and the back.
            for (int side = 0; side < 4; side++)
            {
                float h = heading + side switch { 0 => 0f, 1 => DMath.Pi * 0.5f, 2 => -DMath.Pi * 0.5f, _ => DMath.Pi };
                var p = w.Terrain.Clamp(u.Pos[f] + DMath.Dir(h) * dist);
                int cx = (int)MathF.Floor(p.X), cz = (int)MathF.Floor(p.Y);
                if (w.Terrain.CanTraverse(layer, cx, cz) && !w.Occupancy.IsBlocked(cx, cz))
                {
                    exit = p;
                    heading = h;
                    break;
                }
            }
        }
        exit = w.Terrain.Clamp(exit);
        // The unit starts where it was built (factory center) and drives out to the exit (FactoryExitMotion);
        // aircraft take off from the pad. A moving mobile factory (Fatboy) drops it straight at its back.
        var start = fbp.IsMobile ? exit : FactoryExitMotion.Start(w, f);
        u.Pos[unit] = start;
        u.Heading[unit] = heading;
        u.Elevation[unit] = layer switch
        {
            MotionLayer.Air => w.Terrain.GetSurfaceHeight(start),
            MotionLayer.Land or MotionLayer.Amphibious => w.Terrain.GetHeight(start),
            MotionLayer.Sub => (u.Flags[unit] & UnitFlags.Surfaced) != 0 ? w.Terrain.WaterLevel : w.Terrain.WaterLevel - Naval.NavalMotion.SubDepth,
            _ => w.Terrain.GetSurfaceHeight(start),
        };
        if (layer != MotionLayer.Air && start != exit)
        {
            ref var mv = ref u.Move[unit];
            mv.FactoryExit = exit;
            mv.LeaveTime = FactoryExitMotion.MaxLeaveTime;
        }

        var q = u.Orders[unit];
        q.Clear();
        var rally = fbp.IsMobile ? ReadOnlySpan<Order>.Empty : u.Orders[f].AsSpan();
        if (rally.Length > 0)
        {
            foreach (var o in rally)
            {
                var c = o;
                c.State = 0;
                c.Spawned = UnitHandle.None;
                q.Enqueue(c);
            }
        }
        else
        {
            // Spread units sideways a little so they don't pile up on one spot.
            var dir = DMath.Dir(heading);
            var side = new Vector2(-dir.Y, dir.X);
            float jitter = w.RandomFor(unit, 0xFAC).Range(-2.5f, 2.5f);
            q.Enqueue(Order.MoveTo(w.Terrain.Clamp(exit + dir * ExitMoveDistance + side * jitter)));
        }
    }
}
