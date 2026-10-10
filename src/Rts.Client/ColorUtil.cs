using Raylib_cs;

namespace Rts.Client;

public static class ColorUtil
{
    public static Color FromRgba(uint rgba) =>
        new((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);

    public static Color Scale(Color c, float k) =>
        new((byte)Math.Clamp(c.R * k, 0, 255), (byte)Math.Clamp(c.G * k, 0, 255), (byte)Math.Clamp(c.B * k, 0, 255), c.A);

    public static Color WithAlpha(Color c, byte a) => new(c.R, c.G, c.B, a);

    public static Color Lerp(Color a, Color b, float t) =>
        new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
}
