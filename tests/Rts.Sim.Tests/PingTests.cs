using System.Numerics;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.Snapshot;

namespace Rts.Sim.Tests;

/// <summary>(7b) Map pings (Orders/PingCommands.cs): team visibility, throttle, expiry, save/load, replays.</summary>
public class PingTests
{
    private static readonly SkirmishSettings Settings3 = new() { Seed = 4, MapSize = 256, Players = 3, Teams = [0, 0, 1], AiPlayers = new bool[3] };

    private static Simulation New3() => GameSetup.CreateSkirmish(TestUtil.Db, Settings3);

    private static SimSnapshot Snap(Simulation sim, int view = -1)
    {
        var s = new SimSnapshot();
        sim.WriteSnapshot(s, view);
        return s;
    }

    [Fact]
    public void PingIsVisibleToTheArmyAndItsAllyOnly()
    {
        var sim = New3();
        Assert.Equal(0, Snap(sim).PingCount); // no ping: no state
        Assert.False(PingState.TryGet(sim.World, out _));
        sim.Step([new MapPingCommand(0, PingKind.Attack, new Vector2(120, 130))]);
        Assert.Equal(1, Snap(sim).PingCount);
        Assert.Equal(1, Snap(sim, 0).PingCount);
        var ally = Snap(sim, 1);
        Assert.Equal(1, ally.PingCount);
        Assert.Equal(PingKind.Attack, ally.Pings[0].Kind);
        Assert.Equal(0, ally.Pings[0].Army);
        Assert.Equal(120, ally.Pings[0].Pos.X, 3);
        Assert.Equal(1, ally.PingSerial);
        Assert.Equal(0, Snap(sim, 2).PingCount);
    }

    [Fact]
    public void PingsAreThrottledAndExpire()
    {
        var sim = New3();
        var p = new Vector2(100, 100);
        sim.Step([.. Enumerable.Range(0, 5).Select(_ => (Command)new MapPingCommand(0, PingKind.Alert, p)), new MapPingCommand(1, PingKind.Move, p)]);
        Assert.Equal(4, Snap(sim).PingCount); // 3 of army 0 + 1 of army 1
        TestUtil.Run(sim, 30, new MapPingCommand(0, PingKind.Alert, p)); // still throttled
        Assert.Equal(4, Snap(sim).PingCount);
        TestUtil.Run(sim, 10, new MapPingCommand(0, PingKind.Alert, p)); // 31 ticks later: accepted
        Assert.Equal(5, Snap(sim).PingCount);
        TestUtil.Run(sim, 61);
        Assert.Equal(1, Snap(sim).PingCount); // the first four lived 100 ticks
        TestUtil.Run(sim, 40);
        Assert.Equal(0, Snap(sim).PingCount);
        Assert.Equal(5, Snap(sim).PingSerial);
    }

    [Fact]
    public void SaveLoadKeepsLivePings()
    {
        var sim = New3();
        TestUtil.Run(sim, 10, new MapPingCommand(1, PingKind.Move, new Vector2(80, 90)));
        var info = GameInfo.ForSkirmish(Settings3, sim, 0, false);
        var loaded = SaveGame.Decode(SaveGame.Encode(SaveGame.Capture(sim, info, null, "x")), TestUtil.Db).Sim;
        var a = Snap(sim, 0); var b = Snap(loaded, 0);
        Assert.Equal(1, b.PingCount);
        Assert.Equal(a.Pings[0].Id, b.Pings[0].Id);
        Assert.Equal(a.Pings[0].Tick, b.Pings[0].Tick);
        Assert.Equal(a.Pings[0].Pos, b.Pings[0].Pos);
        TestUtil.Run(sim, 20); TestUtil.Run(loaded, 20);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void ReplayRecreatesThePing()
    {
        var sim = New3();
        var info = GameInfo.ForSkirmish(Settings3, sim, 0, false);
        var rec = new ReplayRecorder();
        var runner = new SimRunner(sim) { Hooks = rec };
        var units = new[] { TestUtil.Spawn(sim, "uef_t1_tank", 0, new Vector2(60, 60)) };
        while (sim.World.Tick < 60)
        {
            if (sim.World.Tick == 4) runner.Enqueue(new MapPingCommand(0, PingKind.Alert, new Vector2(70, 75)));
            if (sim.World.Tick == 5) runner.Enqueue(new IssueOrderCommand(0, units, Order.MoveTo(new Vector2(90, 60)), false));
            if (sim.World.Tick == 20) runner.Enqueue(new MoveWaypointCommand(0, units, new Vector2(90, 60), new Vector2(80, 80)));
            runner.RunOneTick();
        }
        var expected = Snap(sim, 1);
        var player = SaveGame.OpenReplay(SaveGame.EncodeReplay(info, rec.Snapshot()), TestUtil.Db);
        Assert.True(player.RunToEnd(), $"desync at tick {player.DesyncTick}");
        var got = Snap(player.Sim, 1);
        Assert.Equal(1, got.PingCount);
        Assert.Equal(expected.Pings[0].Tick, got.Pings[0].Tick);
        Assert.Equal(expected.Pings[0].Pos, got.Pings[0].Pos);
        Assert.Equal(sim.StateHash(), player.Sim.StateHash());
    }
}
