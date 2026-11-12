using System.Numerics;
using Raylib_cs;
using Rts.Client.UI;
using Rts.Net.Lobby;
using Rts.Net.Session;
using Rts.Sim;
using Rts.Sim.Map;

namespace Rts.Client.Menus;

/// <summary>
/// Multiplayer loading: builds the simulation from the host's <see cref="GameStart"/> on a worker (identical on every
/// peer), then creates the <see cref="GameSession"/> with the lockstep hooks around the replay recording and reports
/// the state hash to the host. The runner is held until the host's Go (everybody loaded with equal hashes) →
/// <see cref="NetInGameScreen"/>. Abort (a hash differs, somebody left) → back to the lobby.
/// </summary>
public sealed class NetLoadingScreen : IScreen
{
    private readonly AppHost _app;
    private readonly NetSession _s;
    private readonly NetGame _game;
    private readonly ClientOptions _opt;
    private readonly Task<(Simulation sim, ulong hash, Rts.Sim.Map.MapFile? map)> _task;
    private GameSession? _session;
    private bool _handedOff, _shownFinal;
    private float _time;

    public NetLoadingScreen(AppHost app, NetSession session, NetGame game)
    {
        _app = app;
        _s = session;
        _game = game;
        _opt = app.MenuGameOptions();
        _task = Task.Run(() =>
        {
            var map = NetGameSetup.LoadMap(game.Start, session.MapDir); // (8b-3) null = generated map
            var sim = NetGameSetup.CreateSimulation(app.Db, game.Start, map);
            return (sim, sim.StateHash(), map);
        });
    }

    public string ScreenshotInfo => _session == null ? "(building)" : "(waiting for the other players)";

    public void Frame(float dt)
    {
        _time += dt;
        if (HandleEvents()) return;
        Raylib.ClearBackground(new Color(10, 14, 20, 255));
        MenuUi.BeginFrame();
        int sw = Ui.Sw, sh = Ui.Sh;
        Raylib.DrawRectangleGradientV(0, 0, sw, sh, new Color(18, 26, 36, 255), new Color(6, 9, 13, 255));

        float w = Ui.P(560), h = Ui.P(230);
        var p = Ui.R((sw - w) * 0.5f, (sh - h) * 0.45f, w, h);
        Ui.Panel(p);
        Ui.PanelTitle(p, "Starting multiplayer game");
        float x = p.X + Ui.P(22), y = p.Y + Ui.P(36);
        var o = _game.Start.Lobby.Options;
        Ui.Text(o.HasMapFile ? $"{o.MapName} · {o.MapSize / 51.2f:0} km · {Rts.Sim.Map.Biomes.Name(o.EffectiveBiome)}"
            : $"{MapPresets.Name(o.Preset)} · {o.MapSize / 51.2f:0} km · seed {o.Seed}", x, y, Ui.F(20), Ui.TextColor, true);
        y += Ui.P(34);
        float px = x;
        foreach (int slot in _game.Start.ArmySlots)
        {
            var s = _game.Start.Lobby.Slots[slot];
            var c = ColorUtil.FromRgba(GameSetup.ArmyColors[s.Color % GameSetup.ArmyColors.Length]);
            string name = s.Kind == NetSlotKind.AI ? $"AI {s.Difficulty}" : s.PlayerId == _s.PlayerId ? "You" : s.Name;
            float nw = Ui.P(14) + Ui.Measure(name, Ui.F(12)) + Ui.P(14);
            if (px + nw > p.X + p.Width - Ui.P(20)) { px = x; y += Ui.P(18); }
            Raylib.DrawRectangle((int)px, (int)(y + Ui.P(3)), (int)Ui.P(10), (int)Ui.P(10), c);
            Ui.Text(name, px + Ui.P(14), y, Ui.F(12), Ui.TextDim);
            px += nw;
        }
        y += Ui.P(36);

        var cancel = Ui.R(p.X + p.Width - Ui.P(150), p.Y + p.Height - Ui.P(50), Ui.P(130), Ui.P(36));
        if (_task.IsFaulted)
        {
            Ui.Text("Could not create the game:", x, y, Ui.F(13), Ui.Bad, true);
            Ui.Text(Ui.Fit(_task.Exception?.GetBaseException().Message ?? "", p.Width - Ui.P(44), Ui.F(11)), x, y + Ui.P(20), Ui.F(11), Ui.TextDim);
            if (MenuUi.BigButton(cancel, "Leave")) _app.Go(new MultiplayerScreen(_app, "The game could not be created"));
            return;
        }
        string stage = !_task.IsCompleted ? "Generating terrain and deposits…"
            : _session == null ? "Preparing graphics…" : "Waiting for the other players…";
        MenuUi.Spinner(new Vector2(x + Ui.P(10), y + Ui.P(10)), Ui.P(9), Ui.Accent);
        Ui.Text(stage, x + Ui.P(30), y + Ui.P(2), Ui.F(13), Ui.TextColor);
        if (MenuUi.BigButton(cancel, "Leave") || MenuUi.Key(KeyboardKey.Escape))
        {
            _app.Go(new MultiplayerScreen(_app));
            return;
        }

        // Create the session after the frame showing "Preparing graphics" was presented (it blocks a moment).
        if (_task.IsCompletedSuccessfully && _session == null && _time > 0.3f)
        {
            if (!_shownFinal) { _shownFinal = true; return; }
            var (sim, hash, map) = _task.Result;
            var start = _game.Start;
            var cfg = NetGameConfig.From(start, _s.PlayerId);
            var rec = NetGameConfig.Recording(start, cfg, sim, _game.LocalArmy, map);
            _session = new GameSession(_app.Db, sim, cfg, _game.LocalArmy, _opt, _app.Audio, rec,
                wrapHooks: inner => _game.CreateHooks(inner));
            _game.AttachRunner(_session.Runner);
            _game.ReportLoaded(hash);
        }
    }

    /// <summary>Returns true if the screen was left.</summary>
    private bool HandleEvents()
    {
        while (_s.TryGetEvent(out var e))
        {
            switch (e)
            {
                case GameGoEvent when _session != null:
                    _handedOff = true;
                    _app.Go(new NetInGameScreen(_app, _s, _game, _session, _opt));
                    return true;
                case StartAbortedEvent a:
                    _session?.Dispose();
                    _session = null;
                    _handedOff = true; // the session is back in its lobby
                    _app.Go(new LobbyScreen(_app, _s, "Start aborted: " + a.Reason));
                    return true;
                case DisconnectedEvent d:
                    _app.Go(new MultiplayerScreen(_app, "Disconnected: " + d.Reason));
                    return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_handedOff) return;
        _session?.Dispose();
        _s.Leave();
    }
}
