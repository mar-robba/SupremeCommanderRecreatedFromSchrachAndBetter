using System.Numerics;

namespace Rts.Client.UI;

/// <summary>(7b) Kinds of game notifications (client side). Append-only.</summary>
public enum AlertKind : byte
{
    UnderAttack, CommanderUnderAttack, AllyUnderAttack, StructureLost, ConstructionDone, UpgradeDone, UnitCapReached, Ping,
}

/// <summary>One notification: where (minimap flash, Backspace jump) and when (client seconds).</summary>
public readonly record struct AlertEntry(AlertKind Kind, Vector3 Pos, double Time, string Text);

/// <summary>
/// (7b) The notification log: recent alerts with a position (minimap flashes, "jump to the last alert" = Backspace,
/// pressed again = the next older one), plus the cooldowns that keep toasts and sounds from spamming. Pure (no raylib):
/// the caller passes the time.
/// </summary>
public sealed class AlertLog
{
    public const int Capacity = 32;
    /// <summary>Seconds a minimap flash lasts.</summary>
    public const double FlashTime = 4.0;
    /// <summary>Alerts of one kind closer than this (world units) within <see cref="AreaCooldown"/> merge into one.</summary>
    public const float AreaRadius = 30f;
    public const double AreaCooldown = 8.0;
    /// <summary>Jump cycle: pressing again within this many seconds goes to the next older alert.</summary>
    public const double CycleWindow = 4.0;

    private readonly List<AlertEntry> _entries = new();
    private readonly List<Rts.Sim.Core.UnitHandle> _selfDestructed = new(); // (10b-2) newest last, at most 256

    /// <summary>(10b-2) Remembers a unit the player self-destructed, so its death raises no "Structure lost" toast.</summary>
    public void NoteSelfDestruct(Rts.Sim.Core.UnitHandle h)
    {
        if (_selfDestructed.Count >= 256) _selfDestructed.RemoveAt(0);
        _selfDestructed.Add(h);
    }

    /// <summary>True (and forgotten) if <paramref name="h"/> was self-destructed by the player.</summary>
    public bool TakeSelfDestructed(Rts.Sim.Core.UnitHandle h)
    {
        int i = _selfDestructed.IndexOf(h);
        if (i < 0) return false;
        _selfDestructed.RemoveAt(i);
        return true;
    }
    private readonly double[] _lastNotified = Enumerable.Repeat(-1e9, 16).ToArray();
    private int _cycle = -1;
    private double _lastJump = -1e9;

    /// <summary>Entries, oldest first.</summary>
    public IReadOnlyList<AlertEntry> Entries => _entries;

    /// <summary>Logs an alert at a position unless one of the same kind is near it and recent. True when logged.</summary>
    public bool Add(AlertKind kind, Vector3 pos, double now, string text)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            if (now - e.Time > AreaCooldown) break;
            if (e.Kind == kind && Vector2.Distance(new(e.Pos.X, e.Pos.Z), new(pos.X, pos.Z)) < AreaRadius) return false;
        }
        _entries.Add(new AlertEntry(kind, pos, now, text));
        if (_entries.Count > Capacity) _entries.RemoveAt(0);
        _cycle = -1; // a new alert restarts the jump cycle at the newest one
        return true;
    }

    /// <summary>Toast / sound cooldown per kind: true (and the clock restarts) when the last one is older than
    /// <paramref name="cooldown"/> seconds.</summary>
    public bool ShouldNotify(AlertKind kind, double now, double cooldown)
    {
        ref double last = ref _lastNotified[(int)kind];
        if (now - last < cooldown) return false;
        last = now;
        return true;
    }

    /// <summary>Entries still flashing on the minimap (age 0..1 of <see cref="FlashTime"/>), newest last.</summary>
    public IEnumerable<(AlertEntry Entry, float Age)> Flashes(double now)
    {
        foreach (var e in _entries)
            if (now - e.Time < FlashTime && now >= e.Time) yield return (e, (float)((now - e.Time) / FlashTime));
    }

    /// <summary>Backspace: the newest alert, or (pressed again within <see cref="CycleWindow"/>) the next older one,
    /// wrapping to the newest. Null when the log is empty.</summary>
    public AlertEntry? NextJump(double now)
    {
        if (_entries.Count == 0) return null;
        bool cycling = _cycle >= 0 && now - _lastJump < CycleWindow;
        _cycle = !cycling ? _entries.Count - 1 : _cycle - 1;
        if (_cycle < 0) _cycle = _entries.Count - 1;
        _lastJump = now;
        return _entries[_cycle];
    }

    public void Clear() { _entries.Clear(); _cycle = -1; }
}
