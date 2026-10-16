using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// 6- and 8-player maps on 512/1024 for every preset (wave 5m): every start is buildable, reaches the others by land
/// (land presets), and gets the same deal: the terrain is rotationally symmetric, start plateaus have the same height,
/// every start has the same deposits nearby and the same ground distances to the other starts.
/// </summary>
public class MapScaleTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var p in MapPresets.All)
        {
            yield return [p, 512, 6, 4];
            yield return [p, 512, 8, 9];
            yield return [p, 1024, 8, 3];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ManyPlayerMapsAreFair(MapPreset preset, int size, int players, int seed)
    {
        var t = MapGenerator.Generate(new MapGenSettings { Preset = preset, Size = size, Players = players, Seed = seed });
        string tag = $"{preset} {size} p{players} seed {seed}";
        Assert.Equal(players, t.StartPositions.Length);
        Assert.Equal(0, t.Deposits.Length % players);
        foreach (var sp in t.StartPositions)
            Assert.True(t.CanTraverse(MotionLayer.Land, (int)sp.X, (int)sp.Y), $"{tag}: start {sp} not on land");

        // The same plateau height at every start.
        float h0 = t.GetHeight(t.StartPositions[0]);
        foreach (var sp in t.StartPositions)
            Assert.True(MathF.Abs(t.GetHeight(sp) - h0) < 0.3f, $"{tag}: start {sp} height {t.GetHeight(sp):F2} vs {h0:F2}");

        // Rotational symmetry of the terrain (other counts than 2/4 are resampled, so not exact).
        var center = new Vector2(size / 2f);
        var rot = Matrix3x2.CreateRotation(MathF.PI * 2f / players, center);
        double diff = 0;
        int cnt = 0;
        float r = size * 0.48f;
        for (int z = 4; z < size; z += 7)
        for (int x = 4; x < size; x += 7)
        {
            var p = new Vector2(x, z);
            if (Vector2.Distance(p, center) > r) continue;
            diff += MathF.Abs(t.GetHeight(p) - t.GetHeight(Vector2.Transform(p, rot)));
            cnt++;
        }
        Assert.True(diff / cnt < 0.15, $"{tag}: mean asymmetry {diff / cnt:F3}");

        // Nearby deposits: the k nearest of each type are at the same distances (to a few cells) from every start.
        foreach (var type in new[] { DepositType.Mass, DepositType.Hydrocarbon })
        {
            int k = Math.Min(6, t.Deposits.Count(d => d.Type == type) / players);
            float[] Nearest(Vector2 sp) => t.Deposits.Where(d => d.Type == type)
                .Select(d => Vector2.Distance(d.Pos, sp)).OrderBy(d => d).Take(k).ToArray();
            var refD = Nearest(t.StartPositions[0]);
            foreach (var sp in t.StartPositions)
            {
                var d = Nearest(sp);
                for (int i = 0; i < k; i++)
                    Assert.True(MathF.Abs(d[i] - refD[i]) < 3f, $"{tag}: {type} #{i} near {sp}: {d[i]:F1} vs {refD[i]:F1}");
            }
        }

        if (MapPresets.NeedsNaval(preset)) return;
        // Ground distances: every start reaches every other one, and its sorted distances match start 0's.
        var dist0 = LandDistances(t, t.StartPositions[0]);
        for (int i = 0; i < players; i++)
        {
            var di = i == 0 ? dist0 : LandDistances(t, t.StartPositions[i]);
            var refList = Sorted(t, dist0, 0);
            var list = Sorted(t, di, i);
            for (int j = 0; j < list.Length; j++)
            {
                Assert.True(list[j] > 0, $"{tag}: start {i} cannot reach a start by land");
                Assert.True(MathF.Abs(list[j] - refList[j]) <= 0.1f * refList[j] + 50,
                    $"{tag}: start {i} ground distance #{j} {list[j]} vs {refList[j]} (start 0)");
            }
        }
    }

    private static int[] Sorted(Terrain t, int[] dist, int self) => t.StartPositions.Where((_, k) => k != self)
        .Select(sp => dist[(int)sp.Y * t.Width + (int)sp.X]).OrderBy(d => d).ToArray();

    /// <summary>8-connected land Dijkstra distance (straight 5, diagonal 7: about 5x cells), -1 = unreachable.</summary>
    private static int[] LandDistances(Terrain t, Vector2 from)
    {
        int w = t.Width, h = t.Height;
        var dist = new int[w * h];
        Array.Fill(dist, -1);
        var q = new PriorityQueue<int, int>();
        int s = (int)from.Y * w + (int)from.X;
        dist[s] = 0;
        q.Enqueue(s, 0);
        while (q.TryDequeue(out int c, out int dc))
        {
            if (dc != dist[c]) continue;
            int x = c % w, z = c / w;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, nz = z + dz;
                if ((dx | dz) == 0 || !t.CanTraverse(MotionLayer.Land, nx, nz)) continue;
                int nc = nz * w + nx, nd = dc + (dx != 0 && dz != 0 ? 7 : 5);
                if (dist[nc] >= 0 && dist[nc] <= nd) continue;
                dist[nc] = nd;
                q.Enqueue(nc, nd);
            }
        }
        return dist;
    }
}
