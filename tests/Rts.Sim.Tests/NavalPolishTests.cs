using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Naval;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Naval polish: ship-to-ship separation, submarine dive/surface.</summary>
public class NavalPolishTests
{
    private static float MinPairRatio(Simulation sim, IReadOnlyList<UnitHandle> ships, out string worst)
    {
        var u = sim.World.Units;
        float best = float.MaxValue;
        worst = "";
        for (int a = 0; a < ships.Count; a++)
        for (int b = a + 1; b < ships.Count; b++)
        {
            int i = ships[a].Index, j = ships[b].Index;
            float d = Vector2.Distance(u.Pos[i], u.Pos[j]);
            float ratio = d / (sim.World.BpOf(i).Radius + sim.World.BpOf(j).Radius);
            if (ratio < best) { best = ratio; worst = $"{a}-{b} d={d:F2}"; }
        }
        return best;
    }

    /// <summary>Smallest hull clearance / (wi + wj) over all pairs (capsules from NavalMotion.Hull; < 0 = overlap).</summary>
    private static float MinHullClearance(Simulation sim, IReadOnlyList<UnitHandle> ships, out string worst)
    {
        var u = sim.World.Units;
        float best = float.MaxValue;
        worst = "";
        for (int a = 0; a < ships.Count; a++)
        for (int b = a + 1; b < ships.Count; b++)
        {
            int i = ships[a].Index, j = ships[b].Index;
            NavalMotion.Hull(sim.World.BpOf(i), out float si, out float wi);
            NavalMotion.Hull(sim.World.BpOf(j), out float sj, out float wj);
            var di = DMath.Dir(u.Heading[i]) * si;
            var dj = DMath.Dir(u.Heading[j]) * sj;
            NavalMotion.ClosestPoints(u.Pos[i] - di, u.Pos[i] + di, u.Pos[j] - dj, u.Pos[j] + dj, out var ci, out var cj);
            float c = (Vector2.Distance(ci, cj) - (wi + wj)) / (wi + wj);
            if (c < best) { best = c; worst = $"{a}-{b} hull distance {Vector2.Distance(ci, cj):F2}"; }
        }
        return best;
    }

    // ------------------------------------------------------------------ separation

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShipsOrderedToOnePointDoNotOverlap(bool groupOrder)
    {
        var sim = NavalWorld.Basin();
        var ships = new List<UnitHandle>();
        for (int k = 0; k < 10; k++)
        {
            string bp = k % 2 == 0 ? "uef_t1_frigate" : "uef_t2_destroyer";
            ships.Add(NavalWorld.Spawn(sim, bp, 0, 80 + (k % 5) * 6, 30 + (k / 5) * 8));
        }
        // Either one order for the whole fleet, or every ship its own order to the same point (no formation spread).
        var cmds = groupOrder
            ? [new IssueOrderCommand(0, ships.ToArray(), Order.MoveTo(new Vector2(100, 90)), false)]
            : ships.Select(h => (Command)new IssueOrderCommand(0, [h], Order.MoveTo(new Vector2(100, 90)), false)).ToArray();
        sim.Step(cmds);
        for (int t = 0; t < 600; t++) sim.Step();
        float ratio = MinPairRatio(sim, ships, out string worst);
        Assert.True(ratio >= 0.8f, $"closest pair {worst}: {ratio:F2} x (ri + rj)");
        float hull = MinHullClearance(sim, ships, out worst);
        Assert.True(hull >= -0.2f, $"hulls overlap: {worst} ({hull:F2})");
        // They all got there (crowded around the point).
        foreach (var h in ships)
            Assert.True(Vector2.Distance(sim.World.Units.Pos[h.Index], new Vector2(100, 90)) < 16f);
    }

    [Fact]
    public void EnemyShipsSeparateToo()
    {
        var sim = NavalWorld.Basin();
        var a = NavalWorld.Spawn(sim, "uef_t1_frigate", 0, 100, 60);
        var b = NavalWorld.Spawn(sim, "uef_t1_frigate", 1, 100.3f, 60.2f);
        for (int t = 0; t < 60; t++) sim.Step();
        float ratio = MinPairRatio(sim, [a, b], out string worst);
        Assert.True(ratio >= 0.8f, $"{worst}: {ratio:F2}");
    }

    [Fact]
    public void SubmergedSubCanSitUnderAShip()
    {
        var sim = NavalWorld.Basin();
        var ship = NavalWorld.Spawn(sim, "uef_t2_destroyer", 0, 100, 60);
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 100.2f, 60.1f);
        for (int t = 0; t < 60; t++) sim.Step();
        var u = sim.World.Units;
        Assert.True(Vector2.Distance(u.Pos[ship.Index], u.Pos[sub.Index]) < 0.5f, "ship and submerged sub don't collide");
    }

    [Fact]
    public void SubmergedSubsSeparateFromEachOther()
    {
        var sim = NavalWorld.Basin();
        var a = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 100, 60);
        var b = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 100.2f, 60.1f);
        for (int t = 0; t < 60; t++) sim.Step();
        float ratio = MinPairRatio(sim, [a, b], out string worst);
        Assert.True(ratio >= 0.8f, $"{worst}: {ratio:F2}");
    }

    [Fact]
    public void SurfacedSubIsPushedApartFromAShip()
    {
        var sim = NavalWorld.Basin();
        var ship = NavalWorld.Spawn(sim, "uef_t2_destroyer", 0, 100, 60);
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 100.2f, 60.1f);
        sim.Step([new SetSubmergedCommand(0, [sub], false)]);
        for (int t = 0; t < 60; t++) sim.Step();
        float ratio = MinPairRatio(sim, [ship, sub], out string worst);
        Assert.True(ratio >= 0.8f, $"{worst}: {ratio:F2}");
    }
}
