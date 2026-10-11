using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

public class MapGeneratorTests
{
    private static readonly MapPreset[] LandPresets = [MapPreset.Plains, MapPreset.Lakes, MapPreset.Highlands, MapPreset.Canyon];

    private static Terrain Gen(MapPreset preset, int size, int players, int seed, float water = -1) =>
        MapGenerator.Generate(new MapGenSettings { Preset = preset, Size = size, Players = players, Seed = seed, WaterAmount = water });

    public static IEnumerable<object[]> LandCases()
    {
        foreach (var p in LandPresets)
        {
            foreach (int players in new[] { 2, 3, 4, 6, 8 })
            foreach (int seed in new[] { 1, 7 })
                yield return [p, 256, players, seed];
            yield return [p, 512, 2, 3];
            yield return [p, 512, 4, 11];
        }
    }

    [Theory]
    [MemberData(nameof(LandCases))]
    public void LandPresetsConnectEveryStartByLand(MapPreset preset, int size, int players, int seed)
    {
        var t = Gen(preset, size, players, seed);
        Assert.Equal(players, t.StartPositions.Length);
        Assert.True(MapGenerator.StartsConnectedByLand(t), $"{preset} {size} p{players} seed {seed}: starts not connected by land");
        // Every start reaches every other one (not only from start 0).
        foreach (var from in t.StartPositions)
        {
            var seen = MapGenerator.FloodLand(t, from);
            foreach (var to in t.StartPositions)
                Assert.True(seen[(int)to.Y * t.Width + (int)to.X], $"{preset}: {from} cannot reach {to}");
        }
    }

    [Theory]
    [InlineData(MapPreset.Lakes, 0f)]
    [InlineData(MapPreset.Lakes, 1f)]
    [InlineData(MapPreset.Plains, 1f)]
    [InlineData(MapPreset.Highlands, 1f)]
    [InlineData(MapPreset.Canyon, 1f)]
    public void ExtremeWaterAmountsStayConnected(MapPreset preset, float water)
    {
        foreach (int seed in new[] { 2, 5 })
        {
            var t = Gen(preset, 256, 4, seed, water);
            Assert.True(MapGenerator.StartsConnectedByLand(t), $"{preset} water {water} seed {seed}");
        }
    }

    [Fact]
    public void WaterAmountControlsLakeCoverage()
    {
        float WaterFraction(Terrain t) => t.Pass.Count(p => (p & PassFlags.WaterSurface) != 0) / (float)t.Pass.Length;
        var dry = Gen(MapPreset.Lakes, 256, 2, 4, 0f);
        var wet = Gen(MapPreset.Lakes, 256, 2, 4, 1f);
        Assert.True(WaterFraction(dry) < 0.02f, $"dry map has {WaterFraction(dry):P1} water");
        Assert.True(WaterFraction(wet) > 0.12f, $"wet map has only {WaterFraction(wet):P1} water");
        // Lakes really exist by default now (the old generator never made any).
        Assert.True(WaterFraction(Gen(MapPreset.Lakes, 256, 2, 42)) > 0.05f);
    }

    [Theory]
    [InlineData(MapPreset.Plains)]
    [InlineData(MapPreset.Lakes)]
    [InlineData(MapPreset.Highlands)]
    [InlineData(MapPreset.Canyon)]
    [InlineData(MapPreset.Archipelago)]
    public void DepositsAreOnBuildableCellsAndStartsAreBuildable(MapPreset preset)
    {
        var db = TestUtil.Db;
        var mex = db["uef_t1_mass_extractor"];
        var hydro = db["uef_t1_hydrocarbon"];
        var factory = db["uef_t1_land_factory"];
        foreach (var (size, players, seed) in new[] { (256, 2, 3), (512, 4, 8), (256, 6, 1) })
        {
            var t = Gen(preset, size, players, seed);
            var occ = new OccupancyGrid(t.Width, t.Height);
            Assert.NotEmpty(t.Deposits);
            foreach (var d in t.Deposits)
            {
                var bp = d.Type == DepositType.Mass ? mex : hydro;
                var site = OccupancyGrid.SnapStructure(bp, d.Pos);
                Assert.True(occ.CanPlace(bp, site, t), $"{preset} {size}: {d.Type} deposit at {d.Pos} is not buildable");
            }
            foreach (var sp in t.StartPositions)
            {
                // Flat buildable start area.
                for (int z = (int)sp.Y - 16; z <= (int)sp.Y + 16; z++)
                for (int x = (int)sp.X - 16; x <= (int)sp.X + 16; x++)
                    if (Vector2.Distance(new Vector2(x + 0.5f, z + 0.5f), sp) <= 16f)
                        Assert.True((t.PassAt(x, z) & PassFlags.Land) != 0, $"{preset}: start {sp} cell {x},{z} not buildable");
                Assert.True(occ.CanPlace(factory, OccupancyGrid.SnapStructure(factory, sp + new Vector2(9, 0)), t));
                // Base resources for every player.
                int mass = t.Deposits.Count(d => d.Type == DepositType.Mass && Vector2.Distance(d.Pos, sp) < 20f);
                int hyd = t.Deposits.Count(d => d.Type == DepositType.Hydrocarbon && Vector2.Distance(d.Pos, sp) < 25f);
                Assert.True(mass >= 4, $"{preset}: start {sp} has {mass} base mass deposits");
                Assert.True(hyd >= 1, $"{preset}: start {sp} has no hydrocarbon");
            }
            // Expansions exist beyond the base clusters.
            int far = t.Deposits.Count(d => t.StartPositions.All(sp => Vector2.Distance(d.Pos, sp) > 30f));
            Assert.True(far >= players * (preset == MapPreset.Archipelago ? 1 : 2), $"{preset} {size} p{players}: only {far} deposits outside the bases");
        }
    }

    [Fact]
    public void LandPresetDepositsAreReachableFromTheStarts()
    {
        foreach (var preset in LandPresets)
        {
            var t = Gen(preset, 256, 2, 9);
            var seen = MapGenerator.FloodLand(t, t.StartPositions[0]);
            foreach (var d in t.Deposits)
                Assert.True(seen[(int)d.Pos.Y * t.Width + (int)d.Pos.X], $"{preset}: deposit {d.Pos} unreachable");
        }
    }

    [Fact]
    public void GenerationIsDeterministic()
    {
        foreach (var preset in MapPresets.All)
        {
            var a = Gen(preset, 256, 3, 1234);
            var b = Gen(preset, 256, 3, 1234);
            Assert.Equal(a.Heights, b.Heights);
            Assert.Equal(a.Deposits, b.Deposits);
            Assert.Equal(a.StartPositions, b.StartPositions);
            var c = Gen(preset, 256, 3, 1235);
            Assert.NotEqual(a.Heights, c.Heights);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void TerrainIsRotationallySymmetric(int players)
    {
        foreach (var preset in MapPresets.All)
        {
            var t = Gen(preset, 256, players, 21);
            int n = t.Width;
            double diff = 0;
            int cnt = 0;
            for (int z = 0; z <= n; z += 3)
            for (int x = 0; x <= n; x += 3)
            {
                // 180° (2 players) or 90° (4 players) rotation about the center.
                var (rx, rz) = players == 2 ? (n - x, n - z) : (n - z, x);
                diff += MathF.Abs(t.VertexHeight(x, z) - t.VertexHeight(rx, rz));
                cnt++;
            }
            Assert.True(diff / cnt < 0.25, $"{preset} p{players}: mean asymmetry {diff / cnt:F3}");
            Assert.Equal(0, t.Deposits.Length % players);
        }
    }

    [Fact]
    public void ArchipelagoStartsAreOnIslands()
    {
        var t = Gen(MapPreset.Archipelago, 512, 4, 5);
        int water = t.Pass.Count(p => (p & PassFlags.WaterSurface) != 0);
        Assert.True(water > t.Pass.Length * 0.3f, "archipelago should be mostly sea");
        foreach (var sp in t.StartPositions)
            Assert.True(t.CanTraverse(MotionLayer.Land, (int)sp.X, (int)sp.Y));
        Assert.True(MapPresets.NeedsNaval(MapPreset.Archipelago));
        Assert.False(MapPresets.NeedsNaval(MapPreset.Canyon));
    }

    [Fact]
    public void LargeMapGeneratesQuickly()
    {
        Gen(MapPreset.Canyon, 256, 2, 1); // JIT warm-up
        foreach (var preset in MapPresets.All)
        {
            var sw = Stopwatch.StartNew();
            var t = Gen(preset, 1024, 8, 3);
            sw.Stop();
            // Target < 1 s; generous bound for slow/shared CI machines.
            Assert.True(sw.Elapsed.TotalSeconds < 2.5, $"{preset} 1024: {sw.Elapsed.TotalMilliseconds:F0} ms");
            if (preset != MapPreset.Archipelago) Assert.True(MapGenerator.StartsConnectedByLand(t), $"{preset} 1024 p8");
        }
    }

    [Fact]
    public void SkirmishUsesThePreset()
    {
        var sim = GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings { Seed = 3, MapSize = 256, Players = 4, MapPreset = MapPreset.Canyon });
        Assert.Equal(4, sim.World.Terrain.StartPositions.Length);
        Assert.True(MapGenerator.StartsConnectedByLand(sim.World.Terrain));
        // Canyon: most of the map is impassable mesa.
        float reach = MapGenerator.FloodLand(sim.World.Terrain, sim.World.Terrain.StartPositions[0]).Count(b => b) / (float)(256 * 256);
        Assert.True(reach < 0.7f, $"canyon reachable fraction {reach:P0}");
    }
}
