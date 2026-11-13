namespace Rts.Client.Audio;

/// <summary>
/// (11b-1) The announcer's queue: pure logic (the caller passes the time), so tests need no audio device.
/// <list type="bullet">
/// <item>never talks over itself: one line at a time, then <see cref="Gap"/> seconds of quiet;</item>
/// <item>a waiting line of higher priority goes first (ties: the older one); a line being spoken is never cut;</item>
/// <item>a waiting line older than its <see cref="VoiceLine.MaxWait"/> is dropped (stale news);</item>
/// <item>each line has its own cooldown (counted from when it was accepted), on top of the alert cooldowns of the callers;</item>
/// <item>at most <see cref="MaxWaiting"/> lines wait: a new line pushes out the lowest-priority waiting one, if it ranks higher.</item>
/// </list>
/// </summary>
public sealed class VoiceQueue
{
    public readonly record struct Waiting(VoiceLine Line, double Queued);

    private readonly Func<string, VoiceLine?> _lookup;
    private readonly List<Waiting> _waiting = new();
    private readonly Dictionary<string, double> _lastAccepted = new();
    private double _speakingUntil = double.NegativeInfinity;

    /// <summary>Quiet seconds between two lines.</summary>
    public double Gap = 0.3;
    public int MaxWaiting = 4;

    public VoiceQueue(Func<string, VoiceLine?> lookup) => _lookup = lookup;

    public VoiceQueue(VoiceLineTable table) : this(id => table[id]) { }

    /// <summary>The line being spoken (until its duration has passed), or null.</summary>
    public VoiceLine? Speaking { get; private set; }

    public IReadOnlyList<Waiting> WaitingLines => _waiting;

    /// <summary>Is a line being spoken at <paramref name="now"/>?</summary>
    public bool Busy(double now) => now < _speakingUntil;

    /// <summary>Queues a line. False: unknown id, still cooling down, already waiting, or the queue is full of more
    /// important lines.</summary>
    public bool Enqueue(string id, double now)
    {
        var line = _lookup(id);
        if (line == null) return false;
        if (_lastAccepted.TryGetValue(id, out double last) && now - last < line.Cooldown) return false;
        foreach (var w in _waiting) if (w.Line.Id == id) return false;
        DropStale(now);
        if (_waiting.Count >= MaxWaiting)
        {
            int low = 0;
            for (int i = 1; i < _waiting.Count; i++)
                if (_waiting[i].Line.Priority < _waiting[low].Line.Priority
                    || _waiting[i].Line.Priority == _waiting[low].Line.Priority && _waiting[i].Queued > _waiting[low].Queued) low = i;
            if (_waiting[low].Line.Priority >= line.Priority) return false;
            _waiting.RemoveAt(low);
        }
        _waiting.Add(new Waiting(line, now));
        _lastAccepted[id] = now;
        return true;
    }

    /// <summary>
    /// The line to start now, or null (still speaking, in the gap after a line, or nothing waiting). The caller plays it
    /// and reports its length with <see cref="Started"/>.
    /// </summary>
    public VoiceLine? Next(double now)
    {
        if (now < _speakingUntil + Gap) return null;
        Speaking = null;
        DropStale(now);
        if (_waiting.Count == 0) return null;
        int best = 0;
        for (int i = 1; i < _waiting.Count; i++)
            if (_waiting[i].Line.Priority > _waiting[best].Line.Priority) best = i; // ties keep the older (earlier in the list)
        var line = _waiting[best].Line;
        _waiting.RemoveAt(best);
        return line;
    }

    /// <summary>The line returned by <see cref="Next"/> started at <paramref name="now"/> and lasts
    /// <paramref name="duration"/> seconds (0 = no file: silence, the next line may follow at once).</summary>
    public void Started(VoiceLine line, double now, double duration)
    {
        Speaking = duration > 0 ? line : null;
        _speakingUntil = duration > 0 ? now + duration : double.NegativeInfinity;
    }

    /// <summary>Drops every waiting line (voice switched off, game left). Cooldowns are kept.</summary>
    public void Clear()
    {
        _waiting.Clear();
        Speaking = null;
        _speakingUntil = double.NegativeInfinity;
    }

    private void DropStale(double now) => _waiting.RemoveAll(w => now - w.Queued > w.Line.MaxWait);
}
