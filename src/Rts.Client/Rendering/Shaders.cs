using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>A loaded shader with cached uniform locations.</summary>
public sealed class ShaderProgram
{
    public Shader Shader;
    private readonly Dictionary<string, int> _locs = new();

    public ShaderProgram(string vs, string fs)
    {
        Shader = Raylib.LoadShaderFromMemory(vs, fs);
        if (Shader.Id == Rlgl.GetShaderIdDefault()) Console.Error.WriteLine("WARNING: shader failed to compile, using default");
    }

    public int Loc(string name)
    {
        if (!_locs.TryGetValue(name, out int l)) _locs[name] = l = Raylib.GetShaderLocation(Shader, name);
        return l;
    }

    public void Use() => Rlgl.EnableShader(Shader.Id);
    public void Set(string n, float v) { int l = Loc(n); if (l >= 0) Raylib.SetShaderValue(Shader, l, v, ShaderUniformDataType.Float); }
    public void Set(string n, int v) { int l = Loc(n); if (l >= 0) Raylib.SetShaderValue(Shader, l, v, ShaderUniformDataType.Int); }
    public void Set(string n, Vector2 v) { int l = Loc(n); if (l >= 0) Raylib.SetShaderValue(Shader, l, v, ShaderUniformDataType.Vec2); }
    public void Set(string n, Vector3 v) { int l = Loc(n); if (l >= 0) Raylib.SetShaderValue(Shader, l, v, ShaderUniformDataType.Vec3); }
    public void Set(string n, Vector4 v) { int l = Loc(n); if (l >= 0) Raylib.SetShaderValue(Shader, l, v, ShaderUniformDataType.Vec4); }
    /// <summary>Sets a column-vector (GL/raylib convention) matrix.</summary>
    public void SetMatrix(string n, Matrix4x4 glMatrix) { int l = Loc(n); if (l >= 0) Raylib.SetShaderValueMatrix(Shader, l, glMatrix); }
}

/// <summary>Global lighting / haze parameters shared by every world shader (SupCom-like warm sun, cool sky).</summary>
public static class Atmosphere
{
    public static Vector3 SunDir => TerrainRenderer.SunDir;
    // (8b-3) Set per biome by every TerrainRenderer (TerrainRenderer.Biome.cs, BiomePalettes); these = Temperate.
    public static Vector3 SunColor = new(1.05f, 0.98f, 0.88f);
    public static Vector3 SkyAmbient = new(0.40f, 0.46f, 0.56f);
    public static Vector3 GroundAmbient = new(0.26f, 0.24f, 0.21f);
    public static Vector3 FogColor = new(0.63f, 0.71f, 0.79f);
    public static Vector3 SkyTop = new(0.27f, 0.45f, 0.74f);
    public static Vector3 Abyss = new(0.11f, 0.13f, 0.17f);
    public static Vector2 FogRange = new(300, 900);
    public static float Time;
    public static float CameraDistance = 80;

    /// <summary>Called once per frame (inside the 3D pass) with the camera distance from its focus point.</summary>
    public static void Update(float cameraDistance)
    {
        CameraDistance = cameraDistance;
        Time = (float)Raylib.GetTime();
        // Haze only affects what is much farther than the focus point (oblique close-up views), never the
        // strategic top-down view.
        FogRange = new Vector2(cameraDistance * 1.25f + 30f, cameraDistance * 4.5f + 260f);
    }

    public static void Apply(ShaderProgram p, FrameView f)
    {
        p.Set("sunDir", SunDir);
        p.Set("sunColor", SunColor);
        p.Set("skyAmb", SkyAmbient);
        p.Set("groundAmb", GroundAmbient);
        p.Set("fogColor", FogColor);
        p.Set("fogRange", FogRange);
        p.Set("camPos", f.CamPos);
        p.Set("time", Time);
    }
}

/// <summary>GLSL 330 shader sources and lazy loaders.</summary>
public static unsafe class Shaders
{
    // ------------------------------------------------------------------ shared GLSL

    public const string CommonFs = """
        uniform vec3 sunDir;
        uniform vec3 sunColor;
        uniform vec3 skyAmb;
        uniform vec3 groundAmb;
        uniform vec3 fogColor;
        uniform vec2 fogRange;
        uniform vec3 camPos;
        uniform float time;

        vec3 applyFog(vec3 c, vec3 wpos) {
            float d = length(wpos - camPos);
            float f = clamp((d - fogRange.x) / max(1.0, fogRange.y - fogRange.x), 0.0, 1.0);
            return mix(c, fogColor, f * f * (3.0 - 2.0 * f) * 0.92);
        }
        float hash12(vec2 p) {
            vec3 p3 = fract(vec3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.x + p3.y) * p3.z);
        }
        float vnoise(vec2 p) {
            vec2 i = floor(p), f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x),
                       mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
        }
        float fbm(vec2 p) {
            float a = 0.5, s = 0.0;
            for (int i = 0; i < 4; i++) { s += a * vnoise(p); p = p * 2.03 + vec2(17.1, 9.7); a *= 0.5; }
            return s;
        }
        float bayer4(vec2 p) {
            ivec2 i = ivec2(mod(p, 4.0));
            int idx = i.x + i.y * 4;
            float m[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
            return (m[idx] + 0.5) / 16.0;
        }
        """;

    /// <summary>Shadow-map lookup (5-tap PCF). Requires uniforms shadowMap, shadowVP, shadowOn.</summary>
    public const string ShadowFs = """
        uniform sampler2D shadowMap;
        uniform mat4 shadowVP;
        uniform float shadowOn;
        uniform float shadowTexel;
        float shadowFactor(vec3 wpos, vec3 n) {
            if (shadowOn < 0.5) return 1.0;
            vec4 lp = shadowVP * vec4(wpos + n * 0.06, 1.0);
            vec3 p = lp.xyz / lp.w * 0.5 + 0.5;
            if (p.x <= 0.0 || p.x >= 1.0 || p.y <= 0.0 || p.y >= 1.0 || p.z >= 1.0) return 1.0;
            float bias = 0.0015;
            float s = 0.0;
            vec2 o = vec2(shadowTexel);
            s += step(p.z - bias, texture(shadowMap, p.xy).r);
            s += step(p.z - bias, texture(shadowMap, p.xy + vec2(o.x, 0.0)).r);
            s += step(p.z - bias, texture(shadowMap, p.xy - vec2(o.x, 0.0)).r);
            s += step(p.z - bias, texture(shadowMap, p.xy + vec2(0.0, o.y)).r);
            s += step(p.z - bias, texture(shadowMap, p.xy - vec2(0.0, o.y)).r);
            return s / 5.0;
        }
        """;

    /// <summary>
    /// Fog of war (see <see cref="FogRenderer"/>): explored/vision grid textures of the previous and current tick
    /// (0 unexplored, 0.5 explored, 1 in vision), blended by the interpolation alpha and softened with a 5-tap filter.
    /// Requires uniforms fogPrev, fogCur, fogOn, fogAlpha, fogScale, fogTexel (FogRenderer.Bind).
    /// </summary>
    public const string FogOfWarFs = """
        uniform sampler2D fogPrev;
        uniform sampler2D fogCur;
        uniform float fogOn;
        uniform float fogAlpha;
        uniform vec2 fogScale;
        uniform vec2 fogTexel;
        float fogSample(vec2 uv) { return mix(texture(fogPrev, uv).r, texture(fogCur, uv).r, fogAlpha); }
        float fogVisibility(vec2 xz) {
            if (fogOn < 0.5) return 1.0;
            vec2 uv = xz * fogScale;
            vec2 o = fogTexel * 0.75;
            return fogSample(uv) * 0.4
                 + (fogSample(uv + o) + fogSample(uv - o) + fogSample(uv + vec2(o.x, -o.y)) + fogSample(uv + vec2(-o.x, o.y))) * 0.15;
        }
        vec3 applyFogOfWar(vec3 c, vec2 xz) {
            float v = fogVisibility(xz);
            if (v > 0.995) return c;
            float explored = smoothstep(0.04, 0.5, v);
            float vis = smoothstep(0.52, 0.98, v);
            float lum = dot(c, vec3(0.299, 0.587, 0.114));
            vec3 dim = mix(vec3(lum), c, 0.4) * 0.5 + vec3(0.0, 0.006, 0.018);
            vec3 dark = vec3(lum) * 0.2 + vec3(0.014, 0.018, 0.03);
            return mix(mix(dark, dim, explored), c, vis);
        }
        """;

    // ------------------------------------------------------------------ units (instanced)

    private const string UnitVs = """
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
        out float vLocalY;
        void main() {
            vec4 wp = instTransform * vec4(vertexPosition, 1.0);
            vWorld = wp.xyz;
            vNormal = mat3(instTransform) * vertexNormal;
            vColor = vertexColor;
            vInst = instData;
            vLocalY = vertexPosition.y;
            gl_Position = viewProj * wp;
        }
        """;

    private static readonly string UnitLitFs = "#version 330\n" + CommonFs + ShadowFs + FogOfWarFs + TerrainRenderer.SelfShadowFs + """
        in vec3 vWorld;
        in vec3 vNormal;
        in vec4 vColor;
        in vec4 vInst;
        uniform float fade;
        out vec4 finalColor;
        void main() {
            if (fade < 0.999 && bayer4(gl_FragCoord.xy) >= fade) discard;
            // (9b) dying units (DeathAnim.PackShaderW): w = fade-out level (integer, /16) + char (fraction); 0 = alive.
            float fadeQ = floor(vInst.w);
            if (fadeQ > 0.5 && bayer4(gl_FragCoord.xy + vec2(2.0, 1.0)) >= 1.0 - fadeQ / 16.0) discard;
            float charF = vInst.w - fadeQ;
            vec3 n = normalize(vNormal);
            float m = vColor.a;
            vec3 albedo = vColor.rgb;
            float glow = 0.0;
            if (m < 0.25) albedo = vInst.rgb * (vColor.r * 1.08);
            else if (m < 0.75) glow = 1.0;
            albedo = mix(albedo, vec3(0.075, 0.068, 0.06), charF * 0.8);
            glow *= 1.0 - charF;
            vec3 V = normalize(camPos - vWorld);
            float ndl = max(dot(n, sunDir), 0.0);
            float sh = min(shadowFactor(vWorld, n), terrainShadowAt(vWorld)); // (9b-2) terrain self-shadows
            vec3 H = normalize(sunDir + V);
            float spec = pow(max(dot(n, H), 0.0), 48.0) * 0.28 * ndl * sh;
            vec3 amb = mix(groundAmb, skyAmb, n.y * 0.5 + 0.5);
            float rim = pow(1.0 - max(dot(n, V), 0.0), 4.0) * 0.4;
            vec3 c = albedo * (amb * 0.95 + sunColor * ndl * mix(0.3, 0.95, sh)) + sunColor * spec + rim * skyAmb * 0.5;
            c = mix(c, vColor.rgb * 1.6, glow);
            c = applyFogOfWar(c, vWorld.xz);
            c = applyFog(c, vWorld);
            finalColor = vec4(c, 1.0);
        }
        """;

    private static readonly string UnitHoloFs = "#version 330\n" + CommonFs + """
        in vec3 vWorld;
        in vec3 vNormal;
        in vec4 vColor;
        in vec4 vInst;
        in float vLocalY;
        uniform float fade;
        out vec4 finalColor;
        void main() {
            float cut = vInst.w;
            vec3 n = normalize(vNormal);
            vec3 V = normalize(camPos - vWorld);
            float fres = pow(1.0 - abs(dot(n, V)), 2.0);
            vec3 holo = vec3(0.35, 0.78, 1.0);
            float lines = smoothstep(0.55, 1.0, sin(vWorld.y * 26.0 - time * 5.0) * 0.5 + 0.5);
            float above = vLocalY - cut;
            if (above > 0.0) {
                float a = 0.05 + 0.32 * fres + 0.07 * lines;
                finalColor = vec4(holo, a * fade);
                return;
            }
            float band = 1.0 - smoothstep(0.0, 0.1, -above);
            vec3 albedo = vColor.a < 0.25 ? vInst.rgb * vColor.r : vColor.rgb;
            float ndl = max(dot(n, sunDir), 0.0);
            vec3 lit = albedo * (0.5 + 0.6 * ndl);
            vec3 c = mix(lit, holo, 0.5) + holo * (fres * 0.6 + lines * 0.12) + vec3(0.85, 0.97, 1.0) * band * 1.3;
            finalColor = vec4(c, (0.62 + 0.25 * fres + band * 0.35) * fade);
        }
        """;

    private const string UnitDepthFs = """
        #version 330
        out vec4 finalColor;
        void main() { finalColor = vec4(1.0); }
        """;

    // ------------------------------------------------------------------ terrain / water / sky

    private const string WorldVs = """
        #version 330
        in vec3 vertexPosition;
        in vec3 vertexNormal;
        in vec4 vertexColor;
        uniform mat4 mvp;
        uniform mat4 matModel;
        out vec3 vWorld;
        out vec3 vNormal;
        out vec4 vColor;
        void main() {
            vWorld = (matModel * vec4(vertexPosition, 1.0)).xyz;
            vNormal = vertexNormal;
            vColor = vertexColor;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private static readonly string TerrainFs = "#version 330\n" + CommonFs + ShadowFs + FogOfWarFs + TerrainRenderer.SelfShadowFs + """
        in vec3 vWorld;
        in vec3 vNormal;
        in vec4 vColor;
        uniform float waterLevel;
        uniform vec2 mapSize;
        uniform vec3 bGrass1, bGrass2, bDry, bDirt, bRock1, bRock2, bSand, bWet, bShoreGlow; // (8b-3) biome palette
        out vec4 finalColor;
        void main() {
            vec3 n = normalize(vNormal);
            vec2 p = vWorld.xz;
            float h = vWorld.y - waterLevel;
            float slope = 1.0 - n.y;
            float n1 = fbm(p * 0.03);
            float n2 = fbm(p * 0.19 + 31.0);
            float n3 = vnoise(p * 1.6);
            float n4 = vnoise(p * 6.0);
            vec3 grass1 = bGrass1;
            vec3 grass2 = bGrass2;
            vec3 dry = bDry;
            vec3 dirt = bDirt;
            vec3 rock1 = bRock1;
            vec3 rock2 = bRock2;
            vec3 sand = bSand;
            vec3 wet = bWet;
            vec3 c = mix(grass1, grass2, smoothstep(0.32, 0.68, n1));
            c = mix(c, dry, smoothstep(9.0, 20.0, h + (n2 - 0.5) * 10.0) * 0.75);
            c = mix(c, dirt, smoothstep(0.6, 0.72, n2) * 0.55);
            float beach = 1.0 - smoothstep(0.35, 1.1, h + (n2 - 0.5) * 0.9);
            c = mix(c, sand, beach);
            c = mix(c, wet, 1.0 - smoothstep(-0.3, 0.3, h));
            float rockT = smoothstep(0.14, 0.27, slope + (n2 - 0.5) * 0.12);
            vec3 rock = mix(rock2, rock1, smoothstep(0.3, 0.7, n3));
            rock *= 0.85 + 0.3 * smoothstep(0.2, 0.8, vnoise(vec2(p.x + p.y, vWorld.y * 3.0) * 0.8));
            c = mix(c, rock, rockT);
            float n5 = vnoise(p * 17.0) * 0.6 + vnoise(p * 41.0) * 0.4;
            float detail = 1.0 - smoothstep(25.0, 70.0, length(vWorld - camPos));
            c *= 0.86 + 0.18 * n3 + 0.08 * n4 + (n5 - 0.5) * 0.16 * detail;
            c *= vColor.r;
            float ndl = max(dot(n, sunDir), 0.0);
            float sh = min(shadowFactor(vWorld, n), terrainShadowGround(vColor.g)); // (9b-2) terrain self-shadows
            vec3 amb = mix(groundAmb, skyAmb, n.y * 0.5 + 0.5);
            vec3 col = c * (amb * 1.05 + sunColor * ndl * mix(0.45, 1.0, sh));
            col += bShoreGlow * (1.0 - smoothstep(-0.3, 1.0, h + (n2 - 0.5) * 0.8)) * (0.8 + 0.2 * sin(time * 0.9 + n1 * 9.0)); // (8b-3) lava shores
            // Outside the playable area: darken.
            vec2 o = max(vec2(0.0), max(-p, p - mapSize));
            col *= 1.0 - clamp(max(o.x, o.y) * 0.1, 0.0, 0.6);
            col = applyFogOfWar(col, vWorld.xz);
            col = applyFog(col, vWorld);
            finalColor = vec4(col, 1.0);
        }
        """;

    private static readonly string WaterFs = "#version 330\n" + CommonFs + FogOfWarFs + """
        in vec3 vWorld;
        in vec3 vNormal;
        in vec4 vColor;
        uniform sampler2D texture0;
        uniform vec2 mapSize;
        uniform float waterLevel;
        uniform vec3 skyTop;
        uniform vec3 shallowCol, deepCol, foamCol; // (8b-3) biome water tint
        uniform float waterGlow;                   // (8b-3) 0 = water, 1 = lava (emissive, flowing crust, little reflection)
        out vec4 finalColor;
        float wave(vec2 p) {
            return vnoise(p * 0.45 + vec2(time * 0.21, time * 0.13)) * 0.6
                 + vnoise(p * 1.1 - vec2(time * 0.17, -time * 0.23)) * 0.3
                 + vnoise(p * 2.7 + vec2(-time * 0.4, time * 0.31)) * 0.1;
        }
        void main() {
            vec2 p = vWorld.xz;
            vec2 uv = (p + 0.5) / (mapSize + 1.0);
            float ground = texture(texture0, uv).r;
            float depth = waterLevel - ground;
            if (depth < -0.02) discard;
            float e = 0.12;
            float h0 = wave(p), hx = wave(p + vec2(e, 0.0)), hz = wave(p + vec2(0.0, e));
            vec3 n = normalize(vec3((h0 - hx) / e * 0.28, 1.0, (h0 - hz) / e * 0.28));
            vec3 V = normalize(camPos - vWorld);
            float fres = 0.03 + 0.97 * pow(1.0 - max(dot(n, V), 0.0), 5.0);
            vec3 shallow = shallowCol;
            vec3 deep = deepCol;
            float dk = 1.0 - exp(-max(depth, 0.0) * 0.32);
            vec3 col = mix(shallow, deep, dk);
            col *= 0.65 + 0.4 * max(dot(n, sunDir), 0.0);
            if (waterGlow > 0.0) { // (8b-3) lava: molten glow under slowly drifting dark crust plates
                vec2 fp = p * 0.07 + vec2(time * 0.011, time * 0.007);
                float cr = fbm(fp + vec2(fbm(p * 0.031 - time * 0.004), fbm(p * 0.029 + 7.3)) * 1.6);
                float crust = smoothstep(0.47, 0.6, cr) * (0.55 + 0.45 * dk);
                vec3 molten = mix(shallow, deep, smoothstep(0.25, 0.55, cr)) * (0.92 + 0.12 * sin(time * 0.8 + cr * 14.0));
                vec3 rockCol = vec3(0.07, 0.045, 0.035) * (0.6 + 0.5 * max(dot(n, sunDir), 0.0)) + deep * 0.35 * (1.0 - smoothstep(0.6, 0.75, cr));
                col = mix(col, mix(molten * 1.25, rockCol, crust), waterGlow);
            }
            vec3 R = reflect(-V, n);
            vec3 sky = mix(fogColor, skyTop, clamp(R.y, 0.0, 1.0));
            col = mix(col, sky, fres * 0.55 * (1.0 - 0.85 * waterGlow));
            float spec = pow(max(dot(R, sunDir), 0.0), 160.0) * 1.6;
            col += sunColor * spec * (1.0 - 0.8 * waterGlow);
            float fn = vnoise(p * 2.2 + vec2(time * 0.35, -time * 0.25));
            float edge = 1.0 - smoothstep(0.0, 0.45, depth);
            float lines = smoothstep(0.82, 1.0, sin(depth * 8.0 - time * 2.0) * 0.5 + 0.5) * (1.0 - smoothstep(0.0, 1.4, depth));
            float foam = clamp(edge * (0.45 + 0.7 * fn) + lines * 0.3 * smoothstep(0.3, 0.8, fn), 0.0, 1.0);
            col = mix(col, foamCol, foam * 0.85);
            float alpha = clamp(mix(0.32, 0.93, dk) + fres * 0.25 + foam * 0.6, 0.0, 1.0);
            alpha = mix(alpha, clamp(0.86 + dk + foam, 0.0, 1.0), waterGlow); // (8b-3) lava is (nearly) opaque
            col = applyFogOfWar(col, vWorld.xz);
            col = applyFog(col, vWorld);
            finalColor = vec4(col, alpha);
        }
        """;

    private const string SkyVs = """
        #version 330
        in vec3 vertexPosition;
        out vec2 vNdc;
        void main() {
            vNdc = vertexPosition.xy;
            gl_Position = vec4(vertexPosition.xy, 0.99999, 1.0);
        }
        """;

    private const string SkyFs = """
        #version 330
        in vec2 vNdc;
        uniform mat4 invViewProj;
        uniform vec3 camPos;
        uniform vec3 fogColor;
        uniform vec3 skyTop;
        uniform vec3 abyss;
        out vec4 finalColor;
        void main() {
            vec4 p = invViewProj * vec4(vNdc, 1.0, 1.0);
            vec3 dir = normalize(p.xyz / p.w - camPos);
            float y = dir.y;
            vec3 c = y > 0.0 ? mix(fogColor, skyTop, pow(clamp(y, 0.0, 1.0), 0.55))
                             : mix(fogColor, abyss, pow(clamp(-y * 1.6, 0.0, 1.0), 0.6));
            finalColor = vec4(c, 1.0);
        }
        """;

    // ------------------------------------------------------------------ particles / icons (quad batches)

    private const string QuadVs = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec2 vertexTexCoord2;
        in vec4 vertexColor;
        uniform mat4 mvp;
        out vec2 fragTexCoord;
        out vec2 fragParams;
        out vec4 fragColor;
        out vec3 fragWorld;
        void main() {
            fragTexCoord = vertexTexCoord;
            fragParams = vertexTexCoord2;
            fragColor = vertexColor;
            fragWorld = vertexPosition;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    /// <summary>Instanced particle quads (<see cref="ParticleInstances"/>): expands one instance into the same corners
    /// and uvs as QuadBatch.Billboard / Flat / Streak.</summary>
    private const string ParticleInstVs = """
        #version 330
        layout(location = 0) in vec2 corner;
        layout(location = 10) in vec4 iPosSize;
        layout(location = 11) in vec4 iDir;
        layout(location = 12) in vec4 iParams;
        layout(location = 13) in vec4 iColor;
        uniform mat4 viewProj;
        uniform vec3 camRight;
        uniform vec3 camUp;
        uniform vec3 camPos;
        out vec2 fragTexCoord;
        out vec2 fragParams;
        out vec4 fragColor;
        out vec3 fragWorld;
        void main() {
            vec3 c = iPosSize.xyz;
            float size = iPosSize.w;
            int mode = int(iParams.z + 0.5);
            vec2 uv = corner * 0.5 + 0.5;
            vec3 world;
            if (mode == 2) {
                vec3 side = cross(iDir.xyz, camPos - c);
                float l = length(side);
                side = l > 1e-5 ? side * (size * 0.5 / l) : vec3(0.0);
                vec3 tail = c - iDir.xyz * iDir.w;
                world = mix(tail, c, uv.y) + side * corner.x;
            } else {
                float cs = cos(iParams.w) * size * 0.5, sn = sin(iParams.w) * size * 0.5;
                vec3 r, u;
                if (mode == 1) { r = vec3(cs, 0.0, sn); u = vec3(-sn, 0.0, cs); }
                else { r = camRight * cs + camUp * sn; u = camUp * cs - camRight * sn; }
                world = c + r * corner.x + u * corner.y;
            }
            fragTexCoord = uv;
            fragParams = iParams.xy;
            fragColor = iColor;
            fragWorld = world;
            gl_Position = viewProj * vec4(world, 1.0);
        }
        """;

    private static readonly string ParticleFs = "#version 330\n" + CommonFs + """
        in vec2 fragTexCoord;
        in vec2 fragParams;
        in vec4 fragColor;
        in vec3 fragWorld;
        out vec4 finalColor;
        void main() {
            vec2 p = fragTexCoord * 2.0 - 1.0;
            float r = length(p);
            int shape = int(fragParams.x + 0.5);
            float seed = fragParams.y;
            float a = 0.0;
            vec3 col = fragColor.rgb;
            if (shape == 0) {            // soft glow with hot core
                a = max(0.0, 1.0 - r); a *= a;
                col = mix(col, vec3(1.0), smoothstep(0.55, 0.0, r) * 0.6);
            } else if (shape == 1) {     // smoke puff
                float nz = vnoise(p * 2.3 + seed * 17.0) * 0.6 + vnoise(p * 5.0 - seed * 7.0) * 0.4;
                a = smoothstep(1.0, 0.3, r + (nz - 0.5) * 0.55);
                col *= 0.8 + 0.4 * nz;
            } else if (shape == 2) {     // shockwave ring
                a = smoothstep(0.14, 0.0, abs(r - 0.82)) + 0.15 * smoothstep(0.82, 0.3, r);
            } else if (shape == 3) {     // streak (u across, v along)
                float x = abs(p.x);
                float body = (1.0 - smoothstep(0.0, 1.0, x)) * smoothstep(1.0, 0.55, abs(p.y));
                float head = smoothstep(-0.2, 1.0, p.y);
                a = body * (0.35 + 0.65 * head);
                col = mix(col, vec3(1.0), (1.0 - smoothstep(0.0, 0.35, x)) * 0.6 * head);
            } else if (shape == 4) {     // solid disk
                a = smoothstep(1.0, 0.8, r);
            } else if (shape == 5) {     // scorch decal
                float nz = vnoise(p * 3.0 + seed * 11.0) * 0.7 + vnoise(p * 7.0 + seed * 5.0) * 0.3;
                a = smoothstep(1.0, 0.25, r + (nz - 0.5) * 0.7);
                col *= 0.7 + 0.5 * nz;
            } else if (shape == 6) {     // fireball: noisy hot blob
                float nz = vnoise(p * 3.0 + seed * 13.0 + time * 1.5);
                a = smoothstep(1.0, 0.35, r + (nz - 0.5) * 0.5);
                col = mix(col, vec3(1.0, 0.95, 0.8), smoothstep(0.6, 0.0, r + (nz - 0.5) * 0.3) * 0.8);
            } else {                     // soft square (debris chunk)
                vec2 q = abs(p);
                a = smoothstep(1.0, 0.7, max(q.x, q.y));
            }
            finalColor = vec4(col, fragColor.a * a);
        }
        """;

    private const string IconFs = """
        #version 330
        in vec2 fragTexCoord;
        in vec2 fragParams;
        in vec4 fragColor;
        uniform sampler2D texture0;
        out vec4 finalColor;
        void main() {
            vec4 t = texture(texture0, fragTexCoord);
            float sum = t.r + t.g + t.b;
            vec3 c = (t.r * fragColor.rgb + t.g * vec3(1.0) + t.b * vec3(0.04, 0.05, 0.07)) / max(sum, 0.001);
            finalColor = vec4(c, t.a * fragColor.a);
        }
        """;

    // ------------------------------------------------------------------ loaders

    private static ShaderProgram? _particleInst;
    private static ShaderProgram? _unitLit, _unitHolo, _unitDepth, _terrain, _water, _sky, _particle, _icon;

    public static ShaderProgram UnitLit => _unitLit ??= new ShaderProgram(UnitVs, UnitLitFs);
    public static ShaderProgram UnitHolo => _unitHolo ??= new ShaderProgram(UnitVs, UnitHoloFs);
    public static ShaderProgram UnitDepth => _unitDepth ??= new ShaderProgram(UnitVs, UnitDepthFs);
    public static ShaderProgram Terrain => _terrain ??= new ShaderProgram(WorldVs, TerrainFs);
    public static ShaderProgram Water => _water ??= new ShaderProgram(WorldVs, WaterFs);
    public static ShaderProgram Sky => _sky ??= new ShaderProgram(SkyVs, SkyFs);
    public static ShaderProgram Particle => _particle ??= new ShaderProgram(QuadVs, ParticleFs);
    public static ShaderProgram ParticleInst => _particleInst ??= new ShaderProgram(ParticleInstVs, ParticleFs);
    public static ShaderProgram Icon => _icon ??= new ShaderProgram(QuadVs, IconFs);

    private const string LitVs = """
        #version 330
        in vec3 vertexPosition;
        in vec3 vertexNormal;
        uniform mat4 mvp;
        uniform mat4 matNormal;
        out vec3 fragNormal;
        void main() {
            fragNormal = normalize(vec3(matNormal * vec4(vertexNormal, 0.0)));
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string LitFs = """
        #version 330
        in vec3 fragNormal;
        uniform vec4 colDiffuse;
        uniform vec3 sunDir;
        out vec4 finalColor;
        void main() {
            vec3 n = normalize(fragNormal);
            float d = max(dot(n, sunDir), 0.0);
            float sky = 0.5 + 0.5 * n.y;
            vec3 c = colDiffuse.rgb * (0.30 + 0.25 * sky + 0.65 * d);
            finalColor = vec4(c, colDiffuse.a);
        }
        """;

    private static Shader? _lit;

    /// <summary>Simple directional-light shader using the material diffuse color (no textures), for DrawMesh.</summary>
    public static Shader LoadLit()
    {
        if (_lit is Shader s) return s;
        var sh = Raylib.LoadShaderFromMemory(LitVs, LitFs);
        sh.Locs[(int)ShaderLocationIndex.MatrixMvp] = Raylib.GetShaderLocation(sh, "mvp");
        sh.Locs[(int)ShaderLocationIndex.MatrixNormal] = Raylib.GetShaderLocation(sh, "matNormal");
        sh.Locs[(int)ShaderLocationIndex.ColorDiffuse] = Raylib.GetShaderLocation(sh, "colDiffuse");
        Raylib.SetShaderValue(sh, Raylib.GetShaderLocation(sh, "sunDir"), TerrainRenderer.SunDir, ShaderUniformDataType.Vec3);
        _lit = sh;
        return sh;
    }
}
