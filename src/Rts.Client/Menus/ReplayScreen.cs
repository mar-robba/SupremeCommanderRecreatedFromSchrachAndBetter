using System.Numerics;
using Raylib_cs;
using Rts.Client.Persistence;
using Rts.Client.UI;
using Rts.Sim;
using Rts.Sim.Persistence;

namespace Rts.Client.Menus;

/// <summary>
/// Replay viewer: a read-only <see cref="GameSession"/> stepped by a <see cref="ReplayPlayer"/> (the runner's hooks).
/// Controls (top center): play/pause (Space), speed 0.5x..16x (+ / -), a timeline with click/drag seek (backward
/// seeks restore the nearest checkpoint and re-simulate: "Seeking…" is shown meanwhile), and the view selector (each
/// army's fog of war, or everything). A red banner reports a desync (the re-simulation no longer matches the
/// recorded hashes), a warning a replay made by another build. Esc: menu (resume, restart, back to the list).
/// </summary>
public sealed class ReplayScreen : IScreen
{
    private static readonly float[] Speeds = [0.5f, 1f, 2f, 4f, 8f, 16f];

    private readonly AppHost _app;
    private readonly GameSession _s;
    private readonly ReplayPlayer _player;
    private readonly ClientOptions _opt;
    private int _speed = 1;
    /// <summary>Viewed army (fog of war), -1 = everything.</summary>
    private int _view = -1;
    private bool _menu;
    private int _frames;
    private Rectangle _panel, _timeline;
    private bool _dragging;
    private int _dragTick;

    private readonly ReplaySeeker _seeker;

    public ReplayScreen(AppHost app, GameSession session, ReplayPlayer player, ClientOptions opt)
    {
        _app = app;
        _s = session;
        _player = player;
        _opt = opt;
        _seeker = new ReplaySeeker(player, session.Runner);
        _s.Runner.Speed = Speeds[_speed];
        _s.MouseOverExtraUi = () => !_menu && (Ui.Hover(_panel) || _dragging);
        SetView(-1);
    }

    public string ScreenshotInfo => _s.ScreenshotInfo() + $"\nreplay: tick {_s.Snapshots.Current.Tick}/{_player.EndTick}, desync {_player.DesyncTick}, hashes ok {_player.HashesVerified}";

    public void Frame(float dt)
    {
        _frames++;
        if (_frames == 2 && _opt.SeekTick >= 0) Seek(_opt.SeekTick);
        if (_opt.PauseMenu && _frames == Math.Max(1, _opt.Frames - 15)) _menu = true;

        if (!_menu)
        {
            if (_s.HandleEscapeForPause()) _menu = true;
            else HandleKeys();
        }
        else if (MenuUi.Key(KeyboardKey.Escape)) _menu = false;

        _s.Frame(dt, interactive: !_menu);
        MenuUi.BeginFrame();
        DrawControls(!_menu);
        DrawBanners();
        if (_menu) DrawMenu();
        Toasts.Draw();
        Ui.DrawTooltip();
    }

    // ------------------------------------------------------------------ controls

    private void HandleKeys()
    {
        if (MenuUi.Key(KeyboardKey.Space)) _s.Runner.Paused = !_s.Runner.Paused;
        if (MenuUi.Key(KeyboardKey.Equal) || MenuUi.Key(KeyboardKey.KpAdd)) SetSpeed(_speed + 1);
        if (MenuUi.Key(KeyboardKey.Minus) || MenuUi.Key(KeyboardKey.KpSubtract)) SetSpeed(_speed - 1);
    }

    private void SetSpeed(int i)
    {
        _speed = Math.Clamp(i, 0, Speeds.Length - 1);
        _s.Runner.Speed = Speeds[_speed];
    }

    private void SetView(int army)
    {
        _view = army;
        if (army >= 0)
        {
            _s.Ctx.LocalArmy = army;
            _s.Fog.SetEnabled(true);
        }
        else _s.Fog.SetEnabled(false);
        _s.Selection.Selected.Clear();
    }

    private void DrawControls(bool interactive)
    {
        var snap = _s.Snapshots.Current;
        // Bottom right, like a video player (the orders/build panels are hidden in replays; minimap + info panel at the left).
        float w = MathF.Min(Ui.Sw - Ui.P(466), Ui.P(860)), h = Ui.P(66);
        _panel = Ui.R(Ui.Sw - Ui.P(4) - w, Ui.Sh - Ui.P(4) - h, w, h);
        Ui.Panel(_panel);
        float x = _panel.X + Ui.P(8), y = _panel.Y + Ui.P(7), bh = Ui.P(24);

        // Play / pause.
        var pr = Ui.R(x, y, Ui.P(34), bh);
        if (Ui.Button(pr, interactive).Left) _s.Runner.Paused = !_s.Runner.Paused;
        DrawPlayPause(pr, _s.Runner.Paused);
        if (Ui.Hover(pr)) Ui.Tooltip(pr, _s.Runner.Paused ? "Play" : "Pause", "Space", above: true);
        x += pr.Width + Ui.P(8);

        // Speed.
        var minus = Ui.R(x, y, Ui.P(24), bh);
        if (Ui.Button(minus, interactive && _speed > 0).Left) SetSpeed(_speed - 1);
        Ui.TextCentered("-", minus, Ui.F(14), Ui.TextColor, true);
        var sr = Ui.R(minus.X + minus.Width, y, Ui.P(44), bh);
        Ui.TextCentered($"x{Speeds[_speed]:0.#}", sr, Ui.F(13), Ui.TextColor, true);
        var plus = Ui.R(sr.X + sr.Width, y, Ui.P(24), bh);
        if (Ui.Button(plus, interactive && _speed < Speeds.Length - 1).Left) SetSpeed(_speed + 1);
        Ui.TextCentered("+", plus, Ui.F(14), Ui.TextColor, true);
        var speedR = Ui.R(minus.X, y, plus.X + plus.Width - minus.X, bh);
        if (Ui.Hover(speedR)) Ui.Tooltip(speedR, "Replay speed", "+ / - keys: 0.5x to 16x", above: true);
        x = plus.X + plus.Width + Ui.P(10);

        // Time label + timeline.
        int start = _player.StartTick, end = Math.Max(_player.EndTick, start + 1);
        int shown = _dragging ? _dragTick : IsSeeking(out int target) ? target : snap.Tick;
        string time = $"{SaveText.Length((shown - start) / 10f)} / {SaveText.Length((end - start) / 10f)}";
        int tf = Ui.F(12.5f);
        float tw = Ui.Measure("00:00:00 / 00:00:00", tf, true) * 0.82f;
        Ui.Text(time, x, y + (bh - tf) * 0.5f - 1, tf, Ui.TextColor, true);
        x += tw + Ui.P(8);
        _timeline = Ui.R(x, y + bh * 0.5f - Ui.P(5), _panel.X + _panel.Width - Ui.P(14) - x, Ui.P(10));
        DrawTimeline(snap.Tick, start, end, interactive);

        // Row 2: view selector + status.
        float y2 = y + bh + Ui.P(6), rh = Ui.P(21);
        MenuUi.Label("View", _panel.X + Ui.P(10), y2, rh);
        float vx = _panel.X + Ui.P(10) + Ui.Measure("VIEW", Ui.F(11.5f), true) + Ui.P(8);
        var players = _player.Info.Players;
        for (int a = -1; a < players.Count; a++)
        {
            string label = a < 0 ? "All (no fog)" : players[a].IsAI ? $"AI {players[a].Difficulty}" : players[a].Name;
            int lf = Ui.F(11.5f);
            float lw = Ui.Measure(label, lf, a == _view) + (a < 0 ? Ui.P(16) : Ui.P(30));
            if (vx + lw > _panel.X + _panel.Width * 0.72f) break;
            var r = Ui.R(vx, y2, lw, rh);
            bool sel = a == _view;
            if (Ui.Button(r, interactive, sel, sel ? MenuUi.PrimaryTint : null).Left) SetView(a);
            float lx = r.X + Ui.P(8);
            if (a >= 0)
            {
                Raylib.DrawRectangle((int)lx, (int)(r.Y + (rh - Ui.P(9)) * 0.5f), (int)Ui.P(9), (int)Ui.P(9), ColorUtil.FromRgba(players[a].Color));
                lx += Ui.P(14);
            }
            Ui.Text(label, lx, r.Y + (rh - lf) * 0.5f - 1, lf, sel ? Color.White : Ui.TextColor, sel);
            if (a >= 0 && Ui.Hover(r)) Ui.Tooltip(r, players[a].Name, $"See what {players[a].Name} saw (fog of war)\nSelection and resources follow this army", above: true);
            vx += lw + Ui.P(4);
        }

        string status;
        var col = Ui.TextDim;
        if (IsSeeking(out int to)) { status = $"Seeking to {SaveText.Length((to - start) / 10f)}…"; col = Ui.Warn; }
        else if (snap.Tick >= _player.EndTick)
        {
            status = "END OF REPLAY";
            col = Ui.Accent;
        }
        else status = _s.Runner.Paused ? "PAUSED" : $"tick {snap.Tick}";
        int sf = Ui.F(11.5f);
        Ui.TextRight(status, _panel.X + _panel.Width - Ui.P(12), y2 + (rh - sf) * 0.5f - 1, sf, col, true);
        if (IsSeeking(out _)) MenuUi.Spinner(new Vector2(_panel.X + _panel.Width - Ui.P(22) - Ui.Measure(status, sf, true), y2 + rh * 0.5f), Ui.P(6), Ui.Warn);
    }

    private void DrawTimeline(int tick, int start, int end, bool interactive)
    {
        var r = _timeline;
        var hit = Ui.R(r.X - Ui.P(4), r.Y - Ui.P(8), r.Width + Ui.P(8), r.Height + Ui.P(16));
        int TickAt(float mx) => start + (int)MathF.Round(Math.Clamp((mx - r.X) / r.Width, 0f, 1f) * (end - start));
        if (interactive && !_dragging && Ui.Hover(hit) && Ui.LeftClickIn(hit)) _dragging = true;
        if (_dragging)
        {
            _dragTick = TickAt(Ui.Mouse.X);
            if (!Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                _dragging = false;
                Seek(_dragTick);
            }
        }
        Raylib.DrawRectangleRec(r, new Color(8, 11, 16, 255));
        float frac = Math.Clamp((tick - start) / (float)(end - start), 0f, 1f);
        Raylib.DrawRectangle((int)r.X, (int)r.Y, (int)(r.Width * frac), (int)r.Height, new Color(70, 130, 190, 255));
        if (_player.DesyncTick >= 0)
        {
            float dx = r.X + r.Width * Math.Clamp((_player.DesyncTick - start) / (float)(end - start), 0f, 1f);
            Raylib.DrawRectangle((int)dx, (int)r.Y, (int)(r.X + r.Width - dx), (int)r.Height, new Color(200, 50, 40, 120));
        }
        Raylib.DrawRectangleLinesEx(r, 1, Ui.PanelBorder);
        int knobTick = _dragging ? _dragTick : IsSeeking(out int target) ? target : tick;
        float kx = r.X + r.Width * Math.Clamp((knobTick - start) / (float)(end - start), 0f, 1f);
        bool hot = _dragging || Ui.Hover(hit);
        Raylib.DrawRectangleRec(Ui.R(kx - Ui.P(4), r.Y - Ui.P(5), Ui.P(8), r.Height + Ui.P(10)), hot ? Color.White : new Color(180, 200, 220, 255));
        if (Ui.Hover(hit) && !_dragging)
            Ui.Tooltip(hit, $"Seek to {SaveText.Length((TickAt(Ui.Mouse.X) - start) / 10f)}", "Click or drag (going back re-simulates from the nearest checkpoint)", above: true);
    }

    private static void DrawPlayPause(Rectangle r, bool paused)
    {
        var c = new Vector2(r.X + r.Width * 0.5f, r.Y + r.Height * 0.5f);
        float s = r.Height * 0.28f;
        if (paused)
            Raylib.DrawTriangle(new Vector2(c.X - s * 0.8f, c.Y - s), new Vector2(c.X - s * 0.8f, c.Y + s), new Vector2(c.X + s, c.Y), Color.White);
        else
        {
            Raylib.DrawRectangle((int)(c.X - s * 0.8f), (int)(c.Y - s), (int)(s * 0.6f), (int)(2 * s), Color.White);
            Raylib.DrawRectangle((int)(c.X + s * 0.2f), (int)(c.Y - s), (int)(s * 0.6f), (int)(2 * s), Color.White);
        }
    }

    private void DrawBanners()
    {
        // Stacked upwards above the control panel.
        float y = _panel.Y - Ui.P(4);
        int fs = Ui.F(13), small = Ui.F(11.5f);
        if (_seeker.Error != null)
        {
            y -= small + Ui.P(4);
            Ui.TextShadow("Seek failed: " + _seeker.Error, _panel.X, y, small, Ui.Bad);
        }
        if (_player.DifferentBuild)
        {
            string t = "Recorded with a different game build: it may desync";
            y -= small + Ui.P(4);
            Ui.TextShadow(t, _panel.X + (_panel.Width - Ui.Measure(t, small)) * 0.5f, y, small, Ui.Warn);
        }
        if (_player.DesyncTick >= 0)
        {
            string t = $"DESYNC at tick {_player.DesyncTick}: the replay no longer matches the recorded game";
            y -= fs + Ui.P(12);
            var r = Ui.R(_panel.X, y, _panel.Width, fs + Ui.P(12));
            Raylib.DrawRectangleRec(r, new Color(150, 28, 24, 235));
            Raylib.DrawRectangleLinesEx(r, 1, new Color(255, 120, 100, 255));
            Ui.TextCentered(Ui.Fit(t, r.Width - Ui.P(12), fs, true), r, fs, Color.White, true);
        }
    }

    private bool IsSeeking(out int target) => _seeker.IsSeeking(out target);

    private void Seek(int tick) => _seeker.SeekTo(tick);

    // ------------------------------------------------------------------ menu

    private void DrawMenu()
    {
        MenuUi.Dim(140);
        float w = Ui.P(320), bh = Ui.P(42), gap = Ui.P(10);
        float h = Ui.P(64) + 4 * (bh + gap) + Ui.P(10);
        var p = Ui.R((Ui.Sw - w) * 0.5f, (Ui.Sh - h) * 0.42f, w, h);
        Ui.Panel(p);
        Ui.TextCentered("REPLAY", Ui.R(p.X, p.Y + Ui.P(12), p.Width, Ui.P(30)), Ui.F(22), Ui.TextColor, true);
        var r = Ui.R(p.X + Ui.P(20), p.Y + Ui.P(60), w - Ui.P(40), bh);
        if (MenuUi.BigButton(r, "Resume", true, true)) _menu = false;
        r.Y += bh + gap;
        if (MenuUi.BigButton(r, "Restart Replay"))
        {
            _menu = false;
            Seek(_player.StartTick);
        }
        r.Y += bh + gap;
        if (MenuUi.BigButton(r, "Back to Replays")) _app.Go(new SaveListScreen(_app, replays: true));
        r.Y += bh + gap;
        if (MenuUi.BigButton(r, "Quit to Desktop")) _app.QuitRequested = true;
        Ui.Text("Esc: resume", p.X, p.Y + p.Height + Ui.P(8), Ui.F(10.5f), Ui.TextFaint);
    }

    public void Dispose() => _s.Dispose();
}
