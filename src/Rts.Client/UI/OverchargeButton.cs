using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;

namespace Rts.Client.UI;

/// <summary>
/// Glyph and tooltip of the Overcharge order button (<see cref="OrdersPanel"/>, state from
/// <see cref="UnitToggles.OverchargeState"/>): a lightning bolt in a ring, bright energy yellow with a glow while
/// armed, plain yellow when ready, grey when the stored energy is below MinEnergy.
/// </summary>
public static class OverchargeButton
{
    public static readonly Color Energy = new(255, 220, 90, 255);
    public static readonly Color EnergyHot = new(255, 248, 200, 255);

    /// <summary>Draws the glyph inside a button rectangle (same layout as the other order glyphs).</summary>
    public static void DrawGlyph(Rectangle br, in UnitToggles.OverchargeSelection s)
    {
        float labelH = Ui.F(10) + Ui.P(4);
        var c = new Vector2(br.X + br.Width * 0.5f, br.Y + (br.Height - labelH) * 0.5f + Ui.P(2));
        float r = MathF.Min(br.Width, br.Height - labelH) * 0.28f;
        float t = MathF.Max(2f, r * 0.2f);
        bool armed = s.On > 0;
        var col = !s.Ready ? new Color(110, 112, 120, 255) : armed ? EnergyHot : Energy;
        if (armed && s.Ready)
        {
            float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 5f);
            Raylib.DrawCircleV(c, r * (1.25f + 0.1f * pulse), new Color((byte)255, (byte)200, (byte)60, (byte)(50 + 40 * pulse)));
        }
        Raylib.DrawRing(c, r * 1.0f, r * 1.0f + t * 0.8f, 0, 360, 28, armed ? col : new Color(col.R, col.G, col.B, (byte)170));
        // Lightning bolt.
        Vector2 P(float x, float y) => c + new Vector2(x * r, y * r);
        Vector2[] bolt = [P(0.18f, -0.85f), P(-0.42f, 0.08f), P(-0.02f, 0.08f), P(-0.2f, 0.85f), P(0.42f, -0.12f), P(0.02f, -0.12f)];
        // Two triangles per half (fan from the inner corners), drawn both windings so the order does not matter.
        Tri(bolt[0], bolt[1], bolt[2], col);
        Tri(bolt[0], bolt[2], bolt[5], col);
        Tri(bolt[3], bolt[4], bolt[5], col);
        Tri(bolt[3], bolt[5], bolt[2], col);
    }

    private static void Tri(Vector2 a, Vector2 b, Vector2 c, Color col)
    {
        Raylib.DrawTriangle(a, b, c, col);
        Raylib.DrawTriangle(a, c, b, col);
    }

    /// <summary>Tooltip: title with the hotkey, the rule (dim) and the current state (green ready / red not ready).</summary>
    public static void Tooltip(Rectangle br, string key, in UnitToggles.OverchargeSelection s)
    {
        string title = s.AllOn ? "Overcharge: on" : s.On > 0 ? "Overcharge: mixed" : "Overcharge: off";
        if (key.Length > 0) title += $"  [{key}]";
        var parts = UnitToggles.OverchargeTooltip(s).Split('\n');
        var lines = new List<TipLine> { new(title, Ui.TextColor, 14, true), new(parts[0], Ui.TextDim, 12) };
        if (parts.Length > 1) lines.Add(new TipLine(parts[1], s.Ready ? Ui.Good : Ui.Bad, 12));
        Ui.Tooltip(br, lines);
    }
}
