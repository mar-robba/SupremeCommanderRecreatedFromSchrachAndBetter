using Raylib_cs;
using Rts.Client.UI;

namespace Rts.Client.Menus;

/// <summary>
/// In-game state: runs a <see cref="GameSession"/> and its overlays.
/// <list type="bullet">
/// <item>Esc with nothing selected and no order mode/placement → pause menu (pauses the sim: single player).</item>
/// <item>Victory / defeat of the local player → banner for a few seconds, then the end screen (the sim thread is
///   stopped so the statistics can be read from the world; "Continue" restarts it).</item>
/// </list>
/// CLI helpers for screenshots: --pause-menu / --endscreen open the overlay ~15 frames before the capture.
/// </summary>
public sealed class InGameScreen : IScreen
{
    private enum State { Playing, Paused, Ending, Stats }

    private readonly AppHost _app;
    private readonly GameSession _s;
    private readonly ClientOptions _opt;
    private State _state;
    private bool _wasPaused;
    private float _endTimer;
    private bool _endWon;
    private bool _shownDefeat, _shownOver;
    private EndScreen? _end;
    private int _frames;

    public InGameScreen(AppHost app, GameSession session, ClientOptions opt)
    {
        _app = app;
        _s = session;
        _opt = opt;
    }

    public GameSession Session => _s;

    public string ScreenshotInfo => _s.ScreenshotInfo();

    public void Frame(float dt)
    {
        _frames++;
        int overlayFrame = Math.Max(1, _opt.Frames - 15);
        if (_opt.PauseMenu && _frames == overlayFrame && _state == State.Playing) OpenPause();
        if (_opt.EndScreen && _frames == overlayFrame && _state != State.Stats) OpenStats();
        if (_opt.FlowTest) FlowTestStep();

        switch (_state)
        {
            case State.Playing:
                if (_s.HandleEscapeForPause()) OpenPause();
                else CheckOutcome();
                break;
            case State.Paused:
                if (MenuUi.Key(KeyboardKey.Escape)) Resume();
                break;
            case State.Ending:
                _endTimer += dt;
                if (_endTimer > 3f) OpenStats();
                break;
        }

        _s.Frame(dt, interactive: _state == State.Playing);

        switch (_state)
        {
            case State.Paused: DrawPause(); break;
            case State.Ending: DrawEndingBanner(); break;
            case State.Stats: DrawStats(); break;
        }
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
        _endTimer = 0;
        _state = State.Ending;
    }

    // ------------------------------------------------------------------ pause menu

    private void OpenPause()
    {
        _wasPaused = _s.Runner.Paused;
        _s.Runner.Paused = true;
        _state = State.Paused;
    }

    private void Resume()
    {
        _s.Runner.Paused = _wasPaused;
        _state = State.Playing;
    }

    private void DrawPause()
    {
        MenuUi.BeginFrame();
        MenuUi.Dim(140);
        float w = Ui.P(320), bh = Ui.P(42), gap = Ui.P(10);
        float h = Ui.P(64) + 4 * (bh + gap) + Ui.P(10);
        var p = Ui.R((Ui.Sw - w) * 0.5f, (Ui.Sh - h) * 0.42f, w, h);
        Ui.Panel(p);
        Ui.TextCentered("PAUSED", Ui.R(p.X, p.Y + Ui.P(12), p.Width, Ui.P(30)), Ui.F(22), Ui.TextColor, true);
        float y = p.Y + Ui.P(60);
        var r = Ui.R(p.X + Ui.P(20), y, w - Ui.P(40), bh);
        if (MenuUi.BigButton(r, "Resume", true, true)) Resume();
        r.Y += bh + gap;
        if (MenuUi.BigButton(r, "Restart")) _app.StartGame(_s.Config, _opt);
        r.Y += bh + gap;
        if (MenuUi.BigButton(r, "Quit to Menu")) _app.ToMainMenu();
        r.Y += bh + gap;
        if (MenuUi.BigButton(r, "Quit to Desktop")) _app.QuitRequested = true;
        Ui.Text("Esc: resume", p.X, p.Y + p.Height + Ui.P(8), Ui.F(10.5f), Ui.TextFaint);
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
        if (_state == State.Paused) _s.Runner.Paused = _wasPaused;
        // Stop the sim thread: the world may now be read directly.
        _s.Runner.Stop();
        var (over, won) = _s.Outcome();
        _end = new EndScreen(_s, over ? (won ? EndScreen.Result.Victory : EndScreen.Result.Defeat) : EndScreen.Result.Summary);
        _state = State.Stats;
    }

    private void DrawStats()
    {
        MenuUi.BeginFrame();
        var action = _end!.Draw();
        switch (action)
        {
            case EndScreen.Action.Continue:
                _end = null;
                _s.Runner.Start();
                _state = State.Playing;
                break;
            case EndScreen.Action.Restart:
                _app.StartGame(_s.Config, _opt);
                break;
            case EndScreen.Action.MainMenu:
                _app.ToMainMenu();
                break;
        }
        Ui.DrawTooltip();
    }

    private static int s_flowGames;
    private int _flowIndex = -1;

    /// <summary>--flow-test: pause, then alternately Restart / Quit to Menu (exercises disposal and re-creation).</summary>
    private void FlowTestStep()
    {
        if (_flowIndex < 0) _flowIndex = ++s_flowGames;
        if (_frames == 40) OpenPause();
        if (_frames == 60) OpenStats();
        if (_frames == 80)
        {
            Console.WriteLine($"flow-test: game {_flowIndex} -> {(_flowIndex % 2 == 1 ? "restart" : "main menu")}");
            if (_flowIndex % 2 == 1) _app.StartGame(_s.Config, _opt);
            else _app.ToMainMenu();
        }
    }

    public void Dispose() => _s.Dispose();
}
