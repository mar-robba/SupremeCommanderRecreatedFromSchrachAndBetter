using Raylib_cs;
using Rts.Client.Persistence;
using Rts.Sim.Snapshot;

namespace Rts.Client.UI;

/// <summary>
/// (6b) Top-bar unit counter "units 123/500" of the local army (game option <c>GameOptions.UnitCap</c>), right of the
/// resource bar: amber at 90% of the cap, red at the cap, plus a one-time "Unit cap reached" toast (re-armed below 90%).
/// Hidden in sandbox (the cap is lifted there).
/// </summary>
public sealed class UnitCapCounter
{
    public enum Level { Normal, Near, Full }

    private bool _alerted;

    public static string Format(int count, int cap) => $"units {count}/{cap}";

    /// <summary>Normal below 90% of the cap, Near from 90%, Full at (or over) the cap.</summary>
    public static Level LevelOf(int count, int cap) =>
        cap <= 0 ? Level.Normal : count >= cap ? Level.Full : count * 10 >= cap * 9 ? Level.Near : Level.Normal;

    /// <summary>True once when the army reaches its cap; armed again when it falls below 90%.</summary>
    public bool ShouldAlert(int count, int cap)
    {
        var lv = LevelOf(count, cap);
        if (lv == Level.Normal) _alerted = false;
        if (lv != Level.Full || _alerted) return false;
        _alerted = true;
        return true;
    }

    /// <summary>Draws the counter at <paramref name="x"/> in the top bar <paramref name="bar"/> and raises the alert.</summary>
    public void Draw(Rectangle bar, float x, SimSnapshot snap, in ArmyView a)
    {
        if (snap.Sandbox != null || a.UnitCap <= 0) return;
        if (ShouldAlert(a.UnitCount, a.UnitCap)) Toasts.Show("Unit cap reached: factories and engineers wait for free slots", error: true);
        var lv = LevelOf(a.UnitCount, a.UnitCap);
        var col = lv switch { Level.Full => Ui.Bad, Level.Near => Ui.Warn, _ => Ui.TextDim };
        int fs = Ui.F(12);
        Ui.Text(Format(a.UnitCount, a.UnitCap), x, (bar.Height - fs) * 0.5f - 1, fs, col, lv != Level.Normal);
    }
}
