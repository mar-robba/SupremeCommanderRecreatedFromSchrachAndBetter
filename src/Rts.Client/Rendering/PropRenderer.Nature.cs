using System.Numerics;
using Raylib_cs;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Map props (8b): trees and rocks (<see cref="PropView.Kind"/> Tree / Rock). Instanced: 4 tree species (variants 0,1
/// conifers, 2,3 broadleaf) and 4 rock shapes, each with a near mesh and a low-poly far mesh (beyond
/// <see cref="NatureFarDistance"/> from the camera). The props are bucketed into a <see cref="NatureCell"/> grid once
/// per snapshot; a frame only visits the cells in the frustum (thousands of props, no per-frame allocations).
/// Trees fall over in <see cref="TreeFallSeconds"/> from StateTick towards FallHeading, burn (glowing crown, flames,
/// smoke, charring foliage) and stay as charred trunks; reclaimed trees shrink and rocks sink (Mass / MassMax).
/// </summary>
public sealed partial class PropRenderer
{
    public const float NatureCell = 32f;
    public const float NatureFarDistance = 150f;
    public const float TreeFallSeconds = 1.3f;
    /// <summary>Tree model radius at scale 1 (the sim's tree radius = 0.45 × size).</summary>
    private const float TreeUnitRadius = 0.45f;

    public int DrawnNature { get; private set; }

    private InstancedMesh[]? _natNear, _natFar; // [0..3] trees, [4..7] rocks
    private float[] _natHeight = new float[8];
    private ShaderProgram? _natShader;
    private SimSnapshot? _natSnap;
    private int _natTick = int.MinValue, _natGw, _natGh, _natCount, _natSeenTick = int.MinValue;
    private int[] _cellStart = [], _cellItems = [], _cellFill = [];
    private float[] _cellMaxY = [];
    private float _natSinceTick, _fireAcc;
    private int _fireRound;

    /// <summary>Buckets the nature props of a new snapshot into the cell grid (counting sort, O(props)).</summary>
    private void BucketNature(SimSnapshot cur)
    {
        if (ReferenceEquals(cur, _natSnap) && cur.Tick == _natTick) return;
        _natSnap = cur;
        _natTick = cur.Tick;
        int gw = Math.Max(1, (int)MathF.Ceiling(_ctx.Terrain.Width / NatureCell));
        int gh = Math.Max(1, (int)MathF.Ceiling(_ctx.Terrain.Height / NatureCell));
        int cells = gw * gh;
        if (_cellStart.Length != cells + 1)
        {
            _cellStart = new int[cells + 1];
            _cellFill = new int[cells];
            _cellMaxY = new float[cells];
        }
        _natGw = gw; _natGh = gh;
        Array.Clear(_cellFill);
        Array.Fill(_cellMaxY, float.MinValue);
        _natCount = 0;
        for (int k = 0; k < cur.PropCount; k++)
        {
            ref var p = ref cur.Props[k];
            if (p.Kind == PropKind.Wreck) continue;
            int c = CellOf(p.Pos);
            _cellFill[c]++;
            if (p.Pos.Y > _cellMaxY[c]) _cellMaxY[c] = p.Pos.Y;
            _natCount++;
        }
        if (_natCount == 0) return;
        if (_cellItems.Length < _natCount) _cellItems = new int[Math.Max(_natCount, _cellItems.Length * 2)];
        int sum = 0;
        for (int c = 0; c < cells; c++) { _cellStart[c] = sum; sum += _cellFill[c]; _cellFill[c] = _cellStart[c]; }
        _cellStart[cells] = sum;
        for (int k = 0; k < cur.PropCount; k++)
        {
            ref var p = ref cur.Props[k];
            if (p.Kind == PropKind.Wreck) continue;
            _cellItems[_cellFill[CellOf(p.Pos)]++] = k;
        }
    }

    private int CellOf(Vector3 pos)
    {
        int cx = Math.Clamp((int)(pos.X / NatureCell), 0, _natGw - 1);
        int cz = Math.Clamp((int)(pos.Z / NatureCell), 0, _natGh - 1);
        return cz * _natGw + cx;
    }

    /// <summary>Draws trees and rocks (inside BeginMode3D). <paramref name="fade"/> = unit mesh fade (strategic zoom).</summary>
    private void DrawNature(SimSnapshot cur, float gameDt, float fade, FrameView fv)
    {
        DrawnNature = 0;
        BucketNature(cur);
        if (_natCount == 0) return;
        EnsureNatureMeshes();
        if (cur.Tick != _natSeenTick) { _natSeenTick = cur.Tick; _natSinceTick = 0; }
        else _natSinceTick = MathF.Min(_natSinceTick + gameDt, 0.1f);
        for (int i = 0; i < 8; i++) { _natNear![i].Clear(); _natFar![i].Clear(); }

        _fireAcc += gameDt;
        bool fire = _ps != null && _fireAcc >= 0.08f && Atmosphere.CameraDistance < 230f;
        if (fire) { _fireAcc = 0; _fireRound++; }
        int fireBudget = 64;
        var cam = fv.CamPos;
        float far2 = NatureFarDistance * NatureFarDistance;
        float half = NatureCell * 0.5f, cellR = NatureCell * 0.7072f + 4f;

        for (int cz = 0; cz < _natGh; cz++)
        for (int cx = 0; cx < _natGw; cx++)
        {
            int c = cz * _natGw + cx;
            int a = _cellStart[c], b = _cellStart[c + 1];
            if (a == b) continue;
            var center = new Vector3(cx * NatureCell + half, _cellMaxY[c], cz * NatureCell + half);
            if (!fv.SphereVisible(center, cellR + 6f)) continue;
            for (int j = a; j < b; j++)
            {
                ref var p = ref cur.Props[_cellItems[j]];
                bool tree = p.Kind == PropKind.Tree;
                int mi = (tree ? 0 : 4) + (p.Variant & 3);
                float s = tree ? p.Radius / TreeUnitRadius : p.Radius;
                float h = _natHeight[mi] * s;
                if (!fv.SphereVisible(p.Pos + new Vector3(0, h * 0.5f, 0), h * 0.6f + 0.5f)) continue;
                DrawnNature++;
                float left = p.MassMax > 0 ? Math.Clamp(p.Mass / p.MassMax, 0f, 1f) : 1f;
                float rnd = Hash(p.Id, 7);
                var data = new Vector4(rnd, 0, 0, tree ? 1f : 0f);
                Matrix4x4 m;
                if (!tree)
                {
                    float sink = 0.12f * s + (1f - left) * 0.5f * s;
                    m = YawScale(p.Heading, s, s * (0.8f + 0.4f * Hash(p.Id, 8)), new Vector3(p.Pos.X, p.Pos.Y - sink, p.Pos.Z));
                }
                else
                {
                    s *= 0.6f + 0.4f * left;
                    var f = p.Flags;
                    float age = (cur.Tick - p.StateTick) * 0.1f + _natSinceTick;
                    if ((f & PropFlags.Burning) != 0)
                    {
                        float prog = Math.Clamp(1f - (p.BurnEnd - cur.Tick - _natSinceTick * 10f) / Rts.Sim.World.PropSystem.BurnTicks, 0f, 1f);
                        data.Y = MathF.Min(1f, prog * 6f) * (1f - SmoothStep(0.7f, 1f, prog));
                        data.Z = prog;
                    }
                    else if ((f & PropFlags.Burnt) != 0)
                    {
                        data.Z = 1f;
                        data.Y = age < 15f ? 0.22f * (1f - age / 15f) : 0f;
                    }
                    if ((f & PropFlags.Fallen) != 0)
                    {
                        // A burnt-out tree re-stamps StateTick at BurnEnd: that is not a new fall.
                        bool fresh = age < TreeFallSeconds && !((f & PropFlags.Burnt) != 0 && p.StateTick >= p.BurnEnd);
                        float t = fresh ? age / TreeFallSeconds : 1f;
                        float ang = (MathF.PI * 0.5f - 0.06f) * t * t;
                        var dir = new Vector3(MathF.Cos(p.FallHeading), 0, MathF.Sin(p.FallHeading));
                        var axis = new Vector3(dir.Z, 0, -dir.X);
                        m = Matrix4x4.CreateScale(s) * Matrix4x4.CreateRotationY(p.Heading)
                            * Matrix4x4.CreateFromAxisAngle(axis, ang)
                            * Matrix4x4.CreateTranslation(p.Pos.X, p.Pos.Y - 0.1f * t * s, p.Pos.Z);
                        data.W = 1f - t;
                    }
                    else m = YawScale(p.Heading, s, s, p.Pos);
                    if (fire && data.Y > 0.15f && fireBudget > 0 && ((p.Id + _fireRound) & 1) == 0)
                    {
                        fireBudget--;
                        var crown = Vector3.Transform(new Vector3(0, _natHeight[mi] * 0.6f, 0), m);
                        _ps!.Fireball(crown + new Vector3((rnd - 0.5f) * 0.5f * s, 0, (Hash(p.Id, 9) - 0.5f) * 0.5f * s), 0.42f * s * data.Y + 0.15f, 0.55f);
                        if (((p.Id + _fireRound) & 3) == 0)
                            _ps.Smoke(crown + new Vector3(0, 0.6f * s, 0), 0.5f * s, 3.2f, new Vector4(0.12f, 0.11f, 0.1f, 0.4f), new Vector3(0.3f, 1.4f, 0.1f), 3f);
                    }
                }
                bool near = Vector3.DistanceSquared(cam, p.Pos) < far2;
                (near ? _natNear! : _natFar!)[mi].Add(m, data);
            }
        }
        if (DrawnNature == 0) return;

        var sp = _natShader ??= new ShaderProgram(NatureVs, NatureFs);
        sp.Use();
        sp.SetMatrix("viewProj", fv.ViewProjGl);
        Atmosphere.Apply(sp, fv);
        sp.Set("fade", fade);
        ShadowMap.Bind(sp);
        FogRenderer.Bind(sp);
        sp.Use();
        for (int i = 0; i < 8; i++) { _natNear![i].Draw(); _natFar![i].Draw(); }
        Rlgl.DisableShader();
    }

    private static float SmoothStep(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Scale (xz = s, y = sy) × RotationY(yaw) × Translation, written out (most props each frame).</summary>
    private static Matrix4x4 YawScale(float yaw, float s, float sy, Vector3 t)
    {
        float c = MathF.Cos(yaw), n = MathF.Sin(yaw);
        return new Matrix4x4(s * c, 0, -s * n, 0, 0, sy, 0, 0, s * n, 0, s * c, 0, t.X, t.Y, t.Z, 1);
    }

    // ------------------------------------------------------------------ meshes

    // Vertex alpha = material: 255 foliage, 128 bark, 0 rock.
    private static Color Leaf(byte r, byte g, byte b) => new(r, g, b, (byte)255);
    private static Color Bark(byte r, byte g, byte b) => new(r, g, b, (byte)128);
    private static Color Stone(byte r, byte g, byte b) => new(r, g, b, (byte)0);

    private void EnsureNatureMeshes()
    {
        if (_natNear != null) return;
        _natNear = new InstancedMesh[8];
        _natFar = new InstancedMesh[8];
        for (int v = 0; v < 4; v++)
        {
            (_natNear[v], _natHeight[v]) = BuildTree(v, false);
            (_natFar[v], _) = BuildTree(v, true);
            (_natNear[4 + v], _natHeight[4 + v]) = BuildRock(v, false);
            (_natFar[4 + v], _) = BuildRock(v, true);
        }
    }

    private static (InstancedMesh, float) BuildTree(int v, bool far)
    {
        var b = new MeshBuilder();
        var bark = Bark(92, 70, 50);
        if (v < 2)
        {
            // Conifer: trunk + stacked cones (variant 1 narrower, bluer).
            var leaf = v == 0 ? Leaf(52, 92, 50) : Leaf(48, 86, 70);
            float w = v == 0 ? 1f : 0.82f;
            b.Cylinder(Vector3.Zero, Axis.Y, 0.9f, 0.11f, 0.07f, far ? 4 : 6, bark);
            if (far) b.Cylinder(new Vector3(0, 0.45f, 0), Axis.Y, 2.35f, 0.68f * w, 0.02f, 5, leaf, leaf);
            else
                for (int i = 0; i < 3; i++)
                    b.Cylinder(new Vector3(0, 0.5f + i * 0.62f, 0), Axis.Y, 1.0f, (0.74f - i * 0.17f) * w, 0.03f, 8, leaf, Leaf(40, 70, 40), phase: i * 0.4f);
        }
        else
        {
            // Broadleaf: trunk + clustered crowns (variant 3 olive / autumn).
            var leaf = v == 2 ? Leaf(70, 108, 48) : Leaf(98, 104, 50);
            b.Cylinder(Vector3.Zero, Axis.Y, 1.1f, 0.13f, 0.08f, far ? 4 : 6, bark);
            if (far) b.Sphere(new Vector3(0, 1.45f, 0), new Vector3(0.78f, 0.66f, 0.78f), 3, 5, leaf);
            else
            {
                b.Sphere(new Vector3(0, 1.55f, 0), new Vector3(0.68f, 0.58f, 0.68f), 4, 7, leaf);
                b.Sphere(new Vector3(0.34f, 1.25f, 0.12f), new Vector3(0.46f, 0.42f, 0.46f), 3, 6, leaf);
                b.Sphere(new Vector3(-0.28f, 1.3f, -0.22f), new Vector3(0.48f, 0.42f, 0.48f), 3, 6, leaf);
            }
        }
        var (_, maxY, _) = b.Bounds();
        return (new InstancedMesh(b.Upload()), maxY);
    }

    private static (InstancedMesh, float) BuildRock(int v, bool far)
    {
        var b = new MeshBuilder();
        var col = v switch { 0 => Stone(118, 112, 104), 1 => Stone(104, 100, 96), 2 => Stone(128, 118, 100), _ => Stone(96, 92, 88) };
        uint seed = 977u + (uint)v * 131u;
        float R() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFF) / 65536f; }
        int chunks = far ? 1 : 2 + v % 2;
        for (int i = 0; i < chunks; i++)
        {
            float sz = i == 0 ? 1f : 0.45f + R() * 0.25f;
            float ang = R() * MathF.Tau, d = i == 0 ? 0 : 0.6f + R() * 0.3f;
            var xf = Matrix4x4.CreateRotationX((R() - 0.5f) * 0.6f) * Matrix4x4.CreateRotationY(R() * MathF.Tau)
                     * Matrix4x4.CreateTranslation(MathF.Cos(ang) * d, 0, MathF.Sin(ang) * d);
            var radii = new Vector3(0.95f * sz, (0.5f + 0.25f * (v & 1)) * sz, (0.7f + 0.2f * R()) * sz);
            if (far) b.Sphere(new Vector3(0, 0.1f, 0), radii, 2, 5, col, xf);
            else b.Sphere(new Vector3(0, 0.1f * sz, 0), radii, 3, 6 + (i & 1), col, xf);
        }
        var (_, maxY, _) = b.Bounds();
        return (new InstancedMesh(b.Upload()), maxY);
    }

    private void DisposeNature()
    {
        if (_natNear == null) return;
        for (int i = 0; i < 8; i++) { _natNear[i].Dispose(); _natFar![i].Dispose(); }
        _natNear = _natFar = null;
    }

    // ------------------------------------------------------------------ shader

    // instData: x = per-prop random, y = fire glow 0..1, z = charring 0..1, w = sway (standing trees).
    private const string NatureVs = """
        #version 330
        in vec3 vertexPosition;
        in vec3 vertexNormal;
        in vec4 vertexColor;
        layout(location = 10) in mat4 instTransform;
        layout(location = 14) in vec4 instData;
        uniform mat4 viewProj;
        uniform float time;
        out vec3 vWorld;
        out vec3 vNormal;
        out vec4 vColor;
        out vec4 vInst;
        out float vLocalY;
        void main() {
            vec4 wp = instTransform * vec4(vertexPosition, 1.0);
            if (vertexColor.a > 0.75 && instData.w > 0.0) {
                float ph = time * 1.4 + wp.x * 0.31 + wp.z * 0.23 + instData.x * 6.0;
                wp.xz += vec2(sin(ph), cos(ph * 0.8)) * 0.035 * vertexPosition.y * instData.w;
            }
            vWorld = wp.xyz;
            vNormal = mat3(instTransform) * vertexNormal;
            vColor = vertexColor;
            vInst = instData;
            vLocalY = vertexPosition.y;
            gl_Position = viewProj * wp;
        }
        """;

    private static readonly string NatureFs = "#version 330\n" + Shaders.CommonFs + Shaders.ShadowFs + Shaders.FogOfWarFs + """
        in vec3 vWorld;
        in vec3 vNormal;
        in vec4 vColor;
        in vec4 vInst;
        in float vLocalY;
        uniform float fade;
        out vec4 finalColor;
        void main() {
            if (fade < 0.999 && bayer4(gl_FragCoord.xy) >= fade) discard;
            float m = vColor.a;
            bool leaf = m > 0.75, rock = m < 0.25;
            float charred = vInst.z;
            vec2 q = vWorld.xz * 2.1 + vec2(vWorld.y * 1.7) + vec2(vInst.x * 37.0);
            float nz = fbm(q);
            // Burnt crowns lose most of their foliage.
            if (leaf && charred > 0.35 && nz < (charred - 0.35) * 1.2) discard;
            vec3 n = normalize(vNormal);
            vec3 base = vColor.rgb;
            if (leaf) base *= 0.82 + 0.36 * vInst.x + (nz - 0.5) * 0.35;
            else if (rock) {
                base *= 0.85 + 0.3 * nz;
                base = mix(base, vec3(0.30, 0.36, 0.22), smoothstep(0.55, 0.9, n.y) * smoothstep(0.45, 0.7, fbm(q * 1.7 + 3.0)) * 0.6);
            }
            base = mix(base, vec3(0.045, 0.04, 0.035), charred * (leaf ? 0.92 : 0.8));
            vec3 V = normalize(camPos - vWorld);
            float ndl = dot(n, sunDir);
            float diff = leaf ? max(ndl * 0.6 + 0.4, 0.0) : max(ndl, 0.0);
            float sh = shadowFactor(vWorld, n);
            vec3 amb = mix(groundAmb, skyAmb, n.y * 0.5 + 0.5);
            float ao = leaf ? 0.7 + 0.3 * clamp(vLocalY / 2.4, 0.0, 1.0) : 1.0;
            vec3 c = base * (amb * 0.9 * ao + sunColor * diff * mix(0.35, 1.0, sh));
            if (rock) {
                vec3 H = normalize(sunDir + V);
                c += sunColor * pow(max(dot(n, H), 0.0), 18.0) * 0.06 * sh;
            }
            float glow = vInst.y;
            if (glow > 0.001) {
                float flick = 0.65 + 0.35 * sin(time * 9.0 + vWorld.x * 2.7 + vWorld.z * 3.1 + nz * 6.0);
                float hot = leaf ? smoothstep(0.35, 0.75, nz) : smoothstep(0.6, 0.9, nz);
                c += vec3(1.0, 0.38, 0.08) * hot * glow * flick * 1.8;
            }
            c = applyFogOfWar(c, vWorld.xz);
            c = applyFog(c, vWorld);
            finalColor = vec4(c, 1.0);
        }
        """;
}
