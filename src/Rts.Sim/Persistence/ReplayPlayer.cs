using System.IO.Compression;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;

namespace Rts.Sim.Persistence;

/// <summary>
/// Re-simulates a replay: restores the initial state and steps it with the recorded commands, comparing the
/// StateHash every 10 ticks (<see cref="DesyncTick"/>). Headless (<see cref="StepOne"/>, <see cref="SeekTo"/>) or
/// driven by a <see cref="SimRunner"/> (set it as <see cref="SimRunner.Hooks"/>; client commands are ignored).
/// Backward seeks restore the nearest in-memory checkpoint (a compressed world state taken every
/// <see cref="CheckpointInterval"/> ticks while playing) and fast-forward from there.
/// </summary>
public sealed class ReplayPlayer : ITickHooks
{
    public readonly GameInfo Info;
    public readonly ReplayLog Log;
    private readonly BlueprintDb _db;
    private readonly Terrain _terrain;
    private readonly Dictionary<int, Command[]> _commands;
    private readonly Dictionary<int, ulong> _hashes = new();
    private readonly SortedList<int, byte[]> _checkpoints = new();

    public Simulation Sim { get; private set; }
    public int StartTick => Log.InitialTick;
    public int EndTick => Log.LastTick;
    public int Tick => Sim.World.Tick;
    public bool AtEnd => Sim.World.Tick >= EndTick;
    /// <summary>First tick whose StateHash differs from the recording (-1 = none so far). Written on the stepping thread.</summary>
    public volatile int DesyncTick = -1;
    /// <summary>Number of hash checks passed.</summary>
    public int HashesVerified { get; private set; }
    /// <summary>Ticks between in-memory checkpoints (0 = none). 600 = one minute of game time.</summary>
    public int CheckpointInterval = 600;
    /// <summary>True if the replay was recorded with a different simulation build (it may desync).</summary>
    public bool DifferentBuild => Info.SimBuild != GameInfo.CurrentSimBuild;

    public ReplayPlayer(GameInfo info, ReplayLog log, BlueprintDb db, Terrain terrain)
    {
        Info = info;
        Log = log;
        _db = db;
        _terrain = terrain;
        _commands = log.DecodeCommands();
        for (int i = 0; i < log.Hashes.Count; i++) _hashes[log.HashTicks[i]] = log.Hashes[i];
        Sim = WorldState.Restore(log.InitialState, db, terrain);
    }

    public IReadOnlyList<Command> CommandsFor(int tick) => _commands.TryGetValue(tick, out var c) ? c : Array.Empty<Command>();

    public IReadOnlyList<Command> BeforeStep(Simulation sim, List<Command> incoming) => CommandsFor(sim.World.Tick + 1);

    public bool Hold(Simulation sim) => sim.World.Tick >= EndTick;

    public void AfterStep(Simulation sim)
    {
        int t = sim.World.Tick;
        if (_hashes.TryGetValue(t, out ulong expected))
        {
            if (sim.StateHash() == expected) HashesVerified++;
            else if (DesyncTick < 0) DesyncTick = t;
        }
        if (CheckpointInterval > 0 && t % CheckpointInterval == 0 && !_checkpoints.ContainsKey(t) && t < EndTick)
            _checkpoints[t] = Compress(WorldState.Capture(sim));
    }

    /// <summary>Steps one tick headless (does nothing at the end of the replay).</summary>
    public void StepOne()
    {
        if (AtEnd) return;
        var cmds = CommandsFor(Sim.World.Tick + 1);
        Sim.Step(cmds);
        Sim.World.Events.Effects.Clear(); // cosmetic, normally drained by the snapshot writer
        AfterStep(Sim);
    }

    /// <summary>Runs to the end; returns true if no desync was found.</summary>
    public bool RunToEnd()
    {
        while (!AtEnd) StepOne();
        return DesyncTick < 0;
    }

    /// <summary>Moves to <paramref name="tick"/> (clamped to the replay). Backward: restores the nearest checkpoint (or
    /// the initial state) and re-simulates. <see cref="Sim"/> may be a NEW object afterwards. Not while it is stepped
    /// by a running SimRunner (use a runner job).</summary>
    public void SeekTo(int tick)
    {
        tick = Math.Clamp(tick, StartTick, EndTick);
        if (tick < Sim.World.Tick)
        {
            int best = -1;
            foreach (var k in _checkpoints.Keys) if (k <= tick) best = k;
            Sim = best >= 0
                ? WorldState.Restore(Decompress(_checkpoints[best]), _db, _terrain)
                : WorldState.Restore(Log.InitialState, _db, _terrain);
        }
        while (Sim.World.Tick < tick) StepOne();
    }

    private static byte[] Compress(byte[] raw)
    {
        var ms = new MemoryStream(raw.Length / 16 + 1024);
        ms.Write(BitConverter.GetBytes(raw.Length));
        using (var z = new BrotliStream(ms, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw);
        return ms.ToArray();
    }

    private static byte[] Decompress(byte[] packed)
    {
        var raw = new byte[BitConverter.ToInt32(packed, 0)];
        using var z = new BrotliStream(new MemoryStream(packed, 4, packed.Length - 4), CompressionMode.Decompress);
        z.ReadExactly(raw);
        return raw;
    }
}
