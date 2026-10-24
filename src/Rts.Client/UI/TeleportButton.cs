using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;

namespace Rts.Client.UI;

/// <summary>
/// Glyph and tooltip of the Teleport order button (<see cref="OrdersPanel"/>, state from
/// <see cref="TeleportInput.State"/>): a small origin ring and a larger destination ring joined by a dotted arc, cyan;
/// it pulses while a selected commander is charging a jump.
/// </summary>
public static class TeleportButton
{
    public static readonly Color Cyan = new(110, 225, 255, 255);
    public static readonly Color CyanHot = new(215, 250, 255, 255);

    /// <summary>Draws the glyph inside a button rectangle (same layout as the other order glyphs).</summary>
    public static void DrawGlyph(Rectangle br, in TeleportInput.TeleportSelection s, bool active)
    {
        float labelH = Ui.F(10) + Ui.P(4);
        var c = new Vector2(br.X + br.Width * 0.5f, br.Y + (br.Height - labelH) * 0.5f + Ui.P(2));
        float r = MathF.Min(br.Width, br.Height - labelH) * 0.28f;
        float t = MathF.Max(2f, r * 0.2f);
        var col = active || s.Charging > 0 ? CyanHot : Cyan;
        var from = c + new Vector2(-0.62f * r, 0.55f * r);
        var to = c + new Vector2(0.55f * r, -0.45f * r);
        if (s.Charging > 0)
        {
            float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 6f);
            Raylib.DrawCircleV(to, r * (0.75f + 0.15f * pulse), new Color((byte)90, (byte)210, (byte)255, (byte)(45 + 45 * pulse)));
        }
        Raylib.DrawRing(from, r * 0.28f, r * 0.28f + t * 0.7f, 0, 360, 20, new Color(col.R, col.G, col.B, (byte)170));
        Raylib.DrawRing(to, r * 0.48f, r * 0.48f + t * 0.8f, 0, 360, 24, col);
        Raylib.DrawCircleV(to, t * 0.7f, col);
        // Dotted arc bulging up-left from origin to destination.
        var mid = (from + to) * 0.5f + new Vector2(-0.45f * r, -0.45f * r);
        for (int i = 1; i <= 5; i++)
        {
            float u = i / 6f;
            var p = (1 - u) * (1 - u) * from + 2 * (1 - u) * u * mid + u * u * to;
            Raylib.DrawCircleV(p, t * 0.45f, col);
        }
    }

    /// <summary>Tooltip: title with the hotkey, the energy rule (dim), an example cost and how many are charging.</summary>
    public static void Tooltip(Rectangle br, string key, in TeleportInput.TeleportSelection s)
    {
        string title = s.Count > 1 ? $"Teleport (×{s.Count})" : "Teleport";
        if (key.Length > 0) title += $"  [{key}]";
        var lines = new List<TipLine> { new(title, Ui.TextColor, 14, true) };
        if (s.Rule is { } tp)
        {
            lines.Add(new TipLine("Click a ground point: the commander teleports there. Shift queues.", Ui.TextDim, 12));
            lines.Add(new TipLine(TeleportInput.RuleText(tp), Ui.TextDim, 12));
            lines.Add(new TipLine($"e.g. 100 away: {tp.Cost(100):0} E, 300 away: {tp.Cost(300):0} E", Ui.EnergyColor, 12));
        }
        if (s.Charging > 0) lines.Add(new TipLine(s.Charging > 1 ? $"{s.Charging} charging a jump" : "Charging a jump", Cyan, 12));
        Ui.Tooltip(br, lines);
    }
}
