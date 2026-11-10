using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Client.Input;
using Rts.Client.Menus;
using Rts.Client.Persistence;
using Rts.Client.Rendering;
using Rts.Client.Sandbox;
using Rts.Client.UI;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.Systems;

namespace Rts.Client;

/// <summary>
/// One running game: the simulation + runner thread and every per-game client object (camera, renderers, input,
/// HUD). Created on the main thread (GPU resources) from a <see cref="Simulation"/> that may have been built on
/// a background thread (<see cref="CreateSimulation"/>). <see cref="Frame"/> is the old in-game frame loop body:
/// update + draw between BeginDrawing/EndDrawing. Overlays (pause menu, end screen) are drawn by
/// <see cref="Menus.InGameScreen"/> on top.
/// To add a per-game client module: field + construct in the constructor + call in <see cref="Frame"/> + dispose.
/// </summary>
public sealed class GameSession : IDisposable
{
    public readonly BlueprintDb Db;
    public readonly Simulation Sim;
    public readonly GameConfig Config;
    public readonly SimRunner Runner;
    public readonly GameContext Ctx;
    public readonly int LocalArmy;
    /// <summary>Replay recording + saving of this game (null when watching a replay).</summary>
    public readonly GameRecording? Recording;
    /// <summary>Replay being watched (read-only session: commands are ignored, the command UI is hidden).</summary>
    public readonly ReplayPlayer? Replay;
    /// <summary>Optional: true if the mouse is over an extra overlay (e.g. replay controls) so the world ignores it.</summary>
    public Func<bool>? MouseOverExtraUi;
    /// <summary>Multiplayer (Menus/NetInGameScreen.cs): called instead of the debug keys (speed and pause are negotiated,
    /// no debug spawns, no fog reveal key).</summary>
    public Action? NetKeys;
    /// <summary>Set by the owner while a text line has the keyboard (multiplayer chat): game hotkeys are blocked.</summary>
    public bool KeyboardCaptured;
    private readonly SnapshotInterpolator _interp;
    private readonly RtsCamera _camera;
    private readonly TerrainRenderer _terrainR;
    private readonly UnitRenderer _unitR;
    private readonly EffectsRenderer _fx;
    private readonly FogRenderer _fog;
    private readonly PropRenderer _props;
    private readonly ShieldRenderer _shields;
    private readonly StrategicHud _stratHud;
    private readonly ExperimentalAlert _expAlert;
    private readonly GameAudio _audio;
    private readonly PerfOverlay _perf;
    private readonly FrameProfiler _prof = new(); // per-stage CPU ms (screenshot perf line, --perf-log)
    private readonly SelectionController _selection;
    private readonly Hud _hud;
    /// <summary>Sandbox panel/console/tools (null outside sandbox mode).</summary>
    private SandboxController? _sandbox; // (6b) also created later: cheat tools opened in a normal game (F9/F10)
    private readonly ClientOptions _opt;
    private readonly OrdersUiDemo? _ordersDemo; // (7b) --ui-formation / --ui-waypoints
    /// <summary>(6b) F9/F10 pressed in a cheats-on game: 1 = open the panel, 2 = the console, once the sim enabled the tools.</summary>
    private int _cheatToolsPending;
    private readonly HashSet<long> _selectedSet = new();
    private bool _disposed;
    private bool _faultLogged; // sim-thread fault written to the crash log once (CrashLog)

    /// <summary>Builds the simulation for a config (pure sim work, safe on a background thread).</summary>
    public static Simulation CreateSimulation(BlueprintDb db, GameConfig cfg, out int localArmy)
    {
        if (cfg.Scene != null) NavalScene.Configure(cfg);
        var settings = cfg.ToSkirmish(out localArmy);
        // AI slots: Army.IsAI + Army.AiDifficulty → driven by the in-sim skirmish AI (Rts.Sim/AI).
        // Sandbox: the other armies are passive unless "Enemy AI" is on (the sandbox panel/console can switch it later).
        if (cfg.Sandbox && !cfg.SandboxAi) settings.AiPlayers = new bool[settings.Players];
        var sim = GameSetup.CreateSkirmish(db, settings);
        if (cfg.Sandbox) SandboxSystem.Enable(sim.World);
        if (cfg.Demo)
        {
            GameApp.SpawnDemo(sim, cfg.Stress);
            FogRenderer.SpawnDemoRadars(sim);
        }
        if (cfg.Scene?.StartsWith("strategic") == true) cfg.SceneCamera = StrategicScene.Setup(sim, cfg.Scene);
        else if (cfg.Scene == NavalScene.Name) NavalScene.Spawn(sim, cfg);
        else if (cfg.Scene?.StartsWith(ExperimentalScene.Name) == true) ExperimentalScene.Setup(sim, cfg);
        else if (cfg.Scene == TransportScene.Name) TransportScene.Spawn(sim, cfg);
        else if (cfg.Scene?.StartsWith(CybranScene.Name) == true) CybranScene.Setup(sim, cfg);
        return sim;
    }

    /// <param name="audio">App-wide audio (null = this game opens its own device from <c>opt.Audio</c>).</param>
    /// <param name="recording">Recording of a loaded game (null = a new recording of this game).</param>
    /// <param name="replay">Watch this replay instead (read-only; <paramref name="sim"/> = its simulation).</param>
    /// <param name="wrapHooks">Multiplayer: wraps the recording hooks (lockstep), before the runner starts.</param>
    public GameSession(BlueprintDb db, Simulation sim, GameConfig cfg, int localArmy, ClientOptions opt, AudioSystem? audio = null,
        GameRecording? recording = null, ReplayPlayer? replay = null, Func<ITickHooks, ITickHooks>? wrapHooks = null)
    {
        Db = db;
        Sim = sim;
        Config = cfg;
        LocalArmy = localArmy;
        var terrain = sim.World.Terrain;
        Runner = new SimRunner(sim);
        // Replays: playback hooks. Games: replay recording (+ saving), see Rts.Client/Persistence.
        if (replay != null) Runner.Hooks = Replay = replay;
        else Runner.Hooks = Recording = recording ?? GameRecording.ForNewGame(cfg, sim, localArmy);
        if (wrapHooks != null) Runner.Hooks = wrapHooks(Runner.Hooks!);
        _interp = new SnapshotInterpolator(Runner);
        _camera = new RtsCamera(terrain) { InputEnabled = opt.ScreenshotPath == null };
        _terrainR = new TerrainRenderer(terrain);
        _unitR = new UnitRenderer(db, terrain);
        UnitRenderer.Gallery = opt.Gallery;
        Ctx = new GameContext
        {
            Db = db, Terrain = terrain, Runner = Runner, Snapshots = _interp, Camera = _camera,
            TerrainRenderer = _terrainR, Feedback = new FeedbackMarkers(), LocalArmy = localArmy,
            ArmyColors = sim.World.Armies.Select(a => ColorUtil.FromRgba(a.Color)).ToArray(),
            ReadOnly = replay != null,
        };
        // Fog of war: the runner filters snapshots for the local (human) army; --nofog / F7 reveal all.
        _fog = new FogRenderer(Ctx, !opt.NoFog && sim.World.Options.Fog != FogMode.None); // (6b) fog option None = no fog
        Ctx.Fog = _fog;
        _fx = new EffectsRenderer(Ctx, opt.FxTest);
        _perf = new PerfOverlay();
        _selection = new SelectionController(Ctx);
        _hud = new Hud(Ctx, _selection);
        _hud.ApplyStartupOptions(opt);
        _ordersDemo = OrdersUiDemo.Create(Ctx, _selection, opt);
        _stratHud = new StrategicHud(Ctx, _selection);
        _props = new PropRenderer(Ctx, _fx.Particles);
        _shields = new ShieldRenderer(Ctx);
        UiSounds.Drain(); // drop clicks queued by the menus
        _audio = audio != null ? new GameAudio(Ctx, audio) : new GameAudio(Ctx, opt.Audio, automated: opt.ScreenshotPath != null);
        _expAlert = new ExperimentalAlert(Ctx, _audio.PlayExperimentalAlert);

        _opt = opt;
        if (cfg.Sandbox) _sandbox = new SandboxController(Ctx, _selection, opt);
        // (6b) F9/F10 outside sandbox open the cheat tools (cheats on) or nothing; the effect debug keys only with --fxtest.
        else EffectsRenderer.DebugKeysEnabled = opt.FxTest;
        // (6b) Locked game speed (option): single player sets it here; multiplayer gets it from the host (NetGame).
        if (replay == null && wrapHooks == null && sim.World.Options.LockedSpeed is float locked) Runner.Speed = locked;

        var start = terrain.StartPositions[localArmy % terrain.StartPositions.Length];
        if (opt.CameraXZD is Vector3 cxz) _camera.LookAt(cxz.X, cxz.Y, cxz.Z);
        else if (cfg.SceneCamera is Vector3 sc) _camera.LookAt(sc.X, sc.Y, sc.Z);
        else _camera.LookAt(start.X, start.Y, 70);

        Runner.Start();
    }

    /// <summary>
    /// (6b) Cheat tools in a normal game: with the Cheats option on, F9 / F10 send <c>EnableCheatToolsCommand</c> and open
    /// the sandbox panel / console once the snapshot shows the sandbox status; cheats off = a toast, nothing else.
    /// </summary>
    private void CheatToolKeys(bool interactive)
    {
        var snap = _interp.Current;
        if (_cheatToolsPending != 0)
        {
            if (snap?.Sandbox == null) return;
            _sandbox = new SandboxController(Ctx, _selection, _opt);
            if (_cheatToolsPending == 1) _sandbox.TogglePanel();
            else _sandbox.ToggleConsole();
            _cheatToolsPending = 0;
            return;
        }
        if (!interactive || Ctx.ReadOnly || KeyboardCaptured || EffectsRenderer.DebugKeysEnabled || snap == null) return;
        bool panel = Raylib.IsKeyPressed(Hotkeys.SandboxPanel.Key), console = Raylib.IsKeyPressed(Hotkeys.SandboxConsole.Keys[0]);
        if (!panel && !console) return;
        if (!snap.Options.Cheats && snap.Sandbox == null)
        {
            Toasts.Show("Cheats are off in this game");
            return;
        }
        _cheatToolsPending = panel ? 1 : 2;
        if (snap.Sandbox == null) Runner.Enqueue(new EnableCheatToolsCommand(LocalArmy));
    }

    public SelectionController Selection => _selection;
    public SnapshotInterpolator Snapshots => _interp;
    public FogRenderer Fog => _fog;
    /// <summary>True in sandbox mode (snapshots carry <c>Sandbox</c> status).</summary>
    public bool IsSandbox => _sandbox != null;

    /// <summary>
    /// True if Esc should open the pause menu: no command mode, no placement, no dialog. With units selected Esc
    /// first clears the selection (returns false then).
    /// </summary>
    public bool HandleEscapeForPause()
    {
        if (!MenuUi.Key(KeyboardKey.Escape)) return false;
        if (_sandbox != null && _sandbox.WantsEscape) return false; // closes the console / cancels the sandbox tool
        if (_hud.HelpOpen) return false; // Esc closes the F1 help (Hud)
        if (_hud.DiplomacyOpen) { _hud.CloseDiplomacy(); return false; } // (6b) Esc closes the F4 diplomacy panel
        var s = _selection;
        if (s.Mode != SelectionController.CommandMode.Normal || s.Placement.Active || s.PendingSelfDestruct != null) return false;
        if (s.Selected.Count > 0)
        {
            s.Selected.Clear();
            return false;
        }
        return true;
    }

    /// <summary>
    /// One frame (call between BeginDrawing/EndDrawing). interactive = false (pause menu / end screen open):
    /// no camera/selection/HUD input and the HUD is hidden; the world keeps rendering.
    /// </summary>
    public void Frame(float dt, bool interactive = true)
    {
        var ctx = Ctx;
        if (!_faultLogged && Runner.Fault is Exception fault) { _faultLogged = true; CrashLog.Record(fault, "sim thread", fatal: false); }
        _perf.BeginFrame();
        _prof.Begin();
        _interp.Update();
        _prof.Lap("interp");
        _fog.Update(_interp, keys: interactive && _sandbox?.TextFocused != true && NetKeys == null && !KeyboardCaptured);
        _prof.Lap("fog");
        bool overUi = interactive && (_hud.MouseOverUi() | MouseOverExtraUi?.Invoke() == true);
        if (_sandbox == null) CheatToolKeys(interactive);
        if (_sandbox != null) overUi |= _sandbox.Update(dt, interactive, overUi);
        if (KeyboardCaptured) _selection.InputBlocked = true;
        if (interactive)
        {
            _camera.Update(dt, overUi, ctx.PickGround);
            _ordersDemo?.Update();
            _selection.Update(overUi);
        }
        _audio.Update(dt, _interp, _selection.Selected.Count, inputBlocked: !interactive || _selection.InputBlocked);
        if (interactive && _sandbox?.TextFocused != true && !ctx.ReadOnly && !KeyboardCaptured)
        {
            if (NetKeys != null) NetKeys();
            else GameApp.DebugKeys(ctx);
        }
        _prof.Lap("input+audio");

        var cur = _interp.Current;
        var prev = _interp.Prev;
        float alpha = _interp.Alpha;
        _selectedSet.Clear();
        foreach (var h in _selection.Selected) _selectedSet.Add(((long)h.Index << 32) | (uint)h.Generation);

        Raylib.ClearBackground(new Color(118, 150, 185, 255));
        Raylib.BeginMode3D(_camera.Camera);
        _terrainR.Draw(_camera.Camera);
        _terrainR.DrawDeposits();
        _prof.Lap("terrain");
        if (_camera.Distance < IconRenderer.HideMeshDistance) _unitR.Draw(cur, prev, alpha, ctx.ArmyColors);
        _prof.Lap("units");
        _props.Draw(cur, dt);
        GameApp.DrawSelection3D(ctx, _selection, cur, prev, alpha);
        OrderMarkers.Draw3D(ctx, _selection); // (7b) waypoint markers, formation drag preview
        TransportOverlay.Draw3D(ctx, _selectedSet, cur, prev, alpha); // ferry routes, cargo/dock lines
        ctx.Feedback.Draw();
        _stratHud.Draw3D(cur);
        TeleportHud.Draw3D(ctx, _selection, cur);
        _prof.Lap("props+sel3d");
        _terrainR.DrawWater();
        if (_camera.Distance < IconRenderer.HideMeshDistance) _unitR.DrawSubmerged(); // subs seen through the water
        if (_camera.Distance < IconRenderer.HideMeshDistance) _unitR.DrawStatus(cur, prev, alpha, ctx.ArmyColors); // cloak/EMP/stealth field
        _prof.Lap("water+subs");
        _fx.Draw(dt, cur, prev, alpha, _interp.NewEffects);
        _shields.Draw(dt, cur, prev, alpha, _interp.NewEffects);
        Raylib.EndMode3D();
        _fx.DrawOverlay(); // nuke screen flash (NukeFx)
        _prof.Lap("effects");

        _props.DrawStrategic(cur);
        IconRenderer.Draw(ctx, cur, prev, alpha, _selectedSet);
        _prof.Lap("icons");
        _fog.DrawBlips(cur, alpha);
        GameApp.DrawHealthBars(ctx, _selectedSet, cur, prev, alpha);
        _shields.DrawBars(cur, prev, alpha, _selectedSet);
        UpgradeOverlay.Draw(ctx, cur, prev, alpha);
        _prof.Lap("overlays2d");
        if (interactive)
        {
            if (_selection.IsDragging)
            {
                var r = _selection.DragRect;
                Raylib.DrawRectangleRec(r, new Color(80, 200, 80, 40));
                Raylib.DrawRectangleLinesEx(r, 1, new Color(120, 255, 120, 220));
            }
            _hud.Draw();
            _prof.Lap("hud");
            _sandbox?.Draw();
            CursorHint.Draw(ctx, _selection, overUi);
            OrderMarkers.Draw2D(ctx, _selection, overUi);
            TeleportHud.Draw2D(ctx, _selection, overUi);
        }
        _prof.Lap("cursor");
        _stratHud.Draw2D(cur, dt);
        _prof.Lap("strathud2d");
        _expAlert.Draw2D(cur, dt);
        _audio.DrawOverlay();
        _prof.Lap("alerts");
        _perf.Draw(_unitR, _fx);
        _prof.End();
        PerfLog.Tick(_prof, _perf, cur);
    }

    /// <summary>Game over for the local player: (true, won) once the game has a winner or the local army is defeated.</summary>
    public (bool over, bool won) Outcome()
    {
        var snap = _interp.Current;
        if ((uint)LocalArmy >= (uint)snap.Armies.Length) return (false, false);
        var me = snap.Armies[LocalArmy];
        if (snap.WinningTeam >= 0) return (true, me.Team == snap.WinningTeam);
        if (me.Defeated) return (true, false);
        return (false, false);
    }

    /// <summary>Line printed after an automated screenshot.</summary>
    public string ScreenshotInfo()
    {
        var cur = _interp.Current;
        var s = $"(tick {cur.Tick}, units {cur.UnitCount}, sim {cur.TickDurationMs:F2} ms)\n{_perf.Summary()}\n{_prof.Summary()}";
        if (Runner.Fault != null) s += "\nSIM FAULT: " + Runner.Fault;
        return s;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Runner.Stop();
        Recording?.Finish(Runner);
        _sandbox?.Dispose();
        _audio.Dispose();
        _hud.Dispose();
        _props.Dispose();
        _shields.Dispose();
        _fx.Dispose();
        _fog.Dispose();
        _unitR.Dispose();
        _terrainR.Dispose();
    }
}
