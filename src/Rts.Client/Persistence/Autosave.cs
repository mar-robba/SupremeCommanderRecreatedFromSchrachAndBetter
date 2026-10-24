using Rts.Sim.Persistence;

namespace Rts.Client.Persistence;

/// <summary>
/// Autosave (wave 7a, pure logic, unit tested): one save every <see cref="IntervalTicks"/> of GAME time (default
/// 3000 ticks = 5 minutes at 10 ticks/s) into a rolling set of <see cref="Slots"/> files <c>autosave_1..3</c> in
/// <see cref="SaveStore.SavesDir"/>, written by the normal save code (<see cref="GameRecording.Save"/>). The interval
/// counts from the first tick seen: a new game autosaves at tick 3000, a game loaded at tick T at T + 3000. A paused
/// game does not advance its tick, so it never autosaves. When it saves is decided by the in-game save UI
/// (<see cref="Allowed"/>: setting on, not a replay, sandbox, scene, multiplayer or finished game).
/// </summary>
public sealed class Autosave
{
    public const int DefaultIntervalTicks = 3000;
    public const int Slots = 3;
    public const string NamePrefix = "autosave_";

    public readonly int IntervalTicks;
    private int _lastTick = -1;
    private string? _lastSlot;

    public Autosave(int intervalTicks = DefaultIntervalTicks) => IntervalTicks = Math.Max(1, intervalTicks);

    /// <summary>Tick of the last autosave (or the first tick seen); -1 before the first <see cref="Due"/> call.</summary>
    public int LastTick => _lastTick;

    /// <summary>
    /// Called with the current sim tick (every frame while playing): true when an autosave is due now; the interval
    /// then restarts from <paramref name="tick"/>. The first call only starts the clock.
    /// </summary>
    public bool Due(int tick)
    {
        if (_lastTick < 0 || tick < _lastTick)
        {
            _lastTick = tick;
            return false;
        }
        if (tick - _lastTick < IntervalTicks) return false;
        _lastTick = tick;
        return true;
    }

    /// <summary>Slot for the next autosave of this game (never the slot it wrote last, whose write may still be running).</summary>
    public string NextSlot()
    {
        _lastSlot = PickSlot(_lastSlot);
        return _lastSlot;
    }

    /// <summary>Save name of slot 1..3: "autosave_1".</summary>
    public static string SlotName(int slot) => NamePrefix + slot;

    /// <summary>Slot number of an autosave file name ("autosave_2" → 2), 0 if it is not one.</summary>
    public static int SlotOf(string fileName)
    {
        if (!fileName.StartsWith(NamePrefix, StringComparison.Ordinal)) return 0;
        return int.TryParse(fileName.AsSpan(NamePrefix.Length), System.Globalization.NumberStyles.None, null, out int n)
            && n >= 1 && n <= Slots && fileName == SlotName(n) ? n : 0;
    }

    /// <summary>
    /// The slot to write: the first empty one, else the one with the oldest file. <paramref name="avoid"/> is skipped
    /// (when another slot exists). Only the <c>autosave_N</c> files are looked at, other saves are never touched.
    /// </summary>
    public static string PickSlot(string? avoid = null)
    {
        string? best = null;
        DateTime bestTime = DateTime.MaxValue;
        for (int i = 1; i <= Slots; i++)
        {
            string name = SlotName(i);
            if (name == avoid) continue;
            string path = SaveStore.SavePath(name);
            DateTime t = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (best == null || t < bestTime)
            {
                best = name;
                bestTime = t;
            }
        }
        return best ?? SlotName(1);
    }

    /// <summary>
    /// True if this game may autosave: the setting is on (or <paramref name="forced"/> by --autosave-every), the game
    /// can be saved, it is not a replay, sandbox or visual test scene, not a --screenshot run (unless forced) and not over.
    /// </summary>
    public static bool Allowed(bool setting, bool forced, bool canSave, bool readOnly, bool sandbox, bool scene, bool screenshotRun, bool ended)
        => (setting || forced) && canSave && !readOnly && !sandbox && !scene && (!screenshotRun || forced) && !ended;

    /// <summary>List title of an autosave file: "Autosave 2" (null if <paramref name="fileName"/> is not one).</summary>
    public static string? Title(string fileName, string saveName)
    {
        int n = SlotOf(fileName);
        if (n == 0) return null;
        return saveName == fileName ? $"Autosave {n}" : $"{saveName} (autosave {n})";
    }
}
