using Rts.Sim;
using Rts.Sim.Orders;

namespace Rts.Net.Lockstep;

/// <summary>
/// (10a-3) Saves at the same tick on every peer, and commands injected by the game start (AI takeovers of a loaded
/// game). The host picks the save tick with <see cref="ScheduleSaveAfterTurns"/>: one past every turn it holds, so no
/// peer can have stepped to it before the order (which reaches every peer before any later turn) arrives.
/// </summary>
public sealed partial class LockstepHooks
{
    private readonly List<int> _saveTicks = new();
    private readonly List<(int tick, Command cmd)> _inject = new();
    private volatile int _lastStepped = -1;

    /// <summary>Called on the sim thread right after the step to a scheduled save tick (the world is at that tick), or
    /// with a negative tick when a scheduled tick was already passed (the save failed on this peer).</summary>
    public Action<Simulation, int>? SaveDue;

    /// <summary>Tick of the last AfterStep (-1 = none yet).</summary>
    public int LastStepped => _lastStepped;

    /// <summary>Earliest scheduled save tick not reached yet (-1 = none): a paused peer runs up to it.</summary>
    public int PendingSaveTick
    {
        get { lock (_lock) return _saveTicks.Count == 0 ? -1 : _saveTicks.Min(); }
    }

    /// <summary>Host: schedules a save one tick past the highest turn of any player it holds (its own turns included)
    /// and returns that tick. Atomic with the turn bookkeeping (no own turn can be produced meanwhile).</summary>
    public int ScheduleSaveAfterTurns()
    {
        lock (_lock)
        {
            int max = _nextSend - 1;
            for (int p = 0; p < Players; p++) max = Math.Max(max, _received[p]);
            int t = Math.Max(max, _lastStepped) + 1;
            _saveTicks.Add(t);
            return t;
        }
    }

    /// <summary>Schedules a save after <paramref name="tick"/> (peers: the host's order). Returns false (and schedules
    /// nothing) when this peer already stepped past it.</summary>
    public bool ScheduleSave(int tick)
    {
        lock (_lock)
        {
            if (tick <= _lastStepped) return false;
            if (!_saveTicks.Contains(tick)) _saveTicks.Add(tick);
            return true;
        }
    }

    /// <summary>Adds <paramref name="cmd"/> to the commands of <paramref name="tick"/> on this peer (call before the
    /// runner starts, identically on every peer, e.g. from the <c>GameStart</c>).</summary>
    public void InjectAt(int tick, Command cmd)
    {
        lock (_lock) _inject.Add((tick, cmd));
    }

    private void InjectCommands(int next)
    {
        if (_inject.Count == 0) return;
        lock (_lock)
        {
            foreach (var (tick, cmd) in _inject)
            {
                if (tick != next) continue;
                cmd.Sequence = _tickCommands.Count + 1;
                cmd.ExecuteTick = next;
                _tickCommands.Add(cmd);
            }
            _inject.RemoveAll(x => x.tick <= next);
        }
    }

    private void CheckSaves(Simulation sim, int t)
    {
        _lastStepped = t;
        bool due = false, missed = false;
        lock (_lock)
        {
            if (_saveTicks.Count == 0) return;
            for (int i = _saveTicks.Count - 1; i >= 0; i--)
            {
                if (_saveTicks[i] == t) due = true;
                else if (_saveTicks[i] < t) missed = true;
                else continue;
                _saveTicks.RemoveAt(i);
            }
        }
        if (missed) SaveDue?.Invoke(sim, -1);
        if (due) SaveDue?.Invoke(sim, t);
    }
}
