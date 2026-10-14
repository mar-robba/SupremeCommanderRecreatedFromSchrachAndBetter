using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// Submerged units (subs below the surface, amphibious units on the sea bed): skipped by the opaque pass and drawn
/// after the water (<see cref="DrawSubmerged"/>) as dim, blue-tinted, translucent models, so they read as "under
/// the water" instead of vanishing behind the nearly opaque deep water. The vertex shader moves every vertex
/// along its view ray until the whole model is just above the water plane: the projection is unchanged, the
/// depth test against the water passes, nearer geometry (ships, land) still hides it, and the order of the
/// model's own faces is kept (the same shift per instance). No shadows.
/// Submarines with <c>UnitFlags.Surfaced</c> (dive toggle) ride at the water surface (the sim eases their
/// elevation, <c>NavalMotion.SubElevation</c>) and so are drawn like ships: opaque, with shadows.
/// </summary>
public sealed unsafe partial class UnitRenderer
{
    private readonly List<(UnitModel model, int index, Vector3 pos, float heading, float turret, Vector4 col)> _submerged = new();
    private static ShaderProgram? _subShader; // shared by every game (like Shaders.*)

    /// <summary>Submerged units drawn last frame (after the water).</summary>
    public int SubmergedUnits => _submerged.Count;

    private bool IsSubmerged(UnitModel model, Vector3 pos) =>
        _terrain != null && !model.Structure && !model.Air && pos.Y < _terrain.WaterLevel - 0.5f;

    /// <summary>Draws the submerged units collected by <see cref="Draw"/>. Call right after the water (inside
    /// BeginMode3D, same frame).</summary>
    public void DrawSubmerged()
    {
        if (_submerged.Count == 0 || _terrain == null) return;
        float fade = MeshFade(Atmosphere.CameraDistance);
        if (fade <= 0.001f) return;
        var fv = FrameView.Current;
        float time = Atmosphere.Time;
        // The opaque pass is done: reuse the instance buffers.
        foreach (var m in _active) { m.Body.Clear(); m.Turret?.Clear(); }
        _active.Clear();
        foreach (var (model, index, pos, heading, turret, col) in _submerged)
            AddUnit(model, index, pos, heading, turret, 0, col with { W = pos.Y + model.MinY - 0.1f }, false, 1f, time);

        var sp = _subShader ??= new ShaderProgram(SubVs, SubFs);
        Rlgl.DrawRenderBatchActive();
        sp.Use();
        sp.SetMatrix("viewProj", fv.ViewProjGl);
        Atmosphere.Apply(sp, fv);
        sp.Set("fade", fade);
        sp.Set("waterLevel", _terrain.WaterLevel);
        FogRenderer.Bind(sp);
        sp.Use();
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Alpha);
        foreach (var m in _active)
        {
            m.Body.Draw();
            m.Turret?.Draw();
            DrawCalls += m.Turret != null ? 2 : 1;
        }
        Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
        Rlgl.DisableShader();
    }

    // instData: rgb = team color, w = world y of the model's lowest point (the shift brings it above the water).
    private const string SubVs = """
        #version 330
        in vec3 vertexPosition;
        in vec3 vertexNormal;
        in vec4 vertexColor;
        layout(location = 10) in mat4 instTransform;
        layout(location = 14) in vec4 instData;
        uniform mat4 viewProj;
        uniform vec3 camPos;
        uniform float waterLevel;
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
            vec3 toCam = normalize(camPos - wp.xyz);
            float lift = max(waterLevel + 0.06 - instData.w, 0.0);
            float t = toCam.y > 0.02 ? lift / toCam.y : 0.0;
            t = min(t, length(camPos - wp.xyz) * 0.9);
            gl_Position = viewProj * vec4(wp.xyz + toCam * t, 1.0);
        }
        """;

    private static readonly string SubFs = "#version 330\n" + Shaders.CommonFs + Shaders.FogOfWarFs + """
        in vec3 vWorld;
        in vec3 vNormal;
        in vec4 vColor;
        in vec4 vInst;
        uniform float fade;
        uniform float waterLevel;
        out vec4 finalColor;
        void main() {
            if (fade < 0.999 && bayer4(gl_FragCoord.xy) >= fade) discard;
            vec3 n = normalize(vNormal);
            float m = vColor.a;
            vec3 albedo = vColor.rgb;
            float glow = 0.0;
            if (m < 0.25) albedo = vInst.rgb * (vColor.r * 1.08);
            else if (m < 0.75) glow = 1.0;
            float ndl = max(dot(n, sunDir), 0.0);
            vec3 c = albedo * (0.5 + 0.6 * ndl);
            float depth = max(waterLevel - vWorld.y, 0.0);
            // Light shafts / caustics flickering over the hull.
            float caust = vnoise(vWorld.xz * 1.4 + vec2(time * 0.5, time * 0.37)) * vnoise(vWorld.xz * 2.3 - vec2(time * 0.41, -time * 0.3));
            vec3 water = vec3(0.05, 0.22, 0.32);
            c = mix(c, water, clamp(0.5 + depth * 0.1, 0.0, 0.85));
            c += vec3(0.10, 0.22, 0.25) * caust * (0.4 + 0.6 * max(n.y, 0.0));
            c += vColor.rgb * glow * 0.5;
            c = applyFogOfWar(c, vWorld.xz);
            c = applyFog(c, vWorld);
            float a = clamp(0.68 - depth * 0.07, 0.3, 0.68);
            finalColor = vec4(c, a);
        }
        """;
}
