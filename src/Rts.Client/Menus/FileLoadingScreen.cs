using System.Numerics;
using Raylib_cs;
using Rts.Client.Persistence;
using Rts.Client.UI;
using Rts.Sim.Persistence;

namespace Rts.Client.Menus;

/// <summary>
/// Opens a save game or a replay file on a worker thread (decode, terrain regeneration, world restore), then creates
/// the <see cref="GameSession"/> on the main thread: a loaded game continues in <see cref="InGameScreen"/> (its replay
/// recording continues too), a replay plays in <see cref="ReplayScreen"/>. Errors (damaged / incompatible files) are
/// shown in a message box; Back returns to the list (or the main menu).
/// </summary>
public sealed class FileLoadingScreen : IScreen
{
    private readonly AppHost _app;
    private readonly string _path;
    private readonly bool _replay;
    private readonly ClientOptions _opt;
    private readonly bool _fromList;
    private readonly Task<object> _task;
    private float _time;
    private int _frames;
    private bool _shownFinal, _done;
    private string? _error;

    public FileLoadingScreen(AppHost app, string path, bool replay, ClientOptions opt, bool fromList = false)
    {
        _app = app;
        _path = path;
        _replay = replay;
        _opt = opt;
        _fromList = fromList;
        var db = app.Db;
        _task = Task.Run<object>(() => replay ? SaveGame.OpenReplay(path, db) : SaveGame.Load(path, db));
    }

    public void Frame(float dt)
    {
        _time += dt;
        _frames++;
        Raylib.ClearBackground(new Color(10, 14, 20, 255));
        MenuUi.BeginFrame();
        Raylib.DrawRectangleGradientV(0, 0, Ui.Sw, Ui.Sh, new Color(18, 26, 36, 255), new Color(6, 9, 13, 255));

        float w = Ui.P(520), h = Ui.P(170);
        var p = Ui.R((Ui.Sw - w) * 0.5f, (Ui.Sh - h) * 0.45f, w, h);
        Ui.Panel(p);
        Ui.PanelTitle(p, _replay ? "Opening replay" : "Loading saved game");
        float x = p.X + Ui.P(22), y = p.Y + Ui.P(36);
        Ui.Text(Ui.Fit(Path.GetFileNameWithoutExtension(_path), w - Ui.P(44), Ui.F(20), true), x, y, Ui.F(20), Ui.TextColor, true);
        y += Ui.P(50);

        if (_task.IsFaulted || _error != null)
        {
            _error ??= SaveText.ErrorMessage(_task.Exception!.GetBaseException());
            if (MessageBox.Draw(_replay ? "Cannot play this replay" : "Cannot load this game", _error, ["Back"], error: true) >= 0) Back();
            return;
        }
        string stage = _task.IsCompleted ? "Preparing graphics…" : _replay ? "Reading the replay…" : "Restoring the world…";
        MenuUi.Spinner(new Vector2(x + Ui.P(10), y + Ui.P(10)), Ui.P(9), Ui.Accent);
        Ui.Text(stage, x + Ui.P(30), y + Ui.P(2), Ui.F(13), Ui.TextColor);

        // Switch after the "Preparing graphics" frame was presented (session creation blocks a moment).
        if (!_task.IsCompletedSuccessfully || _time < 0.2f || _done) return;
        if (!_shownFinal) { _shownFinal = true; return; }
        _done = true;
        try { Start(_task.Result); }
        catch (Exception e)
        {
            _error = SaveText.ErrorMessage(e);
            _done = false;
        }
    }

    private void Start(object result)
    {
        // Screenshot runs: the overlay helpers (--pause-menu, --save-dialog) count the game's own frames.
        var opt = _opt;
        if (opt.ScreenshotPath != null)
        {
            opt = _opt.Copy();
            opt.Frames = Math.Max(1, opt.Frames - _frames);
        }
        if (result is ReplayPlayer player)
        {
            var cfg = ClientGameData.ConfigFor(player.Info);
            cfg.Sandbox = false; // read-only: no sandbox panel/console
            int local = Math.Clamp(player.Info.LocalArmy, 0, Math.Max(0, player.Sim.World.Armies.Length - 1));
            var session = new GameSession(_app.Db, player.Sim, cfg, local, opt, _app.Audio, replay: player);
            _app.Go(new ReplayScreen(_app, session, player, opt));
        }
        else
        {
            var game = (LoadedGame)result;
            var cfg = ClientGameData.ConfigFor(game.Info);
            int local = Math.Clamp(game.Info.LocalArmy, 0, Math.Max(0, game.Sim.World.Armies.Length - 1));
            var session = new GameSession(_app.Db, game.Sim, cfg, local, opt, _app.Audio, new GameRecording(game.Info, game.Replay));
            Toasts.Show($"Loaded: {(game.Info.Name.Length > 0 ? game.Info.Name : Path.GetFileNameWithoutExtension(_path))}");
            _app.Go(new InGameScreen(_app, session, opt));
        }
    }

    private void Back()
    {
        if (_fromList) _app.Go(new SaveListScreen(_app, _replay));
        else _app.ToMainMenu();
    }

    public void Dispose()
    {
        // Leaving before a session took ownership: the decoded world is just garbage (no GPU resources yet).
    }
}
