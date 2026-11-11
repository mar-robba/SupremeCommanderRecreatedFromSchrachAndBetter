using System.Numerics;
using Rts.Net.Headless;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using static Rts.Net.Tests.NetTestUtil;

namespace Rts.Net.Tests;

/// <summary>(7b) Map pings and waypoint edits travel through lockstep like any command.</summary>
public class PingNetTests
{
    private static UnitHandle[] UnitsOf(HeadlessPeer p, int army)
    {
        var u = p.Sim.World.Units;
        var list = new List<UnitHandle>();
        for (int k = 0; k < u.AliveCount; k++)
            if (u.Army[u.AliveList[k]] == army) list.Add(u.HandleOf(u.AliveList[k]));
        return list.ToArray();
    }

    [Fact]
    public void PingAndWaypointEditShowUpOnBothPeers()
    {
        var (host, clients) = MakeLobby(2, 0);
        var peers = Start(host, clients);
        try
        {
            var a = new Vector2(100, 100); var b = new Vector2(120, 90);
            void PerTick(HeadlessPeer p, int tick)
            {
                if (p != peers[0]) return;
                int army = p.Game.LocalArmy;
                if (tick == 10) p.Runner.Enqueue(new IssueOrderCommand(army, UnitsOf(p, army), Order.MoveTo(a), false));
                if (tick == 30) p.Runner.Enqueue(new MoveWaypointCommand(army, UnitsOf(p, army), a, b));
                if (tick == 40) p.Runner.Enqueue(new MapPingCommand(army, PingKind.Attack, new Vector2(110, 115)));
            }
            foreach (var p in peers) p.Run(120, 0, PerTick);
            foreach (var p in peers) Assert.True(p.Wait(60000), $"{p.Session.LocalName} did not finish");
            foreach (var p in peers) Assert.Null(p.Fault);

            int army0 = peers[0].Game.LocalArmy;
            var snaps = peers.Select(p => { var s = new SimSnapshot(); p.Sim.WriteSnapshot(s); return s; }).ToList();
            foreach (var s in snaps)
            {
                Assert.Equal(1, s.PingCount);
                Assert.Equal(army0, s.Pings[0].Army);
                Assert.Equal(PingKind.Attack, s.Pings[0].Kind);
            }
            Assert.Equal(snaps[0].Pings[0].Tick, snaps[1].Pings[0].Tick);
            foreach (var p in peers)
            {
                var q = p.Sim.World.Units.Orders[UnitsOf(p, army0)[0].Index];
                Assert.Equal(b, q.Current.Pos);
            }
            Assert.Equal(peers[0].Sim.StateHash(), peers[1].Sim.StateHash());
            Assert.Equal(peers[0].Hashes.Count, peers[1].Hashes.Count);
            for (int i = 0; i < peers[0].Hashes.Count; i++) Assert.Equal(peers[0].Hashes[i], peers[1].Hashes[i]);
            Assert.All(peers, p => Assert.Equal(-1, p.Game.DesyncTick));
        }
        finally { Close(host, clients, peers); }
    }
}
