using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Client.Persistence;
using Rts.Client.UI;
using Rts.Net.Session;
using Rts.Sim;

namespace Rts.Client.Menus;

/// <summary>
/// In-game state of a multiplayer game: runs the <see cref="GameSession"/> (lockstep hooks) plus the network overlay.
/// <list type="bullet">
/// <item>Enter opens the chat line (Enter sends, Esc cancels); messages fade out after a while.</item>
/// <item>Space / + / − ask the host for pause and speed (applied for everybody, announced as a toast).</item>
/// <item>Esc opens the game menu: the game keeps running (Resume, Pause, Leave Game, Quit to Desktop); no saving.</item>
/// <item>"Waiting for X…" when the sim holds for a missing turn; the players panel shows pings and who left.</item>
/// <item>Drops, desyncs (the replay so far is written at once) and a lost host connection are toasts/banners.</item>
/// <item>Local defeat: banner, then observer mode (fog off; the sim keeps running so the others never wait).
///   Game over: banner, then the end screen (the runner stops; Restart is disabled).</item>
/// </list>
/// Leaving (menu, window closed) leaves the session: the others drop this player (AI takeover or defeat).
/// </summary>
public sealed class NetInGameScreen : IScreen
{
    private enum State { Playing, Menu, Ending, Stats }

    private readonly AppHost _app;
    private readonly NetSession _net;
    private readonly NetGame _game;
    private readonly GameSession _s;
    private readonly ClientOptions _opt;
    private readonly NetTextField _chat = new("", NetSession.MaxChat);
    private readonly List<(ChatLine line, double time)> _feed = new();
    private bool _chatAllies; // the open chat line goes "to allies" (opened with Shift+Enter)
    private State _state;
    private float _endTimer;
    private bool _endWon, _endGameOver, _shownDefeat, _shownOver;
    private EndScreen? _end;
    private double _waitSince = -1;
    private string? _banner;
    private bool _lastPaused;
    private int _frames;

    /// <summary>Chat lines stay this long (seconds) unless the chat line is open.</summary>
    private const double FeedSeconds = 12;

    public NetInGameScreen(AppHost app, NetSession net, NetGame game, GameSession session, ClientOptions opt)
    {
        _app = app;
        _net = net;
        _game = game;
        _s = session;
        _opt = opt;
        _lastPaused = game.Paused;
        _s.NetKeys = SpeedKeys;
        foreach (var l in net.ChatLog.TakeLast(3)) _feed.Add((l, Raylib.GetTime()));
    }

    public string ScreenshotInfo => $"{_s.ScreenshotInfo()}\n(net: {(_net.IsHost ? "host" : "client")}, waiting for {_game.WaitingFor ?? "nobody"}, desync tick {_game.DesyncTick})";

    public void Frame(float dt)
    {
        _frames++;
        HandleEvents();
        double now = Raylib.GetTime();

        switch (_state)
        {
            case State.Playing:
                if (_chat.Focused)
                {
                    if (_chat.HandleKeys())
                    {
                        if (_chat.Text.Trim().Length > 0) _net.SendChat(_chat.Text, _chatAllies);
                        _chat.Text = "";
                        _chat.Focused = false;
                    }
                }
                else if (!MenuUi.Automated && Hotkeys.Chat.Pressed())
                {
                    if (_s.Selection.PendingSelfDestruct == null)
                    {
                        _chatAllies = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
                        _chat.Focus();
                    }
                }
                else if (_s.HandleEscapeForPause()) _state = State.Menu;
                CheckOutcome();
                break;
            case State.Menu:
                if (MenuUi.Key(KeyboardKey.Escape)) _state = State.Playing;
                break;
            case State.Ending:
                _endTimer += dt;
                if (_endTimer > 3f)
                {
                    if (_endGameOver) OpenStats();
                    else
                    {
                        // Defeated while the game goes on: keep watching (the sim must keep running for the others).
                        _s.Fog.SetEnabled(false);
                        Toasts.Show("You were defeated: observing the rest of the game");
                        _state = State.Playing;
                    }
                }
                break;
        }

        _s.KeyboardCaptured = _chat.Focused;
        _s.Frame(dt, interactive: _state == State.Playing);

        if (_state != State.Stats)
        {
            DrawWaiting(now);
            DrawPlayers();
            DrawChat(now);
        }
        switch (_state)
        {
            case State.Menu: DrawMenu(); break;
            case State.Ending: DrawEndingBanner(); break;
            case State.Stats: DrawStats(); break;
        }
        Toasts.Draw();
    }

    // ------------------------------------------------------------------ network events

    private void HandleEvents()
    {
        while (_net.TryGetEvent(out var e))
        {
            switch (e)
            {
                case ChatEvent c:
                    _feed.Add((c.Line, Raylib.GetTime()));
                    if (_feed.Count > 50) _feed.RemoveAt(0);
                    break;
                case PlayerDroppedEvent:
                    break; // the host (or this peer, offline) posts a system chat line
                case DesyncEvent d:
                    // The game goes on, but the peers no longer agree: dump the replay so far for the bug report.
                    _s.Recording?.WriteReplayNow(_s.Runner);
                    _banner = $"DESYNC at tick {d.Tick}: the replay was saved in replays/ ({d.Detail})";
                    Toasts.Show($"Desync at tick {d.Tick}", error: true);
                    break;
                case SpeedChangedEvent sc:
                    string who = string.IsNullOrEmpty(sc.By) ? "" : sc.By + ": ";
                    if (sc.Paused != _lastPaused) Toasts.Show(sc.Paused ? $"{who}game paused" : $"{who}game resumed");
                    else Toasts.Show($"{who}game speed x{sc.Speed:0.##}");
                    _lastPaused = sc.Paused;
                    break;
                case DisconnectedEvent d:
                    if (!_net.IsHost) Toasts.Show($"Connection to the host lost ({d.Reason}): the game continues offline", error: true);
                    break;
            }
        }
    }

    /// <summary>Space / + / − (instead of the single-player debug keys): requests to the host.</summary>
    private void SpeedKeys()
    {
        if (Hotkeys.PauseGame.Pressed()) _game.RequestSpeed(_game.Speed, !_game.Paused);
        // (6b) Locked game speed (lobby option): the host keeps it anyway; say why nothing happens.
        if ((Hotkeys.SpeedUp.Pressed() || Hotkeys.SpeedDown.Pressed()) && _game.LockedSpeed is float locked)
        {
            Toasts.Show($"Game speed is locked at {locked:0.#}x");
            return;
        }
        if (Hotkeys.SpeedUp.Pressed()) _game.RequestSpeed(MathF.Min(8f, _game.Speed * 2f), _game.Paused);
        if (Hotkeys.SpeedDown.Pressed()) _game.RequestSpeed(MathF.Max(0.125f, _game.Speed * 0.5f), _game.Paused);
    }

    private void CheckOutcome()
    {
        var (over, won) = _s.Outcome();
        if (!over) return;
        bool gameOver = _s.Snapshots.Current.WinningTeam >= 0;
        if (gameOver ? _shownOver : _shownDefeat) return;
        if (gameOver) _shownOver = true;
        _shownDefeat = true;
        _endWon = won;
        _endGameOver = gameOver;
        _endTimer = 0;
        _state = State.Ending;
    }

    // ------------------------------------------------------------------ overlay

    private void DrawWaiting(double now)
    {
        string? waiting = _game.WaitingFor;
        if (waiting == null || _game.Paused) { _waitSince = -1; }
        else if (_waitSince < 0) _waitSince = now;
        float y = Ui.P(64);
        if (_banner != null)
        {
            int fs = Ui.F(12.5f);
            float w = Ui.Measure(_banner, fs, true) + Ui.P(24);
            var r = Ui.R((Ui.Sw - w) * 0.5f, y, w, Ui.P(26));
            Raylib.DrawRectangleRec(r, new Color(90, 20, 18, 220));
            Raylib.DrawRectangleLinesEx(r, 1, Ui.Bad);
            Ui.TextCentered(_banner, r, fs, Ui.TextColor, true);
            y += Ui.P(34);
        }
        if (_waitSince >= 0 && now - _waitSince > 0.5)
        {
            string t = $"Waiting for {waiting}…";
            int fs = Ui.F(15);
            float w = Ui.Measure(t, fs, true) + Ui.P(56);
            var r = Ui.R((Ui.Sw - w) * 0.5f, y, w, Ui.P(36));
            Ui.Panel(r);
            MenuUi.Spinner(new Vector2(r.X + Ui.P(20), r.Y + r.Height * 0.5f), Ui.P(8), Ui.Accent);
            Ui.Text(t, r.X + Ui.P(38), r.Y + (r.Height - fs) * 0.5f - 1, fs, Ui.TextColor, true);
        }
    }

    /// <summary>Compact list of the players (army color, name, ping) at the right edge, below the top bar.</summary>
    private void DrawPlayers()
    {
        var list = _game.PlayerStatus();
        int fs = Ui.F(11);
        float lineH = fs + Ui.P(5), w = Ui.P(190);
        var r = Ui.R(Ui.Sw - w - Ui.P(10), Ui.P(80), w, Ui.P(10) + lineH * list.Count);
        Raylib.DrawRectangleRec(r, new Color(10, 14, 20, 170));
        float y = r.Y + Ui.P(5);
        var colors = _s.Ctx.ArmyColors;
        foreach (var p in list)
        {
            var c = (uint)p.Army < (uint)colors.Length ? colors[p.Army] : Ui.TextDim;
            Raylib.DrawRectangle((int)(r.X + Ui.P(8)), (int)(y + Ui.P(2)), (int)Ui.P(8), (int)Ui.P(8), c);
            string name = Ui.Fit(p.Local ? p.Name + " (you)" : p.Name, w - Ui.P(80), fs);
            Ui.Text(name, r.X + Ui.P(22), y, fs, p.Dropped ? Ui.TextFaint : p.Local ? Ui.Accent : Ui.TextColor);
            string right = p.Dropped ? "left" : p.Local ? "" : p.PingMs >= 0 ? $"{p.PingMs} ms" : "…";
            var rc = p.Dropped ? Ui.TextFaint : p.PingMs > 250 ? Ui.Warn : Ui.TextDim;
            Ui.TextRight(right, r.X + r.Width - Ui.P(8), y, fs, rc);
            y += lineH;
        }
        if (_game.Offline) Ui.TextRight("offline", r.X + r.Width - Ui.P(8), r.Y + r.Height + Ui.P(3), fs, Ui.Warn, true);
    }

    /// <summary>"[Allies] " for an allies-only line (shown as "[Allies] name: text").</summary>
    internal static string ChatPrefix(ChatLine line) => line.AlliesOnly ? "[Allies] " : "";

    private void DrawChat(double now)
    {
        float x = Ui.P(14), w = Ui.P(520);
        int fs = Ui.F(12.5f);
        float lineH = fs + Ui.P(5);
        float y = Ui.Sh * 0.62f;
        if (_chat.Focused)
        {
            var input = Ui.R(x, y, w, Ui.P(28));
            _chat.Draw(input, "", _chatAllies ? "To allies:" : "Say:", 12.5f);
            Ui.Text(_chatAllies ? "Enter: send to your team · Esc: cancel" : "Enter: send · Esc: cancel (Shift+Enter: to allies)",
                x, y + Ui.P(31), Ui.F(10), Ui.TextFaint);
        }
        // Recent lines above the input position, oldest on top.
        var lines = _feed.Where(f => _chat.Focused || now - f.time < FeedSeconds).TakeLast(_chat.Focused ? 10 : 6).ToList();
        float ly = y - Ui.P(6) - lineH * lines.Count;
        var lobby = _game.Start.Lobby;
        foreach (var (line, time) in lines)
        {
            double age = now - time;
            byte a = _chat.Focused ? (byte)255 : (byte)(255 * Math.Clamp((FeedSeconds - age) / 2.0, 0, 1));
            float tx = x;
            if (line.IsSystem) Ui.TextShadow(Ui.Fit(line.Text, w, fs), tx, ly, fs, ColorUtil.WithAlpha(Ui.Warn, a));
            else
            {
                var slot = lobby.SlotOfPlayer(line.PlayerId);
                var c = slot != null ? ColorUtil.Scale(ColorUtil.FromRgba(GameSetup.ArmyColors[slot.Color % GameSetup.ArmyColors.Length]), 1.3f) : Ui.Accent;
                string who = ChatPrefix(line) + line.Name + ": ";
                Ui.TextShadow(who, tx, ly, fs, ColorUtil.WithAlpha(c, a), true);
                tx += Ui.Measure(who, fs, true);
                Ui.TextShadow(Ui.Fit(line.Text, x + w - tx, fs), tx, ly, fs, ColorUtil.WithAlpha(Ui.TextColor, a));
            }
            ly += lineH;
        }
    }

    // ------------------------------------------------------------------ game menu (the game keeps running)

    private void DrawMenu()
    {
        MenuUi.BeginFrame();
        MenuUi.Dim(110);
        float w = Ui.P(320), bh = Ui.P(42), gap = Ui.P(10);
        float h = Ui.P(64) + 4 * (bh + gap) + Ui.P(10);
        var p = Ui.R((Ui.Sw - w) * 0.5f, (Ui.Sh - h) * 0.42f, w, h);
        Ui.Panel(p);
        Ui.TextCentered("GAME MENU", Ui.R(p.X, p.Y + Ui.P(12), p.Width, Ui.P(30)), Ui.F(22), Ui.TextColor, true);
        float y = p.Y + Ui.P(60);
        var r = Ui.R(p.X + Ui.P(20), y, w - Ui.P(40), bh);
        if (MenuUi.BigButton(r, "Resume", true, true)) _state = State.Playing;
        r.Y += bh + gap;
        if (MenuUi.BigButton(r, _game.Paused ? "Unpause Game" : "Pause Game")) _game.RequestSpeed(_game.Speed, !_game.Paused);
        r.Y += bh + gap;
        if (MenuUi.BigButton(r, "Leave Game")) _app.ToMainMenu();
        r.Y += bh + gap;
        if (MenuUi.BigButton(r, "Quit to Desktop")) _app.QuitRequested = true;
        Ui.Text("Multiplayer: the game keeps running while this menu is open.", p.X, p.Y + p.Height + Ui.P(8), Ui.F(10.5f), Ui.TextFaint);
        Ui.DrawTooltip();
    }

    // ------------------------------------------------------------------ end of game

    private void DrawEndingBanner()
    {
        MenuUi.BeginFrame();
        float k = Math.Clamp(_endTimer / 0.8f, 0f, 1f);
        MenuUi.Dim((byte)(90 * k));
        string t = _endWon ? "VICTORY" : "DEFEAT";
        int fs = Ui.F(64);
        float w = Ui.Measure(t, fs, true);
        Ui.TextShadow(t, (Ui.Sw - w) * 0.5f, Ui.Sh / 3f, fs, ColorUtil.WithAlpha(_endWon ? Color.Gold : new Color(235, 70, 60, 255), (byte)(255 * k)), true);
    }

    private void OpenStats()
    {
        // Game over for everybody: stop the sim so the statistics can be read from the world.
        _s.Runner.Stop();
        var (over, won) = _s.Outcome();
        _end = new EndScreen(_s, over ? (won ? EndScreen.Result.Victory : EndScreen.Result.Defeat) : EndScreen.Result.Summary) { CanRestart = false };
        _state = State.Stats;
    }

    private void DrawStats()
    {
        MenuUi.BeginFrame();
        switch (_end!.Draw())
        {
            case EndScreen.Action.Continue:
                _end = null;
                if (_s.Outcome() is (true, false)) _s.Fog.SetEnabled(false);
                _s.Runner.Start();
                _state = State.Playing;
                break;
            case EndScreen.Action.MainMenu:
                _app.ToMainMenu();
                break;
        }
        Ui.DrawTooltip();
    }

    public void Dispose()
    {
        _net.Leave();
        _s.Dispose();
    }
}
