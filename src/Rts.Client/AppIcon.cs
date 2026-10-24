using System.Numerics;
using Raylib_cs;

namespace Rts.Client;

/// <summary>
/// The game icon, drawn procedurally (no asset file): a dark rounded tile with radar rings and two gold rank chevrons.
/// Used as the window icon and exported with <c>--export-icon FILE.png</c> (no window; tools/package/package.sh puts
/// it in the release folder for the desktop entry). Pixels come from <see cref="Pixel"/> (pure, antialiased SDFs).
/// </summary>
public static class AppIcon
{
    public const int Size = 256;

    /// <summary><c>Rts.Client --export-icon FILE.png</c>: writes the icon and returns true (the caller exits).</summary>
    public static bool TryExport(string[] args)
    {
        if (args.Length != 2 || args[0] != "--export-icon") return false;
        string path = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        var img = Build(Size);
        bool ok = Raylib.ExportImage(img, path);
        Raylib.UnloadImage(img);
        if (!ok) throw new IOException("could not write the icon " + path);
        Console.WriteLine("Icon saved: " + path);
        return true;
    }

    /// <summary>Sets the window icon (after InitWindow).</summary>
    public static void SetWindowIcon()
    {
        var img = Build(Size);
        Raylib.SetWindowIcon(img);
        Raylib.UnloadImage(img);
    }

    /// <summary>A CPU image (R8G8B8A8) of the icon; no window needed.</summary>
    public static unsafe Image Build(int size)
    {
        var img = Raylib.GenImageColor(size, size, Color.Blank);
        var px = (Color*)img.Data;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = Pixel(x, y, size);
        return img;
    }

    /// <summary>Colour of pixel (x, y) of a size×size icon.</summary>
    public static Color Pixel(int x, int y, int size)
    {
        float s = size * 0.5f;
        var p = new Vector2((x + 0.5f - s) / s, (y + 0.5f - s) / s); // -1..1, y down
        float aa = 1.5f / s;                                          // ~1.5 px antialiasing band
        var c = Vector4.Zero;                                         // premultiplied RGBA 0..1

        // Tile: rounded square, vertical gradient, light rim.
        float tile = RoundBox(p, 0.94f, 0.24f);
        var top = new Vector4(30, 48, 70, 255) / 255f;
        var bottom = new Vector4(9, 14, 23, 255) / 255f;
        var bg = Vector4.Lerp(top, bottom, (p.Y + 1) * 0.5f);
        bg += new Vector4(0.10f, 0.16f, 0.22f, 0) * MathF.Max(0, 1 - (p - new Vector2(0, -0.15f)).Length() / 0.9f) * 0.6f;
        c = Over(c, bg, Cover(tile, aa));
        c = Over(c, new Vector4(110, 180, 245, 255) / 255f, Cover(MathF.Abs(tile + 0.03f) - 0.012f, aa) * 0.55f);

        // Radar rings and cross hair (faint, clipped to the tile).
        float inside = Cover(tile + 0.06f, aa);
        var ring = new Vector4(110, 180, 245, 255) / 255f;
        foreach (float r in (ReadOnlySpan<float>)[0.36f, 0.62f])
            c = Over(c, ring, Cover(MathF.Abs(p.Length() - r) - 0.008f, aa) * 0.35f * inside);
        float cross = MathF.Min(MathF.Abs(p.X), MathF.Abs(p.Y)) - 0.005f;
        c = Over(c, ring, Cover(cross, aa) * 0.18f * inside);

        // Two rank chevrons (gold over orange) with a dark drop shadow.
        var gold = new Vector4(250, 200, 70, 255) / 255f;
        var amber = new Vector4(235, 130, 40, 255) / 255f;
        var shadow = new Vector4(0, 0, 0, 1);
        float upper = Chevron(p - new Vector2(0, -0.08f), 0.50f, 0.36f) - 0.085f;
        float lower = Chevron(p - new Vector2(0, 0.26f), 0.40f, 0.29f) - 0.07f;
        var sh = new Vector2(0.02f, 0.035f);
        c = Over(c, shadow, Cover(MathF.Min(Chevron(p - sh - new Vector2(0, -0.08f), 0.50f, 0.36f) - 0.085f,
            Chevron(p - sh - new Vector2(0, 0.26f), 0.40f, 0.29f) - 0.07f), aa * 4) * 0.45f);
        c = Over(c, amber, Cover(lower, aa));
        c = Over(c, gold, Cover(upper, aa));
        // Top highlight on the upper chevron.
        c = Over(c, new Vector4(1, 0.95f, 0.75f, 1), Cover(upper + 0.05f, aa) * MathF.Max(0, -p.Y - 0.1f) * 0.6f);

        float a = c.W;
        if (a <= 0) return new Color(0, 0, 0, 0);
        return new Color((byte)(c.X / a * 255 + 0.5f), (byte)(c.Y / a * 255 + 0.5f), (byte)(c.Z / a * 255 + 0.5f), (byte)(a * 255 + 0.5f));
    }

    /// <summary>Signed distance to a rounded square of half-size <paramref name="h"/> and corner radius <paramref name="r"/>.</summary>
    private static float RoundBox(Vector2 p, float h, float r)
    {
        var q = new Vector2(MathF.Abs(p.X), MathF.Abs(p.Y)) - new Vector2(h - r);
        return Vector2.Max(q, Vector2.Zero).Length() + MathF.Min(MathF.Max(q.X, q.Y), 0) - r;
    }

    /// <summary>Distance to an upward chevron polyline: apex at the origin, arms down to (±w, d).</summary>
    private static float Chevron(Vector2 p, float w, float d)
    {
        var q = new Vector2(MathF.Abs(p.X), p.Y);
        return Segment(q, new Vector2(0, -d * 0.5f), new Vector2(w, d * 0.5f));
    }

    private static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
        return (p - (a + ab * t)).Length();
    }

    private static float Cover(float d, float aa) => Math.Clamp(0.5f - d / aa, 0, 1);

    /// <summary>Premultiplied "over": straight-alpha colour <paramref name="src"/> with coverage <paramref name="k"/>.</summary>
    private static Vector4 Over(Vector4 dst, Vector4 src, float k)
    {
        float a = src.W * k;
        return new Vector4(src.X * a, src.Y * a, src.Z * a, a) + dst * (1 - a);
    }
}
