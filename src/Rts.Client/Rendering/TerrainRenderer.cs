using System.Numerics;
using Raylib_cs;
using Rts.Sim.Map;

namespace Rts.Client.Rendering;

/// <summary>
/// Terrain as 64x64-cell chunk meshes (16-bit indices) shaded per pixel (height/slope/noise material blend,
/// baked cavity AO in vertex colors, sun + sky lighting, unit shadows, distance haze), a sky/void gradient
/// background, an animated translucent water surface with depth tint and shoreline foam, and deposit markers.
/// </summary>
public sealed unsafe partial class TerrainRenderer : IDisposable
{
    public const int ChunkSize = 64;
    public static readonly Vector3 SunDir = Vector3.Normalize(new Vector3(-0.45f, 0.8f, -0.35f));

    private readonly Terrain _t;
    private Material _material;
    private Material _waterMat;
    private Material _skyMat;
    private Mesh _water;
    private Mesh _sky;
    private Texture2D _heightTex;
    private readonly float _waterLevel;

    /// <summary>Debug (CLI --debugwater H): render the water surface at another level (visual testing only;
    /// gameplay, picking and the sim keep the real level).</summary>
    public static float? DebugWaterLevel;

    public TerrainRenderer(Terrain terrain)
    {
        _t = terrain;
        _waterLevel = DebugWaterLevel ?? terrain.WaterLevel;
        var ao = ComputeAo();
        BuildLodChunks(ao); // TerrainRenderer.Lod.cs

        _material = Raylib.LoadMaterialDefault();
        _material.Shader = Shaders.Terrain.Shader;

        // Heightmap texture (R32F, one texel per vertex) for water depth.
        var heights = terrain.Heights;
        fixed (float* hp = heights)
        {
            var img = new Image { Data = hp, Width = terrain.Width + 1, Height = terrain.Height + 1, Mipmaps = 1, Format = PixelFormat.UncompressedR32 };
            _heightTex = Raylib.LoadTextureFromImage(img);
        }
        Raylib.SetTextureFilter(_heightTex, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(_heightTex, TextureWrap.Clamp);

        _water = Quad(new Vector3(0, _waterLevel, 0), new Vector3(terrain.Width, _waterLevel, terrain.Height));
        _waterMat = Raylib.LoadMaterialDefault();
        _waterMat.Shader = Shaders.Water.Shader;
        _waterMat.Maps[(int)MaterialMapIndex.Albedo].Texture = _heightTex;

        _sky = new Mesh { VertexCount = 3, TriangleCount = 1 };
        _sky.AllocVertices();
        var sv = _sky.VerticesAs<Vector3>();
        sv[0] = new Vector3(-1, -1, 0); sv[1] = new Vector3(3, -1, 0); sv[2] = new Vector3(-1, 3, 0);
        Raylib.UploadMesh(ref _sky, false);
        _skyMat = Raylib.LoadMaterialDefault();
        _skyMat.Shader = Shaders.Sky.Shader;
    }

    private static Mesh Quad(Vector3 a, Vector3 b)
    {
        var m = new Mesh { VertexCount = 4, TriangleCount = 2 };
        m.AllocVertices(); m.AllocNormals(); m.AllocColors(); m.AllocIndices();
        var v = m.VerticesAs<Vector3>();
        v[0] = new Vector3(a.X, a.Y, a.Z); v[1] = new Vector3(b.X, a.Y, a.Z); v[2] = new Vector3(b.X, a.Y, b.Z); v[3] = new Vector3(a.X, a.Y, b.Z);
        var n = m.NormalsAs<Vector3>();
        var c = m.ColorsAs<Color>();
        for (int i = 0; i < 4; i++) { n[i] = Vector3.UnitY; c[i] = Color.White; }
        var ix = m.IndicesAs<ushort>();
        ix[0] = 0; ix[1] = 2; ix[2] = 1; ix[3] = 0; ix[4] = 3; ix[5] = 2;
        Raylib.UploadMesh(ref m, false);
        return m;
    }

    /// <summary>Cavity ambient occlusion: vertices lower than their neighbourhood get darker.</summary>
    private float[] ComputeAo()
    {
        int vw = _t.Width + 1, vh = _t.Height + 1;
        var ao = new float[vw * vh];
        const int r = 4;
        for (int z = 0; z < vh; z++)
        for (int x = 0; x < vw; x++)
        {
            float h = _t.VertexHeight(x, z);
            float sum = 0;
            int cnt = 0;
            for (int dz = -r; dz <= r; dz += 2)
            for (int dx = -r; dx <= r; dx += 2)
            {
                sum += _t.VertexHeight(x + dx, z + dz);
                cnt++;
            }
            float diff = h - sum / cnt;
            ao[z * vw + x] = Math.Clamp(1f + diff * 0.09f, 0.72f, 1.06f);
        }
        return ao;
    }

    /// <summary>Draws the sky/void background and the terrain. Call first inside BeginMode3D.</summary>
    public void Draw(Camera3D cam)
    {
        ShadowMap.Frame++;
        Atmosphere.Update(Vector3.Distance(cam.Position, cam.Target));
        var fv = FrameView.Capture();

        // Background gradient (fullscreen triangle at the far plane, no depth writes).
        var sky = Shaders.Sky;
        sky.SetMatrix("invViewProj", fv.InvViewProjGl);
        sky.Set("camPos", fv.CamPos);
        sky.Set("fogColor", Atmosphere.FogColor);
        sky.Set("skyTop", Atmosphere.SkyTop);
        sky.Set("abyss", Atmosphere.Abyss);
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        Raylib.DrawMesh(_sky, _skyMat, Matrix4x4.Identity);
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();

        var sp = Shaders.Terrain;
        Atmosphere.Apply(sp, fv);
        sp.Set("waterLevel", _waterLevel);
        sp.Set("mapSize", new Vector2(_t.Width, _t.Height));
        ShadowMap.Bind(sp);
        FogRenderer.Bind(sp);
        DrawLodChunks(fv);
        FrameProfiler.Sub("terrain:chunks");
    }

    /// <summary>Deposit marker positions on the ground (computed once).</summary>
    private Vector3[]? _depositPos;

    /// <summary>Beyond this camera distance a deposit is drawn as one flat square (its outline color) instead of the
    /// detailed marker (cubes/cylinders with wires, ~500 immediate-mode vertices per hydrocarbon).</summary>
    private const float DepositDetailDistance = 260f;

    public void DrawDeposits()
    {
        float t = (float)Raylib.GetTime();
        var fv = FrameView.Current;
        if (_depositPos == null)
        {
            _depositPos = new Vector3[_t.Deposits.Length];
            for (int i = 0; i < _depositPos.Length; i++)
            {
                var dp = _t.Deposits[i].Pos;
                _depositPos[i] = new Vector3(dp.X, _t.GetHeight(dp) + 0.06f, dp.Y);
            }
        }
        float detail2 = DepositDetailDistance * DepositDetailDistance;
        for (int i = 0; i < _depositPos.Length; i++)
        {
            var d = _t.Deposits[i];
            var p = _depositPos[i];
            if (!fv.SphereVisible(p, 2.5f)) continue;
            bool far = Vector3.DistanceSquared(p, fv.CamPos) > detail2;
            if (d.Type == DepositType.Mass)
            {
                if (far) { Raylib.DrawPlane(p + new Vector3(0, 0.08f, 0), new Vector2(1.9f), new Color(90, 220, 120, 255)); continue; }
                byte g = (byte)(170 + 60 * MathF.Sin(t * 2f + d.Pos.X));
                Raylib.DrawCube(p, 1.9f, 0.1f, 1.9f, new Color(36, 44, 38, 255));
                Raylib.DrawCube(p + new Vector3(0, 0.03f, 0), 1.2f, 0.1f, 1.2f, new Color((byte)50, g, (byte)80, (byte)255));
                Raylib.DrawCubeWires(p, 1.9f, 0.12f, 1.9f, new Color(90, 220, 120, 255));
            }
            else
            {
                if (far) { Raylib.DrawPlane(p + new Vector3(0, 0.08f, 0), new Vector2(3.4f), new Color(230, 180, 50, 255)); continue; }
                Raylib.DrawCylinder(p, 1.8f, 1.9f, 0.1f, 16, new Color(30, 30, 32, 255));
                Raylib.DrawCylinder(p + new Vector3(0, 0.02f, 0), 0.9f, 0.9f, 0.1f, 12, new Color(70, 60, 30, 255));
                Raylib.DrawCylinderWires(p, 2.0f, 2.0f, 0.12f, 16, new Color(230, 180, 50, 255));
            }
        }
    }

    public void DrawWater()
    {
        if (_waterLevel <= 0) return;
        var fv = FrameView.Current;
        var sp = Shaders.Water;
        Atmosphere.Apply(sp, fv);
        sp.Set("mapSize", new Vector2(_t.Width, _t.Height));
        sp.Set("waterLevel", _waterLevel);
        sp.Set("skyTop", Atmosphere.SkyTop);
        FogRenderer.Bind(sp);
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        Raylib.DrawMesh(_water, _waterMat, Matrix4x4.Identity);
        Rlgl.EnableBackfaceCulling();
    }

    /// <summary>Ray vs heightmap: marches along the ray then refines by bisection. Returns null if no hit.</summary>
    public Vector3? Pick(Ray ray, float maxDist)
    {
        float step = 1.0f;
        float prevT = 0;
        bool prevAbove = Above(ray.Position);
        for (float t = step; t < maxDist; t += step)
        {
            var p = ray.Position + ray.Direction * t;
            bool above = Above(p);
            if (prevAbove && !above)
            {
                float lo = prevT, hi = t;
                for (int i = 0; i < 12; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (Above(ray.Position + ray.Direction * mid)) lo = mid; else hi = mid;
                }
                var hit = ray.Position + ray.Direction * hi;
                if (hit.X < 0 || hit.Z < 0 || hit.X > _t.Width || hit.Z > _t.Height) return null;
                return hit;
            }
            prevAbove = above;
            prevT = t;
            if (t > 50) step = 2f;
        }
        return null;
    }

    private bool Above(Vector3 p) => p.Y > MathF.Max(_t.GetHeight(p.X, p.Z), _t.WaterLevel);

    public void Dispose()
    {
        DisposeLodChunks();
        Raylib.UnloadMesh(_water);
        Raylib.UnloadMesh(_sky);
        Raylib.UnloadTexture(_heightTex);
    }
}
