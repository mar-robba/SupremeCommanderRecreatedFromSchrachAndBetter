using System.Diagnostics;
using System.Runtime.CompilerServices;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Skirmish AI entry point. Runs inside the deterministic simulation at the start of each tick (right after the
/// player commands are applied): every AI army (<see cref="Army.IsAI"/> and not defeated) thinks on its own staggered
/// schedule and emits ordinary <see cref="Command"/>s, which Simulation applies through CommandSystem in army order.
/// <para>Determinism: SEQUENTIAL, iterates Units.AliveList (ascending) and lists only, uses DetRandom derived from the
/// world seed/tick/army, never Dictionary/HashSet iteration. Results do not depend on thread count.</para>
/// <para>Structure: <see cref="AiBrain"/> per army = <see cref="AiPerception"/> (the only view on enemies, ready for
/// fog of war) + <see cref="AiEconomy"/> (commander opening, engineers, power/mass balance, factories, placement) +
/// <see cref="AiMilitary"/> (rally pool, attack waves, defense, air, scouts, commander safety).</para>
/// </summary>
public static class AiSystem
{
    private static readonly ConditionalWeakTable<SimWorld, AiState> _states = new();

    /// <summary>Diagnostics hook (tools only, e.g. the bench with AI_TRACE=1): receives (army, message) for notable
    /// AI decisions (waves, retreats, targets). Never read by the AI, so it cannot affect the simulation (and is not
    /// part of a save).</summary>
    [NoSave] public static Action<int, string>? Trace;

    /// <summary>AI state of a world (null until an AI army has thought once). For tools/tests/debug UI.</summary>
    public static AiState? StateOf(SimWorld w) => _states.TryGetValue(w, out var s) ? s : null;

    /// <summary>
    /// Lets every active AI army think and appends its commands to <paramref name="output"/> (cleared first).
    /// SEQUENTIAL. Call at the start of the tick, then apply output with CommandSystem.Run.
    /// </summary>
    public static void Run(SimWorld w, List<Command> output)
    {
        output.Clear();
        bool any = false;
        foreach (var a in w.Armies)
            if (a.IsAI && !a.Defeated) { any = true; break; }
        if (!any) return;

        var s = _states.GetValue(w, static ww => new AiState(ww));
        // AliveList is from the start of the previous tick: refresh it so the AI sees last tick's spawns
        // (the pipeline rebuilds it again right after the commands; this is a pure function of the unit store).
        w.Units.RebuildAliveList();
        for (int a = 0; a < w.Armies.Length; a++)
        {
            var army = w.Armies[a];
            if (!army.IsAI || army.Defeated) continue;
            var brain = s.Brain(a);
            long t0 = Stopwatch.GetTimestamp();
            brain.Update(output);
            brain.Stats.Record(Stopwatch.GetElapsedTime(t0).TotalMilliseconds); // timing only: never read by the AI
        }
    }
}

/// <summary>Per-world AI state: one brain per AI army plus per-unit-slot bookkeeping shared by all brains.</summary>
public sealed class AiState
{
    public readonly SimWorld World;
    private readonly AiBrain?[] _brains;
    private readonly AiUnitInfo[] _units;

    public AiState(SimWorld w)
    {
        World = w;
        _brains = new AiBrain?[w.Armies.Length];
        _units = new AiUnitInfo[w.Units.Capacity];
    }

    public AiBrain Brain(int army) => _brains[army] ??= new AiBrain(this, army);

    /// <summary>Brain of an army if it exists (it is created the first time the army thinks).</summary>
    public AiBrain? BrainOrNull(int army) => (uint)army < (uint)_brains.Length ? _brains[army] : null;

    /// <summary>AI bookkeeping of a unit slot; reset automatically when the slot holds a new unit.</summary>
    public ref AiUnitInfo Info(int unit)
    {
        ref var info = ref _units[unit];
        int gen = World.Units.Gen[unit];
        if (info.Gen != gen) info = new AiUnitInfo { Gen = gen, Deposit = -1, Group = -1 };
        return ref info;
    }
}

/// <summary>What the AI uses a unit for.</summary>
public enum AiRole : byte { None, Builder, Pool, Attack, Defend, Scout, AirDefense, Bomber, AirScout, Home, Factory, Transport, Naval, Silo }

/// <summary>Current job of a builder (engineer/commander).</summary>
public enum AiTask : byte { None, Build, Finish, Reclaim, Assist, Repair, Return }

/// <summary>Per-unit-slot AI bookkeeping (value type in one array, generation-checked).</summary>
public struct AiUnitInfo
{
    public int Gen;
    public AiRole Role;
    public AiTask Task;
    /// <summary>Tick the current task/role order was given.</summary>
    public int TaskTick;
    /// <summary>Tick after which the task counts as stuck and is reassigned.</summary>
    public int TaskDeadline;
    /// <summary>Deposit claimed by a mex/hydro build task (-1 = none).</summary>
    public int Deposit;
    /// <summary>Attack group id (-1 = none).</summary>
    public int Group;
    /// <summary>Factories: rally point given.</summary>
    public bool RallySet;
    /// <summary>Retreating/holding: the AI leaves the unit alone until this tick.</summary>
    public int HoldUntil;
}

/// <summary>Per-brain timing (wall clock; diagnostics only).</summary>
public sealed class AiStats
{
    public long Calls;
    [Persistence.NoSave] public double TotalMs; // wall clock: not saved
    [Persistence.NoSave] public double MaxMs;
    public int Commands;
    /// <summary>Wall-clock totals per phase (perception scan, economy, military).</summary>
    [Persistence.NoSave] public double ScanMs, EconMs, MilitaryMs;
    /// <summary>Placement searches, failed searches, candidate sites evaluated.</summary>
    public long SiteSearches, SiteFailures, SiteCandidates;

    public void Record(double ms)
    {
        Calls++;
        TotalMs += ms;
        if (ms > MaxMs) MaxMs = ms;
    }

    /// <summary>Average wall-clock milliseconds per tick.</summary>
    public double AvgMs => Calls > 0 ? TotalMs / Calls : 0;
}
