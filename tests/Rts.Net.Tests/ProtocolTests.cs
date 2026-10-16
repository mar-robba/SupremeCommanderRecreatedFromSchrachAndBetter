using System.Numerics;
using Rts.Net.Lobby;
using Rts.Net.Lockstep;
using Rts.Net.Session;
using Rts.Net.Wire;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Net.Tests;

public class ProtocolTests
{
    [Fact]
    public void FrameDecoderReassemblesPartialAndMergedFrames()
    {
        var frames = new[]
        {
            Frame.Make(MsgType.Chat, "hello"u8),
            Frame.Make(MsgType.Go, ReadOnlySpan<byte>.Empty),
            Frame.Make(MsgType.Turn, new byte[10000]),
        };
        var stream = frames.SelectMany(f => f).ToArray();
        foreach (int chunk in new[] { 1, 3, 7, 4096, stream.Length })
        {
            var dec = new FrameDecoder();
            var got = new List<byte[]>();
            for (int off = 0; off < stream.Length; off += chunk)
                dec.Push(stream.AsSpan(off, Math.Min(chunk, stream.Length - off)), got);
            Assert.Equal(3, got.Count);
            Assert.Equal((byte)MsgType.Chat, got[0][0]);
            Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(got[0], 1, 5));
            Assert.Single(got[1]);
            Assert.Equal(10001, got[2].Length);
            Assert.Equal(0, dec.Buffered);
        }
    }

    [Fact]
    public void FrameDecoderRejectsBadLengths()
    {
        var dec = new FrameDecoder();
        var got = new List<byte[]>();
        Assert.Throws<ProtocolException>(() => dec.Push(new byte[] { 0, 0, 0, 0 }, got));
        Assert.Throws<ProtocolException>(() => new FrameDecoder().Push(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 1 }, got));
    }

    [Fact]
    public void TurnCodecRoundTripsCommands()
    {
        var cmds = new List<Command>
        {
            new IssueOrderCommand(1, [new UnitHandle(3, 2), new UnitHandle(9, 1)], Order.MoveTo(new Vector2(10.5f, 20.25f)), true),
            new FactoryEnqueueCommand(1, [new UnitHandle(4, 1)], 17, 3),
            new PlayerLeftCommand(1, DropAction.Defeat),
        };
        var back = TurnCodec.Decode(TurnCodec.Encode(cmds));
        Assert.Equal(3, back.Length);
        var io = Assert.IsType<IssueOrderCommand>(back[0]);
        Assert.Equal(new UnitHandle(9, 1), io.Units[1]);
        Assert.Equal(new Vector2(10.5f, 20.25f), io.Order.Pos);
        Assert.True(io.Queue);
        Assert.Equal(17, Assert.IsType<FactoryEnqueueCommand>(back[1]).BlueprintIndex);
        Assert.Equal(DropAction.Defeat, Assert.IsType<PlayerLeftCommand>(back[2]).Action);
        Assert.Empty(TurnCodec.Decode(TurnCodec.Empty));
        Assert.ThrowsAny<Exception>(() => TurnCodec.Decode([5, 1, 2]));
    }

    [Fact]
    public void StatefulTurnStreamSendsTypesOnce()
    {
        var enc = new TurnEncoder();
        var dec = new TurnDecoder();
        Command Make(int i) => new IssueOrderCommand(2, [new UnitHandle(i, 1)], Order.MoveTo(new Vector2(i, i)), false);
        var first = enc.Encode([Make(1)]);
        var empty = enc.Encode([]);
        var second = enc.Encode([Make(2), Make(3)]);
        Assert.Same(TurnCodec.Empty, empty);
        Assert.True(second.Length < first.Length, $"type table repeated: {first.Length} then {second.Length} bytes");
        Assert.Equal(new UnitHandle(1, 1), Assert.IsType<IssueOrderCommand>(Assert.Single(dec.Decode(first))).Units[0]);
        Assert.Empty(dec.Decode(empty));
        var two = dec.Decode(second);
        Assert.Equal(new Vector2(3, 3), Assert.IsType<IssueOrderCommand>(two[1]).Order.Pos);
    }

    [Fact]
    public void LobbyAndGameStartRoundTrip()
    {
        var l = LobbyState.CreateDefault("Host");
        l.Slots[1].Kind = NetSlotKind.AI;
        l.Slots[1].Difficulty = AiDifficulty.Hard;
        l.Slots[2].Kind = NetSlotKind.Human;
        l.Slots[2].PlayerId = 5;
        l.Slots[2].Name = "Bob";
        l.Slots[2].Team = 2;
        l.Slots[2].Faction = "cybran";
        l.Options.MapSize = 512;
        l.Options.OnDrop = DropAction.Defeat;
        var g = GameStart.From(l, 3);
        var w = new ByteWriter(256);
        g.Write(w);
        var back = GameStart.Read(new ByteReader(w.ToArray()));
        Assert.Equal(3, back.Delay);
        Assert.Equal([0, 1, 2], back.ArmySlots);
        Assert.Equal([0, 5], back.TurnPlayers);
        Assert.Equal([0, 2], back.TurnArmies);
        Assert.Equal("Bob", back.Lobby.Slots[2].Name);
        Assert.Equal("cybran", back.Lobby.Slots[2].Faction);
        Assert.Equal(AiDifficulty.Hard, back.Lobby.Slots[1].Difficulty);
        Assert.Equal(512, back.Lobby.Options.MapSize);
        Assert.Equal(DropAction.Defeat, back.Lobby.Options.OnDrop);
        var sk = NetGameSetup.ToSkirmish(back);
        Assert.Equal([false, true, false], sk.AiPlayers);
        Assert.Equal(2, sk.Teams![2]);
        Assert.Equal("Bob", sk.Names![2]);
    }

    [Theory]
    [InlineData("127.0.0.1:5000", "127.0.0.1", 5000)]
    [InlineData("example.org", "example.org", NetSession.DefaultPort)]
    [InlineData("[::1]:6000", "::1", 6000)]
    [InlineData("::1", "::1", NetSession.DefaultPort)]
    public void AddressParsing(string text, string host, int port)
    {
        Assert.True(NetAddress.TryParse(text, out var h, out var p));
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }

    [Fact]
    public void DelayFollowsRoundTrip()
    {
        Assert.Equal(1, HostSession.DelayFor(1, 1));
        Assert.Equal(2, HostSession.DelayFor(120, 1));
        Assert.True(HostSession.DelayFor(120, 4) > HostSession.DelayFor(120, 1));
        Assert.Equal(LockstepHooks.MaxDelay, HostSession.DelayFor(100000, 1));
    }
}
