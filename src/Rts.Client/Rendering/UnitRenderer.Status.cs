using System.Numerics;
using Raylib_cs;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Stealth/cloak/EMP status visuals (<see cref="UnitView.Status"/>, logic in <see cref="StatusFx"/>), drawn by
/// <see cref="DrawStatus"/> right after the water:
/// - Cloaked: the opaque pass skips the unit (no shadow either); here it is drawn as a translucent, slightly
///   team-tinted shimmer (fresnel rim + drifting ripples + a scan band), no depth writes. A cloaked unit under water
///   stays a normal submerged unit (UnitRenderer.Submerged.cs).
/// - Stunned: flickering blue-white spark bolts around the unit, deterministic per unit and client-time bucket.
/// - StealthField: a faint violet ring (soft fill towards the edge) following the terrain at the field radius.
/// The snapshot only flags stealth/cloak/field on own and allied units (all units for an observer), stun on all.
/// </summary>
public sealed unsafe partial class UnitRenderer
{
    private readonly List<(UnitModel model, int index, Vector3 pos, float heading, float turret, float turn, Vector4 col)> _cloaked = new();
    private readonly List<(int index, Vector3 center, float radius)> _stunned = new();
    private readonly List<(Vector3 center, float radius)> _fields = new();
    private static ShaderProgram? _cloakShader; // shared by every game (like Shaders.*)

    /// <summary>Cloaked / stunned / field-projecting units drawn last frame (perf overlay, tests by eye).</summary>
    public int StatusUnits => _cloaked.Count + _stunned.Count + _fields.Count;

    /// <summary>Draws the status visuals of the units in <paramref name="cur"/>. Call right after
    /// <see cref="DrawSubmerged"/> (inside BeginMode3D, same frame as <see cref="Draw"/>).</summary>
    public void DrawStatus(SimSnapshot cur, SimSnapshot? prev, float alpha, Color[] armyColors)
    {
        _cloaked.Clear();
        _stunned.Clear();
        _fields.Clear();
        float fade = MeshFade(Atmosphere.CameraDistance);
        if (fade <= 0.001f) return;
        var fv = FrameView.Current;
        float time = Atmosphere.Time;

        for (int k = 0; k < cur.UnitCount; k++)
        {
            ref var v = ref cur.Units[k];
            if (v.Status == UnitStatus.None) continue;
            var vis = StatusFx.VisualsFor(v.Status, (v.Flags & UnitFlags.UnderConstruction) != 0);
            if (vis == StatusVisual.None) continue;
            var model = Model(v.Bp);
            Vector3 pos;
            float heading, turret, turn = 0;
            if (prev != null && prev.TryGetUnit(v.Index, v.Gen, out var pv))
            {
                pos = Vector3.Lerp(pv.Pos, v.Pos, alpha);
                turn = Rts.Sim.Core.DMath.AngleDiff(pv.Heading, v.Heading);
                heading = pv.Heading + turn * alpha;
                turret = pv.TurretHeading + Rts.Sim.Core.DMath.AngleDiff(pv.TurretHeading, v.TurretHeading) * alpha;
            }
            else { pos = v.Pos; heading = v.Heading; turret = v.TurretHeading; }

            if ((vis & StatusVisual.Field) != 0)
            {
                float r = StatusFx.FieldRadius(_db[v.Bp], v.Status);
                if (r > 0 && fv.SphereVisible(pos, r)) _fields.Add((pos, r));
            }
            var center = pos + new Vector3(0, (model.MinY + model.MaxY) * 0.5f, 0);
            if (!fv.SphereVisible(center, model.Radius)) continue;
            if ((vis & StatusVisual.Cloak) != 0 && !IsSubmerged(model, pos))
            {
                var col = (uint)v.Army < (uint)armyColors.Length
                    ? new Vector4(armyColors[v.Army].R / 255f, armyColors[v.Army].G / 255f, armyColors[v.Army].B / 255f, 0)
                    : new Vector4(0.7f, 0.7f, 0.7f, 0);
                _cloaked.Add((model, v.Index, pos, heading, turret, turn, col));
            }
            if ((vis & StatusVisual.Stun) != 0) _stunned.Add((v.Index, center, MathF.Max(0.5f, model.Radius * 0.75f)));
        }
        if (_cloaked.Count + _stunned.Count + _fields.Count == 0) return;

        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        if (_fields.Count > 0)
        {
            Raylib.BeginBlendMode(BlendMode.Alpha);
            foreach (var (c, r) in _fields) DrawFieldRing(c, r, time, fade);
            Rlgl.DrawRenderBatchActive();
            Raylib.EndBlendMode();
        }
        Rlgl.EnableBackfaceCulling();
        if (_cloaked.Count > 0) DrawCloaked(fv, fade, time);
        if (_stunned.Count > 0)
        {
            Rlgl.DisableBackfaceCulling();
            Raylib.BeginBlendMode(BlendMode.Additive);
            int bucket = StatusFx.TimeBucket(time);
            Span<Vector3> pts = stackalloc Vector3[StatusFx.BoltPoints];
            foreach (var (index, c, r) in _stunned) DrawSparks(index, c, r, bucket, fv.CamPos, fade, pts);
            Rlgl.DrawRenderBatchActive();
            Raylib.EndBlendMode();
            Rlgl.EnableBackfaceCulling();
        }
        Rlgl.EnableDepthMask();
    }

    private void DrawCloaked(FrameView fv, float fade, float time)
    {
        // The earlier passes are done: reuse the instance buffers (as DrawSubmerged does).
        foreach (var m in _active) { m.Body.Clear(); m.Turret?.Clear(); }
        _active.Clear();
        foreach (var (model, index, pos, heading, turret, turn, col) in _cloaked)
            AddUnit(model, index, pos, heading, turret, turn, col, false, 1f, time);

        var sp = _cloakShader ??= new ShaderProgram(CloakVs, CloakFs);
        sp.Use();
        sp.SetMatrix("viewProj", fv.ViewProjGl);
        Atmosphere.Apply(sp, fv);
        sp.Set("fade", fade);
        sp.Use();
        Raylib.BeginBlendMode(BlendMode.Alpha);
        foreach (var m in _active)
        {
            m.Body.Draw();
            m.Turret?.Draw();
            DrawCalls += m.Turret != null ? 2 : 1;
        }
        Raylib.EndBlendMode();
        Rlgl.DisableShader();
    }

    /// <summary>Faint violet ring at the field radius: a soft fill rising towards the edge, a brighter band at the
    /// edge with a slow travelling shimmer, a short fade outside. Vertices sit just above the terrain/water.</summary>
    private void DrawFieldRing(Vector3 c, float r, float time, float fade)
    {
        if (_terrain == null) return;
        int n = Math.Clamp((int)(r * 3f), 48, 192);
        float pulse = (0.85f + 0.15f * MathF.Sin(time * 2.2f)) * fade;
        Span<float> radii = [r * 0.72f, MathF.Max(r * 0.72f, r - 1.1f), r, r + 0.35f];
        Span<float> alphas = [0f, 26f, 150f, 0f];
        Rlgl.CheckRenderBatchLimit((n + 1) * 18);
        Rlgl.Begin((int)DrawMode.Triangles);
        for (int i = 0; i < n; i++)
        {
            float a0 = i * MathF.Tau / n, a1 = (i + 1) * MathF.Tau / n;
            float s0 = 0.7f + 0.3f * MathF.Sin(a0 * 10f - time * 1.3f), s1 = 0.7f + 0.3f * MathF.Sin(a1 * 10f - time * 1.3f);
            var d0 = new Vector2(MathF.Cos(a0), MathF.Sin(a0));
            var d1 = new Vector2(MathF.Cos(a1), MathF.Sin(a1));
            for (int band = 0; band < 3; band++)
            {
                float ri = radii[band], ro = radii[band + 1];
                float ai = alphas[band] * pulse, ao = alphas[band + 1] * pulse;
                float si0 = band == 0 ? 1f : s0, si1 = band == 0 ? 1f : s1; // shimmer only near the edge
                var p0i = RingPoint(c, d0, ri); var p0o = RingPoint(c, d0, ro);
                var p1i = RingPoint(c, d1, ri); var p1o = RingPoint(c, d1, ro);
                RingVertex(p0i, ai * si0); RingVertex(p0o, ao * s0); RingVertex(p1o, ao * s1);
                RingVertex(p0i, ai * si0); RingVertex(p1o, ao * s1); RingVertex(p1i, ai * si1);
            }
        }
        Rlgl.End();
    }

    private Vector3 RingPoint(Vector3 c, Vector2 dir, float radius)
    {
        var p = new Vector2(c.X + dir.X * radius, c.Z + dir.Y * radius);
        return new Vector3(p.X, _terrain!.GetSurfaceHeight(p) + 0.35f, p.Y);
    }

    private static void RingVertex(Vector3 p, float alpha)
    {
        Rlgl.Color4ub(175, 95, 255, (byte)Math.Clamp(alpha, 0f, 255f));
        Rlgl.Vertex3f(p.X, p.Y, p.Z);
    }

    /// <summary>EMP sparks of one unit: <see cref="StatusFx.BoltsPerUnit"/> jagged bolts, each a wide dim blue
    /// glow strip under a thin white core, reshaped every time bucket.</summary>
    private static void DrawSparks(int index, Vector3 c, float r, int bucket, Vector3 cam, float fade, Span<Vector3> pts)
    {
        float w = Math.Clamp(r * 0.12f, 0.08f, 0.5f);
        Rlgl.CheckRenderBatchLimit(StatusFx.BoltsPerUnit * (StatusFx.BoltPoints - 1) * 12);
        Rlgl.Begin((int)DrawMode.Triangles);
        for (int b = 0; b < StatusFx.BoltsPerUnit; b++)
        {
            uint seed = StatusFx.FlickerSeed(index, bucket, b);
            if (!StatusFx.BoltVisible(seed, b)) continue;
            float k = StatusFx.BoltIntensity(seed) * fade;
            StatusFx.Bolt(seed, c, r, pts);
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                Strip(pts[i], pts[i + 1], w * 2.6f, cam, 70, 130, 255, (byte)(110 * k));
                Strip(pts[i], pts[i + 1], w * 0.7f, cam, 215, 235, 255, (byte)(235 * k));
            }
        }
        Rlgl.End();
    }

    /// <summary>A camera-facing quad along a-b (two triangles; call inside Rlgl.Begin(Triangles)).</summary>
    private static void Strip(Vector3 a, Vector3 b, float width, Vector3 cam, byte r, byte g, byte bl, byte al)
    {
        var side = Vector3.Cross(b - a, cam - (a + b) * 0.5f);
        float len = side.Length();
        if (len < 1e-5f) return;
        side *= width * 0.5f / len;
        Rlgl.Color4ub(r, g, bl, al);
        Rlgl.Vertex3f(a.X - side.X, a.Y - side.Y, a.Z - side.Z);
        Rlgl.Vertex3f(a.X + side.X, a.Y + side.Y, a.Z + side.Z);
        Rlgl.Vertex3f(b.X + side.X, b.Y + side.Y, b.Z + side.Z);
        Rlgl.Vertex3f(a.X - side.X, a.Y - side.Y, a.Z - side.Z);
        Rlgl.Vertex3f(b.X + side.X, b.Y + side.Y, b.Z + side.Z);
        Rlgl.Vertex3f(b.X - side.X, b.Y - side.Y, b.Z - side.Z);
    }

    private const string CloakVs = """
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

    // Translucent shimmer: a faint lit ghost of the model, a strong fresnel rim (team-tinted pale blue), ripples
    // drifting over the hull and a scan band sliding up; emissive parts (vertex alpha 0.25..0.75) stay a little lit.
    private static readonly string CloakFs = "#version 330\n" + Shaders.CommonFs + """
        in vec3 vWorld;
        in vec3 vNormal;
        in vec4 vColor;
        in vec4 vInst;
        uniform float fade;
        out vec4 finalColor;
        void main() {
            if (fade < 0.999 && bayer4(gl_FragCoord.xy) >= fade) discard;
            vec3 n = normalize(vNormal);
            vec3 V = normalize(camPos - vWorld);
            float fres = pow(1.0 - abs(dot(n, V)), 2.2);
            float rip = vnoise(vWorld.xz * 2.6 + vec2(vWorld.y * 1.7, 0.0) + vec2(time * 0.9, -time * 0.6));
            rip *= vnoise(vWorld.zx * 4.1 - vec2(time * 0.5, time * 0.8) + vWorld.y);
            float band = smoothstep(0.85, 1.0, sin(vWorld.y * 9.0 + vWorld.x * 0.6 - time * 3.2) * 0.5 + 0.5);
            float m = vColor.a;
            vec3 albedo = m < 0.25 ? vInst.rgb * vColor.r : vColor.rgb;
            float glow = (m >= 0.25 && m < 0.75) ? 1.0 : 0.0;
            float ndl = max(dot(n, sunDir), 0.0);
            vec3 ghost = albedo * (0.35 + 0.5 * ndl);
            vec3 tint = mix(vec3(0.62, 0.84, 1.0), vInst.rgb, 0.3);
            vec3 c = ghost * 0.4 + tint * (fres * 1.1 + rip * 0.6 + band * 0.3) + vColor.rgb * glow * 0.6;
            c = applyFog(c, vWorld);
            float a = clamp(0.08 + 0.55 * fres + 0.25 * rip + 0.10 * band + 0.25 * glow, 0.0, 0.85);
            finalColor = vec4(c, a * fade);
        }
        """;
}
