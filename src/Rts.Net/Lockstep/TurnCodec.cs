using Rts.Sim.Orders;
using Rts.Sim.Persistence;

namespace Rts.Net.Lockstep;

/// <summary>
/// Turn payloads: [varu count] + (count > 0) one self-contained graph stream with the commands, written by the same
/// generic serializer as the replay command log (Rts.Sim/Persistence), so every Command type - including ones
/// other features add later - goes over the wire without protocol changes. An empty turn is a single 0 byte.
/// </summary>
public static class TurnCodec
{
    public static readonly byte[] Empty = [0];

    public static byte[] Encode(IReadOnlyList<Command> commands)
    {
        if (commands.Count == 0) return Empty;
        var w = new ByteWriter(256);
        w.VarU((ulong)commands.Count);
        var g = new GraphWriter(w);
        foreach (var c in commands) g.WriteRef(c);
        g.Flush();
        return w.ToArray();
    }

    /// <summary>Decodes a turn (throws <see cref="SaveFormatException"/>/<see cref="IncompatibleSaveException"/> on bad data).</summary>
    public static Command[] Decode(byte[] payload)
    {
        var r = new ByteReader(payload);
        int n = r.Count(10_000);
        if (n == 0) return [];
        var g = new GraphReader(r);
        var cmds = new Command[n];
        for (int i = 0; i < n; i++)
            cmds[i] = g.ReadRef() as Command ?? throw ByteReader.Corrupt("not a command");
        g.Complete();
        return cmds;
    }
}

/// <summary>
/// Stateful turn encoder of one player: the graph type table persists across turns (each command type's name and
/// field layout goes over the wire once per game, not once per turn). Turns must be decoded in order by a
/// <see cref="TurnDecoder"/> per sender - guaranteed: every peer gets every player's turns in tick order.
/// </summary>
public sealed class TurnEncoder
{
    private readonly ByteWriter _w = new(512);
    private readonly GraphWriter _g;

    public TurnEncoder() => _g = new GraphWriter(_w);

    public byte[] Encode(IReadOnlyList<Command> commands)
    {
        if (commands.Count == 0) return TurnCodec.Empty;
        _w.Clear();
        _w.VarU((ulong)commands.Count);
        foreach (var c in commands) _g.WriteRef(c);
        _g.ResetObjects();
        return _w.ToArray();
    }
}

/// <summary>Decodes one sender's <see cref="TurnEncoder"/> stream (turns in order). Not thread-safe.</summary>
public sealed class TurnDecoder
{
    private GraphReader? _g;

    public Command[] Decode(byte[] payload)
    {
        var r = new ByteReader(payload);
        int n = r.Count(10_000);
        if (n == 0) return [];
        if (_g == null) _g = new GraphReader(r);
        else _g.ContinueWith(r);
        var cmds = new Command[n];
        for (int i = 0; i < n; i++)
            cmds[i] = _g.ReadRef() as Command ?? throw ByteReader.Corrupt("not a command");
        _g.ResetObjects();
        if (!r.AtEnd) throw ByteReader.Corrupt("turn has trailing bytes");
        return cmds;
    }
}
