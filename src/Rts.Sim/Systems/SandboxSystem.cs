using System.Numerics;
using System.Runtime.CompilerServices;
using Rts.Sim.AI;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Sandbox mode: cheats for testing the game by hand (spawn anything, infinite resources, instant build, build
/// anywhere, AI on/off, kill/heal/give units, clear an area, nuke). A world is a sandbox only if
/// <see cref="Enable"/> was called when it was created; otherwise every <see cref="SandboxCommand"/> is ignored.
/// <para>Everything runs in sequential phases of the tick and only through commands, so it is deterministic and
/// replay-safe: <see cref="Apply"/> (from CommandSystem), <see cref="BeginTick"/> (after the commands: batched
/// spawns, infinite resources, instant build) and <see cref="WriteSnapshot"/> (status for the client).</para>
/// State lives in a per-world side table (like the AI), so SimWorld needs no sandbox fields.
/// </summary>
public static class SandboxSystem
{
    /// <summary>Max units spawned per tick by batched spawn requests (1000 units ≈ 0.4 s of game time).</summary>
    public const int SpawnBudgetPerTick = 250;
    /// <summary>Max units per spawn command.</summary>
    public const int MaxSpawnCount = 5000;
    /// <summary>Stored amount set before the economy runs for armies with infinite resources (clamped to storage).</summary>
    private const float InfiniteStore = 1e9f;

    // SupCom strategic missile: inner ring 30 / 70 000 damage, outer ring 40 / 500.
    public const float NukeInnerRadius = 30f, NukeOuterRadius = 40f, NukeInnerDamage = 70000f, NukeOuterDamage = 500f;

    private static readonly ConditionalWeakTable<SimWorld, SandboxState> _states = new();

    /// <summary>Turns the world into a sandbox (call once at creation, before the runner starts). Unit caps are
    /// lifted to the unit store capacity.</summary>
    public static SandboxState Enable(SimWorld w)
    {
        var s = _states.GetValue(w, static ww => new SandboxState(ww));
        foreach (var a in w.Armies) a.UnitCap = Math.Max(a.UnitCap, w.Units.Capacity);
        s.Publish(w);
        return s;
    }

    public static bool IsEnabled(SimWorld w) => _states.TryGetValue(w, out _);

    /// <summary>Sandbox state (null if the world is not a sandbox). Sim thread only.</summary>
    public static SandboxState? StateOf(SimWorld w) => _states.TryGetValue(w, out var s) ? s : null;

    // ------------------------------------------------------------------ commands

    /// <summary>Applies a sandbox command (called by CommandSystem; ignored outside sandbox). SEQUENTIAL.</summary>
    public static void Apply(SimWorld w, SandboxCommand c)
    {
        var s = StateOf(w);
        if (s == null || (uint)c.Army >= (uint)w.Armies.Length) return;
        switch (c)
        {
            case SandboxSpawnCommand sp: QueueSpawn(w, s, sp); break;
            case SandboxToggleCommand t: SetToggle(w, s, t); break;
            case SandboxSetAiCommand ai: SetAi(w, ai.TargetArmy, ai.On, ai.Difficulty); break;
            case SandboxSetVictoryCommand v: s.Victory = v.On; break;
            case SandboxUnitActionCommand ua: UnitAction(w, ua); break;
            case SandboxClearAreaCommand ca: ClearArea(w, ca.Center, ca.Radius, ca.OnlyArmy, ca.IncludeCommanders); break;
            case SandboxNukeCommand n:
                Nuke(w, c.Army, n.Target,
                    n.InnerRadius > 0 ? n.InnerRadius : NukeInnerRadius, n.OuterRadius > 0 ? n.OuterRadius : NukeOuterRadius,
                    n.InnerDamage > 0 ? n.InnerDamage : NukeInnerDamage, n.OuterDamage > 0 ? n.OuterDamage : NukeOuterDamage);
                break;
        }
        s.Publish(w);
    }

    private static void SetToggle(SimWorld w, SandboxState s, SandboxToggleCommand t)
    {
        var arr = t.Toggle switch
        {
            SandboxToggle.InfiniteResources => s.InfiniteResources,
            SandboxToggle.InstantBuild => s.InstantBuild,
            SandboxToggle.BuildAnywhere => s.BuildAnywhere,
            _ => null,
        };
        if (arr == null) return;
        for (int a = 0; a < arr.Length; a++)
            if (t.TargetArmy < 0 || t.TargetArmy == a) arr[a] = t.On;
    }

    /// <summary>Skirmish AI on/off and difficulty for an army at runtime.</summary>
    public static void SetAi(SimWorld w, int army, bool on, AiDifficulty? difficulty)
    {
        if ((uint)army >= (uint)w.Armies.Length) return;
        var a = w.Armies[army];
        a.IsAI = on;
        if (difficulty is AiDifficulty d && d != a.AiDifficulty)
        {
            a.AiDifficulty = d;
            // An existing brain keeps its memory but thinks with the new settings.
            if (AiSystem.StateOf(w)?.BrainOrNull(army) is AiBrain brain) brain.Cfg = AiConfig.For(d);
        }
    }

    // ------------------------------------------------------------------ unit actions

    private static void UnitAction(SimWorld w, SandboxUnitActionCommand c)
    {
        var u = w.Units;
        foreach (var h in c.Units)
        {
            if (!u.IsActive(h)) continue;
            int i = h.Index;
            var bp = w.BpOf(i);
            switch (c.Action)
            {
                case SandboxUnitAction.Kill: w.KillUnit(i, UnitHandle.None); break;
                case SandboxUnitAction.Delete: ConstructionSystem.RemoveUnitSilently(w, i); break;
                case SandboxUnitAction.Heal:
                    u.Health[i] = u.IsComplete(i) ? bp.MaxHealth : MathF.Max(u.Health[i], u.BuildFraction[i] * bp.MaxHealth);
                    break;
                case SandboxUnitAction.Damage:
                    DamageSystem.Apply(w, i, Math.Clamp(c.Amount, 0f, 1f) * bp.MaxHealth, UnitHandle.None);
                    break;
                case SandboxUnitAction.ChangeOwner: ChangeOwner(w, i, c.TargetArmy); break;
                case SandboxUnitAction.Complete:
                    if (!u.IsComplete(i)) FinishConstruction(w, i);
                    break;
            }
        }
    }

    /// <summary>Gives unit i to another army: orders and work are cleared, a factory's unfinished unit goes with it.
    /// Commanders are never transferred (the assassination rule tracks them per army).</summary>
    public static bool ChangeOwner(SimWorld w, int i, int army)
    {
        var u = w.Units;
        if ((uint)army >= (uint)w.Armies.Length || !u.IsActive(i)) return false;
        int old = u.Army[i];
        if (old == army || w.BpOf(i).Has(UnitCategory.Commander)) return false;
        CommandSystem.StopUnit(w, i);
        u.Move[i].GroupId = 0;
        foreach (ref var ws in u.WeaponsOf(i)) ws.Target = UnitHandle.None;
        u.Army[i] = army;
        w.Armies[old].UnitCount--;
        w.Armies[army].UnitCount++;
        ref var f = ref u.Factory[i];
        f.Queue?.Clear();
        f.Repeat = false;
        if (u.IsActive(f.Current)) ChangeOwner(w, f.Current.Index, army);
        return true;
    }

    /// <summary>Removes every unit (and wreck) within radius of center, silently. Returns the units removed.</summary>
    public static int ClearArea(SimWorld w, Vector2 center, float radius, int onlyArmy = -1, bool includeCommanders = false)
    {
        var u = w.Units;
        float r2 = radius * radius;
        int n = 0;
        // Full ascending scan: also sees units spawned earlier this tick (not yet in AliveList). Deterministic.
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.IsActive(i)) continue;
            if (onlyArmy >= 0 && u.Army[i] != onlyArmy) continue;
            if (Vector2.DistanceSquared(u.Pos[i], center) > r2) continue;
            if (!includeCommanders && w.BpOf(i).Has(UnitCategory.Commander)) continue;
            ConstructionSystem.RemoveUnitSilently(w, i);
            n++;
        }
        if (onlyArmy < 0)
            foreach (int p in w.Props.AliveList)
                if (Vector2.DistanceSquared(w.Props.Pos[p], center) <= r2) w.Props.Free(p);
        return n;
    }

    // ------------------------------------------------------------------ nuke

    /// <summary>
    /// Strategic nuke detonation at target: two rings of area damage through the normal damage pipeline (inner ring
    /// kills everything, outer ring damages) plus a <see cref="EffectKind.Nuke"/> effect (flash, shockwave, mushroom
    /// cloud, sound on the client).
    /// <para>TODO(strategic missiles): once the real nuke launcher/strategic missile is merged, launch that missile at
    /// <paramref name="target"/> from here instead (e.g. spawn the projectile above the target or from the army's
    /// silo) so the sandbox nuke uses exactly the same flight, anti-nuke interception and detonation code. This is
    /// the only place to change: every sandbox nuke (panel, console, --exec) goes through this method.</para>
    /// </summary>
    public static void Nuke(SimWorld w, int army, Vector2 target, float innerRadius, float outerRadius, float innerDamage, float outerDamage)
    {
        target = w.Terrain.Clamp(target);
        w.Events.AreaDamage.Add(new AreaDamageEvent
        {
            Center = target, Radius = innerRadius, Amount = innerDamage, Source = UnitHandle.None, SourceArmy = army, Layers = LayerMask.None,
        });
        w.Events.AreaDamage.Add(new AreaDamageEvent
        {
            Center = target, Radius = outerRadius, Amount = outerDamage, Source = UnitHandle.None, SourceArmy = army, Layers = LayerMask.None,
        });
        var fx = new EffectEvent
        {
            Kind = EffectKind.Nuke,
            Pos = DMath.To3(target, w.Terrain.GetSurfaceHeight(target)),
            Size = outerRadius,
            Army = army,
            BlueprintIndex = -1,
        };
        w.Events.Effects.Add(fx);
        StateOf(w)?.RecentNukes.Add((w.Tick, fx));
    }

    // ------------------------------------------------------------------ per tick

    /// <summary>
    /// Start of the tick, right after the commands (SEQUENTIAL): batched spawns, infinite resources (storage topped up
    /// before the economy runs, so efficiency is always 1 and storage shows full), instant build (sites and factory
    /// items a builder worked on last tick are finished, free of charge; repairs finish too).
    /// </summary>
    public static void BeginTick(SimWorld w)
    {
        var s = StateOf(w);
        if (s == null) return;
        if (s.RecentNukes.Count > 0) s.RecentNukes.RemoveAll(n => n.Tick < w.Tick);
        if (s.SpawnJobs.Count > 0) RunSpawnJobs(w, s);
        if (s.AnyInfiniteResources)
            for (int a = 0; a < w.Armies.Length; a++)
            {
                if (!s.InfiniteResources[a]) continue;
                w.Armies[a].Eco.Mass = InfiniteStore;
                w.Armies[a].Eco.Energy = InfiniteStore;
            }
        if (s.AnyInstantBuild) InstantBuild(w, s);
        if (s.StatusStale(w)) s.Publish(w);
    }

    private static void InstantBuild(SimWorld w, SandboxState s)
    {
        var u = w.Units;
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            if (!u.IsActive(i) || !s.InstantBuild[u.Army[i]]) continue;
            ref var b = ref u.Builder[i];
            if (b.Mode is not (BuilderMode.Build or BuilderMode.Assist or BuilderMode.Repair) || !u.IsActive(b.Target)) continue;
            int t = b.Target.Index;
            if (!u.IsComplete(t)) FinishConstruction(w, t);
            else if (b.Mode == BuilderMode.Repair) u.Health[t] = w.BpOf(t).MaxHealth;
            else continue;
            b.PendingWork = 0;
            u.Econ[i].MassRequest = 0;
            u.Econ[i].EnergyRequest = 0;
        }
    }

    /// <summary>Completes a construction site / unit in production at full health.</summary>
    public static void FinishConstruction(SimWorld w, int t)
    {
        ConstructionSystem.CompleteUnit(w, t);
        w.Units.Health[t] = w.BpOf(t).MaxHealth;
    }

    /// <summary>Snapshot hook (sim thread, after SnapshotWriter): sandbox status, no victory unless enabled, and
    /// nukes are always visible (re-added if the fog filtered them out).</summary>
    public static void WriteSnapshot(SimWorld w, SimSnapshot snap)
    {
        var s = StateOf(w);
        if (s == null) { snap.Sandbox = null; return; }
        snap.Sandbox = s.Status;
        if (!s.Victory) snap.WinningTeam = -1;
        if (snap.ViewArmy >= 0)
            foreach (var (tick, fx) in s.RecentNukes)
                if (tick == w.Tick && !snap.Effects.Exists(e => e.Kind == EffectKind.Nuke && e.Pos == fx.Pos)) snap.Effects.Add(fx);
    }

    // ------------------------------------------------------------------ spawning

    private static void QueueSpawn(SimWorld w, SandboxState s, SandboxSpawnCommand c)
    {
        if ((uint)c.BlueprintIndex >= (uint)w.Blueprints.Count || (uint)c.TargetArmy >= (uint)w.Armies.Length || c.Count <= 0) return;
        var bp = w.Blueprints[c.BlueprintIndex];
        var army = w.Armies[c.TargetArmy];
        if (army.Defeated)
        {
            // Spawning for a defeated army revives it (without an assassination target).
            army.Defeated = false;
            army.Commander = UnitHandle.None;
            w.WinningTeam = -1;
        }
        var center = w.Terrain.Clamp(c.Pos, 1f);
        var mapCenter = new Vector2(w.Terrain.Width * 0.5f, w.Terrain.Height * 0.5f);
        int count = Math.Min(c.Count, MaxSpawnCount);
        var job = new SpawnJob
        {
            Army = c.TargetArmy, Blueprint = c.BlueprintIndex, Center = center, Complete = c.Complete,
            Heading = bp.IsStructure || Vector2.DistanceSquared(mapCenter, center) < 1f ? 0f : DMath.HeadingOf(mapCenter - center),
            Remaining = count,
            MaxCandidates = 4096 + count * 16,
        };
        if (bp.IsStructure && NeedsDeposit(bp) && !s.BuildAnywhere[c.TargetArmy]) job.Deposits = DepositsByDistance(w, bp, center);
        s.SpawnJobs.Add(job);
    }

    private static bool NeedsDeposit(UnitBlueprint bp) => bp.Economy.RequiresMassDeposit || bp.Economy.RequiresHydroDeposit;

    private static int[] DepositsByDistance(SimWorld w, UnitBlueprint bp, Vector2 center)
    {
        var type = bp.Economy.RequiresMassDeposit ? DepositType.Mass : DepositType.Hydrocarbon;
        var deps = w.Terrain.Deposits;
        var list = new List<int>();
        for (int d = 0; d < deps.Length; d++)
            if (deps[d].Type == type) list.Add(d);
        // Stable: by distance, then deposit index.
        list.Sort((a, b) =>
        {
            int c = Vector2.DistanceSquared(deps[a].Pos, center).CompareTo(Vector2.DistanceSquared(deps[b].Pos, center));
            return c != 0 ? c : a.CompareTo(b);
        });
        return list.ToArray();
    }

    private static void RunSpawnJobs(SimWorld w, SandboxState s)
    {
        int budget = SpawnBudgetPerTick;
        while (budget > 0 && s.SpawnJobs.Count > 0)
        {
            var job = s.SpawnJobs[0];
            budget -= RunJob(w, s, job, budget);
            if (job.Remaining <= 0 || job.Tried >= job.MaxCandidates || job.Deposits != null && job.DepositIndex >= job.Deposits.Length)
                s.SpawnJobs.RemoveAt(0);
            else if (budget > 0) s.SpawnJobs.RemoveAt(0); // unit store full: give up
        }
    }

    /// <summary>Spawns up to max units of the job; returns how many were spawned.</summary>
    private static int RunJob(SimWorld w, SandboxState s, SpawnJob job, int max)
    {
        var bp = w.Blueprints[job.Blueprint];
        int spawned = 0;
        if (job.Deposits != null)
        {
            var deps = w.Terrain.Deposits;
            while (spawned < max && job.Remaining > 0 && job.DepositIndex < job.Deposits.Length)
            {
                var p = OccupancyGrid.SnapStructure(bp, deps[job.Deposits[job.DepositIndex++]].Pos);
                if (!w.Occupancy.CanPlace(bp, p, w.Terrain)) continue;
                if (!Spawn(w, job, bp, p)) return spawned;
                spawned++;
            }
            return spawned;
        }

        // Grid step: structures keep a 1-cell lane between them (factory exits, paths), mobile units a small gap.
        float stepX, stepZ;
        if (bp.IsStructure)
        {
            int gap = bp.Has(UnitCategory.Wall) ? 0 : 1;
            stepX = bp.FootprintX + gap;
            stepZ = bp.FootprintZ + gap;
        }
        else stepX = stepZ = MathF.Max(1.2f, bp.Radius * 2f + (bp.Movement.Layer == MotionLayer.Air ? 1.2f : 0.6f));

        while (spawned < max && job.Remaining > 0 && job.Tried < job.MaxCandidates)
        {
            var (ox, oz) = job.Spiral.Next();
            job.Tried++;
            var p = job.Center + new Vector2(ox * stepX, oz * stepZ);
            if (!w.Terrain.InBounds(p) || p != w.Terrain.Clamp(p, 1f)) continue;
            if (bp.IsStructure)
            {
                p = OccupancyGrid.SnapStructure(bp, p);
                if (!CanPlaceIgnoringDeposits(w, bp, p)) continue;
            }
            else if (!MobileCellFree(w, bp, p)) continue;
            if (!Spawn(w, job, bp, p)) return spawned;
            spawned++;
        }
        return spawned;
    }

    private static bool Spawn(SimWorld w, SpawnJob job, UnitBlueprint bp, Vector2 p)
    {
        var h = w.SpawnUnit(job.Blueprint, job.Army, p, job.Heading, job.Complete);
        if (h.IsNone) return false; // unit store full
        job.Remaining--;
        return true;
    }

    private static bool MobileCellFree(SimWorld w, UnitBlueprint bp, Vector2 p)
    {
        int cx = (int)MathF.Floor(p.X), cz = (int)MathF.Floor(p.Y);
        var layer = bp.Movement.Layer;
        if (layer == MotionLayer.Air) return w.Terrain.CellInBounds(cx, cz);
        if (layer == MotionLayer.None) layer = MotionLayer.Land;
        return w.Terrain.CanTraverse(layer, cx, cz) && !w.Occupancy.IsBlocked(cx, cz);
    }

    /// <summary><see cref="OccupancyGrid.CanPlace"/> without the deposit rule (build-anywhere).</summary>
    public static bool CanPlaceIgnoringDeposits(SimWorld w, UnitBlueprint bp, Vector2 pos)
    {
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(bp, pos);
        bool naval = bp.HasAny(UnitCategory.Naval);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            if (w.Occupancy.IsBlocked(x, z)) return false;
            var p = w.Terrain.PassAt(x, z);
            if (naval ? (p & PassFlags.WaterSurface) == 0 : (p & PassFlags.Land) == 0) return false;
        }
        return true;
    }

    /// <summary>Build-anywhere hook for ConstructionSystem: true if the army may place the structure here although
    /// <see cref="OccupancyGrid.CanPlace"/> refused it (only the deposit rule is waived).</summary>
    public static bool BuildAnywhereAllows(SimWorld w, int army, UnitBlueprint bp, Vector2 pos)
    {
        var s = StateOf(w);
        return s != null && (uint)army < (uint)s.BuildAnywhere.Length && s.BuildAnywhere[army] && NeedsDeposit(bp)
               && CanPlaceIgnoringDeposits(w, bp, pos);
    }
}
