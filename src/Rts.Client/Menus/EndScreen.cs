using Raylib_cs;
using Rts.Client.UI;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Client.Menus;

/// <summary>
/// End-of-game statistics overlay: result title, game time and one row per army (team, result, units built,
/// structures built, lost, killed, mass and energy collected) with per-column bars. Constructed only while the sim
/// thread is stopped (reads <see cref="SimWorld"/> directly, copying the values).
/// </summary>
public sealed class EndScreen
{
    public enum Result { Victory, Defeat, Summary }
    public enum Action { None, Continue, Restart, MainMenu }
    /// <summary>False in multiplayer (the Restart button is disabled).</summary>
    public bool CanRestart = true;

    private sealed record Row(string Name, Color Color, string Team, string Outcome, Color OutcomeColor, bool Local, float[] Values);

    private static readonly string[] Columns = ["Units built", "Structures", "Lost", "Killed", "Mass", "Energy"];

    private readonly Result _result;
    private readonly string _subtitle;
    private readonly List<Row> _rows = new();
    private readonly float[] _max = new float[Columns.Length];

    public EndScreen(GameSession s, Result result)
    {
        _result = result;
        var w = s.Sim.World;
        float seconds = w.Tick * SimWorld.Dt;
        _subtitle = $"Game time {(int)(seconds / 60)}:{(int)(seconds % 60):00}  ·  {MapPresets.Name(s.Config.Preset)}, {w.Terrain.Width / 51.2f:0} km";
        foreach (var a in w.Armies)
        {
            var st = a.Stats;
            bool won = w.WinningTeam >= 0 && a.Team == w.WinningTeam;
            string outcome = won ? "Victorious" : a.Defeated ? "Defeated" : w.WinningTeam >= 0 ? "Defeated" : "In play";
            var oc = won ? Color.Gold : outcome == "Defeated" ? Ui.Bad : Ui.TextDim;
            var vals = new float[] { st.UnitsBuilt, st.StructuresBuilt, st.UnitsLost, st.UnitsKilled, st.MassCollected, st.EnergyCollected };
            for (int c = 0; c < vals.Length; c++) _max[c] = MathF.Max(_max[c], vals[c]);
            _rows.Add(new Row(a.Name, ColorUtil.FromRgba(a.Color), a.Team >= 100 ? "—" : a.Team.ToString(), outcome, oc,
                a.Index == s.LocalArmy, vals));
        }
    }

    public Action Draw()
    {
        MenuUi.Dim(175);
        int sw = Ui.Sw;
        string title = _result switch { Result.Victory => "VICTORY", Result.Defeat => "DEFEAT", _ => "GAME SUMMARY" };
        var tc = _result switch { Result.Victory => Color.Gold, Result.Defeat => new Color(235, 70, 60, 255), _ => Ui.TextColor };
        int tfs = Ui.F(52);
        float ty = Ui.Sh * 0.08f;
        Ui.TextShadow(title, (sw - Ui.Measure(title, tfs, true)) * 0.5f, ty, tfs, tc, true);
        Ui.TextCentered(_subtitle, Ui.R(0, ty + tfs + Ui.P(6), sw, Ui.P(18)), Ui.F(13), Ui.TextDim);

        float wName = Ui.P(230), wTeam = Ui.P(56), wRes = Ui.P(104), wVal = Ui.P(100);
        float tableW = wName + wTeam + wRes + wVal * Columns.Length + Ui.P(24);
        float rowH = Ui.P(34);
        float tableH = Ui.P(40) + rowH * _rows.Count + Ui.P(14);
        var p = Ui.R((sw - tableW) * 0.5f, ty + tfs + Ui.P(42), tableW, tableH);
        Ui.Panel(p);
        Ui.PanelTitle(p, "Statistics");
        float x = p.X + Ui.P(12), y = p.Y + Ui.P(26);
        int hfs = Ui.F(10.5f);
        Ui.Text("PLAYER", x, y, hfs, Ui.TextFaint, true);
        Ui.Text("TEAM", x + wName, y, hfs, Ui.TextFaint, true);
        Ui.Text("RESULT", x + wName + wTeam, y, hfs, Ui.TextFaint, true);
        float vx0 = x + wName + wTeam + wRes;
        for (int c = 0; c < Columns.Length; c++)
            Ui.TextRight(Columns[c].ToUpperInvariant(), vx0 + (c + 1) * wVal - Ui.P(10), y, hfs, Ui.TextFaint, true);
        y += Ui.P(16);

        foreach (var row in _rows)
        {
            var rr = Ui.R(p.X + Ui.P(6), y, p.Width - Ui.P(12), rowH - Ui.P(4));
            Raylib.DrawRectangleRec(rr, row.Local ? new Color(46, 70, 98, 170) : new Color(26, 34, 46, 140));
            if (row.Local) Raylib.DrawRectangleLinesEx(rr, 1, ColorUtil.WithAlpha(Ui.Accent, 160));
            float cy = rr.Y + (rr.Height - Ui.F(13)) * 0.5f - 1;
            Raylib.DrawRectangle((int)x, (int)(rr.Y + Ui.P(6)), (int)Ui.P(6), (int)(rr.Height - Ui.P(12)), row.Color);
            Ui.Text(Ui.Fit(row.Name, wName - Ui.P(24), Ui.F(13), true), x + Ui.P(14), cy, Ui.F(13), Ui.TextColor, true);
            Ui.Text(row.Team, x + wName, cy, Ui.F(13), Ui.TextDim);
            Ui.Text(row.Outcome, x + wName + wTeam, cy, Ui.F(12.5f), row.OutcomeColor, true);
            for (int c = 0; c < Columns.Length; c++)
            {
                float v = row.Values[c];
                var cell = Ui.R(vx0 + c * wVal + Ui.P(6), rr.Y + rr.Height - Ui.P(9), wVal - Ui.P(14), Ui.P(4));
                if (_max[c] > 0)
                {
                    var bc = c == 4 ? Ui.MassColor : c == 5 ? Ui.EnergyColor : c == 2 ? Ui.Bad : c == 3 ? Ui.Good : Ui.BuildBlue;
                    Raylib.DrawRectangleRec(cell, new Color(8, 10, 14, 200));
                    Raylib.DrawRectangle((int)cell.X, (int)cell.Y, (int)(cell.Width * v / _max[c]), (int)cell.Height, ColorUtil.WithAlpha(bc, 210));
                }
                Ui.TextRight(Ui.Num(v), vx0 + (c + 1) * wVal - Ui.P(10), cy - Ui.P(3), Ui.F(13), Ui.TextColor, true);
            }
            y += rowH;
        }

        float bw = Ui.P(170), bh = Ui.P(42), gap = Ui.P(14);
        float bx = (sw - (3 * bw + 2 * gap)) * 0.5f, by = p.Y + p.Height + Ui.P(26);
        var action = Action.None;
        if (MenuUi.BigButton(Ui.R(bx, by, bw, bh), _result == Result.Defeat ? "Keep Watching" : "Continue")) action = Action.Continue;
        if (MenuUi.BigButton(Ui.R(bx + bw + gap, by, bw, bh), "Restart", CanRestart)) action = Action.Restart;
        if (MenuUi.BigButton(Ui.R(bx + 2 * (bw + gap), by, bw, bh), "Main Menu", true, true)) action = Action.MainMenu;
        return action;
    }
}
