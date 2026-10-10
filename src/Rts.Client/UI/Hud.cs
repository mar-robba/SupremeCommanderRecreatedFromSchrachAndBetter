using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Client.UI;

/// <summary>
/// FOUNDATION HUD: resource bar, selection summary, debug stats, help.
/// TODO(ui): build menu, factory queue panel, minimap, unit tooltips, order buttons (see docs/ROADMAP.md).
/// </summary>
public sealed class Hud
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    public const int TopBarHeight = 30;

    public Hud(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
    }

    /// <summary>True if the mouse is over a HUD panel (camera/selection should ignore it).</summary>
    public bool MouseOverUi()
    {
        var m = Raylib.GetMousePosition();
        return m.Y < TopBarHeight;
    }

    public void Draw()
    {
        var snap = _ctx.Snapshots.Current;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();

        // Top resource bar.
        Raylib.DrawRectangle(0, 0, sw, TopBarHeight, new Color(15, 20, 28, 230));
        var a = snap.Armies[_ctx.LocalArmy];
        DrawResource(10, "MASS", a.Mass, a.MassMax, a.MassIncome, a.MassDemand, new Color(90, 220, 110, 255));
        DrawResource(330, "ENERGY", a.Energy, a.EnergyMax, a.EnergyIncome, a.EnergyDemand, new Color(240, 210, 70, 255));
        string stats = $"FPS {Raylib.GetFPS()}  |  tick {snap.Tick}  sim {snap.TickDurationMs:F1} ms  x{_ctx.Runner.Speed:0.##}{(_ctx.Runner.Paused ? " PAUSED" : "")}  |  units {snap.UnitCount}  proj {snap.ProjectileCount}";
        Raylib.DrawText(stats, sw - Raylib.MeasureText(stats, 16) - 10, 7, 16, Color.LightGray);

        // Selection summary.
        if (_sel.Selected.Count > 0)
        {
            var counts = new SortedDictionary<string, int>();
            foreach (var h in _sel.Selected)
                if (snap.TryGetUnit(h.Index, h.Generation, out var v)) { var n = _ctx.Db[v.Bp].Name; counts[n] = counts.GetValueOrDefault(n) + 1; }
            int y = sh - 24 - counts.Count * 20;
            Raylib.DrawRectangle(0, y - 8, 300, sh - y + 8, new Color(15, 20, 28, 210));
            Raylib.DrawText($"Selected: {_sel.Selected.Count}", 10, y - 2, 18, Color.White);
            y += 20;
            foreach (var (name, c) in counts) { Raylib.DrawText($"{c} x {name}", 16, y, 16, Color.LightGray); y += 20; }
        }

        if (_sel.Mode != SelectionController.CommandMode.Normal)
        {
            string m = _sel.Mode == SelectionController.CommandMode.AttackMove ? "ATTACK-MOVE: click target point" : "PATROL: click patrol point";
            Raylib.DrawText(m, sw / 2 - Raylib.MeasureText(m, 20) / 2, TopBarHeight + 10, 20, Color.Orange);
        }

        const string help = "LMB select/box  RMB move/attack  Shift queue  A attack-move  P patrol  S stop  Ctrl+0-9 groups  Arrows/MMB pan  Wheel zoom  +/- speed  Space pause  F1/F2 spawn tanks (own/enemy)";
        Raylib.DrawText(help, 10, sh - 18, 14, new Color(200, 200, 200, 160));

        if (snap.WinningTeam >= 0)
        {
            bool won = snap.Armies[_ctx.LocalArmy].Team == snap.WinningTeam;
            string t = won ? "VICTORY" : "DEFEAT";
            Raylib.DrawText(t, sw / 2 - Raylib.MeasureText(t, 72) / 2, sh / 3, 72, won ? Color.Gold : Color.Red);
        }
        if (_ctx.Runner.Fault is Exception e)
        {
            Raylib.DrawRectangle(0, sh / 2 - 40, sw, 120, new Color(80, 0, 0, 230));
            Raylib.DrawText("SIMULATION CRASHED: " + e.GetType().Name + ": " + e.Message, 20, sh / 2 - 30, 20, Color.White);
            Raylib.DrawText((e.StackTrace ?? "").Split('\n').FirstOrDefault() ?? "", 20, sh / 2, 16, Color.LightGray);
        }
    }

    private static void DrawResource(int x, string label, float stored, float max, float income, float demand, Color c)
    {
        Raylib.DrawText(label, x, 7, 16, c);
        int bx = x + 75, bw = 120;
        Raylib.DrawRectangle(bx, 9, bw, 12, new Color(40, 40, 40, 255));
        float f = max > 0 ? Math.Clamp(stored / max, 0, 1) : 0;
        Raylib.DrawRectangle(bx, 9, (int)(bw * f), 12, c);
        Raylib.DrawText($"{stored:0}", bx + bw + 6, 7, 16, Color.White);
        Raylib.DrawText($"+{income:0.#}", bx + bw + 56, 7, 16, new Color(120, 230, 120, 255));
        Raylib.DrawText($"-{demand:0.#}", bx + bw + 106, 7, 16, new Color(230, 120, 120, 255));
    }
}
