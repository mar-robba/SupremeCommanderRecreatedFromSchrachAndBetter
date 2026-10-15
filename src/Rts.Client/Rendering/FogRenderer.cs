using System.Numerics;
using Raylib_cs;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Client side of the fog of war (sim: <c>Rts.Sim/Intel</c>). The sim filters snapshots for the local army
/// (<see cref="Rts.Sim.SimRunner.ViewArmy"/>): unseen enemies, their shots and effects are simply absent, radar contacts
/// come as <see cref="SimSnapshot.Blips"/> and last-seen structures as <see cref="SimSnapshot.Ghosts"/>.
/// This class:
/// <list type="bullet">
/// <item>uploads the explored/vision grid (<see cref="SimSnapshot.Fog"/>) into two R8 textures (previous and current
/// tick, bilinear) that world shaders sample through <see cref="Shaders.FogOfWarFs"/> (blended by the interpolation
/// alpha, so fog edges move smoothly): unexplored = dark, explored = dim/desaturated, in vision = normal;</item>
/// <item>draws radar blips as generic army-colored contacts (screen space, every zoom level);</item>
/// <item>draws the fog, ghosts and blips on the minimap;</item>
/// <item>F7 / <c>--nofog</c>: reveal everything (the runner stops filtering, ViewArmy = -1).</item>
/// </list>
/// </summary>
public sealed class FogRenderer : IDisposable
{
    public const int TexSlotPrev = 5, TexSlotCur = 6;
    private const float TickSeconds = 1f / SimWorld.TicksPerSecond;

    private readonly GameContext _ctx;
    private Texture2D _prev, _cur;
    private int _cols, _rows;
    private float _cell = 1;
    private int _tick = int.MinValue;
    private bool _hasFog;
    private float _alpha = 1;
    private ShaderProgram? _minimapShader;

    /// <summary>Fog of war on (false = reveal all, debug key F7 / CLI --nofog).</summary>
    public bool Enabled { get; private set; }
    /// <summary>The current frame has fog data (enabled and the snapshot is filtered).</summary>
    public bool Active => Enabled && _hasFog;

    public static FogRenderer? Current { get; private set; }

    public FogRenderer(GameContext ctx, bool enabled)
    {
        _ctx = ctx;
        Current = this;
        SetEnabled(enabled);
    }

    public void SetEnabled(bool on)
    {
        Enabled = on;
        _ctx.Runner.ViewArmy = on ? _ctx.LocalArmy : -1;
        _tick = int.MinValue; // re-upload both textures when data comes back
    }

    /// <summary>Call once per frame after the interpolator update (keys = false while a menu overlay is open).</summary>
    public void Update(SnapshotInterpolator interp, bool keys = true)
    {
        if (keys && Raylib.IsKeyPressed(Input.Hotkeys.Fog.Key)) SetEnabled(!Enabled);
        var cur = interp.Current;
        _alpha = interp.Alpha;
        _hasFog = cur.ViewArmy >= 0 && cur.FogCols > 0 && cur.Fog.Length >= cur.FogCols * cur.FogRows;
        if (!_hasFog || !Enabled) return;

        if (cur.FogCols != _cols || cur.FogRows != _rows)
        {
            Unload();
            _cols = cur.FogCols;
            _rows = cur.FogRows;
            _prev = Create(_cols, _rows);
            _cur = Create(_cols, _rows);
            _tick = int.MinValue;
        }
        _cell = cur.FogCellSize;
        if (cur.Tick == _tick) return;
        bool first = _tick == int.MinValue;
        (_prev, _cur) = (_cur, _prev);
        Raylib.UpdateTexture(_cur, cur.Fog);
        if (first) Raylib.UpdateTexture(_prev, cur.Fog);
        _tick = cur.Tick;
    }

    private static unsafe Texture2D Create(int w, int h)
    {
        var data = new byte[w * h];
        Texture2D t;
        fixed (byte* p = data)
        {
            var img = new Image { Data = p, Width = w, Height = h, Mipmaps = 1, Format = PixelFormat.UncompressedGrayscale };
            t = Raylib.LoadTextureFromImage(img);
        }
        Raylib.SetTextureFilter(t, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(t, TextureWrap.Clamp);
        return t;
    }

    /// <summary>Binds the fog textures and uniforms of <see cref="Shaders.FogOfWarFs"/> (fogOn = 0 without fog).</summary>
    public static void Bind(ShaderProgram p)
    {
        var f = Current;
        bool on = f != null && f.Active && f._cur.Id != 0;
        if (on)
        {
            Rlgl.ActiveTextureSlot(TexSlotPrev);
            Rlgl.EnableTexture(f!._prev.Id);
            Rlgl.ActiveTextureSlot(TexSlotCur);
            Rlgl.EnableTexture(f._cur.Id);
            Rlgl.ActiveTextureSlot(0);
            p.Set("fogPrev", TexSlotPrev);
            p.Set("fogCur", TexSlotCur);
            p.Set("fogAlpha", f._alpha);
            p.Set("fogScale", new Vector2(1f / (f._cols * f._cell), 1f / (f._rows * f._cell)));
            p.Set("fogTexel", new Vector2(1f / f._cols, 1f / f._rows));
        }
        p.Set("fogOn", on ? 1f : 0f);
    }

    // ------------------------------------------------------------------ radar blips (3D view)

    /// <summary>Blip position interpolated like projectiles (one tick behind, along the velocity).</summary>
    public static Vector3 BlipPos(in BlipView b, float alpha) =>
        b.Pos - new Vector3(b.Vel.X, 0, b.Vel.Y) * (TickSeconds * (1f - alpha));

    private Texture2D _blipTex;

    /// <summary>64x64 blip sprite: white ring + center dot on a soft dark halo (tinted with the army color).</summary>
    private Texture2D BlipTexture()
    {
        if (_blipTex.Id != 0) return _blipTex;
        const int n = 64;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float d = MathF.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f));
            float ring = Math.Clamp(1f - MathF.Abs(d - 21f) / 3.2f, 0, 1);
            float dot = Math.Clamp(7.5f - d, 0, 1);
            float w = MathF.Max(ring, dot);
            float halo = Math.Clamp((30f - d) / 6f, 0, 1) * 0.45f;
            float a = w + halo * (1 - w);
            byte c = (byte)(a > 0 ? 255 * w / a : 0);
            px[y * n + x] = new Color(c, c, c, (byte)(255 * a));
        }
        var img = Raylib.GenImageColor(n, n, Color.Blank);
        _blipTex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.UpdateTexture(_blipTex, px);
        Raylib.SetTextureFilter(_blipTex, TextureFilter.Bilinear);
        return _blipTex;
    }

    private void DrawBlipSprite(Vector2 s, float size, Color col)
    {
        var tex = BlipTexture();
        Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), new Rectangle(s.X - size * 0.5f, s.Y - size * 0.5f, size, size), Vector2.Zero, 0, col);
    }

    /// <summary>Radar contacts: army-colored pulsing rings with a dot (no type information), constant screen size.
    /// One batched textured quad per blip (thousands are cheap).</summary>
    public void DrawBlips(SimSnapshot cur, float alpha)
    {
        if (cur.BlipCount == 0) return;
        var fv = FrameView.Current;
        float t = (float)Raylib.GetTime();
        for (int k = 0; k < cur.BlipCount; k++)
        {
            ref var b = ref cur.Blips[k];
            if (!fv.WorldToScreen(BlipPos(b, alpha) + new Vector3(0, 0.6f, 0), out var s)) continue;
            if (s.X < -20 || s.Y < -20 || s.X > fv.ScreenW + 20 || s.Y > fv.ScreenH + 20) continue;
            var col = (uint)b.Army < (uint)_ctx.ArmyColors.Length ? _ctx.ArmyColors[b.Army] : Color.Red;
            col = ColorUtil.Lerp(col, Color.White, 0.2f);
            float pulse = 0.5f + 0.5f * MathF.Sin(t * 4f + b.Id * 1.7f);
            DrawBlipSprite(s, 20f + pulse * 3f, ColorUtil.WithAlpha(col, 240));
            DrawBlipSprite(s, 28f + pulse * 12f, ColorUtil.WithAlpha(col, (byte)(90 * (1 - pulse))));
        }
    }

    // ------------------------------------------------------------------ minimap

    private const string MinimapVs = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec4 vertexColor;
        uniform mat4 mvp;
        out vec2 fragTexCoord;
        out vec4 fragColor;
        void main() {
            fragTexCoord = vertexTexCoord;
            fragColor = vertexColor;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string MinimapFs = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        uniform sampler2D texture0;
        out vec4 finalColor;
        void main() {
            float v = texture(texture0, fragTexCoord).r;
            float explored = smoothstep(0.0, 0.5, v);
            float vis = smoothstep(0.5, 1.0, v);
            float a = mix(mix(0.86, 0.5, explored), 0.0, vis);
            finalColor = vec4(0.015, 0.02, 0.035, a);
        }
        """;

    /// <summary>Darkens the minimap terrain: unexplored almost black, explored dimmed. Call after the terrain image.</summary>
    public void DrawMinimapFog(Rectangle m)
    {
        if (!Active || _cur.Id == 0) return;
        _minimapShader ??= new ShaderProgram(MinimapVs, MinimapFs);
        Raylib.BeginShaderMode(_minimapShader.Shader);
        Raylib.DrawTexturePro(_cur, new Rectangle(0, 0, _cols, _rows), m, Vector2.Zero, 0, Color.White);
        Raylib.EndShaderMode();
    }

    /// <summary>Ghost structures (dim outlined squares) and radar blips (rings) on the minimap.</summary>
    public void DrawMinimapContacts(Rectangle m, SimSnapshot snap, float dot)
    {
        var t = _ctx.Terrain;
        float k = m.Width / t.Width;
        for (int i = 0; i < snap.GhostCount; i++)
        {
            ref var g = ref snap.Ghosts[i];
            var bp = _ctx.Db[g.Bp];
            var p = new Vector2(m.X + g.Pos.X / t.Width * m.Width, m.Y + g.Pos.Z / t.Height * m.Height);
            float s = MathF.Max(dot + 1, MathF.Max(bp.FootprintX, bp.FootprintZ) * k);
            var col = ColorUtil.Scale(_ctx.ArmyColors[g.Army], 0.6f);
            Raylib.DrawRectangleV(p - new Vector2(s * 0.5f, s * 0.5f), new Vector2(s, s), ColorUtil.WithAlpha(col, 150));
            Raylib.DrawRectangleLinesEx(new Rectangle(p.X - s * 0.5f - 1, p.Y - s * 0.5f - 1, s + 2, s + 2), 1, new Color(0, 0, 0, 160));
        }
        for (int i = 0; i < snap.BlipCount; i++)
        {
            ref var b = ref snap.Blips[i];
            var p = new Vector2(m.X + b.Pos.X / t.Width * m.Width, m.Y + b.Pos.Z / t.Height * m.Height);
            var col = (uint)b.Army < (uint)_ctx.ArmyColors.Length ? _ctx.ArmyColors[b.Army] : Color.Red;
            DrawBlipSprite(p, dot * 2.6f + 4f, col);
        }
    }

    /// <summary>Demo: a powered T1 radar (+ power generator) in front of each army's start, so radar blips show up.</summary>
    public static void SpawnDemoRadars(Rts.Sim.Simulation sim)
    {
        var w = sim.World;
        int radar = w.Blueprints.IndexOf("uef_t1_radar"), pgen = w.Blueprints.IndexOf("uef_t1_power_generator");
        if (radar < 0 || pgen < 0) return;
        var center = new Vector2(w.Terrain.Width * 0.5f, w.Terrain.Height * 0.5f);
        for (int a = 0; a < w.Armies.Length; a++)
        {
            var sp = w.Terrain.StartPositions[a % w.Terrain.StartPositions.Length];
            var away = Vector2.Normalize(sp - center);
            var side = new Vector2(-away.Y, away.X);
            w.SpawnUnit(radar, a, w.Terrain.Clamp(sp - away * 55f + side * 14f, 4f), 0);
            w.SpawnUnit(pgen, a, w.Terrain.Clamp(sp - away * 55f + side * 18f, 4f), 0);
        }
    }

    private void Unload()
    {
        if (_prev.Id != 0) Raylib.UnloadTexture(_prev);
        if (_cur.Id != 0) Raylib.UnloadTexture(_cur);
        _prev = default;
        _cur = default;
    }

    public void Dispose()
    {
        Unload();
        if (_blipTex.Id != 0) Raylib.UnloadTexture(_blipTex);
        if (_minimapShader != null) Raylib.UnloadShader(_minimapShader.Shader);
        if (Current == this) Current = null;
    }
}
