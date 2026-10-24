using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;
using Rts.Sim.Core;

namespace Rts.Client.UI;

/// <summary>One player row of the diplomacy panel (testable, no raylib).</summary>
public readonly record struct DiplomacyRow(int Army, bool Self, bool Ally, bool Defeated, bool CanGive);

/// <summary>Which rows the diplomacy panel shows and which get give buttons (mirrors <c>DiplomacySystem.CanGive</c>).</summary>
public static class DiplomacyRules
{
    /// <summary>Share of the stored resource one "give" click sends.</summary>
    public const float GiveFraction = 0.25f;

    public static List<DiplomacyRow> Rows(ArmyView[] armies, int local)
    {
        var rows = new List<DiplomacyRow>(armies.Length);
        bool meAlive = (uint)local < (uint)armies.Length && !armies[local].Defeated;
        for (int a = 0; a < armies.Length; a++)
        {
            bool self = a == local;
            bool ally = !self && (uint)local < (uint)armies.Length && armies[a].Team == armies[local].Team;
            rows.Add(new DiplomacyRow(a, self, ally, armies[a].Defeated, ally && meAlive && !armies[a].Defeated));
        }
        return rows;
    }

    /// <summary>Amount one click gives (whole units, 0 when nothing is stored).</summary>
    public static float Share(float stored) => MathF.Floor(MathF.Max(0, stored) * GiveFraction);
}

/// <summary>
/// F4 diplomacy panel: every player (color, name, faction, team, ally/enemy, defeated, units n/cap for you and your
/// allies, score (<c>ArmyStats.Score</c>); your row marked) and, per living ally, "give the selected units" (<see cref="GiveUnitsCommand"/> with your own
/// selected units) and "give mass/energy" (<see cref="DiplomacyRules.GiveFraction"/> of your stored amount per click,
/// <see cref="GiveResourcesCommand"/>). Hidden in replays and for observers (defeated local army).
/// </summary>
public sealed class DiplomacyPanel
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private Rectangle _rect;
    public bool Open { get; private set; }

    public DiplomacyPanel(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
    }

    public static bool Available(GameContext ctx, SimSnapshot snap) =>
        !ctx.ReadOnly && (uint)ctx.LocalArmy < (uint)snap.Armies.Length && !snap.Armies[ctx.LocalArmy].Defeated;

    public void Toggle() => Open = !Open;
    public void Close() => Open = false;

    /// <summary>Before world input: F4 toggle and the panel rect (for <see cref="Hit"/>).</summary>
    public void Update(SimSnapshot snap)
    {
        if (!Available(_ctx, snap)) { Open = false; return; }
        if (Hotkeys.Diplomacy.Pressed()) Toggle();
        float w = Ui.P(776), rowH = Ui.P(30);
        _rect = new Rectangle(Ui.Sw - w - Ui.P(8), Ui.P(38), w, Ui.P(56) + snap.Armies.Length * rowH + Ui.P(26));
    }

    public bool Hit(Vector2 m) => Open && Raylib.CheckCollisionPointRec(m, _rect);

    /// <summary>Top bar button (left of the help button).</summary>
    public void DrawButton(Rectangle r)
    {
        var st = Ui.Button(r, true, Open);
        Ui.TextCentered("Diplomacy", r, Ui.F(11.5f), Ui.TextColor, true);
        if (st.Left) Toggle();
        if (st.Hover) Ui.Tooltip(r, $"Diplomacy  [{Hotkeys.Diplomacy.Label}]", "Players and teams; give units and resources to allies", above: false);
    }

    public void Draw(SimSnapshot snap)
    {
        if (!Open || !Available(_ctx, snap)) return;
        var p = _rect;
        Ui.Panel(p);
        Ui.PanelTitle(p, "Diplomacy");
        var me = snap.Armies[_ctx.LocalArmy];
        var mine = OwnSelected(snap);
        float x = p.X + Ui.P(12), y = p.Y + Ui.P(26), rowH = Ui.P(30);
        float cName = x + Ui.P(18), cFac = cName + Ui.P(130), cTeam = cFac + Ui.P(70), cRel = cTeam + Ui.P(44),
            cUnits = cRel + Ui.P(64), cScore = cUnits + Ui.P(76), cGive = cScore + Ui.P(64);
        int hs = Ui.F(10.5f), fs = Ui.F(12.5f);
        Ui.Text("PLAYER", cName, y, hs, Ui.TextFaint, true);
        Ui.Text("FACTION", cFac, y, hs, Ui.TextFaint, true);
        Ui.Text("TEAM", cTeam, y, hs, Ui.TextFaint, true);
        Ui.Text("STATUS", cRel, y, hs, Ui.TextFaint, true);
        Ui.Text("UNITS", cUnits, y, hs, Ui.TextFaint, true);
        Ui.Text("SCORE", cScore, y, hs, Ui.TextFaint, true);
        Ui.Text("GIVE", cGive, y, hs, Ui.TextFaint, true);
        y += Ui.P(18);
        foreach (var row in DiplomacyRules.Rows(snap.Armies, _ctx.LocalArmy))
        {
            var a = snap.Armies[row.Army];
            float ty = y + (rowH - fs) * 0.5f - 1;
            if (row.Self) Raylib.DrawRectangleRec(Ui.R(p.X + Ui.P(4), y, p.Width - Ui.P(8), rowH - Ui.P(2)), new Color(60, 90, 130, 90));
            var col = ColorUtil.FromRgba(a.Color);
            if (row.Defeated) col = ColorUtil.Scale(col, 0.4f);
            Raylib.DrawRectangleRec(Ui.R(x, y + (rowH - Ui.P(12)) * 0.5f, Ui.P(12), Ui.P(12)), col);
            var tc = row.Defeated ? Ui.TextFaint : Ui.TextColor;
            string name = string.IsNullOrEmpty(a.Name) ? $"Army {row.Army + 1}" : a.Name;
            Ui.Text(Ui.Fit(row.Self ? name + " (you)" : name, Ui.P(124), fs, true), cName, ty, fs, row.Self ? Ui.Accent : tc, true);
            Ui.Text(Ui.Fit(_ctx.Db.Factions.Get(a.Faction)?.Name ?? a.Faction, Ui.P(64), fs), cFac, ty, fs, tc);
            Ui.Text(a.Team >= 100 ? "—" : a.Team.ToString(), cTeam, ty, fs, tc);
            var (rel, rc) = row.Defeated ? ("Defeated", Ui.TextFaint) : row.Self ? ("You", Ui.Accent) : row.Ally ? ("Ally", Ui.Good) : ("Enemy", Ui.Bad);
            Ui.Text(rel, cRel, ty, fs, rc, true);
            Ui.Text(row.Self || row.Ally ? $"{a.UnitCount}/{a.UnitCap}" : "—", cUnits, ty, fs, tc);
            Ui.Text(Ui.Num(a.Score), cScore, ty, fs, tc); // G6: ArmyStats.Score (every player, like SupCom)
            if (row.CanGive)
            {
                float bw = Ui.P(96), bh = rowH - Ui.P(6), by = y + Ui.P(2);
                var ub = Ui.R(cGive, by, bw, bh);
                if (GiveButton(ub, $"Units ({mine.Length})", mine.Length > 0,
                        "Give the selected units", "Your selected units go to this ally (not commanders,\nnot over the ally's unit cap)."))
                    _ctx.Runner.Enqueue(new GiveUnitsCommand(_ctx.LocalArmy, mine, row.Army));
                float m = DiplomacyRules.Share(me.Mass), e = DiplomacyRules.Share(me.Energy);
                if (GiveButton(Ui.R(ub.X + bw + Ui.P(6), by, Ui.P(84), bh), $"Mass {m:0}", m > 0,
                        "Give mass", $"{DiplomacyRules.GiveFraction * 100:0}% of your stored mass per click."))
                    _ctx.Runner.Enqueue(new GiveResourcesCommand(_ctx.LocalArmy, row.Army, m, 0));
                if (GiveButton(Ui.R(ub.X + bw + Ui.P(96), by, Ui.P(92), bh), $"Energy {e:0}", e > 0,
                        "Give energy", $"{DiplomacyRules.GiveFraction * 100:0}% of your stored energy per click."))
                    _ctx.Runner.Enqueue(new GiveResourcesCommand(_ctx.LocalArmy, row.Army, 0, e));
            }
            y += rowH;
        }
        Ui.Text($"Press {Hotkeys.Diplomacy.Label} to close · allies = same team", x, p.Y + p.Height - Ui.P(20), Ui.F(10.5f), Ui.TextFaint);
        // Clicks anywhere on the panel never reach the world.
        if (Ui.Hover(p) && (Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right))) Ui.ConsumeClicks();
    }

    private static bool GiveButton(Rectangle r, string label, bool enabled, string tipTitle, string tip)
    {
        var st = Ui.Button(r, enabled);
        Ui.TextCentered(label, r, Ui.F(11.5f), enabled ? Ui.TextColor : Ui.TextFaint, true);
        if (Ui.Hover(r)) Ui.Tooltip(r, tipTitle, tip);
        return st.Left;
    }

    /// <summary>The local army's units in the current selection (enemy units can be selected for inspection).</summary>
    private UnitHandle[] OwnSelected(SimSnapshot snap)
    {
        var list = new List<UnitHandle>();
        foreach (var h in _sel.Selected)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v) && v.Army == _ctx.LocalArmy) list.Add(h);
        return list.ToArray();
    }
}
