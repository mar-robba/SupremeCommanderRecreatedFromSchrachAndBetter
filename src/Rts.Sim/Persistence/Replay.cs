using Rts.Sim.Orders;

namespace Rts.Sim.Persistence;

/// <summary>
/// Hooks the <see cref="SimRunner"/> calls around every tick (sim thread). Recording sees the sorted commands;
/// playback replaces them with the recorded ones (client commands are dropped: read-only).
/// </summary>
public interface ITickHooks
{
    IReadOnlyList<Command> BeforeStep(Simulation sim, List<Command> incoming);
    void AfterStep(Simulation sim);
    /// <summary>True = the runner does not step (e.g. end of a replay). Client commands still queue up.</summary>
    bool Hold(Simulation sim) => false;
}

/// <summary>
/// A replay: the world state at <see cref="InitialTick"/> + every tick's command list exactly as the sim consumed it
/// (AI commands are not stored: the AI runs inside the sim) + a StateHash every <see cref="HashInterval"/> ticks
/// (desync detection). Commands are graph-serialized, so new command types are recorded automatically.
/// </summary>
public sealed class ReplayLog
{
    public const int HashInterval = 10;

    public int InitialTick;
    public byte[] InitialState = [];
    /// <summary>Command log chunks; each is an independent graph stream (a new one starts after every load).</summary>
    public List<byte[]> Chunks = new();
    public List<int> HashTicks = new();
    public List<ulong> Hashes = new();
    /// <summary>Last tick simulated while recording (the replay ends there).</summary>
    public int LastTick;

    public void Write(ByteWriter o)
    {
        o.VarU(1); // log version
        o.VarU((ulong)InitialTick);
        o.Block(InitialState);
        o.VarU((ulong)Chunks.Count);
        foreach (var c in Chunks) o.Block(c);
        o.VarU((ulong)Hashes.Count);
        int prev = 0;
        for (int i = 0; i < Hashes.Count; i++)
        {
            o.VarU((ulong)(HashTicks[i] - prev));
            prev = HashTicks[i];
            o.Raw(Hashes[i]);
        }
        o.VarU((ulong)LastTick);
    }

    public static ReplayLog Read(ByteReader r)
    {
        if (r.VarU() != 1) throw new IncompatibleSaveException("Unknown replay log version");
        var log = new ReplayLog { InitialTick = r.Count(int.MaxValue), InitialState = r.Block() };
        int chunks = r.Count(r.Remaining);
        for (int i = 0; i < chunks; i++) log.Chunks.Add(r.Block());
        int n = r.Count(r.Remaining / 9);
        int tick = 0;
        for (int i = 0; i < n; i++)
        {
            tick += r.Count(int.MaxValue);
            log.HashTicks.Add(tick);
            log.Hashes.Add(r.Raw<ulong>());
        }
        log.LastTick = r.Count(int.MaxValue);
        return log;
    }

    /// <summary>All recorded commands by tick.</summary>
    public Dictionary<int, Command[]> DecodeCommands()
    {
        var map = new Dictionary<int, Command[]>();
        foreach (var chunk in Chunks)
        {
            var g = new GraphReader(new ByteReader(chunk));
            while (!g.In.AtEnd)
            {
                int tick = g.In.Count(int.MaxValue);
                int n = g.In.Count(g.In.Remaining);
                var cmds = new Command[n];
                for (int i = 0; i < n; i++)
                    cmds[i] = g.ReadRef() as Command ?? throw ByteReader.Corrupt("not a command");
                g.ResetObjects();
                map[tick] = map.TryGetValue(tick, out var prev) ? [.. prev, .. cmds] : cmds;
            }
        }
        return map;
    }

    public int CommandCount => DecodeCommands().Values.Sum(c => c.Length);
}

/// <summary>
/// Records a replay while a game runs (attach as <see cref="SimRunner.Hooks"/>). Cost per tick: nothing unless
/// commands were issued, plus one StateHash every 10 ticks. Sim thread only (or with the runner stopped).
/// </summary>
public sealed class ReplayRecorder : ITickHooks
{
    private readonly ReplayLog _log;
    private ByteWriter? _chunk;
    private GraphWriter? _writer;

    /// <param name="continueFrom">Log loaded with a save game: recording continues it (full replay from the start).</param>
    public ReplayRecorder(ReplayLog? continueFrom = null) => _log = continueFrom ?? new ReplayLog();

    public bool Started => _log.InitialState.Length > 0;
    public int LastTick => _log.LastTick;

    public IReadOnlyList<Command> BeforeStep(Simulation sim, List<Command> incoming)
    {
        if (!Started)
        {
            _log.InitialTick = sim.World.Tick;
            _log.InitialState = WorldState.Capture(sim);
            _log.LastTick = sim.World.Tick;
        }
        if (incoming.Count == 0) return incoming;
        if (_writer == null)
        {
            _chunk = new ByteWriter(1 << 12);
            _writer = new GraphWriter(_chunk);
        }
        _chunk!.VarU((ulong)(sim.World.Tick + 1));
        _chunk.VarU((ulong)incoming.Count);
        foreach (var c in incoming) _writer.WriteRef(c);
        _writer.ResetObjects();
        return incoming;
    }

    public void AfterStep(Simulation sim)
    {
        int t = sim.World.Tick;
        _log.LastTick = t;
        if (t % ReplayLog.HashInterval == 0)
        {
            _log.HashTicks.Add(t);
            _log.Hashes.Add(sim.StateHash());
        }
    }

    /// <summary>Copy of the log so far (the recorder keeps going). Sim thread / runner stopped only.</summary>
    public ReplayLog Snapshot()
    {
        var copy = new ReplayLog
        {
            InitialTick = _log.InitialTick, InitialState = _log.InitialState, LastTick = _log.LastTick,
            Chunks = new List<byte[]>(_log.Chunks), HashTicks = new List<int>(_log.HashTicks), Hashes = new List<ulong>(_log.Hashes),
        };
        if (_chunk != null && _chunk.Length > 0) copy.Chunks.Add(_chunk.ToArray());
        return copy;
    }
}
