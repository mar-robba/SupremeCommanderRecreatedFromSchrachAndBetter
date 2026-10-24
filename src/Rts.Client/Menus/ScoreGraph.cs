using System.Numerics;
using Raylib_cs;
using Rts.Client.UI;
using Rts.Sim.World;

namespace Rts.Client.Menus;

/// <summary>
/// (6b, G6) End-screen line chart: one line per army (army color, yours thicker) of a metric picked with the tabs
/// (score, mass/energy income, units, kills, mass collected) over game time. Built from
/// <see cref="ArmyStats.History"/> (one sample per 30 s) plus a live last point, copied while the sim is stopped.
/// </summary>
public sealed class ScoreGraph
{
    public enum Metric { Score, MassIncome, EnergyIncome, Units, Kills, MassCollected }
    public static readonly string[] MetricNames = ["Score", "Mass income", "Energy income", "Units", "Kills", "Mass collected"];

    public sealed record Series(Color Color, bool Local, ScoreSample[] Samples);

    public readonly List<Series> Lines = new();
    public readonly int EndTick;
    public Metric Selected;

    public ScoreGraph(SimWorld w, int localArmy)
    {
        EndTick = w.Tick;
        foreach (var a in w.Armies)
        {
            var list = new List<ScoreSample>(a.Stats.History);
            if (list.Count == 0 || list[^1].Tick < w.Tick) list.Add(a.Stats.Sample(w.Tick, a.Eco, a.UnitCount));
            Lines.Add(new Series(ColorUtil.FromRgba(a.Color), a.Index == localArmy, list.ToArray()));
        }
    }

    public static float ValueOf(in ScoreSample s, Metric m) => m switch
    {
        Metric.Score => s.Score,
        Metric.MassIncome => s.MassIncome,
        Metric.EnergyIncome => s.EnergyIncome,
        Metric.Units => s.Units,
        Metric.Kills => s.Kills,
        _ => s.MassCollected,
    };

    /// <summary>Largest value of the metric over all lines (at least 1, so the axis never collapses).</summary>
    public float MaxOf(Metric m)
    {
        float max = 1;
        foreach (var l in Lines)
            foreach (var s in l.Samples) max = MathF.Max(max, ValueOf(s, m));
        return max;
    }

    /// <summary>True when there is something to draw (at least two points in time).</summary>
    public bool HasData => Lines.Any(l => l.Samples.Length >= 2);

    private static string Time(int tick)
    {
        int sec = (int)(tick * SimWorld.Dt);
        return $"{sec / 60}:{sec % 60:00}";
    }

    public void Draw(Rectangle p)
    {
        Ui.Panel(p);
        Ui.PanelTitle(p, "Over time");
        // Metric tabs along the top.
        float tx = p.X + Ui.P(110), tw = Ui.P(116), th = Ui.P(20), ty = p.Y + Ui.P(5);
        for (int m = 0; m < MetricNames.Length; m++)
        {
            var r = Ui.R(tx + m * (tw + Ui.P(4)), ty, tw, th);
            var st = Ui.Button(r, true, (int)Selected == m);
            Ui.TextCentered(MetricNames[m], r, Ui.F(10.5f), Ui.TextColor, (int)Selected == m);
            if (st.Left) Selected = (Metric)m;
        }
        var plot = Ui.R(p.X + Ui.P(58), p.Y + Ui.P(36), p.Width - Ui.P(76), p.Height - Ui.P(58));
        if (!HasData || EndTick <= 0)
        {
            Ui.TextCentered("The game was too short for a graph (one point every 30 s)", plot, Ui.F(12), Ui.TextDim);
            return;
        }
        float max = MaxOf(Selected);
        int fs = Ui.F(10);
        for (int g = 0; g <= 2; g++)
        {
            float gy = plot.Y + plot.Height * (1 - g * 0.5f);
            Raylib.DrawLineEx(new Vector2(plot.X, gy), new Vector2(plot.X + plot.Width, gy), 1, new Color(70, 84, 104, g == 0 ? 200 : 90));
            Ui.TextRight(Ui.Num(max * g * 0.5f), plot.X - Ui.P(6), gy - fs * 0.5f, fs, Ui.TextFaint);
        }
        for (int g = 0; g <= 2; g++)
        {
            string t = Time(EndTick * g / 2);
            float gx = plot.X + plot.Width * g * 0.5f - Ui.Measure(t, fs) * g * 0.25f;
            Ui.Text(t, gx, plot.Y + plot.Height + Ui.P(4), fs, Ui.TextFaint);
        }
        // Your line last, so it stays on top.
        foreach (var l in Lines.OrderBy(l => l.Local))
        {
            float thick = l.Local ? Ui.P(3) : Ui.P(2);
            Vector2? prev = null;
            foreach (var s in l.Samples)
            {
                var pt = new Vector2(plot.X + plot.Width * s.Tick / EndTick, plot.Y + plot.Height * (1 - ValueOf(s, Selected) / max));
                if (prev is { } q) Raylib.DrawLineEx(q, pt, thick, l.Color);
                prev = pt;
            }
        }
    }
}
