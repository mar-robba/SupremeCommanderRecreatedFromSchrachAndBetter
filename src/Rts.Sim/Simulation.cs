using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim;

/// <summary>
/// The fixed-order tick pipeline. One call to <see cref="Step"/> advances the world by SimWorld.Dt (0.1 s).
/// Deterministic: same initial world + same commands per tick => same state, regardless of thread count.
/// </summary>
public sealed class Simulation
{
    public readonly SimWorld World;
    [field: Persistence.NoSave] public double LastTickMs { get; private set; } // wall clock, not saved
    /// <summary>Commands produced by the skirmish AI this tick (reused list).</summary>
    private readonly List<Command> _aiCommands = new();

    public Simulation(SimWorld world) => World = world;

    public void Step(IReadOnlyList<Command> commands)
    {
        var sw = Stopwatch.StartNew();
        var w = World;
        w.Tick++;
        Combat.EmpStun.Expire(w); // EMP stuns that ran out (Combat/EmpStun)

        CommandSystem.Run(w, commands);
        AI.AiSystem.Run(w, _aiCommands); // skirmish AI armies think and issue normal commands
        CommandSystem.Run(w, _aiCommands);
        SandboxSystem.BeginTick(w); // sandbox only: batched spawns, infinite resources, instant build
        Transport.TransportSystem.Run(w); // load/unload/dock (before AliveList: carried units leave the map), fuel
        w.Units.RebuildAliveList();
        w.Projectiles.RebuildAliveList();
        w.Grid.Rebuild(w.Units);

        IntelSystem.Run(w);
        AdjacencySystem.Run(w); // adjacency bonuses (recomputed only when structures change)
        EconomySystem.Run(w);
        OrderSystem.Run(w);
        ConstructionSystem.Run(w);
        FactorySystem.Run(w);
        PathSystem.Run(w);
        MovementSystem.Run(w);
        Experimental.ExperimentalSystem.Run(w); // crush, death blasts, mobile factories, energy-per-shot charging
        Strategic.StrategicSystem.Run(w); // shields, silos, launches, missile defense
        TargetingSystem.Run(w);
        WeaponSystem.Run(w);
        ProjectileSystem.Run(w);
        DamageSystem.Run(w);
        CleanupSystem.Run(w);

        LastTickMs = sw.Elapsed.TotalMilliseconds;
    }

    public void Step() => Step(Array.Empty<Command>());

    public void WriteSnapshot(SimSnapshot s) => WriteSnapshot(s, -1);

    /// <summary>viewArmy &gt;= 0: fog-of-war filtered snapshot for that army (-1 = everything visible).</summary>
    public void WriteSnapshot(SimSnapshot s, int viewArmy)
    {
        SnapshotWriter.Write(World, s, viewArmy);
        s.TickDurationMs = LastTickMs;
        SandboxSystem.WriteSnapshot(World, s); // sandbox status (null outside sandbox)
    }

    /// <summary>Hash of the gameplay-relevant state (for determinism tests and multiplayer desync checks).</summary>
    public ulong StateHash()
    {
        var w = World;
        var u = w.Units;
        ulong h = 1469598103934665603UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        void MixF(float f) => Mix(BitConverter.SingleToUInt32Bits(f));
        Mix((ulong)w.Tick);
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Mix((ulong)i); Mix((ulong)u.Gen[i]); Mix((ulong)u.Bp[i]); Mix((ulong)u.Army[i]);
            MixF(u.Pos[i].X); MixF(u.Pos[i].Y); MixF(u.Heading[i]); MixF(u.Health[i]); MixF(u.BuildFraction[i]);
            if (u.Upgrade[i].Active) { Mix((ulong)u.Upgrade[i].TargetBp); MixF(u.Upgrade[i].Fraction); }
        }
        var p = w.Projectiles;
        for (int i = 0; i < p.Capacity; i++)
        {
            if (!p.Alive[i]) continue;
            Mix((ulong)i); MixF(p.Pos[i].X); MixF(p.Pos[i].Y); MixF(p.Pos[i].Z);
        }
        foreach (int i in w.Props.AliveList) { Mix((ulong)i); MixF(w.Props.Mass[i]); MixF(w.Props.Energy[i]); }
        foreach (var a in w.Armies) { MixF(a.Eco.Mass); MixF(a.Eco.Energy); }
        Mix(Strategic.StrategicSystem.Hash(w));
        Mix(Transport.TransportSystem.Hash(w));
        Mix(Experimental.ExperimentalSystem.Hash(w));
        return h;
    }
}
