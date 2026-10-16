using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;

namespace Rts.Client.UI;

/// <summary>
/// F1 help overlay (also the "?" button of the top bar): every hotkey and mouse action, generated from the hotkey
/// table (<see cref="Hotkeys.All"/>, <see cref="Hotkeys.Mouse"/>), grouped and laid out in columns. Replays hide the
/// order / building groups, normal games hide the sandbox group. F1, Esc or a click closes it.
/// </summary>
public sealed class HelpOverlay
{
    private readonly GameContext _ctx;
    public bool Open { get; private set; }
    private int _openedFrame = -1, _frame;

    public HelpOverlay(GameContext ctx) => _ctx = ctx;

    public void Toggle()
    {
        Open = !Open;
        _openedFrame = _frame;
    }

    public void Close() => Open = false;

    /// <summary>Lines of the overlay, by group (pure: used by the tests too). Key label + action.</summary>
    public static List<(string Group, List<(string Keys, string Action, bool Mouse)> Lines)> Content(bool replay, bool sandbox)
    {
        var res = new List<(string, List<(string, string, bool)>)>();
        foreach (var g in Hotkeys.Groups)
        {
            if (replay && g is "Orders" or "Toggles" or "Transports" or "Building" or "Factory" or "Dialogs") continue;
            if (!replay && g == "Replay") continue;
            if (!sandbox && g == "Sandbox") continue;
            var lines = new List<(string, string, bool)>();
            foreach (var k in Hotkeys.All)
            {
                if (k.Group != g) continue;
                if (replay && k.Scope is HotkeyScope.Play) continue;
                if (sandbox && k.Scope == HotkeyScope.Debug) continue;
                lines.Add((k.Label, k.Action, false));
            }
            foreach (var m in Hotkeys.Mouse)
                if (m.Group == g && !(replay && g == "Orders")) lines.Add((m.Input, m.Action, true));
            if (lines.Count > 0) res.Add((g, lines));
        }
        return res;
    }

    /// <summary>Blocks world input while open.</summary>
    public bool Hit() => Open;

    public void Update()
    {
        _frame++;
        if (Hotkeys.Help.Pressed()) Toggle();
        else if (Open && Raylib.IsKeyPressed(KeyboardKey.Escape)) Open = false;
    }

    public void Draw()
    {
        if (!Open) return;
        var groups = Content(_ctx.ReadOnly, _ctx.ControlAnyArmy);
        Raylib.DrawRectangle(0, 0, Ui.Sw, Ui.Sh, new Color(0, 0, 0, 150));
        float w = MathF.Min(Ui.Sw - Ui.P(32), Ui.P(1180)), h = MathF.Min(Ui.Sh - Ui.P(70), Ui.P(660));
        var p = Ui.R((Ui.Sw - w) * 0.5f, MathF.Max(Ui.P(36), (Ui.Sh - h) * 0.5f), w, h);
        Ui.Panel(p);
        Ui.Text("CONTROLS", p.X + Ui.P(16), p.Y + Ui.P(12), Ui.F(17), Ui.TextColor, true);
        Ui.TextRight("F1 / Esc / click: close", p.X + p.Width - Ui.P(16), p.Y + Ui.P(16), Ui.F(11), Ui.TextFaint);

        // Pick the largest font that fits (3 columns, groups flow top to bottom).
        var body = new Rectangle(p.X + Ui.P(16), p.Y + Ui.P(44), p.Width - Ui.P(32), p.Height - Ui.P(56));
        int cols = body.Width > Ui.P(900) ? 3 : 2;
        float colW = (body.Width - (cols - 1) * Ui.P(18)) / cols;
        float f = 12f;
        while (f > 9f && !Layout(groups, body, cols, colW, f, draw: false)) f -= 0.5f;
        Layout(groups, body, cols, colW, f, draw: true);

        if (_frame != _openedFrame && (Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right)))
        {
            Open = false;
            Ui.ConsumeClicks();
        }
    }

    /// <summary>Lays out (and optionally draws) the groups; false if they don't fit.</summary>
    private static bool Layout(List<(string Group, List<(string Keys, string Action, bool Mouse)> Lines)> groups, Rectangle body, int cols, float colW, float fontPx, bool draw)
    {
        int fs = Ui.F(fontPx), ft = Ui.F(fontPx + 1.5f);
        float keyW = Ui.P(fontPx * 9.5f), lineH = fs + Ui.P(3);
        int col = 0;
        float x = body.X, y = body.Y;
        foreach (var (group, lines) in groups)
        {
            // Height of this group (wrapped actions).
            float gh = ft + Ui.P(6);
            var wrapped = new List<List<string>>(lines.Count);
            foreach (var l in lines)
            {
                var wl = Ui.Wrap(l.Action, colW - keyW - Ui.P(6), fs);
                wrapped.Add(wl);
                gh += Math.Max(1, wl.Count) * lineH;
            }
            gh += Ui.P(8);
            if (y + gh > body.Y + body.Height && y > body.Y)
            {
                col++;
                if (col >= cols) return false;
                x = body.X + col * (colW + Ui.P(18));
                y = body.Y;
            }
            if (draw)
            {
                Ui.Text(group.ToUpperInvariant(), x, y, ft, Ui.Accent, true);
                Raylib.DrawLineEx(new Vector2(x, y + ft + Ui.P(2)), new Vector2(x + colW, y + ft + Ui.P(2)), 1, ColorUtil.WithAlpha(Ui.PanelBorder, 160));
            }
            float ly = y + ft + Ui.P(6);
            for (int i = 0; i < lines.Count; i++)
            {
                if (draw)
                {
                    var (keys, _, mouse) = lines[i];
                    Ui.Text(Ui.Fit(keys, keyW - Ui.P(4), fs, true), x, ly, fs, mouse ? new Color(170, 200, 230, 255) : new Color(255, 230, 150, 255), true);
                    for (int k = 0; k < wrapped[i].Count; k++)
                        Ui.Text(wrapped[i][k], x + keyW, ly + k * lineH, fs, Ui.TextDim);
                }
                ly += Math.Max(1, wrapped[i].Count) * lineH;
            }
            y += gh;
            if (y > body.Y + body.Height + 1 && col == cols - 1) return false;
        }
        return true;
    }
}
