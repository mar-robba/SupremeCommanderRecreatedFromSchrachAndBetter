using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Client.Input;
using Rts.Client.Rendering;
using Rts.Client.UI;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Client;

public sealed class ClientOptions
{
    public int Seed = 42;
    public int MapSize = 256;
    public int Players = 2;
    public bool Demo;
    public int Width = 1600;
    public int Height = 900;
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
    /// <summary>Audio options: --nosound, --sound (keep audio in screenshot runs), --volume F, --sfx-volume F,
    /// --music-volume F, --nomusic (see Audio/GameAudio.cs).</summary>
    public AudioSettings Audio = new();

    public static ClientOptions Parse(string[] args)
    {
        var o = new ClientOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing value for " + a);
            switch (a)
            {
                case "--seed": o.Seed = int.Parse(Next()); break;
                case "--map": o.MapSize = int.Parse(Next()); break;
                case "--players": o.Players = int.Parse(Next()); break;
                case "--demo": o.Demo = true; break;
                case "--size":
                    var wh = Next().Split('x');
                    o.Width = int.Parse(wh[0]); o.Height = int.Parse(wh[1]);
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
                default:
                    if (AudioSettings.ParseArg(a, Next, o.Audio)) break;
                    throw new ArgumentException("unknown argument " + a);
            }
        }
        return o;
    }
}

public static class GameApp
{
    public static void Run(ClientOptions opt)
    {
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        var sim = GameSetup.CreateSkirmish(db, new SkirmishSettings
        {
            Seed = opt.Seed, MapSize = opt.MapSize, Players = opt.Players,
            AiPlayers = Enumerable.Range(0, opt.Players).Select(i => i > 0).ToArray(),
        });
        if (opt.Demo) SpawnDemo(sim, opt.Stress);

        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.ResizableWindow | (opt.NoVsync ? 0 : ConfigFlags.VSyncHint));
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(opt.Width, opt.Height, "RTS — Supreme Commander-like");
        Raylib.SetExitKey(KeyboardKey.Null);
        Rlgl.SetClipPlanes(0.5, 6000);

        var terrain = sim.World.Terrain;
        using var runner = new SimRunner(sim);
        var interp = new SnapshotInterpolator(runner);
        var camera = new RtsCamera(terrain) { InputEnabled = opt.ScreenshotPath == null };
        using var terrainR = new TerrainRenderer(terrain);
        using var unitR = new UnitRenderer(db, terrain);
        UnitRenderer.Gallery = opt.Gallery;
        var ctx = new GameContext
        {
            Db = db, Terrain = terrain, Runner = runner, Snapshots = interp, Camera = camera,
            TerrainRenderer = terrainR, Feedback = new FeedbackMarkers(), LocalArmy = 0,
            ArmyColors = sim.World.Armies.Select(a => ColorUtil.FromRgba(a.Color)).ToArray(),
        };
        using var fx = new EffectsRenderer(ctx, opt.FxTest);
        var perf = new PerfOverlay();
        var selection = new SelectionController(ctx);
        var hud = new Hud(ctx, selection);
        hud.ApplyStartupOptions(opt);
        using var props = new PropRenderer(ctx, fx.Particles);
        using var audio = new GameAudio(ctx, opt.Audio, automated: opt.ScreenshotPath != null);

        var start = terrain.StartPositions[0];
        if (opt.CameraXZD is Vector3 cxz) camera.LookAt(cxz.X, cxz.Y, cxz.Z);
        else camera.LookAt(start.X, start.Y, 70);

        runner.Start();
        int frame = 0;
        var selectedSet = new HashSet<long>();
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();
            perf.BeginFrame();
            interp.Update();
            bool overUi = hud.MouseOverUi();
            camera.Update(dt, overUi, ctx.PickGround);
            selection.Update(overUi);
            audio.Update(dt, interp, selection.Selected.Count, selection.InputBlocked);
            DebugKeys(ctx);

            var cur = interp.Current;
            var prev = interp.Prev;
            float alpha = interp.Alpha;
            selectedSet.Clear();
            foreach (var h in selection.Selected) selectedSet.Add(((long)h.Index << 32) | (uint)h.Generation);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(118, 150, 185, 255));
            Raylib.BeginMode3D(camera.Camera);
            terrainR.Draw(camera.Camera);
            terrainR.DrawDeposits();
            if (camera.Distance < IconRenderer.HideMeshDistance) unitR.Draw(cur, prev, alpha, ctx.ArmyColors);
            props.Draw(cur, dt);
            DrawSelection3D(ctx, selection, cur, prev, alpha);
            ctx.Feedback.Draw();
            terrainR.DrawWater();
            fx.Draw(dt, cur, prev, alpha, interp.NewEffects);
            Raylib.EndMode3D();

            props.DrawStrategic(cur);
            IconRenderer.Draw(ctx, cur, prev, alpha, selectedSet);
            DrawHealthBars(ctx, selectedSet, cur, prev, alpha);
            if (selection.IsDragging)
            {
                var r = selection.DragRect;
                Raylib.DrawRectangleRec(r, new Color(80, 200, 80, 40));
                Raylib.DrawRectangleLinesEx(r, 1, new Color(120, 255, 120, 220));
            }
            hud.Draw();
            CursorHint.Draw(ctx, selection, overUi);
            audio.DrawOverlay();
            perf.Draw(unitR, fx);
            Raylib.EndDrawing();

            frame++;
            if (opt.ScreenshotPath != null && frame >= opt.Frames)
            {
                var img = Raylib.LoadImageFromScreen();
                Raylib.ExportImage(img, opt.ScreenshotPath);
                Raylib.UnloadImage(img);
                Console.WriteLine($"Screenshot saved: {opt.ScreenshotPath} (tick {cur.Tick}, units {cur.UnitCount}, sim {cur.TickDurationMs:F2} ms)");
                Console.WriteLine(perf.Summary());
                if (runner.Fault != null) Console.WriteLine("SIM FAULT: " + runner.Fault);
                break;
            }
        }
        runner.Stop();
        Raylib.CloseWindow();
    }

    /// <summary>Demo battle: two groups of mixed T1 units per army near each base, attack-moving at each other.</summary>
    private static void SpawnDemo(Simulation sim, int stress = 0)
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

    private static void DebugKeys(GameContext ctx)
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

    private static void DrawSelection3D(GameContext ctx, SelectionController sel, Rts.Sim.Snapshot.SimSnapshot cur, Rts.Sim.Snapshot.SimSnapshot? prev, float alpha)
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

    private static void DrawHealthBars(GameContext ctx, HashSet<long> selected, Rts.Sim.Snapshot.SimSnapshot cur, Rts.Sim.Snapshot.SimSnapshot? prev, float alpha)
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
