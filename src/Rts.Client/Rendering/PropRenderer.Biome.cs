using System.Numerics;
using Raylib_cs;
using Rts.Sim.Map;

namespace Rts.Client.Rendering;

/// <summary>(8b-3) Per-biome look of the map props: rock colours/shape and the NatureFs top-surface tint (moss, sand
/// dust, snow, ash) plus an emissive crack glow on rocks (lava).</summary>
public sealed record NatureLook(Color[] Rocks, int RockSidesDelta, float RockHeight, Vector3 Moss, float MossAmount, Vector3 RockGlow)
{
    private static Color S(byte r, byte g, byte b) => new(r, g, b, (byte)0);

    public static readonly NatureLook Temperate = new([S(118, 112, 104), S(104, 100, 96), S(128, 118, 100), S(96, 92, 88)], 0, 1f, new(0.30f, 0.36f, 0.22f), 0.6f, Vector3.Zero);
    public static readonly NatureLook Desert = new([S(176, 128, 90), S(150, 108, 78), S(190, 150, 106), S(132, 96, 70)], -1, 0.85f, new(0.74f, 0.60f, 0.42f), 0.55f, Vector3.Zero);
    public static readonly NatureLook Tundra = new([S(112, 118, 126), S(96, 102, 112), S(126, 130, 136), S(86, 90, 98)], 0, 1f, new(0.88f, 0.91f, 0.95f), 0.95f, Vector3.Zero);
    public static readonly NatureLook Lava = new([S(46, 42, 42), S(36, 34, 36), S(58, 50, 46), S(30, 28, 30)], -1, 1.1f, new(0.30f, 0.28f, 0.27f), 0.35f, new(0.80f, 0.27f, 0.06f));
    public static readonly NatureLook Tropical = new([S(92, 94, 84), S(76, 82, 72), S(104, 100, 86), S(66, 70, 62)], 0, 1f, new(0.17f, 0.33f, 0.11f), 0.85f, Vector3.Zero);

    public static NatureLook For(Biome b) => b switch
    {
        Biome.Desert => Desert, Biome.Tundra => Tundra, Biome.Lava => Lava, Biome.Tropical => Tropical, _ => Temperate,
    };
}

public sealed partial class PropRenderer
{
    private Biome _natBiome;

    private Biome NatureBiome => _ctx.TerrainRenderer.Biome;

    private static Matrix4x4 Tilt(Vector3 at, float yaw, float tilt) =>
        Matrix4x4.CreateRotationZ(tilt) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(at);

    /// <summary>A tapering limb from <paramref name="at"/>, tilted <paramref name="tilt"/> from vertical towards yaw.</summary>
    private static void Limb(MeshBuilder b, Vector3 at, float yaw, float tilt, float len, float r, int sides, Color c) =>
        b.Cylinder(Vector3.Zero, Axis.Y, len, r, r * 0.45f, sides, c, xf: Tilt(at, yaw, tilt));

    /// <summary>Tree variant <paramref name="v"/> (0..3) of a biome; Temperate = the original 8b species.</summary>
    private static (InstancedMesh, float) BuildBiomeTree(Biome biome, int v, bool far)
    {
        if (biome == Biome.Temperate) return BuildTree(v, far);
        var b = new MeshBuilder();
        int sd = far ? 4 : 6;
        switch (biome)
        {
            case Biome.Desert:
                if (v < 2)
                {
                    // Saguaro cactus: bark material (no sway), arms bent upwards.
                    var cac = v == 0 ? Bark(66, 106, 58) : Bark(84, 112, 62);
                    float hgt = v == 0 ? 2.3f : 1.5f, r = v == 0 ? 0.16f : 0.13f;
                    b.Cylinder(Vector3.Zero, Axis.Y, hgt, r, r * 0.85f, sd, cac);
                    b.Sphere(new Vector3(0, hgt, 0), new Vector3(r * 0.85f, r * 0.7f, r * 0.85f), 2, sd, cac);
                    int arms = far ? 1 : v == 0 ? 2 : 1;
                    for (int i = 0; i < arms; i++)
                    {
                        float s = i == 0 ? 1f : -1f, y = hgt * (i == 0 ? 0.42f : 0.55f), ar = r * 0.6f;
                        b.Cylinder(new Vector3(0, y, 0), Axis.X, 0.34f * s, ar, ar, sd, cac);
                        b.Cylinder(new Vector3(0.32f * s, y - ar * 0.5f, 0), Axis.Y, hgt * 0.32f, ar, ar * 0.85f, sd, cac);
                        b.Sphere(new Vector3(0.32f * s, y - ar * 0.5f + hgt * 0.32f, 0), new Vector3(ar * 0.85f, ar * 0.7f, ar * 0.85f), 2, sd, cac);
                    }
                }
                else if (v == 2)
                {
                    // Dry shrub: low dusty clumps on a stub.
                    var leaf = Leaf(132, 120, 74);
                    b.Cylinder(Vector3.Zero, Axis.Y, 0.3f, 0.07f, 0.05f, 4, Bark(104, 84, 60));
                    b.Sphere(new Vector3(0, 0.42f, 0), new Vector3(0.55f, 0.36f, 0.55f), far ? 2 : 3, far ? 5 : 7, leaf);
                    if (!far) b.Sphere(new Vector3(0.35f, 0.3f, 0.18f), new Vector3(0.34f, 0.26f, 0.34f), 3, 6, Leaf(118, 112, 70));
                }
                else
                {
                    // Dead tree: bleached trunk and bare branches.
                    var wood = Bark(118, 98, 76);
                    b.Cylinder(Vector3.Zero, Axis.Y, 1.3f, 0.12f, 0.07f, sd, wood);
                    for (int i = 0; i < (far ? 2 : 4); i++)
                        Limb(b, new Vector3(0, 0.8f + i * 0.14f, 0), i * 2.1f, 0.55f + 0.12f * i, 0.75f - i * 0.08f, 0.055f, 4, wood);
                }
                break;
            case Biome.Tundra:
                if (v < 2)
                {
                    // Snowy conifer: dark tiers, each with a snow cone over its upper part.
                    var leaf = v == 0 ? Leaf(34, 62, 44) : Leaf(40, 66, 60);
                    var snow = Leaf(206, 215, 226); // a bit darker than the snow ground so crowns stay readable
                    float w = v == 0 ? 1f : 0.8f;
                    b.Cylinder(Vector3.Zero, Axis.Y, 0.9f, 0.11f, 0.07f, sd, Bark(78, 64, 52));
                    int tiers = far ? 1 : 3;
                    for (int i = 0; i < tiers; i++)
                    {
                        float y0 = far ? 0.45f : 0.5f + i * 0.62f, len = far ? 2.35f : 1.0f, rr = (far ? 0.68f : 0.74f - i * 0.17f) * w;
                        b.Cylinder(new Vector3(0, y0, 0), Axis.Y, len, rr, 0.03f, far ? 5 : 8, leaf, phase: i * 0.4f);
                        // Snow on the upper half only: the dark lower ring shows from above (readable on snow).
                        float sy = len * 0.5f, sr = rr - (rr - 0.03f) * 0.5f + 0.03f;
                        b.Cylinder(new Vector3(0, y0 + sy, 0), Axis.Y, len * 0.52f, sr, 0.045f, far ? 5 : 8, snow, phase: i * 0.4f);
                    }
                }
                else
                {
                    // Birch: white bark, thin branches; variant 3 keeps a few yellow leaves.
                    var bark = Bark(214, 210, 198);
                    b.Cylinder(Vector3.Zero, Axis.Y, 1.7f, 0.09f, 0.05f, sd, bark);
                    for (int i = 0; i < (far ? 2 : 5); i++)
                        Limb(b, new Vector3(0, 0.75f + i * 0.17f, 0), i * 2.4f, 0.5f + 0.06f * i, 0.6f - i * 0.05f, 0.035f, 4, Bark(190, 186, 176));
                    if (v == 3)
                    {
                        var yl = Leaf(196, 170, 70);
                        b.Sphere(new Vector3(0, 1.7f, 0), new Vector3(0.36f, 0.3f, 0.36f), far ? 2 : 3, far ? 5 : 6, yl);
                        if (!far) b.Sphere(new Vector3(0.3f, 1.3f, 0.1f), new Vector3(0.26f, 0.22f, 0.26f), 3, 5, Leaf(184, 150, 60));
                    }
                }
                break;
            case Biome.Lava:
            {
                var coal = Bark(30, 26, 24);
                if (v == 0)
                {
                    // Charred dead conifer: black spike with stubby drooping branches.
                    b.Cylinder(Vector3.Zero, Axis.Y, 2.1f, 0.12f, 0.03f, sd, coal);
                    for (int i = 0; i < (far ? 2 : 7); i++)
                        Limb(b, new Vector3(0, 0.6f + i * 0.2f, 0), i * 2.4f, 1.75f, 0.42f - i * 0.03f, 0.04f, 3, coal);
                }
                else if (v == 1)
                {
                    // Dead broadleaf: forked black branches.
                    b.Cylinder(Vector3.Zero, Axis.Y, 1.1f, 0.13f, 0.08f, sd, coal);
                    for (int i = 0; i < (far ? 2 : 3); i++)
                    {
                        float yaw = i * 2.1f;
                        Limb(b, new Vector3(0, 1.0f, 0), yaw, 0.6f, 0.8f, 0.06f, 4, coal);
                        if (!far) Limb(b, new Vector3(-MathF.Cos(yaw) * 0.25f, 1.42f, MathF.Sin(yaw) * 0.25f), yaw + 0.9f, 0.9f, 0.4f, 0.03f, 3, coal);
                    }
                }
                else
                {
                    // Ash-crowned trees: grey ash foliage (variant 3 a conifer).
                    var ash = v == 2 ? Leaf(96, 92, 88) : Leaf(74, 70, 66);
                    b.Cylinder(Vector3.Zero, Axis.Y, v == 2 ? 1.1f : 0.9f, 0.12f, 0.07f, sd, Bark(44, 38, 34));
                    if (v == 2)
                    {
                        b.Sphere(new Vector3(0, 1.5f, 0), new Vector3(0.66f, 0.52f, 0.66f), far ? 3 : 4, far ? 5 : 7, ash);
                        if (!far) b.Sphere(new Vector3(0.3f, 1.2f, -0.15f), new Vector3(0.4f, 0.34f, 0.4f), 3, 6, Leaf(84, 80, 76));
                    }
                    else
                        for (int i = 0; i < (far ? 1 : 2); i++)
                            b.Cylinder(new Vector3(0, 0.55f + i * 0.75f, 0), Axis.Y, far ? 1.9f : 1.1f, 0.62f - i * 0.18f, 0.03f, far ? 5 : 7, ash, phase: i * 0.5f);
                }
                break;
            }
            default: // Tropical
                if (v < 2)
                {
                    // Palm: leaning segmented trunk, drooping fronds.
                    var bark = v == 0 ? Bark(124, 102, 72) : Bark(108, 90, 66);
                    int segs = far ? 3 : 5;
                    float segLen = (v == 0 ? 2.3f : 2.7f) / segs, lean = v == 0 ? 0.09f : -0.07f, ang = 0, r = 0.12f;
                    var p = Vector3.Zero;
                    for (int i = 0; i < segs; i++)
                    {
                        ang += lean * (1 + i * 0.5f) * (5f / segs);
                        b.Cylinder(Vector3.Zero, Axis.Y, segLen * 1.04f, r, r * 0.9f, sd, bark, xf: Tilt(p, 0, ang));
                        p += new Vector3(-MathF.Sin(ang), MathF.Cos(ang), 0) * segLen;
                        r *= 0.9f;
                    }
                    var leaf = v == 0 ? Leaf(56, 114, 40) : Leaf(72, 124, 44);
                    int fronds = far ? 5 : v == 0 ? 7 : 8;
                    for (int k = 0; k < fronds; k++)
                    {
                        float yaw = k * MathF.Tau / fronds + v * 0.3f, droop = -0.35f - 0.2f * (k & 1);
                        var xf = Matrix4x4.CreateRotationZ(droop) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(p);
                        b.Sphere(new Vector3(0.55f, 0, 0), new Vector3(0.62f, 0.05f, 0.17f), 2, far ? 4 : 5, k % 3 == 0 ? Leaf(46, 98, 36) : leaf, xf);
                    }
                    if (!far) b.Sphere(p - new Vector3(0, 0.08f, 0), new Vector3(0.14f, 0.12f, 0.14f), 2, 5, Bark(92, 70, 40));
                }
                else
                {
                    // Lush broadleaf (variant 3 a taller jungle tree with a wide flat canopy).
                    var leaf = v == 2 ? Leaf(36, 98, 34) : Leaf(50, 112, 40);
                    float th = v == 2 ? 1.1f : 1.6f;
                    b.Cylinder(Vector3.Zero, Axis.Y, th, 0.14f, 0.09f, sd, Bark(86, 68, 50));
                    if (v == 2)
                    {
                        b.Sphere(new Vector3(0, 1.55f, 0), new Vector3(0.78f, 0.62f, 0.78f), far ? 3 : 4, far ? 5 : 7, leaf);
                        if (!far)
                        {
                            b.Sphere(new Vector3(0.42f, 1.2f, 0.18f), new Vector3(0.5f, 0.44f, 0.5f), 3, 6, Leaf(44, 106, 36));
                            b.Sphere(new Vector3(-0.36f, 1.25f, -0.3f), new Vector3(0.52f, 0.44f, 0.52f), 3, 6, leaf);
                        }
                    }
                    else
                    {
                        b.Sphere(new Vector3(0, th + 0.25f, 0), new Vector3(1.0f, 0.38f, 1.0f), far ? 2 : 3, far ? 6 : 8, leaf);
                        if (!far) b.Sphere(new Vector3(0.2f, th - 0.3f, -0.1f), new Vector3(0.55f, 0.32f, 0.55f), 3, 6, Leaf(40, 96, 34));
                    }
                }
                break;
        }
        var (_, maxY, _) = b.Bounds();
        return (new InstancedMesh(b.Upload()), maxY);
    }
}
