using Rts.Client.Menus;
using Rts.Sim;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;

namespace Rts.Client.Persistence;

/// <summary>
/// Replay recording and saving of one game (the runner's <see cref="SimRunner.Hooks"/>, wraps a
/// <see cref="ReplayRecorder"/>). The replay file is written when the game ends (a team won) and again when the
/// session closes (quit / restart / exit), always to the same file. Saves capture the world on the sim thread (a
/// runner job) and encode + write on a worker; file IO never blocks the render thread. Pending writes are awaited
/// at application exit (<see cref="WaitForPendingWrites"/>).
/// </summary>
public sealed class GameRecording : ITickHooks
{
    /// <summary>Write replay files (off in automated screenshot runs unless --write-replay).</summary>
    public static bool AutoWriteReplays = true;
    /// <summary>Shorter games are not kept as replays.</summary>
    public const int MinReplayTicks = 50;

    private static readonly object s_ioLock = new();
    private static Task s_io = Task.CompletedTask;

    private readonly ReplayRecorder _rec;
    /// <summary>Header template (never modified after construction: cloned by every writer).</summary>
    private readonly GameInfo _info;
    private string? _replayPath;
    private bool _endWritten;
    private bool _finished;

    public GameRecording(GameInfo info, ReplayLog? continueFrom = null)
    {
        _info = info;
        _rec = new ReplayRecorder(continueFrom);
    }

    /// <summary>Recording of a new game (call before the runner starts: reads the world).</summary>
    public static GameRecording ForNewGame(GameConfig cfg, Simulation sim, int localArmy)
    {
        var info = GameInfo.ForSkirmish(cfg.ToSkirmish(out _), sim, localArmy, cfg.Sandbox);
        info.Scene = cfg.Scene;
        info.ClientData = ClientGameData.From(cfg).ToJson();
        info.Name = cfg.Sandbox ? "Sandbox" : cfg.Scene != null ? "Scene " + cfg.Scene : "Skirmish";
        return new GameRecording(info);
    }

    /// <summary>Path of the replay file once written (null before).</summary>
    public string? ReplayPath => Volatile.Read(ref _replayPath);

    // ------------------------------------------------------------------ ITickHooks (sim thread)

    public IReadOnlyList<Command> BeforeStep(Simulation sim, List<Command> incoming) => _rec.BeforeStep(sim, incoming);

    public void AfterStep(Simulation sim)
    {
        _rec.AfterStep(sim);
        if (!_endWritten && sim.World.WinningTeam >= 0)
        {
            _endWritten = true;
            QueueReplayWrite(sim);
        }
    }

    // ------------------------------------------------------------------ saving (render thread)

    /// <summary>
    /// Saves the game as <paramref name="name"/>: the world is captured on the sim thread (between two ticks, also while
    /// paused), then encoded and written on a worker. A toast reports the result; <paramref name="done"/> (optional)
    /// is called on the worker with the saved path or null on failure. <paramref name="toast"/> replaces the success
    /// toast text (autosave: "Autosaved").
    /// </summary>
    public void Save(SimRunner runner, string name, ClientGameData clientData, Action<string?>? done = null, string? toast = null)
    {
        var info = _info.Clone();
        info.ClientData = clientData.ToJson();
        OnSimThread(runner, r =>
        {
            SaveGame.Captured c;
            try { c = SaveGame.Capture(r.Simulation, info, _rec.Started ? _rec : null, name); }
            catch (Exception e)
            {
                Toasts.Show("Save failed: " + SaveText.ErrorMessage(e), error: true);
                done?.Invoke(null);
                return;
            }
            QueueIo(() =>
            {
                try
                {
                    string path = SaveStore.WriteSave(SaveGame.Encode(c), name);
                    Toasts.Show(toast ?? (name == SaveStore.QuickSaveName ? "Quicksave done" : $"Game saved: {name}"));
                    done?.Invoke(path);
                }
                catch (Exception e)
                {
                    Toasts.Show("Save failed: " + SaveText.ErrorMessage(e), error: true);
                    done?.Invoke(null);
                }
            });
        });
    }

    /// <summary>
    /// End of the session (render thread, runner already stopped): runs pending runner jobs (a save requested just
    /// before quitting) and writes the final replay on a worker.
    /// </summary>
    public void Finish(SimRunner runner)
    {
        if (_finished) return;
        _finished = true;
        runner.RunJobs();
        QueueReplayWrite(runner.Simulation);
    }

    /// <summary>Writes the replay recorded so far now (e.g. a multiplayer desync dump); later writes update the same file.</summary>
    public void WriteReplayNow(SimRunner runner) => OnSimThread(runner, r => QueueReplayWrite(r.Simulation));

    /// <summary>Waits (bounded) for the save/replay writes still running, e.g. at application exit.</summary>
    public static void WaitForPendingWrites(TimeSpan timeout)
    {
        Task t;
        lock (s_ioLock) t = s_io;
        try { t.Wait(timeout); } catch (AggregateException) { }
    }

    // ------------------------------------------------------------------ internals

    /// <summary>Runs <paramref name="job"/> on the sim thread, or right now if the runner is stopped (end screen).</summary>
    private static void OnSimThread(SimRunner runner, Action<SimRunner> job)
    {
        runner.Post(job);
        if (!runner.IsRunning) runner.RunJobs();
    }

    /// <summary>Sim thread or runner stopped: snapshot of the replay so far, written on a worker.</summary>
    private void QueueReplayWrite(Simulation sim)
    {
        if (!AutoWriteReplays || !_rec.Started) return;
        var log = _rec.Snapshot();
        if (log.LastTick - log.InitialTick < MinReplayTicks) return;
        var info = _info.Clone();
        info.WinningTeam = sim.World.WinningTeam;
        info.CreatedUtc = DateTime.UtcNow;
        var terrain = info.Map == null ? sim.World.Terrain : null; // immutable: serialized on the worker
        QueueIo(() =>
        {
            try
            {
                var file = SaveGame.EncodeReplay(info, log, terrain != null ? SaveGame.TerrainBytes(terrain) : null);
                if (_replayPath == null) Volatile.Write(ref _replayPath, SaveStore.WriteReplay(file, info));
                else SaveStore.WriteAtomic(_replayPath, file);
            }
            catch (Exception e)
            {
                Toasts.Show("Replay not written: " + SaveText.ErrorMessage(e), error: true);
            }
        });
    }

    /// <summary>File writes run one after the other (a later replay write overwrites an earlier one).</summary>
    private static void QueueIo(Action write)
    {
        lock (s_ioLock) s_io = s_io.ContinueWith(_ => write(), TaskScheduler.Default);
    }
}
