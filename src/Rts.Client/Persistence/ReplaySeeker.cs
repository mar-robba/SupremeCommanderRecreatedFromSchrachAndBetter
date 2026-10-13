using Rts.Sim;
using Rts.Sim.Persistence;

namespace Rts.Client.Persistence;

/// <summary>
/// Seeking in a replay played by a <see cref="SimRunner"/>: a runner job (sim thread, also while paused) moves the
/// <see cref="ReplayPlayer"/> to the target and swaps its simulation into the runner. Forward seeks go in chunks of
/// <see cref="Chunk"/> ticks, each publishing a snapshot so the view follows; backward seeks restore the nearest
/// checkpoint and re-simulate. A new target given while seeking is picked up by the running job.
/// </summary>
public sealed class ReplaySeeker
{
    public const int Chunk = 50;
    private readonly ReplayPlayer _player;
    private readonly SimRunner _runner;
    private readonly object _lock = new();
    private bool _seeking;
    private int _target;
    /// <summary>Message of the last failed seek (null = none).</summary>
    public volatile string? Error;

    public ReplaySeeker(ReplayPlayer player, SimRunner runner)
    {
        _player = player;
        _runner = runner;
    }

    /// <summary>True while a seek runs; <paramref name="target"/> = its target tick.</summary>
    public bool IsSeeking(out int target)
    {
        lock (_lock)
        {
            target = _target;
            return _seeking;
        }
    }

    /// <summary>Any thread: starts (or retargets) a seek to <paramref name="tick"/> (clamped to the replay).</summary>
    public void SeekTo(int tick)
    {
        tick = Math.Clamp(tick, _player.StartTick, _player.EndTick);
        lock (_lock)
        {
            _target = tick;
            if (_seeking) return;
            _seeking = true;
        }
        Error = null;
        _runner.Post(Job);
    }

    private void Job(SimRunner r)
    {
        try
        {
            int target;
            lock (_lock) target = _target;
            int cur = _player.Tick;
            _player.SeekTo(target < cur ? target : Math.Min(target, cur + Chunk));
            r.ReplaceSimulation(_player.Sim);
            lock (_lock)
            {
                if (_player.Tick != _target)
                {
                    r.Post(Job);
                    return;
                }
                _seeking = false;
            }
        }
        catch (Exception e)
        {
            Error = e.Message;
            lock (_lock) _seeking = false;
        }
    }
}
