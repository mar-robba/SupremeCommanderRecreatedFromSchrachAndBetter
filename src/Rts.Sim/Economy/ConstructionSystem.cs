using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Engineers/commanders: executes the front Build/Repair/Reclaim order (OrderSystem ignores these) and Guard-assist,
/// applies build power to construction sites, creates wrecks on unit death. SEQUENTIAL (it spawns/kills units).
/// <para>Each tick:</para>
/// <list type="number">
/// <item><b>Apply</b>: every builder (engineers, commanders AND factories) applies the work it requested last tick
///   (<see cref="BuilderState.PendingWork"/>) scaled by its economy efficiency (EconomySystem already paid for it).</item>
/// <item><b>Decide</b>: mobile builders execute their front order (move into range, place the site, request work).
///   Factories decide in FactorySystem, which runs right after and uses <see cref="Work"/> too.</item>
/// </list>
/// Requests are capped by the target's remaining work minus what other builders already reserved this tick,
/// so assisting builders never overshoot and resources spent always match progress.
/// </summary>
public static class ConstructionSystem
{
    /// <summary>Build range = base + half size of the target (+ commander bonus).</summary>
    public const float BuildRangeBase = 5f;
    public const float CommanderRangeBonus = 2f;
    /// <summary>Mass deposits within this distance of the clicked point are snapped to.</summary>
    public const float DepositSnapRadius = 4f;
    /// <summary>Fraction of the build cost paid to repair a unit from 0 to full health.</summary>
    public const float RepairCostFraction = 0.5f;
    /// <summary>Reclaiming a living unit drains its health at this multiple of build speed (BuildRate/BuildTime).</summary>
    public const float UnitReclaimSpeed = 1f;
    /// <summary>Prop reclaim speed: mass (and energy) per second per point of BuildRate.</summary>
    public const float PropReclaimMassPerRate = 2f;
    public const float PropReclaimEnergyPerRate = 20f;
    public const float MinPropReclaimTime = 0.5f;
    /// <summary>A Reclaim order without a target unit picks the prop whose edge is within this distance of Pos.</summary>
    public const float PropPickRadius = 2.5f;
    private const float CompleteEpsilon = 1e-4f;

    [Persistence.NoSave] private static readonly List<int> _tmp = new(); // scratch, cleared before use

    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        w.Units.Builder[i] = new BuilderState { Target = UnitHandle.None };
    }

    /// <summary>Unit death hook: complete units leave a wreck worth BuildCostMass·WreckMassFraction.</summary>
    public static void OnUnitDeath(SimWorld w, int i, UnitBlueprint bp)
    {
        var u = w.Units;
        // Unfinished sites (and reclaimed units, which get BuildFraction 0) leave nothing.
        if (!u.IsComplete(i) || u.BuildFraction[i] <= 0) return;
        float mass = bp.Economy.BuildCostMass * bp.WreckMassFraction;
        if (mass <= 0.01f) return;
        w.Props.Spawn(u.Pos[i], u.Heading[i], bp.Index, HalfSize(bp), mass, 0);
    }

    /// <summary>Stops any build/repair/reclaim activity of unit i (called on Stop / order replace).</summary>
    public static void StopBuilder(SimWorld w, int i)
    {
        Idle(w, i);
        ref var e = ref w.Units.Econ[i];
        e.MassRequest = 0;
        e.EnergyRequest = 0;
    }

    public static void Run(SimWorld w)
    {
        ApplyPendingWork(w);
        ExecuteOrders(w);
        UpgradeSystem.Run(w); // upgrading non-factory structures (mexes, radars) request their own upgrade work
    }

    // ------------------------------------------------------------------ apply

    private static void ApplyPendingWork(SimWorld w)
    {
        var u = w.Units;
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            ref var b = ref u.Builder[i];
            float work = b.PendingWork;
            if (work <= 0)
            {
                if (b.AppliedRate != 0 && b.Mode != BuilderMode.Reclaim) b.AppliedRate = 0;
                continue;
            }
            b.PendingWork = 0;
            if (!u.IsActive(i) || !u.IsActive(b.Target)) { b.AppliedRate = 0; continue; }
            float eff = u.Econ[i].Efficiency;
            b.AppliedRate = (b.Mode == BuilderMode.Upgrade ? UpgradeSystem.BuildPower(w, i, b.Target.Index) : w.BpOf(i).Economy.BuildRate) * eff;
            float delta = work * eff;
            if (delta <= 0) continue;
            int t = b.Target.Index;
            var tbp = w.BpOf(t);
            if (b.Mode == BuilderMode.Repair)
            {
                if (u.IsComplete(t)) u.Health[t] = MathF.Min(tbp.MaxHealth, u.Health[t] + delta * tbp.MaxHealth);
            }
            else if (b.Mode == BuilderMode.Upgrade)
            {
                UpgradeSystem.AddProgress(w, t, delta);
            }
            else if (!u.IsComplete(t))
            {
                AddBuildProgress(w, t, tbp, delta);
            }
        }
    }

    private static void AddBuildProgress(SimWorld w, int t, UnitBlueprint tbp, float delta)
    {
        var u = w.Units;
        u.BuildFraction[t] = MathF.Min(1f, u.BuildFraction[t] + delta);
        u.Health[t] = MathF.Min(tbp.MaxHealth, u.Health[t] + delta * tbp.MaxHealth);
        if (u.BuildFraction[t] >= 1f - CompleteEpsilon) CompleteUnit(w, t);
    }

    /// <summary>Finishes construction of unit t (clears UnderConstruction, emits BuildComplete).</summary>
    public static void CompleteUnit(SimWorld w, int t)
    {
        var u = w.Units;
        var bp = w.BpOf(t);
        u.BuildFraction[t] = 1f;
        u.Flags[t] &= ~UnitFlags.UnderConstruction;
        var stats = w.Armies[u.Army[t]].Stats;
        if (bp.IsStructure) stats.StructuresBuilt++; else stats.UnitsBuilt++;
        w.Events.Effects.Add(new EffectEvent
        {
            Kind = EffectKind.BuildComplete,
            Pos = DMath.To3(u.Pos[t], u.Elevation[t]),
            Size = MathF.Max(bp.Visual.Size.X, bp.Visual.Size.Z),
            Army = u.Army[t],
            BlueprintIndex = bp.Index,
        });
    }

    // ------------------------------------------------------------------ decide

    private static void ExecuteOrders(SimWorld w)
    {
        var u = w.Units;
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            var q = u.Orders[i];
            // Fast path: no orders and nothing to reset (most units, most of the time).
            if (q.IsEmpty && u.Builder[i].Mode == BuilderMode.None && (u.Flags[i] & UnitFlags.Building) == 0)
            {
                ref var e0 = ref u.Econ[i];
                if (e0.MassRequest == 0 && e0.EnergyRequest == 0 && e0.ReclaimMass == 0 && e0.ReclaimEnergy == 0) continue;
            }
            if (!u.IsActive(i)) continue;
            var bp = w.BpOf(i);
            if (bp.IsStructure) continue; // factories: FactorySystem
            bool complete = u.IsComplete(i);
            if (bp.Economy.BuildRate <= 0)
            {
                // Non-builders can't execute construction orders: drop them so the queue doesn't stall.
                if (complete && !q.IsEmpty && q.Current.Type is OrderType.Build or OrderType.Repair or OrderType.Reclaim or OrderType.Capture)
                    q.Pop();
                continue;
            }

            ref var e = ref u.Econ[i];
            e.MassRequest = 0;
            e.EnergyRequest = 0;
            e.ReclaimMass = 0;
            e.ReclaimEnergy = 0;
            if (!complete || (u.Flags[i] & UnitFlags.Paused) != 0 || q.IsEmpty)
            {
                Idle(w, i);
                continue;
            }

            ref var o = ref q.Current;
            switch (o.Type)
            {
                case OrderType.Build: ExecBuild(w, i, bp, q, ref o); break;
                case OrderType.Repair: ExecRepair(w, i, bp, q, ref o); break;
                case OrderType.Reclaim: ExecReclaim(w, i, bp, q, ref o); break;
                case OrderType.Guard: ExecGuardAssist(w, i, bp, ref o); break;
                case OrderType.Capture: Finish(w, i, q); break; // not implemented yet
                default: Idle(w, i); break;
            }
        }
    }

    private static void ExecBuild(SimWorld w, int i, UnitBlueprint bp, OrderQueue q, ref Order o)
    {
        var u = w.Units;
        if ((uint)o.BlueprintIndex >= (uint)w.Blueprints.Count) { Finish(w, i, q); return; }
        var sbp = w.Blueprints[o.BlueprintIndex];
        ref var mv = ref u.Move[i];

        if (!o.Spawned.IsNone)
        {
            // Construction started: keep building until complete (someone else may finish it).
            if (!u.IsActive(o.Spawned) || u.IsComplete(o.Spawned.Index)) { Finish(w, i, q); return; }
            if (!MoveIntoRange(w, i, BuildRange(bp, sbp), u.Pos[o.Spawned.Index])) { Idle(w, i); return; }
            Work(w, i, o.Spawned, BuilderMode.Build);
            return;
        }

        if (o.State == 0)
        {
            o.Pos = ResolveSite(w, sbp, o.Pos);
            o.FormationOffset = Vector2.Zero;
            o.State = 1;
        }
        var site = o.Pos;
        if (!MoveIntoRange(w, i, BuildRange(bp, sbp), site)) { Idle(w, i); return; }

        int army = u.Army[i];
        if (sbp.IsStructure)
        {
            int occ = w.Occupancy.At((int)MathF.Floor(site.X), (int)MathF.Floor(site.Y));
            if (occ >= 0)
            {
                // Same unfinished structure of an ally exactly here: assist it. Anything else: give up.
                if (u.IsActive(occ) && u.Bp[occ] == o.BlueprintIndex && !u.IsComplete(occ)
                    && w.IsAlly(army, u.Army[occ]) && Vector2.DistanceSquared(u.Pos[occ], site) < 0.01f)
                {
                    o.Spawned = u.HandleOf(occ);
                    Work(w, i, o.Spawned, BuilderMode.Build);
                    return;
                }
                Finish(w, i, q);
                return;
            }
            if (!w.Occupancy.CanPlace(sbp, site, w.Terrain) && !SandboxSystem.BuildAnywhereAllows(w, army, sbp, site)) { Finish(w, i, q); return; }
        }

        else if (Experimental.ExperimentalSystem.FindMobileSite(w, army, o.BlueprintIndex, site) is var ms && ms >= 0)
        {
            o.Spawned = u.HandleOf(ms); // same unfinished mobile unit (experimental) already started here: assist it
            Work(w, i, o.Spawned, BuilderMode.Build);
            return;
        }

        var a = w.Armies[army];
        if (a.UnitCount >= a.UnitCap) { Idle(w, i); return; }
        if (sbp.IsStructure && !ClearFootprint(w, sbp, site, army)) { Idle(w, i); return; } // enemy in the way: wait
        var h = w.SpawnUnit(o.BlueprintIndex, army, site, o.Heading, complete: false);
        if (h.IsNone) { Idle(w, i); return; }
        if (sbp.IsStructure) AbsorbProps(w, sbp, u.Pos[h.Index], army);
        o.Spawned = h;
        w.Events.Effects.Add(new EffectEvent
        {
            Kind = EffectKind.BuildStarted,
            Pos = DMath.To3(u.Pos[h.Index], u.Elevation[h.Index]),
            Size = MathF.Max(sbp.Visual.Size.X, sbp.Visual.Size.Z),
            Army = army,
            BlueprintIndex = sbp.Index,
        });
        Work(w, i, h, BuilderMode.Build);
    }

    private static void ExecRepair(SimWorld w, int i, UnitBlueprint bp, OrderQueue q, ref Order o)
    {
        var u = w.Units;
        var target = o.Target;
        if (!u.IsActive(target) || target.Index == i || !w.IsAlly(u.Army[i], u.Army[target.Index])) { Finish(w, i, q); return; }
        int t = target.Index;
        var tbp = w.BpOf(t);
        var mode = u.IsComplete(t) ? BuilderMode.Repair : BuilderMode.Build; // repairing a site finishes it
        // At full health, "repairing" an upgrading structure assists its upgrade.
        if (mode == BuilderMode.Repair && u.Health[t] >= tbp.MaxHealth && u.Upgrade[t].Active) mode = BuilderMode.Upgrade;
        if (mode == BuilderMode.Repair && u.Health[t] >= tbp.MaxHealth) { Finish(w, i, q); return; }
        if (!MoveIntoRange(w, i, BuildRange(bp, tbp), u.Pos[t])) { Idle(w, i); return; }
        Work(w, i, target, mode);
    }

    private static void ExecReclaim(SimWorld w, int i, UnitBlueprint bp, OrderQueue q, ref Order o)
    {
        var u = w.Units;
        float dt = SimWorld.Dt;
        float rate = bp.Economy.BuildRate;
        ref var e = ref u.Econ[i];

        if (!o.Target.IsNone)
        {
            // Reclaim a unit: drains its health, gives back its mass proportionally; no wreck.
            if (!u.IsActive(o.Target) || o.Target.Index == i) { Finish(w, i, q); return; }
            int t = o.Target.Index;
            var tbp = w.BpOf(t);
            if (tbp.Has(UnitCategory.Commander)) { Finish(w, i, q); return; }
            if (!MoveIntoRange(w, i, BuildRange(bp, tbp), u.Pos[t])) { Idle(w, i); return; }
            float hp = MathF.Min(u.Health[t], rate * UnitReclaimSpeed / tbp.Economy.BuildTime * dt * tbp.MaxHealth);
            u.Health[t] -= hp;
            e.ReclaimMass += tbp.Economy.BuildCostMass * (hp / tbp.MaxHealth) / dt;
            SetReclaiming(w, i, o.Target, rate);
            if (u.Health[t] <= 1e-3f)
            {
                EmitReclaim(w, u.Pos[t], u.Elevation[t], HalfSize(tbp), u.Army[i], tbp.Index);
                RemoveUnitSilently(w, t);
                Finish(w, i, q);
            }
            return;
        }

        // Reclaim a prop (wreck): resolve the nearest one once, then drain it.
        var props = w.Props;
        if (o.State == 0)
        {
            int p = props.FindNearest(o.Pos, PropPickRadius);
            if (p < 0) { Finish(w, i, q); return; }
            o.Spawned = props.HandleOf(p);
            o.Pos = props.Pos[p];
            o.FormationOffset = Vector2.Zero;
            o.State = 1;
        }
        if (!props.IsActive(o.Spawned)) { Finish(w, i, q); return; }
        int pi = o.Spawned.Index;
        float range = BuildRangeBase + props.Radius[pi] + (bp.Has(UnitCategory.Commander) ? CommanderRangeBonus : 0);
        if (!MoveIntoRange(w, i, range, props.Pos[pi])) { Idle(w, i); return; }
        float time = MathF.Max(MinPropReclaimTime, MathF.Max(
            props.MassMax[pi] / (rate * PropReclaimMassPerRate),
            props.EnergyMax[pi] / (rate * PropReclaimEnergyPerRate)));
        float dm = MathF.Min(props.Mass[pi], props.MassMax[pi] * dt / time);
        float de = MathF.Min(props.Energy[pi], props.EnergyMax[pi] * dt / time);
        props.Mass[pi] -= dm;
        props.Energy[pi] -= de;
        e.ReclaimMass += dm / dt;
        e.ReclaimEnergy += de / dt;
        SetReclaiming(w, i, UnitHandle.None, rate);
        if (props.Mass[pi] <= 1e-4f && props.Energy[pi] <= 1e-4f)
        {
            EmitReclaim(w, props.Pos[pi], w.Terrain.GetHeight(props.Pos[pi]), props.Radius[pi], u.Army[i], props.BlueprintIndex[pi]);
            props.Free(pi);
            Finish(w, i, q);
        }
    }

    /// <summary>
    /// Engineers guarding a unit assist it: help build what it is building (factory queue, engineer site),
    /// finish it if it is itself a site, or repair it when damaged. Following is done by OrderSystem.
    /// </summary>
    private static void ExecGuardAssist(SimWorld w, int i, UnitBlueprint bp, ref Order o)
    {
        var u = w.Units;
        var g = o.Target;
        if (!u.IsActive(g) || g.Index == i || !w.IsAlly(u.Army[i], u.Army[g.Index])) { Idle(w, i); return; }
        int t = g.Index;
        var tbp = w.BpOf(t);
        var work = UnitHandle.None;
        var mode = BuilderMode.None;
        int anchor = t; // unit we must be in range of
        ref var tb = ref u.Builder[t];
        if (!u.IsComplete(t))
        {
            work = g; mode = BuilderMode.Build;
        }
        else if (u.Upgrade[t].Active)
        {
            work = g; mode = BuilderMode.Upgrade; // assist an in-place upgrade (factory production is paused meanwhile)
        }
        else if (tbp.Has(UnitCategory.Factory) && u.IsActive(u.Factory[t].Current) && !u.IsComplete(u.Factory[t].Current.Index))
        {
            work = u.Factory[t].Current; mode = BuilderMode.Assist; // the unit is inside the factory: range to the factory
        }
        else if (tb.Mode is BuilderMode.Build or BuilderMode.Assist or BuilderMode.Repair or BuilderMode.Upgrade && u.IsActive(tb.Target) && tb.Target.Index != i)
        {
            int bt = tb.Target.Index;
            if (tb.Mode == BuilderMode.Upgrade) { if (u.Upgrade[bt].Active) { work = tb.Target; mode = BuilderMode.Upgrade; anchor = bt; } }
            else if (!u.IsComplete(bt)) { work = tb.Target; mode = BuilderMode.Assist; anchor = bt; }
            else if (tb.Mode == BuilderMode.Repair && u.Health[bt] < w.BpOf(bt).MaxHealth) { work = tb.Target; mode = BuilderMode.Repair; anchor = bt; }
        }
        if (work.IsNone && u.IsComplete(t) && u.Health[t] < tbp.MaxHealth)
        {
            work = g; mode = BuilderMode.Repair;
        }
        if (work.IsNone) { Idle(w, i); return; }
        if (!MoveIntoRange(w, i, BuildRange(bp, w.BpOf(anchor)), u.Pos[anchor])) { Idle(w, i); return; }
        Work(w, i, work, mode);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Builder i works on target this tick: sets builder state and requests resources for the work that will be
    /// applied next tick (after EconomySystem paid for it). Mode Repair restores health, otherwise build progress.
    /// Used by FactorySystem as well. SEQUENTIAL.
    /// </summary>
    public static void Work(SimWorld w, int i, UnitHandle target, BuilderMode mode)
    {
        var u = w.Units;
        ref var b = ref u.Builder[i];
        b.Mode = mode;
        b.Target = target;
        b.PendingWork = 0;
        u.Flags[i] |= UnitFlags.Building;
        int t = target.Index;
        if (mode == BuilderMode.Upgrade) { UpgradeSystem.RequestWork(w, i, t, ref b); return; }
        var tbp = w.BpOf(t);
        float step = w.BpOf(i).Economy.BuildRate / tbp.Economy.BuildTime * SimWorld.Dt;
        ref var tb = ref u.Builder[t];
        float reserved = tb.ReservedTick == w.Tick ? tb.ReservedWork : 0;
        float remaining, costScale;
        if (mode == BuilderMode.Repair)
        {
            remaining = (tbp.MaxHealth - u.Health[t]) / tbp.MaxHealth - reserved;
            costScale = RepairCostFraction;
        }
        else
        {
            remaining = 1f - u.BuildFraction[t] - reserved;
            costScale = 1f;
        }
        float work = MathF.Min(step, remaining);
        if (work <= 0) return;
        b.PendingWork = work;
        tb.ReservedWork = reserved + work;
        tb.ReservedTick = w.Tick;
        ref var e = ref u.Econ[i];
        e.MassRequest += tbp.Economy.BuildCostMass * costScale * work / SimWorld.Dt;
        e.EnergyRequest += tbp.Economy.BuildCostEnergy * costScale * work / SimWorld.Dt;
    }

    /// <summary>Clears builder state (no target, no pending work, Building flag off). Leaves movement alone.</summary>
    public static void Idle(SimWorld w, int i)
    {
        ref var b = ref w.Units.Builder[i];
        b.Mode = BuilderMode.None;
        b.Target = UnitHandle.None;
        b.PendingWork = 0;
        w.Units.Flags[i] &= ~UnitFlags.Building;
    }

    private static void SetReclaiming(SimWorld w, int i, UnitHandle target, float rate)
    {
        ref var b = ref w.Units.Builder[i];
        b.Mode = BuilderMode.Reclaim;
        b.Target = target;
        b.PendingWork = 0;
        b.AppliedRate = rate;
        w.Units.Flags[i] |= UnitFlags.Building;
    }

    private static void Finish(SimWorld w, int i, OrderQueue q)
    {
        q.Pop();
        Idle(w, i);
        w.Units.Move[i].HasGoal = false;
    }

    /// <summary>Returns true if within range (and stops moving); otherwise steers toward the point.</summary>
    private static bool MoveIntoRange(SimWorld w, int i, float range, Vector2 point)
    {
        ref var mv = ref w.Units.Move[i];
        if (Vector2.DistanceSquared(w.Units.Pos[i], point) <= range * range)
        {
            mv.HasGoal = false;
            return true;
        }
        OrderSystem.SetGoal(ref mv, w.Terrain.Clamp(point), MathF.Max(1f, range * 0.7f));
        return false;
    }

    /// <summary>Half extent of a unit: footprint half size for structures, radius for mobile units.</summary>
    public static float HalfSize(UnitBlueprint bp) =>
        bp.IsStructure ? MathF.Max(bp.FootprintX, bp.FootprintZ) * 0.5f : bp.Radius;

    public static float BuildRange(UnitBlueprint builder, UnitBlueprint target) =>
        BuildRangeBase + HalfSize(target) + (builder.Has(UnitCategory.Commander) ? CommanderRangeBonus : 0);

    /// <summary>Final position of a structure ordered at pos: snapped to a nearby deposit if required, then to the grid.</summary>
    public static Vector2 ResolveSite(SimWorld w, UnitBlueprint sbp, Vector2 pos)
    {
        if (sbp.Economy.RequiresMassDeposit || sbp.Economy.RequiresHydroDeposit)
        {
            var type = sbp.Economy.RequiresMassDeposit ? Map.DepositType.Mass : Map.DepositType.Hydrocarbon;
            float best = DepositSnapRadius * DepositSnapRadius;
            var deps = w.Terrain.Deposits;
            for (int d = 0; d < deps.Length; d++)
            {
                if (deps[d].Type != type) continue;
                float d2 = Vector2.DistanceSquared(deps[d].Pos, pos);
                if (d2 < best) { best = d2; pos = deps[d].Pos; }
            }
        }
        return sbp.IsStructure ? OccupancyGrid.SnapStructure(sbp, pos) : pos;
    }

    /// <summary>
    /// Pushes own/allied ground units out of a new structure's footprint. Returns false (placement must wait)
    /// if an enemy ground unit stands there.
    /// </summary>
    private static bool ClearFootprint(SimWorld w, UnitBlueprint sbp, Vector2 site, int army)
    {
        var u = w.Units;
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(sbp, site);
        float minX = x0, maxX = x1 + 1, minZ = z0, maxZ = z1 + 1;
        _tmp.Clear();
        w.Grid.QueryRadius(u, site, MathF.Max(maxX - minX, maxZ - minZ) * 0.75f + 3f, _tmp);
        foreach (int j in _tmp)
            if (InFootprint(w, j, minX, maxX, minZ, maxZ) && w.IsEnemy(army, u.Army[j])) return false;
        Span<Vector2> cand = stackalloc Vector2[4];
        Span<float> dist = stackalloc float[4];
        foreach (int j in _tmp)
        {
            if (!InFootprint(w, j, minX, maxX, minZ, maxZ)) continue;
            var jbp = w.BpOf(j);
            float r = jbp.Radius + 0.05f;
            var p = u.Pos[j];
            cand[0] = new Vector2(minX - r, p.Y); dist[0] = p.X - (minX - r);
            cand[1] = new Vector2(maxX + r, p.Y); dist[1] = maxX + r - p.X;
            cand[2] = new Vector2(p.X, minZ - r); dist[2] = p.Y - (minZ - r);
            cand[3] = new Vector2(p.X, maxZ + r); dist[3] = maxZ + r - p.Y;
            for (int pass = 0; pass < 4; pass++)
            {
                int best = -1;
                for (int c = 0; c < 4; c++)
                    if (dist[c] < float.MaxValue && (best < 0 || dist[c] < dist[best])) best = c;
                if (best < 0) break;
                var np = cand[best];
                dist[best] = float.MaxValue;
                int cx = (int)MathF.Floor(np.X), cz = (int)MathF.Floor(np.Y);
                if (!w.Terrain.CanTraverse(jbp.Movement.Layer, cx, cz) || w.Occupancy.IsBlocked(cx, cz)) continue;
                u.Pos[j] = np;
                break;
            }
        }
        return true;
    }

    private static bool InFootprint(SimWorld w, int j, float minX, float maxX, float minZ, float maxZ)
    {
        var u = w.Units;
        if (!u.IsActive(j) || !u.IsComplete(j)) return false;
        var jbp = w.BpOf(j);
        if (jbp.IsStructure || jbp.Movement.Layer == MotionLayer.Air) return false;
        float r = jbp.Radius;
        var p = u.Pos[j];
        return p.X > minX - r && p.X < maxX + r && p.Y > minZ - r && p.Y < maxZ + r;
    }

    /// <summary>A new structure absorbs (auto-reclaims) props under its footprint; their value goes to storage.</summary>
    private static void AbsorbProps(SimWorld w, UnitBlueprint sbp, Vector2 site, int army)
    {
        var props = w.Props;
        if (props.Count == 0) return;
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(sbp, site);
        var eco = w.Armies[army].Eco;
        // Free() only marks the list dirty; the span we iterate stays valid until the next AliveList access.
        foreach (int p in props.AliveList)
        {
            var pp = props.Pos[p];
            if (pp.X < x0 || pp.X > x1 + 1 || pp.Y < z0 || pp.Y > z1 + 1) continue;
            eco.Mass = MathF.Min(eco.MassMax, eco.Mass + props.Mass[p]);
            eco.Energy = MathF.Min(eco.EnergyMax, eco.Energy + props.Energy[p]);
            props.Free(p);
        }
    }

    /// <summary>Removes a unit without a wreck and without a death explosion (reclaimed / cancelled). SEQUENTIAL.</summary>
    public static void RemoveUnitSilently(SimWorld w, int t)
    {
        var u = w.Units;
        if (!u.IsActive(t)) return;
        int bpIndex = u.Bp[t];
        u.BuildFraction[t] = 0; // => no wreck (see OnUnitDeath)
        int effectsBefore = w.Events.Effects.Count;
        w.KillUnit(t, UnitHandle.None);
        var fx = w.Events.Effects;
        for (int k = fx.Count - 1; k >= effectsBefore; k--)
            if (fx[k].Kind == EffectKind.UnitDeath && fx[k].BlueprintIndex == bpIndex) { fx.RemoveAt(k); break; }
    }

    private static void EmitReclaim(SimWorld w, Vector2 pos, float y, float size, int army, int bpIndex) =>
        w.Events.Effects.Add(new EffectEvent { Kind = EffectKind.Reclaim, Pos = DMath.To3(pos, y), Size = size * 2, Army = army, BlueprintIndex = bpIndex });
}
