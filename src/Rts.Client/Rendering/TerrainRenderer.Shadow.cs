using System.Diagnostics;
using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// (9b-2) Terrain self-shadows: the shadow line of <see cref="TerrainShadowBake"/> (baked once per game on the CPU,
/// with <see cref="SunDir"/>, the sun of the unit shadow map). The ground's lit factor goes into the terrain LOD
/// meshes (vertex colour G; coarse strides take the footprint mean), the line itself into an R32F texture with one
/// texel per height vertex that the unit shader compares with the fragment height (units in a valley's shadow
/// darken; aircraft above it stay lit).
/// Switch: <see cref="SelfShadows"/> (Graphics setting "Terrain shadows"; <c>--terrain-shadows on|off</c>).
/// </summary>
public sealed unsafe partial class TerrainRenderer
{
    /// <summary>Texture unit of the shadow-line texture (4 = unit shadow map, 5/6 = fog of war).</summary>
    public const int SelfShadowSlot = 7;

    /// <summary>Settings switch (applied by GraphicsSettings); off = the shaders skip the lookup.</summary>
    public static bool SelfShadows = true;
    /// <summary><c>--terrain-shadows on|off</c>: wins over the saved setting for that run.</summary>
    public static bool? SelfShadowsCli;
    public static bool SelfShadowsActive => SelfShadowsCli ?? SelfShadows;

    /// <summary>Time of the last bake (ms, for the perf report).</summary>
    public static double LastShadowBakeMs { get; private set; }

    /// <summary>The terrain of the running game (for <see cref="BindSelfShadow"/> from other renderers).</summary>
    public static TerrainRenderer? Current { get; private set; }

    private Texture2D _shadowTex;
    private bool _shadowBaked;

    /// <summary>Debug (CLI <c>--sun-elevation DEG</c>): lowers or raises the sun (same azimuth) for every light, the
    /// unit shadow map and the terrain shadows. Call before the renderers are built (visual testing only).</summary>
    public static void SetSunElevation(float degrees)
    {
        var d = SunDir;
        float az = MathF.Atan2(d.Z, d.X), el = Math.Clamp(degrees, 3f, 89f) * MathF.PI / 180f;
        SunDir = Vector3.Normalize(new Vector3(MathF.Cos(az) * MathF.Cos(el), MathF.Sin(el), MathF.Sin(az) * MathF.Cos(el)));
    }

    /// <summary>GLSL: terrain shadow lookups. Requires the uniforms set by <see cref="BindSelfShadow"/>.</summary>
    public const string SelfShadowFs = """
        uniform sampler2D terrainShadowTex;
        uniform float terrainShadowOn;
        uniform vec2 terrainShadowScale;
        // Lit fraction of a point at its own height (units, props, aircraft): one fetch of the shadow line.
        float terrainShadowAt(vec3 wpos) {
            if (terrainShadowOn < 0.5) return 1.0;
            float d = texture(terrainShadowTex, (wpos.xz + 0.5) * terrainShadowScale).r - wpos.y;
            return 1.0 - smoothstep(0.0, 1.2, d); // TerrainShadowBake.Lit (Softness 0.6)
        }
        // Lit fraction of the ground: baked per vertex into the terrain meshes (vertex colour G, see
        // TerrainRenderer.Shadow.cs), so the ground pays no texture fetch (a full-map view fetched ~0.55 ms).
        float terrainShadowGround(float vertexLit) {
            return terrainShadowOn < 0.5 ? 1.0 : vertexLit;
        }
        """;

    /// <summary>Ground lit factor per height vertex, only while the terrain meshes are built (G of their colours).</summary>
    private float[]? _groundLit;

    private void InitSelfShadow() => Current = this;

    /// <summary>Bakes the shadow line once per game (45-90 ms on a 1025² grid), uploads it for the units and keeps
    /// the ground's lit factor for <see cref="BuildLodChunks"/>. Always runs, even with the setting off: the ground
    /// factor lives in the vertex data, and the setting can be switched on mid-game.</summary>
    private void BakeSelfShadow()
    {
        var sw = Stopwatch.StartNew();
        int vw = _t.Width + 1, vh = _t.Height + 1;
        var line = TerrainShadowBake.Bake(_t.Heights, vw, vh, SunDir);
        TerrainShadowBake.Blur(line, vw, vh);
        _groundLit = TerrainShadowBake.GroundLit(_t.Heights, line);
        LastShadowBakeMs = sw.Elapsed.TotalMilliseconds;
        fixed (float* p = line)
        {
            var img = new Image { Data = p, Width = vw, Height = vh, Mipmaps = 1, Format = PixelFormat.UncompressedR32 };
            _shadowTex = Raylib.LoadTextureFromImage(img);
        }
        // Bilinear, no mipmaps: unit meshes are only drawn up to camera distance 260, where the texture is magnified.
        Raylib.SetTextureFilter(_shadowTex, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(_shadowTex, TextureWrap.Clamp);
        _shadowBaked = true;
        Console.WriteLine($"terrain shadows: baked {vw}x{vh} in {LastShadowBakeMs:F1} ms");
    }

    /// <summary>Vertex colour G of a terrain LOD vertex: the ground lit factor averaged over the stride footprint.</summary>
    private byte GroundLitByte(int wx, int wz, int stride) => _groundLit == null ? (byte)255
        : (byte)Math.Clamp(TerrainShadowBake.BoxMean(_groundLit, _t.Width + 1, _t.Height + 1, wx, wz, stride / 2) * 255f + 0.5f, 0f, 255f);

    /// <summary>Binds the terrain shadow textures and uniforms to a shader that includes <see cref="SelfShadowFs"/>.</summary>
    public static void BindSelfShadow(ShaderProgram p)
    {
        var tr = Current;
        bool on = SelfShadowsActive && tr != null;
        if (on)
        {
            Rlgl.ActiveTextureSlot(SelfShadowSlot);
            Rlgl.EnableTexture(tr!._shadowTex.Id);
            Rlgl.ActiveTextureSlot(0);
            p.Set("terrainShadowTex", SelfShadowSlot);
            p.Set("terrainShadowScale", new Vector2(1f / (tr._t.Width + 1), 1f / (tr._t.Height + 1)));
        }
        p.Set("terrainShadowOn", on ? 1f : 0f);
    }

    private void DisposeSelfShadow()
    {
        if (_shadowBaked) Raylib.UnloadTexture(_shadowTex);
        if (Current == this) Current = null;
    }
}
