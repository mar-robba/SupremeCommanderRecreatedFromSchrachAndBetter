using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>(11a-1) Ships meeting head-on in a narrow channel pass each other (steer to their own right, the
/// lower-priority side yields) instead of pushing into a deadlock. ARCHITECTURE "Pathing: clearance, slope classes,
/// channels".</summary>
public class NavalChannelTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("uef_t2_destroyer", 5, 3)]
    [InlineData("uef_t1_frigate", 4, 4)]
    [InlineData("uef_t3_battleship", 9, 2)]
    [InlineData("uef_t2_destroyer", 8, 3)] // two lanes
    [InlineData("uef_t3_battleship", 14, 2)]
    public void GroupsMeetingHeadOnInAChannelPass(string ship, int width, int perGroup)
    {
        // Land at 5, a straight water channel (vertex rows g0..g0+width at 0) along x, open water pools at both ends.
        int g0 = 48 - width / 2;
        var sim = PathTestUtil.World(128, (x, z) => z >= g0 && z <= g0 + width || x < 24 || x > 104 ? 0f : 5f, db: PathTestUtil.NavalDb);
        var w = sim.World;
        var west = new List<UnitHandle>();
        var east = new List<UnitHandle>();
        for (int k = 0; k < perGroup; k++)
        {
            west.Add(PathTestUtil.Spawn(sim, ship, 0, new Vector2(10, 30 + 10 * k), 0));
            east.Add(PathTestUtil.Spawn(sim, ship, 0, new Vector2(118, 30 + 10 * k), MathF.PI));
        }
        var westDest = new Vector2(116, 48);
        var eastDest = new Vector2(12, 48);
        sim.Step([new IssueOrderCommand(0, west.ToArray(), Order.MoveTo(westDest), false),
                  new IssueOrderCommand(0, east.ToArray(), Order.MoveTo(eastDest), false)]);
        var all = west.Concat(east).ToArray();
        int t = 0;
        float minGap = float.MaxValue;
        for (; t < 3000; t++)
        {
            sim.Step();
            foreach (var a in west)
            foreach (var b in east)
                minGap = MathF.Min(minGap, Vector2.Distance(w.Units.Pos[a.Index], w.Units.Pos[b.Index]));
            if (all.All(h => w.Units.Orders[h.Index].IsEmpty && !w.Units.Move[h.Index].HasGoal)) break;
        }
        output.WriteLine($"{ship} x{perGroup} each way, channel {width}: all idle after {t} ticks, closest oncoming centers {minGap:F2}");
        foreach (var h in west) Assert.True(w.Units.Pos[h.Index].X > 100, $"{ship} {h.Index} (west group) stuck at {w.Units.Pos[h.Index]}");
        foreach (var h in east) Assert.True(w.Units.Pos[h.Index].X < 28, $"{ship} {h.Index} (east group) stuck at {w.Units.Pos[h.Index]}");
        Assert.True(t < 3000, "ships still moving after 300 s");
        // Before the rule oncoming hulls drove through each other (closest centers 0.03-0.86): now they keep apart.
        float r = w.Blueprints[ship].Radius;
        Assert.True(minGap > 0.9f * r, $"oncoming {ship}s overlapped: closest centers {minGap:F2}");
    }
}
