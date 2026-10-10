using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// Procedural strategic-icon atlas rasterized on the CPU from signed distance functions (anti-aliased, mipmapped).
/// Each 64px cell = frame shape by domain (circle land, triangle air, shield naval, square structure,
/// diamond commander, octagon experimental) + white role glyph + tech pips, in a normal and a selected
/// (white border) variant. Channels: R = fill (tinted with the army color by the shader), G = white, B = dark border.
/// </summary>
public static unsafe class IconAtlas
{
    public const int Cell = 64;
    public const int Cols = 16;
    public const int TexSize = Cell * Cols;

    public static readonly string[] Kinds =
    [
        "land_direct", "land_artillery", "land_aa", "land_engineer", "commander", "air_fighter", "air_bomber",
        "air_gunship", "air_transport", "naval", "factory", "eco_mass", "eco_energy", "defense", "intel", "shield",
        "experimental", "unknown",
    ];

    private static readonly Dictionary<string, int> KindIndex = Kinds.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);
    private static Texture2D? _tex;

    public static int KindOf(string icon) => KindIndex.TryGetValue(icon, out int i) ? i : KindIndex["unknown"];

    public static Texture2D Texture => _tex ??= Build();

    /// <summary>UV rectangle (u0, v0, u1, v1) of a cell. tech: 1..3 (0 = no pips).</summary>
    public static Vector4 Uv(int kind, int tech, bool selected)
    {
        int idx = (kind * 4 + Math.Clamp(tech, 0, 3)) * 2 + (selected ? 1 : 0);
        int cx = idx % Cols, cy = idx / Cols;
        float s = 1f / Cols;
        return new Vector4(cx * s, cy * s, (cx + 1) * s, (cy + 1) * s);
    }

    private static Texture2D Build()
    {
        var px = new byte[TexSize * TexSize * 4];
        for (int k = 0; k < Kinds.Length; k++)
        for (int tech = 0; tech < 4; tech++)
        for (int sel = 0; sel < 2; sel++)
        {
            int idx = (k * 4 + tech) * 2 + sel;
            RasterCell(px, (idx % Cols) * Cell, (idx / Cols) * Cell, Kinds[k], tech, sel == 1);
        }
        Texture2D tex;
        fixed (byte* p = px)
        {
            var img = new Image { Data = p, Width = TexSize, Height = TexSize, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
            tex = Raylib.LoadTextureFromImage(img);
        }
        Raylib.GenTextureMipmaps(ref tex);
        Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
        Raylib.SetTextureWrap(tex, TextureWrap.Clamp);
        return tex;
    }

    // ------------------------------------------------------------------ SDF helpers (pixel units, y down)

    private static float Circle(Vector2 p, float r) => p.Length() - r;

    private static float Box(Vector2 p, Vector2 b)
    {
        var d = Vector2.Abs(p) - b;
        return Vector2.Max(d, Vector2.Zero).Length() + MathF.Min(MathF.Max(d.X, d.Y), 0);
    }

    private static float Diamond(Vector2 p, float r) => (MathF.Abs(p.X) + MathF.Abs(p.Y) - r) * 0.7071f;

    private static float Segment(Vector2 p, Vector2 a, Vector2 b, float w)
    {
        var pa = p - a;
        var ba = b - a;
        float h = Math.Clamp(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba), 0, 1);
        return (pa - ba * h).Length() - w;
    }

    /// <summary>Upward-pointing triangle (apex at -y).</summary>
    private static float Triangle(Vector2 p, float r)
    {
        // Equilateral triangle centered at origin, circumradius-ish r.
        const float k = 1.7320508f;
        p.Y = -p.Y - r * 0.25f;
        p.X = MathF.Abs(p.X) - r;
        p.Y = p.Y + r / k;
        if (p.X + k * p.Y > 0) p = new Vector2(p.X - k * p.Y, -k * p.X - p.Y) / 2;
        p.X -= Math.Clamp(p.X, -2 * r, 0);
        return -p.Length() * MathF.Sign(p.Y);
    }

    private static float Shape(string kind, Vector2 p)
    {
        if (kind == "commander") return Diamond(p, 25);
        if (kind == "experimental") return MathF.Max(Box(p, new Vector2(21, 21)), Diamond(p, 28));
        if (kind.StartsWith("air_")) return Triangle(p, 21);
        if (kind == "naval") return MathF.Max(Box(p, new Vector2(19, 17)), (MathF.Abs(p.X) + p.Y) * 0.7071f - 17);
        if (kind.StartsWith("land_")) return Circle(p, 17.5f);
        return Box(p, new Vector2(17, 17)); // structures
    }

    private static float Glyph(string kind, Vector2 p)
    {
        const float w = 2.6f;
        switch (kind)
        {
            case "land_direct":
            case "air_bomber":
            case "naval":
            case "unknown":
                return Circle(p, 5.5f);
            case "land_artillery":
                return MathF.Min(MathF.Abs(Circle(p, 7f)) - 2.2f, Circle(p, 2.2f));
            case "land_aa":
            case "air_fighter":
                return MathF.Min(Segment(p, new(-8, 4), new(0, -5), w), Segment(p, new(0, -5), new(8, 4), w));
            case "land_engineer":
            case "air_gunship":
                return MathF.Min(Segment(p, new(-8, 0), new(8, 0), w), Segment(p, new(0, -8), new(0, 8), w));
            case "commander":
                return MathF.Min(MathF.Min(Segment(p, new(-10, 0), new(10, 0), w), Segment(p, new(0, -10), new(0, 10), w)), Circle(p, 4.5f));
            case "air_transport":
                return Segment(p, new(-8, 2), new(8, 2), w + 0.4f);
            case "factory":
                return MathF.Min(MathF.Min(Segment(p, new(-8, -6), new(8, -6), 2f), Segment(p, new(-8, 0), new(8, 0), 2f)), Segment(p, new(-8, 6), new(8, 6), 2f));
            case "eco_mass":
                return Diamond(p, 8f);
            case "eco_energy":
                return MathF.Min(MathF.Min(Segment(p, new(3, -10), new(-4, 1), w), Segment(p, new(-4, 1), new(4, -1), w)), Segment(p, new(4, -1), new(-3, 10), w));
            case "defense":
                return MathF.Min(MathF.Abs(Circle(p, 7.5f)) - 1.8f, MathF.Min(Segment(p, new(-11, 0), new(11, 0), 1.6f), Segment(p, new(0, -11), new(0, 11), 1.6f)));
            case "intel":
                return MathF.Min(MathF.Abs(Circle(p, 9f)) - 1.6f, MathF.Min(MathF.Abs(Circle(p, 4.5f)) - 1.6f, Circle(p, 1.6f)));
            case "shield":
                return MathF.Max(MathF.Abs(Circle(p - new Vector2(0, 4), 9f)) - 2f, p.Y - 4);
            case "experimental":
                return MathF.Min(Segment(p, new(-9, -9), new(9, 9), 3.2f), Segment(p, new(-9, 9), new(9, -9), 3.2f));
            default:
                return 99;
        }
    }

    private static void RasterCell(byte[] px, int x0, int y0, string kind, int tech, bool selected)
    {
        bool hasPips = tech > 1 && kind != "experimental" && kind != "commander";
        float border = selected ? 3.6f : 2.4f;
        for (int y = 0; y < Cell; y++)
        for (int x = 0; x < Cell; x++)
        {
            var p = new Vector2(x + 0.5f - Cell / 2f, y + 0.5f - Cell / 2f + (hasPips ? -3f : 0f));
            float ds = Shape(kind, p);
            float dg = Glyph(kind, p);
            float pip = 99;
            if (hasPips)
            {
                // Tech pips below the frame.
                var q = new Vector2(x + 0.5f - Cell / 2f, y + 0.5f - (Cell - 7f));
                float span = (tech - 1) * 8f;
                for (int i = 0; i < tech; i++)
                    pip = MathF.Min(pip, Box(q - new Vector2(-span / 2 + i * 8f, 0), new Vector2(2.6f, 2.6f)));
            }
            float aOuter = Math.Clamp(0.5f - (MathF.Min(ds, pip) - border), 0, 1);
            float aFill = Math.Clamp(0.5f - ds, 0, 1);
            float aGlyph = MathF.Max(Math.Clamp(0.5f - dg, 0, 1) * aFill, Math.Clamp(0.5f - pip, 0, 1));
            float g = aGlyph;
            float f = MathF.Max(0, aFill - g);
            float b = MathF.Max(0, aOuter - f - g);
            int o = ((y0 + y) * TexSize + x0 + x) * 4;
            float white = g + (selected ? b : 0);
            float dark = selected ? 0 : b;
            px[o] = (byte)(f * 255);
            px[o + 1] = (byte)(white * 255);
            px[o + 2] = (byte)(dark * 255);
            px[o + 3] = (byte)(aOuter * 255);
        }
    }
}
