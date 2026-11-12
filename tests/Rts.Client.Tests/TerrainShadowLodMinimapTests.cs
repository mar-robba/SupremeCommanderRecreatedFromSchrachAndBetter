using System.Numerics;
using Raylib_cs;
using Rts.Client.Menus;
using Rts.Client.Rendering;
using Rts.Client.UI;
using Xunit;

namespace Rts.Client.Tests;

/// <summary>(9b-2) Terrain self-shadow bake, mesh LOD simplifier, unit LOD hysteresis, minimap size/zoom maths and
/// the new settings (pure code, no GL).</summary>
public class TerrainShadowLodMinimapTests
{
    private static readonly Vector3 SunFromPlusX = Vector3.Normalize(new Vector3(1, 1, 0)); // 45° elevation

    [Fact]
    public void FlatTerrainIsLitEverywhere()
    {
        int w = 33, h = 33;
        var heights = Enumerable.Repeat(5f, w * h).ToArray();
        var s = TerrainShadowBake.Bake(heights, w, h, SunFromPlusX, parallel: false);
        for (int i = 0; i < s.Length; i++) Assert.Equal(1f, TerrainShadowBake.Lit(heights[i], s[i]), 3);
    }

    [Fact]
    public void RidgeShadowsTheSideAwayFromTheSunOnly()
    {
        int w = 41, h = 9;
        var heights = new float[w * h];
        for (int z = 0; z < h; z++)
            for (int x = 18; x <= 22; x++) heights[z * w + x] = 12f; // a wall at x = 18..22
        var s = TerrainShadowBake.Bake(heights, w, h, SunFromPlusX, parallel: false);
        int z0 = 4 * w;
        Assert.True(TerrainShadowBake.Lit(0, s[z0 + 15]) < 0.01f, "3 cells behind the wall (away from the sun): shadow");
        Assert.True(TerrainShadowBake.Lit(0, s[z0 + 10]) < 0.01f, "8 cells behind a 12-high wall at 45°: still shadow");
        Assert.True(TerrainShadowBake.Lit(0, s[z0 + 4]) > 0.99f, "14 cells behind: lit again");
        Assert.True(TerrainShadowBake.Lit(0, s[z0 + 25]) > 0.99f, "sun side: lit");
        Assert.True(TerrainShadowBake.Lit(12, s[z0 + 20]) > 0.99f, "wall top: lit");
        TerrainShadowBake.Blur(s, w, h);
        Assert.True(TerrainShadowBake.Lit(0, s[z0 + 14]) < 0.05f && TerrainShadowBake.Lit(0, s[z0 + 30]) > 0.95f, "blur keeps the shape");
    }

    private static (Vector3[] pos, Vector3[] nrm, Color[] col, ushort[] idx) Sphere(int rings, int segs)
    {
        var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var col = new List<Color>(); var idx = new List<ushort>();
        for (int r = 0; r <= rings; r++)
        for (int s = 0; s <= segs; s++)
        {
            float th = MathF.PI * r / rings, ph = 2 * MathF.PI * s / segs;
            var p = new Vector3(MathF.Sin(th) * MathF.Cos(ph), MathF.Cos(th), MathF.Sin(th) * MathF.Sin(ph));
            pos.Add(p); nrm.Add(p);
            col.Add(p.Y > 0 ? new Color(200, 200, 200, 255) : new Color(200, 200, 200, 0)); // two material tags
        }
        for (int r = 0; r < rings; r++)
        for (int s = 0; s < segs; s++)
        {
            int a = r * (segs + 1) + s, b = a + segs + 1;
            idx.AddRange([(ushort)a, (ushort)b, (ushort)(a + 1), (ushort)(a + 1), (ushort)b, (ushort)(b + 1)]);
        }
        return (pos.ToArray(), nrm.ToArray(), col.ToArray(), idx.ToArray());
    }

    [Fact]
    public void MeshLodReducesADenseMeshAndKeepsMaterialTags()
    {
        var (pos, nrm, col, idx) = Sphere(24, 48);
        var lod = MeshLod.Simplify(pos, nrm, col, idx);
        Assert.InRange(lod.Triangles, 20, idx.Length / 3 / 4);
        Assert.Contains(lod.Col, c => c.A == 255);
        Assert.Contains(lod.Col, c => c.A == 0);
        Assert.All(lod.Idx, i => Assert.InRange(i, 0, lod.Pos.Length - 1));
        Assert.Empty(MeshLod.Simplify([], [], [], []).Idx);
    }

    [Fact]
    public void UnitLodSwitchesWithHysteresis()
    {
        float k = UnitLod.PixelsAtUnitDistance(1080, 2.4142f); // 45° fovy
        Assert.Equal(1080 * 0.5f * 2.4142f, k, 2);
        Assert.Equal(7f, UnitLod.ProjectedRadius(1f, k / 7f, k), 3);
        Assert.False(UnitLod.UseLod(false, 6.5f, 7f)); // full mesh until below 7·0.88
        Assert.True(UnitLod.UseLod(true, 6.5f, 7f)); // LOD until above 7·1.12
        Assert.True(UnitLod.UseLod(false, 6f, 7f));
        Assert.False(UnitLod.UseLod(true, 8f, 7f));
        float d = UnitLod.SwitchDistance(1f, k, 7f);
        Assert.True(UnitLod.UseLod(false, UnitLod.ProjectedRadius(1f, d * 1.01f, k), 7f));
        Assert.True(UnitLod.ParseCli("off") == false && UnitLod.ParseCli("on"));
    }

    [Fact]
    public void MinimapViewZoomsAroundTheFocusAndStaysOnTheMap()
    {
        var m = new Rectangle(10, 20, 100, 100);
        Assert.Equal(m, MinimapView.Virtual(m, 1f, new Vector2(0.3f, 0.7f)));
        var c = MinimapView.Virtual(m, 2f, new Vector2(0.5f, 0.5f));
        Assert.Equal(new Rectangle(-40, -30, 200, 200), c);
        var corner = MinimapView.Virtual(m, 4f, Vector2.Zero);
        Assert.Equal((10f, 20f), (corner.X, corner.Y)); // clamped: the map's corner stays in the panel's corner
        var far = MinimapView.Virtual(m, 4f, Vector2.One);
        Assert.Equal((110f - 400f, 120f - 400f), (far.X, far.Y));
        var p = MinimapView.ToScreen(c, 512, 512, 300, 100);
        var back = MinimapView.ToWorld(c, 512, 512, p);
        Assert.Equal(300f, back.X, 2);
        Assert.Equal(100f, back.Y, 2);
        Assert.Equal(1.5f, MinimapView.StepZoom(1f, +1));
        Assert.Equal(8f, MinimapView.StepZoom(8f, +1));
        Assert.Equal(0.75f, MinimapView.StepSize(0.75f, -1));
        var panel = MinimapView.Panel(4, 500, 172, 1.5f);
        Assert.Equal(new Rectangle(4, 242, 258, 258), panel);
    }

    [Fact]
    public void MinimapAndGraphicsSettingsRoundTripAndClamp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rts-9b2-{Guid.NewGuid():N}.json");
        try
        {
            var s = new AppSettings { TerrainShadows = false, UnitLod = false, MinimapSize = 1.5f, MinimapZoom = 3f,
                MinimapResources = false, MinimapUnits = false, MinimapTrees = true };
            s.SaveTo(path);
            var r = AppSettings.LoadFrom(path);
            Assert.Equal((false, false, 1.5f, 3f, false, false, true),
                (r.TerrainShadows, r.UnitLod, r.MinimapSize, r.MinimapZoom, r.MinimapResources, r.MinimapUnits, r.MinimapTrees));
        }
        finally { File.Delete(path); }
        var bad = new AppSettings { MinimapSize = 9f, MinimapZoom = 2.4f }.Normalize();
        Assert.Equal((1.75f, 2f), (bad.MinimapSize, bad.MinimapZoom));
        var spec = new AppSettings();
        MinimapView.ApplySpec(spec, "size=1.25, zoom=4,resources=off,trees=on");
        Assert.Equal((1.25f, 4f, false, true, true), (spec.MinimapSize, spec.MinimapZoom, spec.MinimapResources, spec.MinimapTrees, spec.MinimapUnits));
        Assert.Throws<ArgumentException>(() => MinimapView.ApplySpec(spec, "colour=red"));
    }
}
