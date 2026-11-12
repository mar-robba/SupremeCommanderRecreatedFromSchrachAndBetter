using System.Diagnostics;
using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// (9b-2) Terrain self-shadows: the shadow line of <see cref="TerrainShadowBake"/> (baked once per game on the CPU,
/// with <see cref="SunDir"/>, the sun of the unit shadow map) in an R32F texture with one texel per height vertex.
/// The terrain shader compares it with the heightmap texture (so the result does not depend on the terrain LOD
/// mesh), the unit shader with the fragment height (units in a valley's shadow darken; aircraft above it stay lit).
/// Switch: <see cref="SelfShadows"/> (Graphics setting "Terrain shadows"; <c>--terrain-shadows on|off</c>).
/// </summary>
public sealed unsafe partial class TerrainRenderer
{
    /// <summary>Texture units of the shadow-line and height textures (4 = unit shadow map, 5/6 = fog of war).</summary>
    public const int SelfShadowSlot = 7, HeightSlot = 8;

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
        uniform sampler2D terrainHeightTex;
        uniform float terrainShadowOn;
        uniform vec2 terrainShadowScale;
        // Lit fraction of a point at its own height (units, props, aircraft).
        float terrainShadowAt(vec3 wpos) {
            if (terrainShadowOn < 0.5) return 1.0;
            float d = texture(terrainShadowTex, (wpos.xz + 0.5) * terrainShadowScale).r - wpos.y;
            return 1.0 - smoothstep(0.0, 1.2, d); // TerrainShadowBake.Lit (Softness 0.6)
        }
        // Lit fraction of the ground (true heightmap height, independent of the terrain LOD mesh).
        float terrainShadowGround(vec2 xz) {
            if (terrainShadowOn < 0.5) return 1.0;
            vec2 uv = (xz + 0.5) * terrainShadowScale;
            float d = texture(terrainShadowTex, uv).r - texture(terrainHeightTex, uv).r;
            return 1.0 - smoothstep(0.0, 1.2, d);
        }
        """;

    private void InitSelfShadow()
    {
        Current = this;
        if (SelfShadowsActive) EnsureShadowBaked();
    }

    /// <summary>Bakes the shadow line (first use; ~tens of ms on a 1024 map) and uploads it.</summary>
    private void EnsureShadowBaked()
    {
        if (_shadowBaked) return;
        _shadowBaked = true;
        var sw = Stopwatch.StartNew();
        var line = TerrainShadowBake.Bake(_t.Heights, _t.Width + 1, _t.Height + 1, SunDir);
        TerrainShadowBake.Blur(line, _t.Width + 1, _t.Height + 1);
        LastShadowBakeMs = sw.Elapsed.TotalMilliseconds;
        fixed (float* p = line)
        {
            var img = new Image { Data = p, Width = _t.Width + 1, Height = _t.Height + 1, Mipmaps = 1, Format = PixelFormat.UncompressedR32 };
            _shadowTex = Raylib.LoadTextureFromImage(img);
        }
        Raylib.SetTextureFilter(_shadowTex, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(_shadowTex, TextureWrap.Clamp);
        Console.WriteLine($"terrain shadows: baked {_t.Width + 1}x{_t.Height + 1} in {LastShadowBakeMs:F1} ms");
    }

    /// <summary>Binds the terrain shadow textures and uniforms to a shader that includes <see cref="SelfShadowFs"/>.</summary>
    public static void BindSelfShadow(ShaderProgram p)
    {
        var tr = Current;
        bool on = SelfShadowsActive && tr != null;
        if (on)
        {
            tr!.EnsureShadowBaked();
            Rlgl.ActiveTextureSlot(SelfShadowSlot);
            Rlgl.EnableTexture(tr._shadowTex.Id);
            Rlgl.ActiveTextureSlot(HeightSlot);
            Rlgl.EnableTexture(tr._heightTex.Id);
            Rlgl.ActiveTextureSlot(0);
            p.Set("terrainShadowTex", SelfShadowSlot);
            p.Set("terrainHeightTex", HeightSlot);
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
