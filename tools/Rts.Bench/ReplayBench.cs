using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;

/// <summary>
/// Save/replay tools:
///   replay &lt;file.rtsreplay&gt; [threads]   re-simulates a replay headless and checks every recorded state hash.
///                                          Exit code 0 = in sync, 1 = desync, 2 = cannot open.
///   savebench [minutes] [mapSize] [players] [preset] [seed] [extraUnitsPerArmy] [threads]
///                                          records an AI match (Hard AIs), optionally adds a big sandbox army per player,
///                                          then measures save/load (cold + warm), file sizes and the recording cost per
///                                          tick, and writes the replay + save so `replay` can verify them.
/// </summary>
public static class ReplayBench
{
    public static int Verify(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("usage: Rts.Bench replay <file.rtsreplay> [threads]"); return 2; }
        if (args.Length > 1 && int.Parse(args[1]) > 0) Par.MaxThreads = int.Parse(args[1]);
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        var sw = Stopwatch.StartNew();
        ReplayPlayer player;
        try { player = SaveGame.OpenReplay(args[0], db); }
        catch (Exception e) when (e is SaveFormatException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"cannot open replay: {e.Message}");
            return 2;
        }
        var info = player.Info;
        Console.WriteLine($"replay {Path.GetFileName(args[0])}: '{info.Name}', {info.MapDescription}, {info.PlayersDescription}" +
                          $"{(info.Sandbox ? ", sandbox" : "")}; ticks {player.StartTick}..{player.EndTick} ({Fmt(player.EndTick - player.StartTick)}), " +
                          $"{player.Log.CommandCount} commands, {player.Log.Hashes.Count} hashes; opened in {sw.Elapsed.TotalMilliseconds:F0} ms; threads {Par.MaxThreads}");
        if (player.DifferentBuild)
            Console.WriteLine($"WARNING: recorded by a different build ({info.SimBuild}, this is {GameInfo.CurrentSimBuild}): a desync is expected if the sim code changed");

        player.CheckpointInterval = 0; // pure re-simulation (the viewer keeps seek checkpoints)
        sw.Restart();
        var progress = Stopwatch.StartNew();
        while (!player.AtEnd && player.DesyncTick < 0)
        {
            player.StepOne();
            if (progress.Elapsed.TotalSeconds >= 5)
            {
                progress.Restart();
                Console.WriteLine($"  [{Fmt(player.Tick)}] {player.HashesVerified} hashes ok, {player.Sim.World.Units.AliveCount} units");
            }
        }
        double secs = sw.Elapsed.TotalSeconds;
        int ticks = player.Tick - player.StartTick;
        Console.WriteLine($"re-simulated {ticks} ticks in {secs:F1} s ({ticks / Math.Max(secs, 1e-9):F0} ticks/s, {ticks / 10.0 / Math.Max(secs, 1e-9):F0}x real time); " +
                          $"{player.HashesVerified}/{player.Log.Hashes.Count} hashes verified; final hash {player.Sim.StateHash():X16}");
        if (player.DesyncTick >= 0)
        {
            Console.WriteLine($"DESYNC at tick {player.DesyncTick} ({Fmt(player.DesyncTick)})");
            return 1;
        }
        Console.WriteLine("OK: replay in sync");
        return 0;
    }

    public static void SaveBench(string[] args)
    {
        var ci = CultureInfo.InvariantCulture;
        float minutes = args.Length > 0 ? float.Parse(args[0], ci) : 25;
        int mapSize = args.Length > 1 ? int.Parse(args[1]) : 512;
        int players = args.Length > 2 ? int.Parse(args[2]) : 4;
        var preset = MapPreset.Plains;
        if (args.Length > 3 && !MapPresets.TryParse(args[3], out preset)) throw new ArgumentException("unknown preset " + args[3]);
        int seed = args.Length > 4 ? int.Parse(args[4]) : 42;
        int extra = args.Length > 5 ? int.Parse(args[5]) : 0;
        if (args.Length > 6 && int.Parse(args[6]) > 0) Par.MaxThreads = int.Parse(args[6]);

        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        var settings = new SkirmishSettings
        {
            MapSize = mapSize, Seed = seed, Players = players, MapPreset = preset,
            UnitCapacity = Math.Max(16384, players * extra + 8000),
            AiPlayers = Enumerable.Repeat(true, players).ToArray(),
            AiDifficulties = Enumerable.Repeat(AiDifficulty.Hard, players).ToArray(),
        };
        var sim = GameSetup.CreateSkirmish(db, settings);
        var w = sim.World;
        if (extra > 0) Rts.Sim.Systems.SandboxSystem.Enable(w);
        var info = GameInfo.ForSkirmish(settings, sim, localArmy: 0, sandbox: extra > 0);
        info.Name = "savebench";
        var recorder = new ReplayRecorder();
        Console.WriteLine($"savebench: map {mapSize} {MapPresets.Name(preset)}, seed {seed}, {players} Hard AIs, {minutes} min, extra units/army {extra}, threads {Par.MaxThreads}");

        var none = new List<Command>();
        int maxTicks = (int)(minutes * 60 * SimWorld.TicksPerSecond);
        double simMs = 0, hookMs = 0, firstHookMs = 0, hashMs = 0;
        int hashCount = 0;
        var wall = Stopwatch.StartNew();
        var sw = new Stopwatch();
        for (int t = 0; t < maxTicks && w.WinningTeam < 0; t++)
        {
            var cmds = none;
            if (t == 0 && extra > 0) cmds = SpawnArmies(sim, players, extra);
            sw.Restart();
            var run = recorder.BeforeStep(sim, cmds);
            if (t == 0) { firstHookMs = sw.Elapsed.TotalMilliseconds; sw.Restart(); }
            double before = sw.Elapsed.TotalMilliseconds;
            sim.Step(run);
            simMs += sim.LastTickMs;
            sw.Restart();
            bool hashTick = w.Tick % ReplayLog.HashInterval == 0;
            recorder.AfterStep(sim);
            double after = sw.Elapsed.TotalMilliseconds;
            hookMs += before + after;
            if (hashTick) { hashMs += after; hashCount++; }
            w.Events.Effects.Clear(); // cosmetic, normally drained by the snapshot writer
            if (w.Tick % 3000 == 0)
                Console.WriteLine($"  [{Fmt(w.Tick)}] units {w.Units.AliveCount}, projectiles {w.Projectiles.AliveCount}, wall {wall.Elapsed.TotalSeconds:F0} s");
        }
        int ticks = w.Tick;
        Console.WriteLine($"game: {Fmt(ticks)} ({ticks} ticks){(w.WinningTeam >= 0 ? $", team {w.WinningTeam} won" : "")}; units {w.Units.AliveCount}, projectiles {w.Projectiles.AliveCount}");
        Console.WriteLine($"recording cost: initial capture {firstHookMs:F0} ms once; then avg {(hookMs) / Math.Max(1, ticks):F3} ms/tick " +
                          $"(state hash every {ReplayLog.HashInterval} ticks: {hashMs / Math.Max(1, hashCount):F2} ms each) vs sim avg {simMs / Math.Max(1, ticks):F2} ms/tick");

        // Save: capture (sim thread) + encode (any thread), cold and warm.
        SaveGame.Captured cap = null!;
        byte[] file = [];
        var capMs = new List<double>();
        var encMs = new List<double>();
        for (int i = 0; i < 4; i++)
        {
            sw.Restart();
            cap = SaveGame.Capture(sim, info, recorder, "savebench");
            capMs.Add(sw.Elapsed.TotalMilliseconds);
            sw.Restart();
            file = SaveGame.Encode(cap);
            encMs.Add(sw.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine($"save: raw state {cap.State.Length / 1048576.0:F1} MB (+ replay initial state {cap.Replay!.InitialState.Length / 1048576.0:F1} MB), " +
                          $"file {file.Length / 1024.0:F0} KB; capture cold {capMs[0]:F0} ms, warm {capMs.Skip(1).Average():F0} ms (on the sim thread: game pause); " +
                          $"encode cold {encMs[0]:F0} ms, warm {encMs.Skip(1).Average():F0} ms (background)");

        var loadMs = new List<double>();
        LoadedGame loaded = null!;
        for (int i = 0; i < 4; i++)
        {
            sw.Restart();
            loaded = SaveGame.Decode(file, db);
            loadMs.Add(sw.Elapsed.TotalMilliseconds);
        }
        bool same = loaded.Sim.StateHash() == sim.StateHash();
        Console.WriteLine($"load: cold {loadMs[0]:F0} ms, warm {loadMs.Skip(1).Average():F0} ms; state hash {(same ? "identical" : "DIFFERENT")}");

        sw.Restart();
        var log = recorder.Snapshot();
        var replay = SaveGame.EncodeReplay(info.Clone(), log);
        double replayMs = sw.Elapsed.TotalMilliseconds;
        string replayPath = SaveStore.WriteReplay(replay, info);
        string savePath = SaveStore.WriteSave(file, "savebench");
        Console.WriteLine($"replay: {replay.Length / 1024.0:F0} KB ({log.CommandCount} commands, {log.Hashes.Count} hashes), encoded in {replayMs:F0} ms -> {Path.GetFullPath(replayPath)}");
        Console.WriteLine($"save written -> {Path.GetFullPath(savePath)}");
        Console.WriteLine($"verify with: dotnet run -c Release --project tools/Rts.Bench -- replay \"{Path.GetFullPath(replayPath)}\"");
    }

    /// <summary>Sandbox-style mass spawn (recorded like the client's sandbox console): <paramref name="perArmy"/> land
    /// units per player around its start, ordered to attack-move to the map centre.</summary>
    private static List<Command> SpawnArmies(Simulation sim, int players, int perArmy)
    {
        var w = sim.World;
        string[] kinds = ["uef_t1_tank", "uef_t1_artillery", "uef_t1_mobile_aa", "uef_t2_mobile_missile"];
        var centre = new Vector2(w.Terrain.Width, w.Terrain.Height) * 0.5f;
        var cmds = new List<Command>();
        for (int a = 0; a < players; a++)
        {
            var start = w.Terrain.StartPositions[a];
            var dir = Vector2.Normalize(centre - start);
            for (int k = 0; k < perArmy; k += 50)
            {
                int n = Math.Min(50, perArmy - k);
                var at = start + dir * (12 + k / 50 % 8 * 4) + new Vector2(-dir.Y, dir.X) * (k / 400 * 6 - 12);
                cmds.Add(new SandboxSpawnCommand(a, a, w.Blueprints.IndexOf(kinds[k / 50 % kinds.Length]), at, n));
            }
        }
        return cmds;
    }

    private static string Fmt(int ticks)
    {
        int s = ticks / SimWorld.TicksPerSecond;
        return $"{s / 60}:{s % 60:D2}";
    }
}
