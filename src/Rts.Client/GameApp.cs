using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Client.Input;
using Rts.Client.Menus;
using Rts.Client.Rendering;
using Rts.Client.UI;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Client;

/// <summary>Which client state the app starts in.</summary>
public enum StartScreen { Game, MainMenu, Setup, Settings, Saves, Replays, LoadFile, ReplayFile, Multiplayer }

public sealed class ClientOptions
{
    public int Seed = 42;
    public int MapSize = 256;
    public int Players = 2;
    public MapPreset Preset = MapPreset.Plains;
    /// <summary>0..1, negative = preset default.</summary>
    public float WaterAmount = -1;
    public bool MapSizeGiven, PresetGiven, PlayersGiven, SizeGiven;
    /// <summary>--faction ID: the local player's faction; --factions a,b,...: every slot in order ("random" allowed).</summary>
    public string? Faction;
    public string[]? Factions;
    /// <summary>--options SPEC: game options (<c>GameOptions.Parse</c>, e.g. "victory=supremacy,cap=500,cheats=off");
    /// null = CLI defaults <c>new GameOptions()</c> (cheats on). Also presets the --setup screen.</summary>
    public string? GameOptions;
    public bool Demo;
    /// <summary>--scene NAME: scripted visual test scene: "strategic[:shields|:tml|:nuke]" (StrategicScene), "naval" (NavalScene), "experimental[:mavor|:fog]" (ExperimentalScene) "transport" (TransportScene), "cybran[:land|:air|:naval|:structures|:experimental]" (CybranScene) or "units8a[:walls|:naval|:salem]" (Units8aScene).</summary>
    public string? Scene;
    public int Width = 1600;
    public int Height = 900;
    /// <summary>
    /// No arguments (or only window flags) → main menu; --menu / --setup / --settings → that screen; any game
    /// argument (--demo, --map, --seed, --screenshot, --ui-demo, --stress, ...) → straight into a game as before.
    /// </summary>
    public StartScreen StartScreen = StartScreen.MainMenu;
    /// <summary>Screenshot helpers: open the pause menu / end screen ~15 frames before the capture.</summary>
    public bool PauseMenu;
    public bool EndScreen;
    /// <summary>Screenshot helper: --help-overlay opens the F1 help overlay at the start of the game.</summary>
    public bool HelpOverlay;
    /// <summary>--diplomacy: open the F4 diplomacy panel at start (screenshots).</summary>
    public bool Diplomacy;
    /// <summary>--teams 1,1,2,2: team per army for CLI games (0 = none/FFA).</summary>
    public int[]? Teams;
    /// <summary>Screenshot helper: the skirmish setup screen starts the game as soon as the preview is ready.</summary>
    public bool AutoStart;
    /// <summary>Debug: scripted menu flow (game → Restart → Quit to Menu → Skirmish → autostart → …) to exercise
    /// screen switches and resource disposal.</summary>
    public bool FlowTest;
    /// <summary>If set: run Frames frames, save a PNG screenshot here and exit (for automated visual checks).</summary>
    public string? ScreenshotPath;
    public int Frames = 120;
    /// <summary>Optional camera override: x, z, distance.</summary>
    public Vector3? CameraXZD;
    /// <summary>UI test helpers (see UI/UiStartup.cs): --select commander|engineer|factory|army, --ui-demo,
    /// --ui-place &lt;blueprintId&gt;, --mouse x,y.</summary>
    public string? Select;
    public bool UiDemo;
    /// <summary>--tech-demo: T2/T3 structures/units and an upgrading factory + mex (UI/TechDemo.cs).</summary>
    public bool TechDemo;
    public string? UiPlace;
    public Vector2? MousePos;
    /// <summary>(7b) Screenshot hooks: --ui-formation x1,z1:x2,z2, --ui-waypoints x,z (Input/OrdersUiDemo.cs).</summary>
    public string? UiFormation, UiWaypoints, UiPings; // (7b) --ui-pings x,z
    /// <summary>Extra mixed units per army spawned in the demo (performance testing).</summary>
    public int Stress;
    /// <summary>Client-side fake projectiles/effects for testing visuals (also toggled with F9).</summary>
    public bool FxTest;
    /// <summary>Debug: render one model of every unit shape near the player start.</summary>
    public bool Gallery;
    public bool NoVsync;
    /// <summary>Audio options: --nosound, --sound (keep audio in screenshot runs), --volume F, --sfx-volume F,
    /// --music-volume F, --nomusic (see Audio/GameAudio.cs).</summary>
    public AudioSettings Audio = new();
    /// <summary>A volume option was given on the command line (overrides the saved settings for this run).</summary>
    public bool VolumeGiven;
    /// <summary>Disable the fog of war (reveal all; also toggled with F7).</summary>
    public bool NoFog;
    /// <summary>--sandbox: start (or set up) a sandbox game (Rts.Client/Sandbox).</summary>
    public bool Sandbox;
    /// <summary>--exec "cmd; cmd": sandbox console script run at the start of the game (implies --sandbox).</summary>
    public string? Exec;
    /// <summary>Save/load and replays (Rts.Client/Persistence): --saves / --replays open the lists, --load FILE / --replay FILE
    /// open a file (--seek TICK: replay start), --save-at N quicksaves at frame N, --save-dialog opens the pause menu's
    /// save dialog (screenshots), --write-replay keeps the replay of a --screenshot run.</summary>
    public string? LoadPath, ReplayPath;
    public int SeekTick = -1, SaveAt = -1;
    public bool SaveDialog, WriteReplay;
    /// <summary>--autosave-every TICKS: autosave interval for tests/screenshots (forces autosave on, also in --screenshot runs).</summary>
    public int AutosaveEvery = -1;
    /// <summary>Multiplayer (Menus/MultiplayerScreen.cs): --host [port] (0 = not hosting), --join address, --name.</summary>
    public int HostPort;
    public string? JoinAddress, PlayerName;

    public ClientOptions Copy() => (ClientOptions)MemberwiseClone();

    public static ClientOptions Parse(string[] args)
    {
        var o = new ClientOptions();
        StartScreen? screen = null;
        bool gameArg = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing value for " + a);
            // Window/audio/fog flags don't force a direct game (they also apply to games started from the menus).
            if (a is not ("--size" or "--novsync" or "--menu" or "--setup" or "--settings" or "--autostart" or "--flow-test"
                or "--nofog" or "--nosound" or "--sound" or "--volume" or "--sfx-volume" or "--music-volume" or "--nomusic" or "--name"
                or "--perf-log" or "--terrain-lod" or "--settings-tab")) gameArg = true;
            switch (a)
            {
                case "--seed": o.Seed = int.Parse(Next()); break;
                case "--map": o.MapSize = int.Parse(Next()); o.MapSizeGiven = true; break;
                case "--players": o.Players = int.Parse(Next()); o.PlayersGiven = true; break;
                case "--faction": o.Faction = Next(); break;
                case "--factions": o.Factions = Next().Split(','); break;
                case "--options":
                    o.GameOptions = Next();
                    try { Rts.Sim.GameOptions.Parse(o.GameOptions); }
                    catch (FormatException e) { throw new ArgumentException(e.Message); }
                    break;
                case "--preset":
                    string pn = Next();
                    if (!MapPresets.TryParse(pn, out o.Preset)) throw new ArgumentException("unknown preset " + pn + " (plains, lakes, highlands, canyon, archipelago)");
                    o.PresetGiven = true;
                    break;
                case "--water": o.WaterAmount = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--menu": screen = StartScreen.MainMenu; break;
                case "--setup": screen = StartScreen.Setup; break;
                case "--settings": screen = StartScreen.Settings; break;
                case "--settings-tab": SettingsScreen.Tab = SettingsScreen.ParseTab(Next()); screen ??= StartScreen.Settings; break;
                case "--pause-menu": o.PauseMenu = true; break;
                case "--autostart": o.AutoStart = true; break;
                case "--flow-test": o.FlowTest = o.AutoStart = true; break;
                case "--endscreen": o.EndScreen = true; break;
                case "--demo": o.Demo = true; break;
                case "--size":
                    var wh = Next().Split('x');
                    o.Width = int.Parse(wh[0]); o.Height = int.Parse(wh[1]);
                    o.SizeGiven = true;
                    break;
                case "--screenshot": o.ScreenshotPath = Path.GetFullPath(Next()); break;
                case "--frames": o.Frames = int.Parse(Next()); break;
                case "--camera":
                    var c = Next().Split(',').Select(float.Parse).ToArray();
                    o.CameraXZD = new Vector3(c[0], c[1], c[2]);
                    break;
                case "--select": o.Select = Next().ToLowerInvariant(); break;
                case "--ui-demo": o.UiDemo = true; break;
                case "--tech-demo": o.TechDemo = true; break;
                case "--scene": o.Scene = Next().ToLowerInvariant(); break;
                case "--ui-place": o.UiPlace = Next(); break;
                case "--ui-formation": o.UiFormation = Next(); break;
                case "--ui-waypoints": o.UiWaypoints = Next(); break;
                case "--ui-pings": o.UiPings = Next(); break;
                case "--mouse":
                    var mp = Next().Split(',').Select(float.Parse).ToArray();
                    o.MousePos = new Vector2(mp[0], mp[1]);
                    break;
                case "--stress": o.Stress = int.Parse(Next()); o.Demo = true; break;
                case "--fxtest": o.FxTest = true; break;
                case "--gallery": o.Gallery = true; break;
                case "--novsync": o.NoVsync = true; break;
                case "--perf-log": Rendering.PerfLog.Enabled = true; break;
                case "--terrain-lod": Rendering.TerrainRenderer.ForceLod = int.Parse(Next()); break;
                case "--nofog": o.NoFog = true; break;
                case "--sandbox": o.Sandbox = true; break;
                case "--exec": o.Exec = Next(); o.Sandbox = true; break;
                case "--debugwater": TerrainRenderer.DebugWaterLevel = float.Parse(Next()); break;
                case "--saves": screen = StartScreen.Saves; break;
                case "--replays": screen = StartScreen.Replays; break;
                case "--load": o.LoadPath = Next(); screen = StartScreen.LoadFile; break;
                case "--replay": o.ReplayPath = Next(); screen = StartScreen.ReplayFile; break;
                case "--seek": o.SeekTick = int.Parse(Next()); break;
                case "--save-at": o.SaveAt = int.Parse(Next()); break;
                case "--save-dialog": o.SaveDialog = o.PauseMenu = true; break;
                case "--write-replay": o.WriteReplay = true; break;
                case "--autosave-every": o.AutosaveEvery = int.Parse(Next()); break;
                case "--host":
                    o.HostPort = i + 1 < args.Length && int.TryParse(args[i + 1], out int hp) ? hp : Rts.Net.Session.NetSession.DefaultPort;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out _)) i++;
                    screen = StartScreen.Multiplayer;
                    break;
                case "--join": o.JoinAddress = Next(); screen = StartScreen.Multiplayer; break;
                case "--name": o.PlayerName = Next(); break;
                case "--multiplayer": screen = StartScreen.Multiplayer; break;
                case "--help-overlay": o.HelpOverlay = true; gameArg = true; break;
                case "--diplomacy": o.Diplomacy = true; gameArg = true; break;
                case "--teams": o.Teams = Next().Split(',').Select(int.Parse).ToArray(); gameArg = true; break;
                default:
                    if (AudioSettings.ParseArg(a, Next, o.Audio))
                    {
                        if (a is "--volume" or "--sfx-volume" or "--music-volume" or "--nomusic") o.VolumeGiven = true;
                        break;
                    }
                    throw new ArgumentException("unknown argument " + a);
            }
        }
        o.StartScreen = screen ?? (gameArg ? StartScreen.Game : StartScreen.MainMenu);
        if (o.Scene == NavalScene.Name) NavalScene.ApplyDefaults(o);
        else if (o.Scene?.StartsWith(ExperimentalScene.Name) == true) ExperimentalScene.ApplyDefaults(o);
        else if (o.Scene == TransportScene.Name) TransportScene.ApplyDefaults(o);
        else if (o.Scene?.StartsWith(CybranScene.Name) == true) CybranScene.ApplyDefaults(o);
        else if (o.Scene?.StartsWith(Units8aScene.Name) == true) Units8aScene.ApplyDefaults(o);
        return o;
    }
}

/// <summary>
/// Application shell: window, settings and the screen loop (<see cref="AppHost"/>: main menu → skirmish setup →
/// loading → in-game with pause menu and end screen). The in-game frame lives in <see cref="GameSession.Frame"/>.
/// Command-line game arguments still go straight into a game (automated screenshots rely on it).
/// </summary>
public static class GameApp
{
    public static void Run(ClientOptions opt)
    {
        AppSettings.Persist = opt.ScreenshotPath == null;
        MenuUi.Automated = opt.ScreenshotPath != null;
        Persistence.GameRecording.AutoWriteReplays = opt.ScreenshotPath == null || opt.WriteReplay;
        var settings = AppSettings.Load();
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        SimWarmup.Start(db); // JIT warm-up of the sim on a background thread while the menu/loading shows (wave 5m)

        // Direct game (CLI): the simulation is created before the window, exactly as before the menus existed.
        GameConfig? directCfg = null;
        Simulation? directSim = null;
        int directLocal = 0;
        if (opt.StartScreen == StartScreen.Game)
        {
            directCfg = GameConfig.FromOptions(opt);
            if (directCfg.FactionProblem(db.Factions) is string bad) throw new ArgumentException(bad);
            directSim = GameSession.CreateSimulation(db, directCfg, out directLocal);
        }

        int width = opt.SizeGiven || opt.ScreenshotPath != null ? opt.Width : settings.Width;
        int height = opt.SizeGiven || opt.ScreenshotPath != null ? opt.Height : settings.Height;
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.ResizableWindow | (opt.NoVsync ? 0 : ConfigFlags.VSyncHint));
        Rendering.FrameProfiler.GpuEnabled = opt.ScreenshotPath != null || Rendering.PerfLog.Enabled; // GPU stage timings
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(width, height, "RTS — Supreme Commander-like");
        AppIcon.SetWindowIcon();
        GraphicsSettings.Init(settings, opt); // shadows, particles, terrain detail, UI scale, VSync, FPS cap, camera (CLI wins)
        Raylib.SetExitKey(KeyboardKey.Null);
        Rlgl.SetClipPlanes(0.5, 6000);
        if (opt.ScreenshotPath == null && settings.Fullscreen) settings.ApplyFullscreen();
        // Saved volumes, unless given on the command line; one audio device for the whole app (menus + games).
        if (!opt.VolumeGiven) settings.ApplyVolume(opt.Audio);
        var audio = new AudioSystem(opt.Audio, automated: opt.ScreenshotPath != null);

        var app = new AppHost(db, opt, audio);
        app.Current = opt.StartScreen switch
        {
            StartScreen.Game => new InGameScreen(app, new GameSession(db, directSim!, directCfg!, directLocal, opt, audio), opt),
            StartScreen.Setup => new SkirmishSetupScreen(app, opt.Sandbox),
            StartScreen.Settings => new SettingsScreen(app),
            StartScreen.Saves => new SaveListScreen(app, replays: false),
            StartScreen.Replays => new SaveListScreen(app, replays: true),
            StartScreen.LoadFile => new FileLoadingScreen(app, opt.LoadPath!, replay: false, opt),
            StartScreen.ReplayFile => new FileLoadingScreen(app, opt.ReplayPath!, replay: true, opt),
            StartScreen.Multiplayer => new MultiplayerScreen(app),
            _ => new MainMenuScreen(app),
        };

        int frame = 0;
        while (!Raylib.WindowShouldClose() && !app.QuitRequested)
        {
            float dt = Raylib.GetFrameTime();
            Raylib.BeginDrawing();
            app.Current.Frame(dt);
            Raylib.EndDrawing();
            if (app.Current is not (InGameScreen or NetInGameScreen)) audio.MenuUpdate(dt); // in game GameAudio drives the device

            frame++;
            if (opt.ScreenshotPath != null && frame >= opt.Frames)
            {
                var img = Raylib.LoadImageFromScreen();
                Raylib.ExportImage(img, opt.ScreenshotPath);
                Raylib.UnloadImage(img);
                Console.WriteLine($"Screenshot saved: {opt.ScreenshotPath} {app.Current.ScreenshotInfo}");
                break;
            }
            app.EndFrame();
        }
        app.Dispose();
        Persistence.GameRecording.WaitForPendingWrites(TimeSpan.FromSeconds(15)); // replay/save files still being written
        audio.Dispose();
        Raylib.CloseWindow();
    }

    /// <summary>Demo battle: two groups of mixed T1 units per army near each base, attack-moving at each other.</summary>
    public static void SpawnDemo(Simulation sim, int stress = 0)
    {
        var w = sim.World;
        var db = w.Blueprints;
        string[] kinds = ["uef_t1_tank", "uef_t1_tank", "uef_t1_artillery", "uef_t1_mobile_aa", "uef_t1_scout"];
        var center = new Vector2(w.Terrain.Width * 0.5f, w.Terrain.Height * 0.5f);
        var cmds = new List<Command>();
        for (int a = 0; a < w.Armies.Length; a++)
        {
            var sp = w.Terrain.StartPositions[a];
            var dir = Vector2.Normalize(center - sp);
            var right = new Vector2(-dir.Y, dir.X);
            var units = new List<UnitHandle>();
            for (int k = 0; k < 60; k++)
            {
                var p = sp + dir * (14 + (k / 12) * 2.2f) + right * ((k % 12) - 5.5f) * 2.2f;
                var h = w.SpawnUnit(db.IndexOf(kinds[k % kinds.Length]), a, p, DMath.HeadingOf(dir));
                if (!h.IsNone) units.Add(h);
            }
            cmds.Add(new IssueOrderCommand(a, units.ToArray(), Order.AttackMoveTo(center), false));
            if (stress > 0) cmds.Add(SpawnStress(w, a, sp, dir, right, center, stress));
        }
        sim.Step(cmds);
    }

    /// <summary>--stress N: N extra mixed mobile units for army a in a grid in front of its base (perf testing).</summary>
    private static Command SpawnStress(SimWorld w, int a, Vector2 sp, Vector2 dir, Vector2 right, Vector2 center, int n)
    {
        var db = w.Blueprints;
        var kinds = db.All.Where(b => b.IsMobile && !b.Has(UnitCategory.Commander)).Select(b => b.Index).ToArray();
        int side = (int)MathF.Ceiling(MathF.Sqrt(n));
        var units = new List<UnitHandle>(n);
        for (int k = 0; k < n; k++)
        {
            var p = w.Terrain.Clamp(sp + dir * (24 + (k / side) * 1.9f) + right * ((k % side) - side * 0.5f) * 1.9f, 2f);
            var h = w.SpawnUnit(kinds[k % kinds.Length], a, p, DMath.HeadingOf(dir));
            if (!h.IsNone) units.Add(h);
        }
        return new IssueOrderCommand(a, units.ToArray(), Order.AttackMoveTo(center), false);
    }

    public static void DebugKeys(GameContext ctx)
    {
        if (Input.Hotkeys.PauseGame.Pressed()) ctx.Runner.Paused = !ctx.Runner.Paused;
        // (6b) Game options: a locked speed ignores + / - (pause still works); debug spawns only with cheats (or sandbox).
        var snap = ctx.Snapshots.Current;
        bool up = Input.Hotkeys.SpeedUp.Pressed(), down = Input.Hotkeys.SpeedDown.Pressed();
        if ((up || down) && snap?.Options.LockedSpeed is float locked) Persistence.Toasts.Show($"Game speed is locked at {locked:0.#}x");
        else
        {
            if (up) ctx.Runner.Speed = MathF.Min(8f, ctx.Runner.Speed * 2f);
            if (down) ctx.Runner.Speed = MathF.Max(0.125f, ctx.Runner.Speed * 0.5f);
        }
        // F1 = help overlay (UI/HelpOverlay); the debug tank spawns are Ctrl+F1 / Ctrl+F2.
        bool f1 = Input.Hotkeys.DebugSpawnOwn.Pressed(), f2 = Input.Hotkeys.DebugSpawnEnemy.Pressed();
        if ((f1 || f2) && (snap?.Options.Cheats == true || snap?.Sandbox != null) && ctx.PickGround(Raylib.GetMousePosition()) is Vector3 g)
        {
            int army = f1 ? ctx.LocalArmy : (ctx.LocalArmy + 1) % ctx.ArmyColors.Length;
            ctx.Runner.Enqueue(new DebugSpawnCommand(army, ctx.Db.IndexOf("uef_t1_tank"), new Vector2(g.X, g.Z), 10));
        }
    }

    public static void DrawSelection3D(GameContext ctx, SelectionController sel, Rts.Sim.Snapshot.SimSnapshot cur, Rts.Sim.Snapshot.SimSnapshot? prev, float alpha)
    {
        foreach (var h in sel.Selected)
        {
            if (!cur.TryGetUnit(h.Index, h.Generation, out var v)) continue;
            var bp = ctx.Db[v.Bp];
            var (pos, _) = UnitRenderer.Interpolate(v, prev, alpha);
            float r = MathF.Max(bp.Radius, MathF.Max(bp.Visual.Size.X, bp.Visual.Size.Z) * 0.6f) + 0.3f;
            Raylib.DrawCircle3D(pos + new Vector3(0, 0.15f, 0), r, Vector3.UnitX, 90, new Color(80, 255, 80, 255));
            // Queued orders as a polyline.
            var last = pos;
            for (int o = 0; o < v.OrderCount; o++)
            {
                var ov = cur.Orders[v.OrderStart + o];
                var c = ov.Type switch
                {
                    OrderType.Move => new Color(60, 220, 60, 200),
                    OrderType.AttackMove or OrderType.Attack => new Color(230, 60, 50, 200),
                    OrderType.Patrol => new Color(80, 170, 255, 200),
                    OrderType.Teleport => new Color(110, 225, 255, 200),
                    OrderType.Build or OrderType.Repair => new Color(240, 220, 60, 200),
                    _ => new Color(200, 200, 200, 200),
                };
                var p = ov.Pos + new Vector3(0, 0.3f, 0);
                Raylib.DrawLine3D(last + new Vector3(0, 0.3f, 0), p, c);
                last = p;
            }
        }
    }

    public static void DrawHealthBars(GameContext ctx, HashSet<long> selected, Rts.Sim.Snapshot.SimSnapshot cur, Rts.Sim.Snapshot.SimSnapshot? prev, float alpha)
    {
        if (ctx.Camera.Distance > IconRenderer.IconDistance) return;
        for (int k = 0; k < cur.UnitCount; k++)
        {
            ref var v = ref cur.Units[k];
            bool sel = selected.Contains(((long)v.Index << 32) | (uint)v.Gen);
            bool building = (v.Flags & UnitFlags.UnderConstruction) != 0;
            if (!sel && v.HealthFrac >= 0.999f && !building) continue;
            var bp = ctx.Db[v.Bp];
            var (pos, _) = UnitRenderer.Interpolate(v, prev, alpha);
            var sp = Raylib.GetWorldToScreen(pos + new Vector3(0, bp.Visual.Size.Y + 1.0f, 0), ctx.Camera.Camera);
            float w = Math.Clamp(bp.Visual.Size.X * 2400f / ctx.Camera.Distance / 40f, 16f, 60f);
            float f = building ? v.BuildFrac : v.HealthFrac;
            var col = building ? new Color(90, 170, 255, 255) : f > 0.5f ? new Color(70, 220, 70, 255) : f > 0.25f ? new Color(240, 200, 40, 255) : new Color(230, 50, 40, 255);
            Raylib.DrawRectangle((int)(sp.X - w / 2) - 1, (int)sp.Y - 1, (int)w + 2, 5, Color.Black);
            Raylib.DrawRectangle((int)(sp.X - w / 2), (int)sp.Y, (int)(w * f), 3, col);
            if (sel && !building) TransportOverlay.DrawFuelSliver(sp.X - w / 2, sp.Y, w, v.Fuel); // aircraft fuel
        }
    }
}
