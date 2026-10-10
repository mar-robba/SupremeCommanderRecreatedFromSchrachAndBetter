using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Draws units from interpolated snapshots. FOUNDATION VERSION: one lit-looking box per unit (body + turret
/// hint) tinted by army color. TODO(client): procedural meshes per Visual.Shape, GPU instancing, team-color
/// shader, death/muzzle effects, strategic icons are drawn by IconRenderer.
/// </summary>
public sealed unsafe class UnitRenderer : IDisposable
{
    private readonly BlueprintDb _db;
    private Mesh _cube;
    private Material _material;

    public UnitRenderer(BlueprintDb db)
    {
        _db = db;
        _cube = Raylib.GenMeshCube(1, 1, 1);
        _material = Raylib.LoadMaterialDefault();
        _material.Shader = Shaders.LoadLit();
    }

    /// <summary>Interpolated world position/heading of a unit view.</summary>
    public static (Vector3 pos, float heading) Interpolate(in UnitView cur, SimSnapshot? prev, float alpha)
    {
        if (prev != null && prev.TryGetUnit(cur.Index, cur.Gen, out var pv))
        {
            float dh = Rts.Sim.Core.DMath.AngleDiff(pv.Heading, cur.Heading);
            return (Vector3.Lerp(pv.Pos, cur.Pos, alpha), pv.Heading + dh * alpha);
        }
        return (cur.Pos, cur.Heading);
    }

    public static Matrix4x4 ModelMatrix(Vector3 pos, float heading, Vector3 size)
    {
        // Model forward is +Z; heading h points along (cos h, sin h) in the (x, z) plane.
        var m = Matrix4x4.CreateScale(size) * Matrix4x4.CreateRotationY(MathF.PI * 0.5f - heading) * Matrix4x4.CreateTranslation(pos);
        return Matrix4x4.Transpose(m); // raylib expects column-major
    }

    public void Draw(SimSnapshot cur, SimSnapshot? prev, float alpha, Color[] armyColors)
    {
        for (int k = 0; k < cur.UnitCount; k++)
        {
            ref var v = ref cur.Units[k];
            var bp = _db[v.Bp];
            var (pos, heading) = Interpolate(v, prev, alpha);
            var size = bp.Visual.Size;
            var color = armyColors[v.Army];
            bool building = (v.Flags & UnitFlags.UnderConstruction) != 0;
            if (building)
            {
                // Construction site: grows with build fraction, pale.
                float f = MathF.Max(0.08f, v.BuildFrac);
                size = new Vector3(size.X, size.Y * f, size.Z);
                color = ColorUtil.Lerp(new Color(200, 220, 240, 255), color, 0.3f);
            }
            var body = ColorUtil.Scale(color, 0.85f);
            _material.Maps[(int)MaterialMapIndex.Albedo].Color = body;
            Raylib.DrawMesh(_cube, _material, ModelMatrix(pos + new Vector3(0, size.Y * 0.5f, 0), heading, size));
            if (!building && bp.Weapons.Length > 0 && bp.IsMobile)
            {
                // Turret block (shows facing).
                var tsize = new Vector3(size.X * 0.55f, size.Y * 0.45f, size.Z * 0.7f);
                _material.Maps[(int)MaterialMapIndex.Albedo].Color = ColorUtil.Scale(color, 1.15f);
                var tpos = pos + new Vector3(0, size.Y + tsize.Y * 0.5f, 0);
                Raylib.DrawMesh(_cube, _material, ModelMatrix(tpos, v.TurretHeading, tsize));
            }
        }
    }

    public void Dispose()
    {
        Raylib.UnloadMesh(_cube);
    }
}
