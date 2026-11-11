using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>Map props (wave 8b): generator placement, reclaim, fall and burn, save/load.</summary>
public class MapPropsTests
{
    private readonly ITestOutputHelper _out;
    public MapPropsTests(ITestOutputHelper output) => _out = output;

    private static Terrain Gen(int size, int players, MapPreset preset, int seed = 7) =>
        MapGenerator.Generate(new MapGenSettings { Size = size, Players = players, Seed = seed, Preset = preset });

    [Fact]
    public void PlacementIsDeterministicPerSeed()
    {
        var a = Gen(256, 2, MapPreset.Plains, 11).Props;
        var b = Gen(256, 2, MapPreset.Plains, 11).Props;
        var c = Gen(256, 2, MapPreset.Plains, 12).Props;
        Assert.True(a.Length > 0);
        Assert.Equal(a, b);
        Assert.NotEqual(a.Length == c.Length ? a : [], c);
    }

    [Theory]
    [InlineData(2, MapPreset.Plains)]
    [InlineData(4, MapPreset.Highlands)]
    [InlineData(3, MapPreset.Lakes)]
    [InlineData(6, MapPreset.Canyon)]
    public void PlacementIsSymmetric(int players, MapPreset preset)
    {
        var t = Gen(256, players, preset);
        var props = t.Props;
        var center = new Vector2(t.Width * 0.5f, t.Height * 0.5f);
        float step = MathF.Tau / players;
        Assert.Equal(0, props.Length % players);
        // Every prop has a copy of the same kind/size rotated by one player step.
        foreach (var p in props)
        {
            var d = p.Pos - center;
            var r = center + new Vector2(d.X * MathF.Cos(step) - d.Y * MathF.Sin(step), d.X * MathF.Sin(step) + d.Y * MathF.Cos(step));
            bool found = props.Any(q => q.Kind == p.Kind && q.Variant == p.Variant && MathF.Abs(q.Radius - p.Radius) < 1e-4f
                                        && Vector2.DistanceSquared(q.Pos, r) < 1e-3f);
            Assert.True(found, $"no rotated copy of {p.Kind} at {p.Pos}");
        }
    }

    [Theory]
    [InlineData(MapPreset.Plains)]
    [InlineData(MapPreset.Lakes)]
    [InlineData(MapPreset.Highlands)]
    [InlineData(MapPreset.Canyon)]
    [InlineData(MapPreset.Archipelago)]
    public void PropsAvoidStartsDepositsWaterAndSteepGround(MapPreset preset)
    {
        var t = Gen(256, 4, preset);
        Assert.Contains(t.Props, p => p.Kind == PropKind.Tree);
        Assert.Contains(t.Props, p => p.Kind == PropKind.Rock);
        foreach (var p in t.Props)
        {
            Assert.True((t.PassAt((int)p.Pos.X, (int)p.Pos.Y) & PassFlags.Land) != 0, $"{p.Kind} at {p.Pos} not on walkable land");
            Assert.True(t.GetHeight(p.Pos) > t.WaterLevel, $"{p.Kind} at {p.Pos} under water");
            foreach (var s in t.StartPositions) Assert.True(Vector2.Distance(s, p.Pos) > 25f, $"{p.Kind} at {p.Pos} on a start plateau");
            foreach (var d in t.Deposits) Assert.True(Vector2.Distance(d.Pos, p.Pos) > 3.4f, $"{p.Kind} at {p.Pos} on a deposit");
            Assert.True(p.Kind == PropKind.Tree ? p.Energy > p.Mass && p.Mass > 0 : p.Mass > 0 && p.Energy == 0);
        }
    }

    [Fact]
    public void DensityScalesWithMapSize()
    {
        int Count(int size) => Gen(size, 4, MapPreset.Plains).Props.Length;
        int c256 = Count(256), c512 = Count(512), c1024 = Count(1024);
        _out.WriteLine($"props 256={c256} 512={c512} 1024={c1024}");
        Assert.InRange(c256, 200, 2500);
        Assert.InRange(c1024, 3000, 30000);
        Assert.InRange(c1024 / (float)c256, 8f, 30f);
    }

    [Fact]
    public void SkirmishSpawnsTheMapProps()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        Assert.Equal(w.Terrain.Props.Length, w.Props.Count);
        Assert.True(w.Props.Capacity >= w.Terrain.Props.Length + 4096);
        int p = w.Props.AliveList[0];
        Assert.NotEqual(PropKind.Wreck, w.Props.Kind[p]);
        var snap = new Snapshot.SimSnapshot();
        sim.WriteSnapshot(snap);
        Assert.Equal(w.Props.Count, snap.PropCount);
        Assert.Equal(w.Props.Kind[p], snap.Props[0].Kind);
    }

    [Fact]
    public void GridQueriesMatchALinearScan()
    {
        var store = new PropStore(2000);
        store.SetBounds(200, 200);
        var rng = new DetRandom(5);
        var live = new List<int>();
        for (int k = 0; k < 1500; k++)
            live.Add(store.Spawn(new Vector2(rng.Range(0f, 200f), rng.Range(0f, 200f)), 0, -1, rng.Range(0.3f, 6f), 1, 0));
        store.FindNearest(Vector2.Zero, 1); // builds the grid; later spawns/frees update it
        for (int k = 0; k < 400; k++) { int j = rng.Range(0, live.Count); store.Free(live[j]); live.RemoveAt(j); }
        for (int k = 0; k < 300; k++) live.Add(store.Spawn(new Vector2(rng.Range(0f, 260f), rng.Range(0f, 200f)), 0, -1, 1, 1, 0));
        var got = new List<int>();
        for (int q = 0; q < 300; q++)
        {
            var pos = new Vector2(rng.Range(-10f, 270f), rng.Range(-10f, 210f));
            float r = rng.Range(0f, 30f);
            int best = -1; float bestD = float.MaxValue;
            var expect = new List<int>();
            foreach (int i in store.AliveList)
            {
                float d = Vector2.Distance(store.Pos[i], pos) - store.Radius[i];
                if (d <= r && d < bestD) { bestD = d; best = i; }
                if (Vector2.DistanceSquared(store.Pos[i], pos) <= r * r) expect.Add(i);
            }
            Assert.Equal(best, store.FindNearest(pos, r));
            got.Clear();
            store.QueryRadius(pos, r, got);
            Assert.Equal(expect, got);
        }
    }

    private static (Simulation sim, int tree) WithTree(Vector2 offset)
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        foreach (int p in w.Props.AliveList.ToArray()) w.Props.Free(p); // a clean area
        var pos = w.Units.Pos[w.Armies[0].Commander.Index] + offset;
        int tree = w.Props.SpawnNature(PropKind.Tree, pos, 0, 2, 0.5f, 0.4f, 10f);
        return (sim, tree);
    }

    [Fact]
    public void EngineerReclaimsTreesAndRocks()
    {
        var (sim, tree) = WithTree(new Vector2(6, 0));
        var w = sim.World;
        var cmd = w.Armies[0].Commander;
        int rock = w.Props.SpawnNature(PropKind.Rock, w.Props.Pos[tree] + new Vector2(0, 5), 0, 1, 1.5f, 12f, 0);
        w.Armies[0].Eco.Mass = 0;
        w.Armies[0].Eco.Energy = 0;
        sim.Step([new IssueOrderCommand(0, [cmd], Order.ReclaimAt(UnitHandle.None, w.Props.Pos[tree]), false)]);
        sim.Step([new IssueOrderCommand(0, [cmd], Order.ReclaimAt(UnitHandle.None, w.Props.Pos[rock]), true)]);
        TestUtil.Run(sim, 150);
        Assert.False(w.Props.Alive[tree]);
        Assert.False(w.Props.Alive[rock]);
        Assert.True(w.Armies[0].Eco.Mass > 12f, $"mass {w.Armies[0].Eco.Mass}");
    }

    [Fact]
    public void PatrollingEngineerReclaimsAForest()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        foreach (int p in w.Props.AliveList.ToArray()) w.Props.Free(p);
        var basePos = w.Units.Pos[w.Armies[0].Commander.Index] + new Vector2(10, 0);
        var eng = TestUtil.Spawn(sim, w.Blueprints.Factions.RoleBlueprint(w.Armies[0].Faction, FactionRoles.EngineerT1) is int e && e >= 0 ? w.Blueprints[e].Id : "uef_t1_engineer", 0, basePos);
        for (int k = 0; k < 6; k++) w.Props.SpawnNature(PropKind.Tree, basePos + new Vector2(4 + k * 1.3f, 2), 0, 0, 0.5f, 0.4f, 10f);
        sim.Step([new IssueOrderCommand(0, [eng], Order.PatrolTo(basePos + new Vector2(0, 6)), false)]);
        TestUtil.Run(sim, 600);
        Assert.Equal(0, w.Props.Count);
    }

    [Fact]
    public void UnitsWalkingOverATreeKnockItOver()
    {
        var (sim, tree) = WithTree(new Vector2(8, 0));
        var w = sim.World;
        var cmd = w.Armies[0].Commander;
        var target = w.Props.Pos[tree] + new Vector2(8, 0);
        TestUtil.Run(sim, 200, new IssueOrderCommand(0, [cmd], Order.MoveTo(target), false));
        Assert.True(w.Props.Alive[tree]);
        Assert.True((w.Props.Flags[tree] & PropFlags.Fallen) != 0, "tree still standing");
        Assert.True(MathF.Abs(DMath.AngleDiff(w.Props.FallHeading[tree], 0f)) < 0.8f, "falls along the walk direction");
        Assert.True(w.Props.StateTick[tree] > 0);
        Assert.Equal(10f, w.Props.Energy[tree], 3); // fallen trees keep their value
    }

    [Fact]
    public void AreaDamageBurnsTreesAndBurntTreesAreWorthLess()
    {
        var (sim, tree) = WithTree(new Vector2(12, 12));
        var w = sim.World;
        int far = w.Props.SpawnNature(PropKind.Tree, w.Props.Pos[tree] + new Vector2(30, 0), 0, 0, 0.5f, 0.4f, 10f);
        int rock = w.Props.SpawnNature(PropKind.Rock, w.Props.Pos[tree] + new Vector2(0, 1.5f), 0, 0, 1f, 5f, 0);
        w.Events.AreaDamage.Add(new AreaDamageEvent { Center = w.Props.Pos[tree], Radius = 3, Amount = 100, Source = UnitHandle.None, SourceArmy = 1 });
        sim.Step();
        Assert.True((w.Props.Flags[tree] & PropFlags.Burning) != 0);
        Assert.Equal(PropFlags.None, w.Props.Flags[far]);
        Assert.Equal(PropFlags.None, w.Props.Flags[rock]);
        Assert.Equal(PropFlags.None, w.Props.Flags[tree] & PropFlags.Fallen); // weak blast: stays up
        TestUtil.Run(sim, PropSystem.BurnTicks + PropSystem.BurnJitterTicks + 2);
        Assert.Equal(PropFlags.Burnt, w.Props.Flags[tree]);
        Assert.Equal(10f * PropSystem.BurntEnergyFactor, w.Props.Energy[tree], 3);
        Assert.Equal(0.4f * PropSystem.BurntMassFactor, w.Props.Mass[tree], 3);
        Assert.Empty(w.Props.Burning);

        // A big blast knocks trees over (away from the center) and sets them on fire.
        w.Events.AreaDamage.Add(new AreaDamageEvent { Center = w.Props.Pos[far] - new Vector2(2, 0), Radius = 4, Amount = 5000, Source = UnitHandle.None, SourceArmy = 1 });
        sim.Step();
        Assert.Equal(PropFlags.Fallen | PropFlags.Burning, w.Props.Flags[far]);
        Assert.True(MathF.Abs(DMath.AngleDiff(w.Props.FallHeading[far], 0f)) < 0.01f);
    }

    [Fact]
    public void NukeClearsTheForest()
    {
        var sim = TestUtil.NewSkirmish(seed: 3, mapSize: 512);
        var w = sim.World;
        var c = new Vector2(w.Terrain.Width * 0.5f, w.Terrain.Height * 0.5f);
        int before = 0;
        foreach (int p in w.Props.AliveList) if (Vector2.Distance(w.Props.Pos[p], c) < 60) before++;
        Assert.True(before > 0, "no props near the center");
        Strategic.StrategicApi.Detonate(w, 1, c, 20f, 50000f, 45f, 5000f);
        TestUtil.Run(sim, 4);
        int inner = 0, standing = 0;
        foreach (int p in w.Props.AliveList)
        {
            float d = Vector2.Distance(w.Props.Pos[p], c);
            if (d < 20) inner++;
            if (d < 45 && w.Props.Kind[p] == PropKind.Tree && (w.Props.Flags[p] & PropFlags.Fallen) == 0) standing++;
        }
        Assert.Equal(0, inner);
        Assert.Equal(0, standing);
    }

    [Fact]
    public void BurningForestSurvivesSaveLoad()
    {
        var a = TestUtil.NewSkirmish(seed: 5);
        var w = a.World;
        int k = 0;
        foreach (int p in w.Props.AliveList)
            if (w.Props.Kind[p] == PropKind.Tree && k++ % 5 == 0)
                w.Events.AreaDamage.Add(new AreaDamageEvent { Center = w.Props.Pos[p], Radius = 2, Amount = 100, Source = UnitHandle.None, SourceArmy = 1 });
        a.Step();
        Assert.NotEmpty(w.Props.Burning);
        TestUtil.Run(a, 70);
        var b = WorldState.Restore(WorldState.Capture(a), w.Blueprints, w.Terrain);
        for (int t = 0; t < 200; t++) { a.Step(); b.Step(); }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(a.World.Props.FindNearest(new Vector2(128, 128), 50), b.World.Props.FindNearest(new Vector2(128, 128), 50));
        Assert.True(WorldState.Capture(a).AsSpan().SequenceEqual(WorldState.Capture(b)), "serialized state differs");
    }
}
