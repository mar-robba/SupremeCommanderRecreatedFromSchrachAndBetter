using System.Numerics;
using Raylib_cs;
using Rts.Client.UI;
using Rts.Sim;
using Rts.Sim.Map;

namespace Rts.Client.Menus;

/// <summary>
/// Builds the simulation (map generation, armies) on a worker thread while showing progress, then creates the
/// <see cref="GameSession"/> (GPU resources) on the main thread and switches to <see cref="InGameScreen"/>.
/// </summary>
public sealed class LoadingScreen : IScreen
{
    private readonly AppHost _app;
    private readonly GameConfig _cfg;
    private readonly ClientOptions _opt;
    private readonly Task<(Simulation sim, int local)> _task;
    private float _time;
    private bool _done;

    public LoadingScreen(AppHost app, GameConfig cfg, ClientOptions opt)
    {
        _app = app;
        _cfg = cfg;
        _opt = opt;
        _task = Task.Run(() =>
        {
            var sim = GameSession.CreateSimulation(app.Db, cfg, out int local);
            return (sim, local);
        });
    }

    public void Frame(float dt)
    {
        _time += dt;
        Raylib.ClearBackground(new Color(10, 14, 20, 255));
        MenuUi.BeginFrame();
        int sw = Ui.Sw, sh = Ui.Sh;
        Raylib.DrawRectangleGradientV(0, 0, sw, sh, new Color(18, 26, 36, 255), new Color(6, 9, 13, 255));

        float w = Ui.P(520), h = Ui.P(210);
        var p = Ui.R((sw - w) * 0.5f, (sh - h) * 0.45f, w, h);
        Ui.Panel(p);
        Ui.PanelTitle(p, "Loading");
        float x = p.X + Ui.P(22), y = p.Y + Ui.P(36);
        Ui.Text($"{MapPresets.Name(_cfg.Preset)} · {_cfg.MapSize / 51.2f:0} km · seed {_cfg.Seed}", x, y, Ui.F(20), Ui.TextColor, true);
        y += Ui.P(34);
        // Players line: colored names.
        float px = x;
        int a = 0;
        foreach (var s in _cfg.OpenSlots)
        {
            var c = ColorUtil.FromRgba(GameSetup.ArmyColors[s.ColorIndex % GameSetup.ArmyColors.Length]);
            string name = s.Kind == SlotKind.Human ? "You" : $"AI {s.Difficulty}";
            Raylib.DrawRectangle((int)px, (int)(y + Ui.P(3)), (int)Ui.P(10), (int)Ui.P(10), c);
            Ui.Text(name, px + Ui.P(14), y, Ui.F(12), Ui.TextDim);
            px += Ui.P(14) + Ui.Measure(name, Ui.F(12)) + Ui.P(14);
            if (++a >= 6 && _cfg.Players > 6) { Ui.Text($"+{_cfg.Players - 6}", px, y, Ui.F(12), Ui.TextDim); break; }
        }
        y += Ui.P(36);

        if (_task.IsFaulted)
        {
            Ui.Text("Could not create the game:", x, y, Ui.F(13), Ui.Bad, true);
            Ui.Text(_task.Exception?.GetBaseException().Message ?? "", x, y + Ui.P(20), Ui.F(11), Ui.TextDim);
            if (MenuUi.BigButton(Ui.R(p.X + p.Width - Ui.P(150), p.Y + p.Height - Ui.P(50), Ui.P(130), Ui.P(36)), "Back"))
                _app.ToMainMenu();
            return;
        }
        string stage = _task.IsCompleted ? "Preparing graphics…" : "Generating terrain and deposits…";
        MenuUi.Spinner(new Vector2(x + Ui.P(10), y + Ui.P(10)), Ui.P(9), Ui.Accent);
        Ui.Text(stage, x + Ui.P(30), y + Ui.P(2), Ui.F(13), Ui.TextColor);

        // Switch after the frame showing "Preparing graphics" was presented (session creation blocks a moment).
        if (_task.IsCompletedSuccessfully && _time > 0.3f && !_done)
        {
            if (!_shownFinal) { _shownFinal = true; return; }
            _done = true;
            var (sim, local) = _task.Result;
            var session = new GameSession(_app.Db, sim, _cfg, local, _opt, _app.Audio);
            _app.Go(new InGameScreen(_app, session, _opt));
        }
    }

    private bool _shownFinal;

    public void Dispose()
    {
        // If we leave before the session took ownership, nothing GPU-side was created; the sim is just garbage.
    }
}
