using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>Simplified mesh arrays (16-bit indices, the vertex layout of <see cref="MeshBuilder"/>).</summary>
public sealed record LodMeshData(Vector3[] Pos, Vector3[] Nrm, Color[] Col, ushort[] Idx)
{
    public int Triangles => Idx.Length / 3;
}

/// <summary>
/// (9b-2) Low-poly unit models for far units by vertex clustering (pure, no GL; <c>UnitLodTests</c>).
/// The model's bounding box is cut into cubic cells of <c>extent / cellsPerAxis</c>; every vertex snaps to the
/// mean position of its cell, so triangles with two corners in one cell vanish (small details: barrels, antennas,
/// bevels) and big faces keep their shape. Output vertices are split by (cell, normal bucket, material tag), so the
/// flat-shaded look and the vertex-alpha material tags (team colour / metal / glow, read by the unit shader) survive.
/// </summary>
public static class MeshLod
{
    /// <summary>Default grid resolution along the longest model axis.</summary>
    public const int DefaultCells = 7;

    public static LodMeshData Simplify(ReadOnlySpan<Vector3> pos, ReadOnlySpan<Vector3> nrm, ReadOnlySpan<Color> col,
                                       ReadOnlySpan<ushort> idx, int cellsPerAxis = DefaultCells)
    {
        int n = pos.Length;
        if (n == 0 || idx.Length < 3) return new LodMeshData([], [], [], []);
        var min = pos[0];
        var max = pos[0];
        for (int i = 1; i < n; i++) { min = Vector3.Min(min, pos[i]); max = Vector3.Max(max, pos[i]); }
        var ext = max - min;
        float cell = MathF.Max(MathF.Max(ext.X, ext.Y), MathF.Max(ext.Z, 1e-4f)) / Math.Max(1, cellsPerAxis);

        // 1) Cell per vertex and the mean position of each cell (shared by every face touching it: no cracks).
        var cellOf = new int[n];
        var cellIds = new Dictionary<long, int>();
        var cellSum = new List<Vector3>();
        var cellCnt = new List<int>();
        for (int i = 0; i < n; i++)
        {
            var q = (pos[i] - min) / cell;
            long key = ((long)(int)q.X << 42) ^ ((long)(int)q.Y << 21) ^ (int)q.Z;
            if (!cellIds.TryGetValue(key, out int c))
            {
                c = cellSum.Count;
                cellIds[key] = c;
                cellSum.Add(Vector3.Zero);
                cellCnt.Add(0);
            }
            cellOf[i] = c;
            cellSum[c] += pos[i];
            cellCnt[c]++;
        }

        // 2) Output vertex per (cell, normal bucket, material tag): mean normal and colour of its members.
        var outIds = new Dictionary<long, int>();
        var oPos = new List<Vector3>();
        var oNrm = new List<Vector3>();
        var oCol = new List<Vector4>();
        var oCnt = new List<int>();
        var outOf = new int[n];
        for (int i = 0; i < n; i++)
        {
            long key = ((long)cellOf[i] << 16) | ((long)NormalBucket(nrm[i]) << 8) | col[i].A;
            if (!outIds.TryGetValue(key, out int o))
            {
                o = oPos.Count;
                outIds[key] = o;
                oPos.Add(cellSum[cellOf[i]] / cellCnt[cellOf[i]]);
                oNrm.Add(Vector3.Zero);
                oCol.Add(Vector4.Zero);
                oCnt.Add(0);
            }
            outOf[i] = o;
            oNrm[o] += nrm[i];
            oCol[o] += new Vector4(col[i].R, col[i].G, col[i].B, col[i].A);
            oCnt[o]++;
        }

        // 3) Triangles whose three corners stay in three different cells, without duplicates.
        var tris = new List<ushort>();
        var seen = new HashSet<(int, int, int)>();
        var used = new int[oPos.Count];
        Array.Fill(used, -1);
        var fPos = new List<Vector3>();
        var fNrm = new List<Vector3>();
        var fCol = new List<Color>();
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            int a = idx[t], b = idx[t + 1], c = idx[t + 2];
            if (cellOf[a] == cellOf[b] || cellOf[b] == cellOf[c] || cellOf[a] == cellOf[c]) continue;
            int oa = outOf[a], ob = outOf[b], oc = outOf[c];
            var pa = oPos[oa];
            if (Vector3.Cross(oPos[ob] - pa, oPos[oc] - pa).LengthSquared() < cell * cell * cell * cell * 1e-4f) continue;
            if (!seen.Add(Canonical(oa, ob, oc))) continue;
            tris.Add(Emit(oa)); tris.Add(Emit(ob)); tris.Add(Emit(oc));
        }
        return new LodMeshData(fPos.ToArray(), fNrm.ToArray(), fCol.ToArray(), tris.ToArray());

        ushort Emit(int o)
        {
            if (used[o] < 0)
            {
                used[o] = fPos.Count;
                fPos.Add(oPos[o]);
                var nn = oNrm[o];
                fNrm.Add(nn.LengthSquared() > 1e-12f ? Vector3.Normalize(nn) : Vector3.UnitY);
                var cc = oCol[o] / oCnt[o];
                // The tag (alpha) is the bucket key: identical for every member, so it survives exactly.
                fCol.Add(new Color((byte)MathF.Round(cc.X), (byte)MathF.Round(cc.Y), (byte)MathF.Round(cc.Z), (byte)MathF.Round(cc.W)));
            }
            return (ushort)used[o];
        }
    }

    /// <summary>One of 27 directions (each axis -, 0, +; threshold cos 67.5°): faces of similar orientation merge.</summary>
    public static int NormalBucket(Vector3 n)
    {
        static int Q(float v) => v > 0.38f ? 2 : v < -0.38f ? 0 : 1;
        return Q(n.X) * 9 + Q(n.Y) * 3 + Q(n.Z);
    }

    /// <summary>Same triangle with the same winding in any rotation (dedupe key).</summary>
    private static (int, int, int) Canonical(int a, int b, int c)
    {
        if (a <= b && a <= c) return (a, b, c);
        if (b <= a && b <= c) return (b, c, a);
        return (c, a, b);
    }
}
