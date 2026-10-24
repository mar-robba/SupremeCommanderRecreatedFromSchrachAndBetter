using System.Numerics;
using Raylib_cs;
using Rts.Sim.Snapshot;

namespace Rts.Client.UI;

/// <summary>
/// Top resource bar (SupCom-style): for mass and energy the stored/max bar, income and expense rates, net
/// rate, and a flashing red STALL warning when efficiency drops below 100%. Hover shows a detail tooltip.
/// </summary>
public static class ResourceBar
{
    private static float GroupWidth(Rectangle bar) => MathF.Min(Ui.P(330), (bar.Width - Ui.P(320)) * 0.5f);

    /// <summary>(6b) X right of the mass + energy groups (the unit cap counter goes there).</summary>
    public static float RightEdge(Rectangle bar) => bar.X + Ui.P(8) + GroupWidth(bar) * 2 + Ui.P(12);

    public static void Draw(Rectangle bar, in ArmyView a)
    {
        float x = bar.X + Ui.P(8);
        float groupW = GroupWidth(bar);
        DrawGroup(new Rectangle(x, bar.Y, groupW, bar.Height), "MASS", Ui.MassColor,
            a.Mass, a.MassMax, a.MassIncome, a.MassDemand, a.MassEfficiency, true);
        DrawGroup(new Rectangle(x + groupW + Ui.P(12), bar.Y, groupW, bar.Height), "ENERGY", Ui.EnergyColor,
            a.Energy, a.EnergyMax, a.EnergyIncome, a.EnergyDemand, a.EnergyEfficiency, false);
    }

    private static void DrawGroup(Rectangle r, string label, Color color, float stored, float max, float income, float demand,
        float efficiency, bool mass)
    {
        bool stall = demand > 0.01f && efficiency < 0.999f;
        bool blink = ((int)(Raylib.GetTime() * 3) & 1) == 0;
        if (stall)
        {
            var bg = blink ? new Color(170, 30, 25, 200) : new Color(90, 20, 18, 160);
            Raylib.DrawRectangleRec(new Rectangle(r.X - Ui.P(4), r.Y + 2, r.Width + Ui.P(8), r.Height - 4), bg);
        }

        int fs = Ui.F(13);
        float cy = r.Y + (r.Height - fs) * 0.5f - 1;
        float x = r.X;

        // Icon: mass = green diamond, energy = yellow bolt-ish triangle pair.
        float ic = Ui.P(7);
        var c = new Vector2(x + ic, r.Y + r.Height * 0.5f);
        if (mass)
        {
            Raylib.DrawPoly(c, 4, ic + 1.5f, 0, Color.Black);
            Raylib.DrawPoly(c, 4, ic, 0, color);
        }
        else
        {
            Raylib.DrawCircleV(c, ic + 1.5f, Color.Black);
            Raylib.DrawCircleV(c, ic, color);
            Raylib.DrawLineEx(c + new Vector2(ic * 0.25f, -ic * 0.7f), c + new Vector2(-ic * 0.3f, ic * 0.05f), 2, Color.Black);
            Raylib.DrawLineEx(c + new Vector2(-ic * 0.3f, ic * 0.05f), c + new Vector2(ic * 0.3f, -ic * 0.05f), 2, Color.Black);
            Raylib.DrawLineEx(c + new Vector2(ic * 0.3f, -ic * 0.05f), c + new Vector2(-ic * 0.25f, ic * 0.7f), 2, Color.Black);
        }
        x += ic * 2 + Ui.P(6);

        // Storage bar with "stored / max".
        float barW = r.Width - (x - r.X) - Ui.P(120);
        barW = MathF.Max(Ui.P(80), barW);
        var barRect = new Rectangle(x, r.Y + r.Height * 0.5f - Ui.P(8), barW, Ui.P(16));
        float frac = max > 0 ? stored / max : 0;
        Ui.Bar(barRect, frac, ColorUtil.Scale(color, 0.85f), $"{Ui.Num(stored)} / {Ui.Num(max)}", new Color(18, 22, 28, 255));
        x += barW + Ui.P(8);

        // Rates.
        string inc = "+" + Ui.Num(income);
        string exp = "-" + Ui.Num(demand);
        Ui.Text(inc, x, cy, fs, Ui.Good, true);
        x += MathF.Max(Ui.Measure(inc, fs, true) + Ui.P(6), Ui.P(48));
        Ui.Text(exp, x, cy, fs, stall && blink ? Color.White : Ui.Bad, true);
        x += MathF.Max(Ui.Measure(exp, fs, true) + Ui.P(6), Ui.P(48));
        if (stall)
            Ui.Text("STALL", x, cy, fs, blink ? Color.White : new Color(255, 140, 120, 255), true);

        var hover = new Rectangle(r.X - Ui.P(4), r.Y, r.Width + Ui.P(8), r.Height);
        if (Ui.Hover(hover))
        {
            float net = income - demand;
            var lines = new List<TipLine>
            {
                new(mass ? "Mass" : "Energy", color, 14, true),
                new($"Stored: {stored:0} / {max:0}  ({(max > 0 ? frac * 100 : 0):0}%)", Ui.TextColor, 12),
                new($"Income: +{income:0.0}/s     Expense: -{demand:0.0}/s", Ui.TextColor, 12),
                new($"Net: {(net >= 0 ? "+" : "")}{net:0.0}/s     Efficiency: {Math.Clamp(efficiency, 0, 1) * 100:0}%", net >= 0 ? Ui.Good : Ui.Bad, 12),
            };
            if (stall) lines.Add(new TipLine("STALLING: demand exceeds income and storage is empty - construction slows down.", Ui.Bad, 11.5f, true));
            lines.Add(new TipLine(mass
                ? "Build mass extractors on mass deposits to raise income; mass storage raises the cap."
                : "Build power generators (or hydrocarbon plants on deposits) to raise income; energy storage raises the cap.",
                Ui.TextFaint, 11));
            Ui.Tooltip(hover, lines, above: false);
        }
    }
}
