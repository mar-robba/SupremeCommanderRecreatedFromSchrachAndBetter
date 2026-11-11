using Raylib_cs;
using Rts.Client.UI;

namespace Rts.Client.Audio;

/// <summary>(7b) Notification sounds + toasts raised by <see cref="AlertMonitor"/> and the map pings.</summary>
public sealed partial class GameAudio
{
    /// <summary>Toast (under the top bar) + the kind's alert sound. Callers rate-limit (AlertLog cooldowns).
    /// A "text ×N" toast replaces the earlier toast of the same text.</summary>
    public void Notify(AlertKind kind, string text, Color color)
    {
        int cut = text.IndexOf(" ×", StringComparison.Ordinal);
        string key = cut > 0 ? text[..cut] : text;
        if (text.Length > 0) // empty: sound only (own pings)
        {
            _toasts.RemoveAll(t => t.text.StartsWith(key, StringComparison.Ordinal));
            Toast(text, color, kind is AlertKind.ConstructionDone or AlertKind.UpgradeDone ? 2.5 : 3);
        }
        if (!_engine.Ready) return;
        double now = Raylib.GetTime();
        var id = AlertSound(kind);
        if (id is SoundId.AlertNotice && now - _lastNotice < 2.5) return; // a burst of finished walls: one chime
        if (id is SoundId.AlertNotice) _lastNotice = now;
        PlayUi(id);
    }

    private double _lastNotice = -100;

    public static SoundId AlertSound(AlertKind kind) => kind switch
    {
        AlertKind.CommanderUnderAttack => SoundId.CommanderAlert,
        AlertKind.UnderAttack or AlertKind.AllyUnderAttack => SoundId.UnderAttack,
        AlertKind.StructureLost => SoundId.AlertStructureLost,
        AlertKind.UnitCapReached => SoundId.UiError,
        AlertKind.Ping => SoundId.AlertPing,
        _ => SoundId.AlertNotice,
    };
}
