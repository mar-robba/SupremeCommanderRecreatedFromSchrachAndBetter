using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Shield domes from <see cref="SimSnapshot.Shields"/>: translucent ellipsoids (fresnel rim + hex lattice, army-tinted
/// blue), expanding ripples + flash where <see cref="EffectKind.ShieldHit"/> events land, a fade-out when a shield
/// collapses and a fade-in when it comes back up; dimmed by the fog of war. Mobile domes follow the interpolated unit.
/// Also draws the shield HP bars (2D, above the unit health bars).
/// </summary>
public sealed class ShieldRenderer : IDisposable
{
    private const int MaxHits = 6;
    private const float RippleLife = 1.1f;

    private sealed class Fx
    {
        public float Vis;
        public bool Up;
        public float Flash;
        public readonly Vector4[] Hits = new Vector4[MaxHits];
        public int Next;
        public int Frame;
        public Vector3 Center;
        public float Radius, HeightScale, HpFrac, RechargeFrac;
        public int Army, Unit, Gen, Bp;
        public float Dist;
    }

    private readonly GameContext _ctx;
    private readonly Dictionary<long, Fx> _fx = new();
    private readonly List<Fx> _draw = new();
    private readonly Dictionary<int, int> _unitSlot = new();
    private readonly bool[] _hasShield;
    private ShaderProgram? _prog;
    private Mesh _mesh;
    private Material _mat;
    private bool _gpu;
    private int _frame;

    public ShieldRenderer(GameContext ctx)
    {
        _ctx = ctx;
        _hasShield = ctx.Db.All.Select(b => b.Shield != null).ToArray();
    }

    private void EnsureGpu()
    {
        if (_gpu) return;
        _gpu = true;
        _prog = new ShaderProgram(Vs, Fs);
        _mesh = Raylib.GenMeshSphere(1f, 28, 40);
        _mat = Raylib.LoadMaterialDefault();
        _mat.Shader = _prog.Shader;
    }

    private static long Key(int unit, int gen) => ((long)unit << 32) | (uint)gen;

    /// <summary>Updates dome states from the snapshot + new effects and draws them (inside the 3D pass, after opaque geometry).</summary>
    public void Draw(float dt, SimSnapshot cur, SimSnapshot? prev, float alpha, List<EffectEvent> newEffects)
    {
        if (cur.ShieldCount == 0 && _fx.Count == 0) return;
        float gdt = EffectsRenderer.GameDt(_ctx, dt);
        _frame++;
        // Interpolated unit positions for mobile shields.
        _unitSlot.Clear();
        for (int k = 0; k < cur.UnitCount; k++)
            if ((uint)cur.Units[k].Bp < (uint)_hasShield.Length && _hasShield[cur.Units[k].Bp]) _unitSlot[cur.Units[k].Index] = k;

        for (int k = 0; k < cur.ShieldCount; k++)
        {
            ref var s = ref cur.Shields[k];
            long key = Key(s.Unit, s.Gen);
            if (!_fx.TryGetValue(key, out var f))
            {
                _fx[key] = f = new Fx { Vis = s.Up ? 1f : 0f };
                for (int i = 0; i < MaxHits; i++) f.Hits[i].W = -1;
            }
            f.Frame = _frame;
            if (f.Up && !s.Up) f.Flash = 1f; // collapse flash
            f.Up = s.Up;
            f.Center = s.Center;
            if (_unitSlot.TryGetValue(s.Unit, out int slot))
            {
                ref var v = ref cur.Units[slot];
                var (pos, _) = Rendering.UnitRenderer.Interpolate(v, prev, alpha);
                f.Center = pos + (s.Center - v.Pos);
            }
            f.Radius = s.Radius; f.HeightScale = s.HeightScale; f.HpFrac = s.HpFrac; f.RechargeFrac = s.RechargeFrac;
            f.Army = s.Army; f.Unit = s.Unit; f.Gen = s.Gen; f.Bp = s.Bp;
        }
        // Remove domes no longer in the snapshot (unit died, or went out of sight) after they faded.
        List<long>? dead = null;
        foreach (var (key, f) in _fx)
        {
            if (f.Frame != _frame) { f.Up = false; if (f.Vis <= 0.01f) (dead ??= new()).Add(key); }
        }
        if (dead != null) foreach (var k in dead) _fx.Remove(k);

        // Hit ripples: attach each ShieldHit to the closest dome whose surface it lies on.
        foreach (var e in newEffects)
        {
            if (e.Kind != EffectKind.ShieldHit && e.Kind != EffectKind.ShieldUp) continue;
            Fx? best = null;
            float bestD = float.MaxValue;
            foreach (var f in _fx.Values)
            {
                var d = e.Pos - f.Center;
                d.Y /= MathF.Max(0.2f, f.HeightScale);
                float r = MathF.Abs(d.Length() - f.Radius);
                if (r < bestD && r < f.Radius * 0.35f + 2f) { bestD = r; best = f; }
            }
            if (best == null) continue;
            if (e.Kind == EffectKind.ShieldUp) { best.Flash = MathF.Max(best.Flash, 0.6f); continue; }
            best.Hits[best.Next] = new Vector4(e.Pos, 0f);
            best.Next = (best.Next + 1) % MaxHits;
            best.Flash = MathF.Max(best.Flash, Math.Clamp(e.Size * 0.25f, 0.15f, 0.7f));
        }

        // Animate.
        var cam = FrameView.Current.CamPos;
        _draw.Clear();
        foreach (var f in _fx.Values)
        {
            float target = f.Up ? 1f : 0f;
            f.Vis += (target - f.Vis) * MathF.Min(1f, gdt * (f.Up ? 2.5f : 4f));
            if (MathF.Abs(target - f.Vis) < 0.003f) f.Vis = target;
            f.Flash = MathF.Max(0f, f.Flash - gdt * 2.2f);
            for (int i = 0; i < MaxHits; i++)
                if (f.Hits[i].W >= 0) { f.Hits[i].W += gdt; if (f.Hits[i].W > RippleLife) f.Hits[i].W = -1; }
            if (f.Vis <= 0.004f && f.Flash <= 0.004f) continue;
            if (!FrameView.Current.SphereVisible(f.Center, f.Radius)) continue;
            f.Dist = Vector3.DistanceSquared(cam, f.Center);
            _draw.Add(f);
        }
        if (_draw.Count == 0) return;
        _draw.Sort((a, b) => b.Dist.CompareTo(a.Dist)); // far to near for blending
        EnsureGpu();
        var p = _prog!;
        Rlgl.DrawRenderBatchActive();
        p.Set("camPos", cam);
        p.Set("time", Atmosphere.Time);
        FogRenderer.Bind(p);
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Alpha);
        int hitsLoc = p.Loc("hits");
        foreach (var f in _draw)
        {
            var army = (uint)f.Army < (uint)_ctx.ArmyColors.Length ? _ctx.ArmyColors[f.Army] : Color.SkyBlue;
            var tint = Vector3.Lerp(new Vector3(0.35f, 0.62f, 1.0f), new Vector3(army.R, army.G, army.B) / 255f, 0.3f);
            p.Set("color", tint);
            p.Set("alpha", MathF.Min(1f, f.Vis + f.Flash * 0.5f));
            p.Set("flash", f.Flash);
            p.Set("hp", f.Up ? f.HpFrac : 0f);
            p.Set("radii", new Vector3(f.Radius, f.Radius * f.HeightScale, f.Radius));
            p.Set("center", f.Center);
            if (hitsLoc >= 0) Raylib.SetShaderValueV(p.Shader, hitsLoc, f.Hits, ShaderUniformDataType.Vec4, MaxHits);
            var model = Matrix4x4.CreateScale(f.Radius, f.Radius * f.HeightScale, f.Radius) * Matrix4x4.CreateTranslation(f.Center);
            // Inner (back) faces first, dimmer, then the outer surface.
            Rlgl.SetCullFace(0);
            p.Set("side", 0.45f);
            Raylib.DrawMesh(_mesh, _mat, Matrix4x4.Transpose(model));
            Rlgl.SetCullFace(1);
            p.Set("side", 1f);
            Raylib.DrawMesh(_mesh, _mat, Matrix4x4.Transpose(model));
        }
        Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
    }

    /// <summary>Shield HP bars (2D, after EndMode3D): selected, damaged, or recharging shields, above the health bar.</summary>
    public void DrawBars(SimSnapshot cur, SimSnapshot? prev, float alpha, HashSet<long> selected)
    {
        if (_ctx.Camera.Distance > IconRenderer.IconDistance) return;
        foreach (var f in _fx.Values)
        {
            if (f.Frame != _frame) continue;
            bool sel = selected.Contains(Key(f.Unit, f.Gen));
            if (!sel && f.Up && f.HpFrac >= 0.999f) continue;
            var bp = _ctx.Db[f.Bp];
            var basePos = new Vector3(f.Center.X, f.Center.Y - (bp.Shield?.CenterHeight ?? 0), f.Center.Z);
            var sp = Raylib.GetWorldToScreen(basePos + new Vector3(0, bp.Visual.Size.Y + 1.0f, 0), _ctx.Camera.Camera);
            float w = Math.Clamp(bp.Visual.Size.X * 2400f / _ctx.Camera.Distance / 40f, 16f, 60f);
            int x = (int)(sp.X - w / 2), y = (int)sp.Y - 5;
            Raylib.DrawRectangle(x - 1, y - 1, (int)w + 2, 4, Color.Black);
            if (f.Up) Raylib.DrawRectangle(x, y, (int)(w * f.HpFrac), 2, new Color(90, 190, 255, 255));
            else Raylib.DrawRectangle(x, y, (int)(w * f.RechargeFrac), 2, new Color(120, 120, 150, 255));
        }
    }

    public void Dispose()
    {
        if (!_gpu) return;
        Raylib.UnloadMesh(_mesh);
        if (_prog != null) Raylib.UnloadShader(_prog.Shader);
    }

    // ------------------------------------------------------------------ shader

    private const string Vs = """
        #version 330
        in vec3 vertexPosition;
        in vec3 vertexNormal;
        uniform mat4 mvp;
        uniform mat4 matModel;
        uniform vec3 radii;
        out vec3 vWorld;
        out vec3 vNormal;
        out vec3 vLocal;
        void main() {
            vWorld = (matModel * vec4(vertexPosition, 1.0)).xyz;
            vNormal = normalize(vertexNormal / max(radii, vec3(0.001)));
            vLocal = vertexPosition;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private static readonly string Fs = "#version 330\n" + Shaders.FogOfWarFs + """
        in vec3 vWorld;
        in vec3 vNormal;
        in vec3 vLocal;
        uniform vec3 camPos;
        uniform float time;
        uniform vec3 color;
        uniform float alpha;
        uniform float flash;
        uniform float hp;
        uniform float side;
        uniform vec3 radii;
        uniform vec3 center;
        uniform vec4 hits[6];
        out vec4 fragColor;

        float hexDist(vec2 p) { p = abs(p); return max(dot(p, vec2(0.5, 0.8660254)), p.x); }
        // 0 on hex cell edges, 0.5 at cell centers.
        float hexEdge(vec2 uv) {
            vec2 r = vec2(1.0, 1.7320508);
            vec2 h = r * 0.5;
            vec2 a = mod(uv, r) - h;
            vec2 b = mod(uv - h, r) - h;
            vec2 g = dot(a, a) < dot(b, b) ? a : b;
            return 0.5 - hexDist(g);
        }

        void main() {
            vec3 N = normalize(vNormal);
            vec3 V = normalize(camPos - vWorld);
            float ndv = abs(dot(N, V));
            float fres = pow(1.0 - ndv, 2.4);
            // Hex lattice: top-down projection near the pole, cylindrical (arc length, height) on the sides.
            float ny = abs(N.y);
            float cell = 1.0 / 2.4;
            float lon = atan(vLocal.z, vLocal.x) * radii.x;
            float e1 = hexEdge((vWorld.xz - center.xz) * cell);
            float e2 = hexEdge(vec2(lon, vWorld.y - center.y) * cell);
            float e = mix(e2, e1, smoothstep(0.35, 0.75, ny));
            float lattice = 1.0 - smoothstep(0.0, 0.06, e);
            float pulse = 0.75 + 0.25 * sin(time * 2.0 + vWorld.y * 0.4 + lon * 0.15);
            float ripple = 0.0;
            for (int i = 0; i < 6; i++) {
                vec4 h = hits[i];
                if (h.w < 0.0) continue;
                float d = distance(vWorld, h.xyz);
                float front = h.w * 16.0;
                float ring = exp(-pow((d - front) / 1.8, 2.0)) * max(0.0, 1.0 - h.w / 1.1);
                float core = exp(-d * d / 9.0) * max(0.0, 1.0 - h.w / 0.45);
                ripple += 1.3 * ring + 2.0 * core;
            }
            float weak = 1.0 - clamp(hp, 0.0, 1.0);
            float flicker = weak * weak * (0.5 + 0.5 * sin(time * 23.0 + vWorld.x));
            float a = 0.05 + 0.5 * fres + 0.16 * lattice * pulse * (0.35 + 0.65 * fres) + 0.75 * ripple + 0.35 * flash;
            a *= 1.0 - 0.5 * flicker;
            a *= alpha * side;
            vec3 c = color * (0.75 + 0.9 * fres) + vec3(0.55, 0.8, 1.0) * (ripple + flash * 0.8) + color * lattice * 0.4;
            float vis = fogVisibility(vWorld.xz);
            a *= smoothstep(0.25, 0.8, vis);
            fragColor = vec4(c, clamp(a, 0.0, 0.9));
        }
        """;
}
