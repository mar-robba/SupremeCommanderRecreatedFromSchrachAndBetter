using System.Numerics;
using Raylib_cs;

namespace Rts.Client.UI;

/// <summary>
/// (9b-2) Minimap size and zoom maths (pure, no GL; <c>MinimapViewTests</c>). The minimap draws the whole map into a
/// "virtual" rectangle <see cref="Virtual"/> = the visible map rectangle scaled by the zoom and shifted so the camera
/// focus sits in the middle (clamped so the map always covers the panel); a scissor cuts it to the panel. Every
/// map↔screen conversion of the minimap (dots, fog, pings, clicks) goes through that one rectangle.
/// </summary>
public static class MinimapView
{
    /// <summary>Panel size multipliers (settings "Minimap size", buttons -/+ on the minimap).</summary>
    public static readonly float[] Sizes = [0.75f, 1f, 1.25f, 1.5f, 1.75f];
    /// <summary>Zoom steps (mouse wheel over the minimap).</summary>
    public static readonly float[] Zooms = [1f, 1.5f, 2f, 3f, 4f, 6f, 8f];
    public const float MaxZoom = 8f;

    private static int Nearest(float[] steps, float v)
    {
        int best = 0;
        for (int i = 1; i < steps.Length; i++)
            if (MathF.Abs(steps[i] - v) < MathF.Abs(steps[best] - v)) best = i;
        return best;
    }

    /// <summary>Snaps a zoom to the nearest step (1..<see cref="MaxZoom"/>).</summary>
    public static float ClampZoom(float z) => Zooms[Nearest(Zooms, float.IsFinite(z) ? z : 1f)];

    /// <summary>Next zoom step in direction <paramref name="dir"/> (+1 in, -1 out), clamped at the ends.</summary>
    public static float StepZoom(float z, int dir) => Zooms[Math.Clamp(Nearest(Zooms, z) + Math.Sign(dir), 0, Zooms.Length - 1)];

    /// <summary>Next panel size step in direction <paramref name="dir"/>, clamped at the ends.</summary>
    public static float StepSize(float s, int dir) => Sizes[Math.Clamp(Nearest(Sizes, s) + Math.Sign(dir), 0, Sizes.Length - 1)];

    /// <summary>Square minimap panel of side <paramref name="baseSide"/>·<paramref name="size"/>, left edge at
    /// <paramref name="x"/>, bottom edge at <paramref name="bottom"/> (it grows upwards).</summary>
    public static Rectangle Panel(float x, float bottom, float baseSide, float size)
    {
        float s = MathF.Round(baseSide * Math.Clamp(size, Sizes[0], Sizes[^1]));
        return new Rectangle(x, bottom - s, s, s);
    }

    /// <summary>Screen rectangle of the WHOLE map for a visible map rectangle <paramref name="m"/>, a zoom and the
    /// camera focus in map fractions (0..1): zoom 1 = <paramref name="m"/>; otherwise the focus is centred and the
    /// rectangle clamped so it always covers <paramref name="m"/>.</summary>
    public static Rectangle Virtual(Rectangle m, float zoom, Vector2 focus01)
    {
        if (zoom <= 1.0001f) return m;
        float w = m.Width * zoom, h = m.Height * zoom;
        float x = m.X + m.Width * 0.5f - Math.Clamp(focus01.X, 0f, 1f) * w;
        float y = m.Y + m.Height * 0.5f - Math.Clamp(focus01.Y, 0f, 1f) * h;
        x = Math.Clamp(x, m.X + m.Width - w, m.X);
        y = Math.Clamp(y, m.Y + m.Height - h, m.Y);
        return new Rectangle(x, y, w, h);
    }

    /// <summary>CLI <c>--minimap size=1.5,zoom=3,resources=off,units=on,trees=on,buttons=on</c> → settings (any subset of
    /// keys; values clamped/snapped). Throws <see cref="ArgumentException"/> on unknown keys or values.</summary>
    public static void ApplySpec(Menus.AppSettings s, string spec)
    {
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) throw new ArgumentException("--minimap: expected key=value, got " + part);
            string key = part[..eq].ToLowerInvariant(), val = part[(eq + 1)..].ToLowerInvariant();
            bool OnOff() => val switch
            {
                "on" or "true" or "1" => true,
                "off" or "false" or "0" => false,
                _ => throw new ArgumentException($"--minimap {key}: on or off, got {val}"),
            };
            float Num() => float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)
                ? f : throw new ArgumentException($"--minimap {key}: number expected, got {val}");
            switch (key)
            {
                case "size": s.MinimapSize = Sizes[Nearest(Sizes, Num())]; break;
                case "zoom": s.MinimapZoom = ClampZoom(Num()); break;
                case "resources": s.MinimapResources = OnOff(); break;
                case "units": s.MinimapUnits = OnOff(); break;
                case "trees": s.MinimapTrees = OnOff(); break;
                case "buttons": Minimap.ButtonsAlways = OnOff(); break;
                default: throw new ArgumentException("--minimap: unknown key " + key + " (size, zoom, resources, units, trees, buttons)");
            }
        }
    }

    /// <summary>World (x, z) → minimap screen point through the virtual map rectangle <paramref name="v"/>.</summary>
    public static Vector2 ToScreen(Rectangle v, float mapW, float mapH, float x, float z) =>
        new(v.X + x / mapW * v.Width, v.Y + z / mapH * v.Height);

    /// <summary>Minimap screen point → world (x, z), clamped to the map.</summary>
    public static Vector2 ToWorld(Rectangle v, float mapW, float mapH, Vector2 p) =>
        new(Math.Clamp((p.X - v.X) / v.Width, 0, 1) * mapW, Math.Clamp((p.Y - v.Y) / v.Height, 0, 1) * mapH);
}
