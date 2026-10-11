using System.Numerics;
using Raylib_cs;
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
public enum StartScreen { Game, MainMenu, Setup, Settings }

public sealed class ClientOptions
{
    public int Seed = 42;
    public int MapSize = 256;
    public int Players = 2;
    public MapPreset Preset = MapPreset.Plains;
    /// <summary>0..1, negative = preset default.</summary>
    public float WaterAmount = -1;
    public bool MapSizeGiven, PresetGiven, PlayersGiven, SizeGiven;
    public bool Demo;
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
    public string? UiPlace;
    public Vector2? MousePos;
    /// <summary>Extra mixed units per army spawned in the demo (performance testing).</summary>
    public int Stress;
    /// <summary>Client-side fake projectiles/effects for testing visuals (also toggled with F9).</summary>
    public bool FxTest;
    /// <summary>Debug: render one model of every unit shape near the player start.</summary>
    public bool Gallery;
    public bool NoVsync;

    public static ClientOptions Parse(string[] args)
    {
        var o = new ClientOptions();
        StartScreen? screen = null;
        bool gameArg = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing value for " + a);
            if (a is not ("--size" or "--novsync" or "--menu" or "--setup" or "--settings" or "--autostart" or "--flow-test")) gameArg = true;
            switch (a)
            {
                case "--seed": o.Seed = int.Parse(Next()); break;
                case "--map": o.MapSize = int.Parse(Next()); o.MapSizeGiven = true; break;
                case "--players": o.Players = int.Parse(Next()); o.PlayersGiven = true; break;
                case "--preset":
                    string pn = Next();
                    if (!MapPresets.TryParse(pn, out o.Preset)) throw new ArgumentException("unknown preset " + pn + " (plains, lakes, highlands, canyon, archipelago)");
                    o.PresetGiven = true;
                    break;
                case "--water": o.WaterAmount = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--menu": screen = StartScreen.MainMenu; break;
                case "--setup": screen = StartScreen.Setup; break;
                case "--settings": screen = StartScreen.Settings; break;
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
                case "--ui-place": o.UiPlace = Next(); break;
                case "--mouse":
                    var mp = Next().Split(',').Select(float.Parse).ToArray();
                    o.MousePos = new Vector2(mp[0], mp[1]);
                    break;
                case "--stress": o.Stress = int.Parse(Next()); o.Demo = true; break;
                case "--fxtest": o.FxTest = true; break;
                case "--gallery": o.Gallery = true; break;
                case "--novsync": o.NoVsync = true; break;
                case "--debugwater": TerrainRenderer.DebugWaterLevel = float.Parse(Next()); break;
                default: throw new ArgumentException("unknown argument " + a);
            }
        }
        o.StartScreen = screen ?? (gameArg ? StartScreen.Game : StartScreen.MainMenu);
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
        var settings = AppSettings.Load();
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

        // Direct game (CLI): the simulation is created before the window, exactly as before the menus existed.
        GameConfig? directCfg = null;
        Simulation? directSim = null;
        int directLocal = 0;
        if (opt.StartScreen == StartScreen.Game)
        {
            directCfg = GameConfig.FromOptions(opt);
            directSim = GameSession.CreateSimulation(db, directCfg, out directLocal);
        }

        int width = opt.SizeGiven || opt.ScreenshotPath != null ? opt.Width : settings.Width;
        int height = opt.SizeGiven || opt.ScreenshotPath != null ? opt.Height : settings.Height;
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.ResizableWindow | (opt.NoVsync ? 0 : ConfigFlags.VSyncHint));
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(width, height, "RTS — Supreme Commander-like");
        Raylib.SetExitKey(KeyboardKey.Null);
        Rlgl.SetClipPlanes(0.5, 6000);
        if (opt.ScreenshotPath == null && settings.Fullscreen) settings.ApplyFullscreen();
        settings.ApplyVolume();

        var app = new AppHost(db, opt);
        app.Current = opt.StartScreen switch
        {
            StartScreen.Game => new InGameScreen(app, new GameSession(db, directSim!, directCfg!, directLocal, opt), opt),
            StartScreen.Setup => new SkirmishSetupScreen(app),
            StartScreen.Settings => new SettingsScreen(app),
            _ => new MainMenuScreen(app),
        };

        int frame = 0;
        while (!Raylib.WindowShouldClose() && !app.QuitRequested)
        {
            float dt = Raylib.GetFrameTime();
            Raylib.BeginDrawing();
            app.Current.Frame(dt);
            Raylib.EndDrawing();

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
        if (Raylib.IsKeyPressed(KeyboardKey.Space)) ctx.Runner.Paused = !ctx.Runner.Paused;
        if (Raylib.IsKeyPressed(KeyboardKey.Equal) || Raylib.IsKeyPressed(KeyboardKey.KpAdd)) ctx.Runner.Speed = MathF.Min(8f, ctx.Runner.Speed * 2f);
        if (Raylib.IsKeyPressed(KeyboardKey.Minus) || Raylib.IsKeyPressed(KeyboardKey.KpSubtract)) ctx.Runner.Speed = MathF.Max(0.125f, ctx.Runner.Speed * 0.5f);
        bool f1 = Raylib.IsKeyPressed(KeyboardKey.F1), f2 = Raylib.IsKeyPressed(KeyboardKey.F2);
        if ((f1 || f2) && ctx.PickGround(Raylib.GetMousePosition()) is Vector3 g)
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
        }
    }
}
