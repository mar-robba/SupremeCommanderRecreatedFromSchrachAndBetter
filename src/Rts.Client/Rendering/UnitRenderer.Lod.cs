using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// (9b-2) Low-poly LOD for far units: a mobile unit or structure whose bounding sphere projects below
/// <see cref="UnitLod.PixelRadius"/> pixels is added to its model's <see cref="UnitModel.LodBody"/> /
/// <see cref="UnitModel.LodTurret"/> (built on first use by <see cref="MeshLod.Simplify"/>) instead of the full
/// meshes. Same instance data (team colour, death-fade w), same shaders: only the opaque shadow + lit passes of
/// <see cref="Draw"/> gather LOD units; the hologram, cloak, submerged and cargo passes always use the full meshes.
/// Per-unit hysteresis (<see cref="UnitLod.UseLod"/>) keyed by the unit index.
/// </summary>
public sealed unsafe partial class UnitRenderer
{
    /// <summary>Settings switch (Graphics "Unit LOD").</summary>
    public static bool LodEnabled = true;
    /// <summary>CLI override (<c>--unit-lod on|off</c>); null = use the setting.</summary>
    public static bool? LodCli;
    public static bool LodActive => LodCli ?? LodEnabled;

    /// <summary>A model uses its LOD only when it has at most this fraction of the full triangles.</summary>
    public const float LodMaxTriFraction = 0.8f;

    private bool _lodGather;
    private Vector3 _lodCam;
    private float _lodPxK;
    private byte[] _lodWas = new byte[256];

    /// <summary>Units drawn with the LOD mesh last frame (perf overlay / log).</summary>
    public static int LodUnits { get; private set; }
    /// <summary>Triangles of the LOD vs full meshes of the units drawn last frame (perf log).</summary>
    public static long LodTriangles { get; private set; }
    public static long FullTriangles { get; private set; }

    private void BeginLod(in FrameView fv)
    {
        _lodGather = LodActive;
        _lodCam = fv.CamPos;
        _lodPxK = UnitLod.PixelsAtUnitDistance(fv.ScreenH, fv.Proj.M22);
        LodUnits = 0;
    }

    /// <summary>Whether this unit uses its LOD mesh this frame. <paramref name="index"/> &lt; 0 = no hysteresis
    /// (ghosts, dying units).</summary>
    private bool LodFor(UnitModel model, int index, Vector3 pos)
    {
        if (!_lodGather) return false;
        float midY = (model.MinY + model.MaxY) * 0.5f;
        float px = UnitLod.ProjectedRadius(model.Radius, Vector3.Distance(_lodCam, pos + new Vector3(0, midY, 0)), _lodPxK);
        bool was = false;
        if (index >= 0)
        {
            if (index >= _lodWas.Length) Array.Resize(ref _lodWas, Math.Max(index + 1, _lodWas.Length * 2));
            was = _lodWas[index] != 0;
        }
        bool lod = UnitLod.UseLod(was, px, UnitLod.PixelRadius);
        if (index >= 0) _lodWas[index] = lod ? (byte)1 : (byte)0;
        if (!lod) return false;
        if (model.LodState == 0) BuildLod(model);
        if (model.LodState != 1) return false;
        LodUnits++;
        return true;
    }

    private static void BuildLod(UnitModel model)
    {
        model.LodState = 2;
        var body = Simplify(model.Body.Mesh);
        var turret = model.Turret != null ? Simplify(model.Turret.Mesh) : null;
        int full = model.Body.Mesh.TriangleCount + (model.Turret?.Mesh.TriangleCount ?? 0);
        int lod = body.Triangles + (turret?.Triangles ?? 0);
        if (PerfLog.Enabled) Console.WriteLine($"unit lod: {full} -> {lod} triangles (radius {model.Radius:F1})");
        if (body.Triangles == 0 || lod > full * LodMaxTriFraction) return;
        model.LodBody = new InstancedMesh(Upload(body));
        if (model.Turret != null)
            model.LodTurret = new InstancedMesh(Upload(turret!.Triangles > 0 ? turret : CopyOf(model.Turret.Mesh)));
        model.LodState = 1;
    }

    private static LodMeshData Simplify(Mesh m) =>
        MeshLod.Simplify(m.VerticesAs<Vector3>(), m.NormalsAs<Vector3>(), m.ColorsAs<Color>(), m.IndicesAs<ushort>());

    private static LodMeshData CopyOf(Mesh m) =>
        new(m.VerticesAs<Vector3>().ToArray(), m.NormalsAs<Vector3>().ToArray(), m.ColorsAs<Color>().ToArray(), m.IndicesAs<ushort>().ToArray());

    private static Mesh Upload(LodMeshData d)
    {
        var mesh = new Mesh { VertexCount = d.Pos.Length, TriangleCount = d.Triangles };
        mesh.AllocVertices();
        mesh.AllocNormals();
        mesh.AllocColors();
        mesh.AllocIndices();
        d.Pos.CopyTo(mesh.VerticesAs<Vector3>());
        d.Nrm.CopyTo(mesh.NormalsAs<Vector3>());
        d.Col.CopyTo(mesh.ColorsAs<Color>());
        d.Idx.CopyTo(mesh.IndicesAs<ushort>());
        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }

    /// <summary>Opaque passes done: drop the LOD instances so the later passes (cloak, submerged...) only see full meshes.</summary>
    private void EndLod()
    {
        long lt = 0, ft = 0;
        foreach (var m in _active)
        {
            if (m.LodBody != null) lt += (long)m.LodBody.Count * m.LodBody.Mesh.TriangleCount;
            if (m.LodTurret != null) lt += (long)m.LodTurret.Count * m.LodTurret.Mesh.TriangleCount;
            ft += (long)m.Body.Count * m.Body.Mesh.TriangleCount;
            if (m.Turret != null) ft += (long)m.Turret.Count * m.Turret.Mesh.TriangleCount;
            m.LodBody?.Clear();
            m.LodTurret?.Clear();
        }
        LodTriangles = lt;
        FullTriangles = ft;
        _lodGather = false;
    }

    private static int LodCount(UnitModel m) => (m.LodBody?.Count ?? 0) + (m.LodTurret?.Count ?? 0);
}
