using System.Numerics;
using Raylib_cs;
using Rts.Sim.Snapshot;

namespace Rts.Client.Rendering;

/// <summary>
/// Wrecks (<see cref="SimSnapshot.Props"/>): the procedural model of the unit that died, drawn instanced with a
/// burnt-metal shader (desaturated, charred and rusty patches, no team color, dead lights), slightly sunken,
/// tilted and with the turret knocked askew. Fresh wrecks glow with cooling embers and smoke for a while;
/// wrecks sink further as they are reclaimed (Mass / MassMax). Generic props (Bp = -1) use a rubble mesh.
/// Meshes dither out with the unit meshes when zooming out; the strategic view shows small gray dots instead.
/// The models are separate GPU copies (own instance buffers), independent from <see cref="UnitRenderer"/>.
/// </summary>
public sealed class PropRenderer : IDisposable
{
    private readonly GameContext _ctx;
    private readonly ParticleSystem? _ps;
    private readonly Dictionary<int, UnitModel> _models = new();
    private UnitModel? _rubble;
    private readonly List<InstancedMesh> _used = new();
    private readonly Dictionary<int, double> _born = new();
    private readonly List<int> _gone = new();
    private readonly HashSet<int> _alive = new();
    private ShaderProgram? _shader;
    private double _gameTime;
    private float _smokeAcc;

    /// <summary>Seconds (game time) a fresh wreck glows / smokes.</summary>
    public const float EmberSeconds = 22f;

    public int DrawnProps { get; private set; }

    public PropRenderer(GameContext ctx, ParticleSystem? particles = null)
    {
        _ctx = ctx;
        _ps = particles;
    }

    private ShaderProgram Shader => _shader ??= new ShaderProgram(VertexShader, FragmentShader);

    private UnitModel Model(int bp)
    {
        if ((uint)bp >= (uint)_ctx.Db.Count) return _rubble ??= BuildRubble();
        if (!_models.TryGetValue(bp, out var m)) _models[bp] = m = UnitModels.Build(_ctx.Db[bp]);
        return m;
    }

    private static float Hash(int id, int salt)
    {
        uint h = (uint)id * 0x9E3779B1u ^ (uint)salt * 0x85EBCA77u;
        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
        return (h & 0xFFFFFF) / 16777216f;
    }

    /// <summary>Draws wrecks. Call inside BeginMode3D after the units (before water). dt is the real frame time;
    /// embers cool and smoke rises in game time (frozen while paused).</summary>
    public void Draw(SimSnapshot cur, float dt)
    {
        float gameDt = EffectsRenderer.GameDt(_ctx, dt);
        _gameTime += gameDt;
        TrackAges(cur);
        DrawnProps = 0;
        float fade = UnitRenderer.MeshFade(Atmosphere.CameraDistance);
        if (fade <= 0.001f || cur.PropCount == 0) return;
        var fv = FrameView.Current;

        foreach (var im in _used) im.Clear();
        _used.Clear();
        _smokeAcc += gameDt;
        bool smoke = _ps != null && _smokeAcc >= 0.12f;
        if (smoke) _smokeAcc = 0;

        for (int k = 0; k < cur.PropCount; k++)
        {
            ref var p = ref cur.Props[k];
            var model = Model(p.Bp);
            bool rubble = (uint)p.Bp >= (uint)_ctx.Db.Count;
            var bp = rubble ? null : _ctx.Db[p.Bp];
            float h = MathF.Max(0.3f, model.MaxY - model.MinY);
            if (!fv.SphereVisible(p.Pos + new Vector3(0, h * 0.3f, 0), rubble ? p.Radius * 1.5f + 0.5f : model.Radius)) continue;
            DrawnProps++;

            float r1 = Hash(p.Id, 1), r2 = Hash(p.Id, 2), r3 = Hash(p.Id, 3), r4 = Hash(p.Id, 4);
            float left = p.MassMax > 0 ? Math.Clamp(p.Mass / p.MassMax, 0f, 1f) : 1f;
            double age = _born.TryGetValue(p.Id, out double b) ? _gameTime - b : 1e9;
            float ember = age < EmberSeconds ? MathF.Pow(1f - (float)age / EmberSeconds, 1.5f) : 0f;

            bool structure = bp?.IsStructure ?? false;
            bool air = model.Air;
            float tiltAmp = structure ? 0.05f : air ? 0.45f : 0.3f;
            float pitch = (r1 - 0.5f) * tiltAmp, roll = (r2 - 0.5f) * tiltAmp * (air ? 1.6f : 1f);
            float sink = (structure ? 0.06f : air ? 0.22f : 0.12f) * h + (1f - left) * 0.45f * h;
            float squash = structure ? 0.78f : air ? 0.85f : 0.92f;
            float heading = p.Heading + (structure ? 0f : (r3 - 0.5f) * 0.4f);
            float scale = rubble ? MathF.Max(0.6f, p.Radius) : 1f;

            var body = Matrix4x4.CreateScale(scale, squash * scale, scale)
                       * Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationZ(roll)
                       * Matrix4x4.CreateRotationY(MathF.PI * 0.5f - heading)
                       * Matrix4x4.CreateTranslation(p.Pos.X, p.Pos.Y - sink, p.Pos.Z);
            var data = new Vector4(r4, ember, left, 0);
            Add(model.Body, body, data);

            if (model.Turret != null)
            {
                // Turret twisted to a random angle; sometimes blown off and lying next to the hull.
                bool blownOff = !structure && r4 < 0.3f;
                float yaw = (r3 * 2f - 1f) * MathF.PI;
                foreach (var pivot in model.TurretPivots)
                {
                    Matrix4x4 local;
                    if (blownOff)
                    {
                        var side = new Vector3(r1 < 0.5f ? -1f : 1f, 0, r2 - 0.5f) * (model.Radius * 0.55f);
                        local = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationZ(0.5f * (r1 < 0.5f ? 1 : -1))
                                * Matrix4x4.CreateTranslation(pivot.X + side.X, pivot.Y * 0.25f, pivot.Z + side.Z);
                    }
                    else local = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationX((r2 - 0.5f) * 0.25f) * Matrix4x4.CreateTranslation(pivot);
                    Add(model.Turret, local * body, data with { W = 1 });
                }
            }

            if (smoke && ember > 0.05f && Atmosphere.CameraDistance < 220f)
            {
                var top = new Vector3(p.Pos.X + (r1 - 0.5f) * p.Radius, p.Pos.Y + h * 0.5f, p.Pos.Z + (r2 - 0.5f) * p.Radius);
                float s = Math.Clamp(p.Radius, 0.5f, 3f);
                _ps!.Smoke(top, 0.35f * s, 2.2f + 1.5f * ember, new Vector4(0.16f, 0.15f, 0.14f, 0.35f * ember + 0.08f), new Vector3(0.25f, 0.9f, 0.1f), 2.6f);
            }
        }
        if (_used.Count == 0) return;

        var sp = Shader;
        sp.Use();
        sp.SetMatrix("viewProj", fv.ViewProjGl);
        Atmosphere.Apply(sp, fv);
        sp.Set("fade", fade);
        ShadowMap.Bind(sp);
        sp.Use();
        foreach (var im in _used) im.Draw();
        Rlgl.DisableShader();
    }

    private void Add(InstancedMesh im, in Matrix4x4 m, Vector4 data)
    {
        if (im.Count == 0) _used.Add(im);
        im.Add(m, data);
    }

    private void TrackAges(SimSnapshot cur)
    {
        _alive.Clear();
        for (int k = 0; k < cur.PropCount; k++)
        {
            int id = cur.Props[k].Id;
            _alive.Add(id);
            _born.TryAdd(id, _gameTime);
        }
        if (_born.Count == _alive.Count) return;
        _gone.Clear();
        foreach (var id in _born.Keys) if (!_alive.Contains(id)) _gone.Add(id);
        foreach (var id in _gone) _born.Remove(id);
    }

    /// <summary>Strategic zoom: wrecks as tiny gray dots (2D, call before the unit icons).</summary>
    public void DrawStrategic(SimSnapshot cur)
    {
        float fade = IconRenderer.IconFade(_ctx.Camera.Distance);
        if (fade <= 0.001f || cur.PropCount == 0) return;
        var fv = FrameView.Current;
        byte a = (byte)(200 * fade);
        var col = new Color((byte)150, (byte)146, (byte)136, a);
        var edge = new Color((byte)20, (byte)20, (byte)20, (byte)(160 * fade));
        for (int k = 0; k < cur.PropCount; k++)
        {
            ref var p = ref cur.Props[k];
            if (!fv.WorldToScreen(p.Pos, out var s)) continue;
            if (s.X < -4 || s.Y < -4 || s.X > fv.ScreenW + 4 || s.Y > fv.ScreenH + 4) continue;
            int size = p.Radius >= 2f ? 4 : 3;
            int x = (int)s.X - size / 2, y = (int)s.Y - size / 2;
            Raylib.DrawRectangle(x - 1, y - 1, size + 2, size + 2, edge);
            Raylib.DrawRectangle(x, y, size, size, col);
        }
    }

    /// <summary>Generic rubble: a pile of tilted dark chunks around the origin (unit radius ≈ 1).</summary>
    private static UnitModel BuildRubble()
    {
        var b = new MeshBuilder();
        var dark = Mat.M(70, 66, 62);
        var rust = Mat.M(96, 72, 54);
        uint s = 12345;
        float R() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFF) / 65536f; }
        for (int i = 0; i < 9; i++)
        {
            float ang = R() * MathF.Tau, d = R() * 0.7f;
            var size = new Vector3(0.25f + R() * 0.45f, 0.12f + R() * 0.3f, 0.25f + R() * 0.5f);
            var xf = Matrix4x4.CreateRotationY(R() * MathF.Tau) * Matrix4x4.CreateRotationX((R() - 0.5f) * 0.8f)
                     * Matrix4x4.CreateTranslation(MathF.Cos(ang) * d, size.Y * 0.3f, MathF.Sin(ang) * d);
            b.Box(Vector3.Zero, size, i % 3 == 0 ? rust : dark, null, xf);
        }
        var (minY, maxY, _) = b.Bounds();
        return new UnitModel { Body = new InstancedMesh(b.Upload()), MinY = minY, MaxY = maxY, Radius = 1.2f };
    }

    public void Dispose()
    {
        foreach (var m in _models.Values)
        {
            m.Body.Dispose();
            m.Turret?.Dispose();
        }
        _models.Clear();
        _rubble?.Body.Dispose();
        _rubble = null;
    }

    // ------------------------------------------------------------------ shader

    private const string VertexShader = """
        #version 330
        in vec3 vertexPosition;
        in vec3 vertexNormal;
        in vec4 vertexColor;
        layout(location = 10) in mat4 instTransform;
        layout(location = 14) in vec4 instData;
        uniform mat4 viewProj;
        out vec3 vWorld;
        out vec3 vNormal;
        out vec4 vColor;
        out vec4 vInst;
        void main() {
            vec4 wp = instTransform * vec4(vertexPosition, 1.0);
            vWorld = wp.xyz;
            vNormal = mat3(instTransform) * vertexNormal;
            vColor = vertexColor;
            vInst = instData;
            gl_Position = viewProj * wp;
        }
        """;

    // vInst: x = per-wreck random, y = ember glow 0..1, z = mass left 0..1, w = 1 for turrets.
    private static readonly string FragmentShader = "#version 330\n" + Shaders.CommonFs + Shaders.ShadowFs + """
        in vec3 vWorld;
        in vec3 vNormal;
        in vec4 vColor;
        in vec4 vInst;
        uniform float fade;
        out vec4 finalColor;
        void main() {
            if (fade < 0.999 && bayer4(gl_FragCoord.xy) >= fade) discard;
            vec3 n = normalize(vNormal);
            float m = vColor.a;
            vec3 base = vColor.rgb;
            if (m < 0.25) base = vec3(0.30, 0.29, 0.28) * (vColor.r * 1.3);   // team paint burnt off
            else if (m < 0.75) base = vec3(0.07, 0.07, 0.07);                 // dead lights
            float lum = dot(base, vec3(0.3, 0.59, 0.11));
            vec3 albedo = mix(vec3(lum), base, 0.3) * 0.62;
            vec2 q = vWorld.xz * 1.7 + vec2(vInst.x * 41.0, vInst.x * 17.0) + vec2(vWorld.y * 1.3);
            float burn = fbm(q);
            albedo = mix(albedo, vec3(0.05, 0.045, 0.04), smoothstep(0.45, 0.66, burn) * 0.9);
            float rust = smoothstep(0.58, 0.78, fbm(q * 2.3 + 7.0));
            albedo = mix(albedo, vec3(0.30, 0.17, 0.09) * 0.75, rust * 0.55);
            // Reclaimed wrecks fade towards a dull gray.
            albedo = mix(vec3(0.16), albedo, 0.45 + 0.55 * vInst.z);

            vec3 V = normalize(camPos - vWorld);
            float ndl = max(dot(n, sunDir), 0.0);
            float sh = shadowFactor(vWorld, n);
            vec3 H = normalize(sunDir + V);
            float spec = pow(max(dot(n, H), 0.0), 24.0) * 0.1 * ndl * sh * (1.0 - smoothstep(0.45, 0.66, burn));
            vec3 amb = mix(groundAmb, skyAmb, n.y * 0.5 + 0.5);
            vec3 c = albedo * (amb * 0.95 + sunColor * ndl * mix(0.3, 0.95, sh)) + sunColor * spec;

            float ember = vInst.y;
            if (ember > 0.001) {
                // Thin glowing cracks (ridges of the noise) inside the charred areas.
                float rn = fbm(vWorld.xz * 4.0 + vec2(vWorld.y * 3.0) + vec2(vInst.x * 13.0));
                float cracks = smoothstep(0.88, 0.97, 1.0 - abs(rn * 2.0 - 1.0)) * smoothstep(0.4, 0.6, burn);
                float flicker = 0.7 + 0.3 * sin(time * 8.0 + vWorld.x * 3.1 + vWorld.z * 2.3);
                c += vec3(1.0, 0.36, 0.07) * cracks * ember * flicker * 2.2;
            }
            c = applyFog(c, vWorld);
            finalColor = vec4(c, 1.0);
        }
        """;
}
