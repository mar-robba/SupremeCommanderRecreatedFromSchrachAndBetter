using System.Numerics;
using Raylib_cs;
using Rts.Client.UI;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Audio;

/// <summary>
/// (11b-1) The game events that the voice announcer speaks (<see cref="VoiceAnnouncer"/>, lines in data/audio/voice/lines.json).
/// The beeps and toasts stay; the voice is said on top. Sources: the alerts of <see cref="AlertMonitor"/> (through
/// <see cref="Notify"/>), the stall warnings, the strategic launch klaxon and the experimental klaxon, plus
/// <see cref="VoiceFrame"/> for events that have no alert of their own (commander deaths, defeated players, enhancement /
/// experimental complete, tactical launches). Everything comes from snapshots (the client never reads the sim).
/// </summary>
public sealed partial class GameAudio
{
    private int _voiceLaunchSerial = int.MinValue;
    private int _voiceTick = -1;
    /// <summary>Client time when an army's commander death was seen (so its "defeated" line is not said too).</summary>
    private readonly Dictionary<int, double> _commanderSeenDead = new();

    /// <summary>Queues a voice line (cooldowns and the voice on/off setting apply inside).</summary>
    private void Say(string id) => _sys.Voice.Say(id, Raylib.GetTime());

    /// <summary>The voice line of an <see cref="AlertMonitor"/> alert, or null (construction done, pings: beep only).</summary>
    public static string? VoiceFor(AlertKind kind, string text) => kind switch
    {
        AlertKind.CommanderUnderAttack => VoiceIds.CommanderUnderAttack,
        AlertKind.UnderAttack => text.StartsWith(AlertMonitor.StructuresUnderAttackText, StringComparison.Ordinal)
            ? VoiceIds.BaseUnderAttack : VoiceIds.UnitsUnderAttack,
        AlertKind.AllyUnderAttack => VoiceIds.AllyUnderAttack,
        AlertKind.StructureLost => VoiceIds.StructureLost,
        AlertKind.UpgradeDone => VoiceIds.UpgradeComplete,
        AlertKind.UnitCapReached => VoiceIds.UnitCapReached,
        _ => null,
    };

    private void SayAlert(AlertKind kind, string text)
    {
        if (VoiceFor(kind, text) is { } id) Say(id);
    }

    /// <summary>Once per frame with a device: the voice-only events of the new snapshot.</summary>
    private void VoiceFrame(SnapshotInterpolator interp)
    {
        var cur = interp.Current;
        if (cur.Tick == _voiceTick) return;
        _voiceTick = cur.Tick;
        var prev = interp.Prev;
        int me = _ctx.LocalArmy;
        VoiceLaunches(cur, me);
        foreach (var e in interp.NewEffects) VoiceEffect(prev, cur, e, me);
        // A player defeated (prev → cur, consecutive snapshots only: no lines after a replay seek or a load).
        if (prev == null || prev.Tick >= cur.Tick || cur.Tick - prev.Tick > 20) return;
        int n = Math.Min(prev.Armies.Length, cur.Armies.Length);
        double now = Raylib.GetTime();
        for (int a = 0; a < n; a++)
        {
            if (a == me || prev.Armies[a].Defeated || !cur.Armies[a].Defeated) continue;
            if (_commanderSeenDead.TryGetValue(a, out double t) && now - t < 15) continue; // "commander destroyed" was said
            Say(_ctx.IsEnemy(a) ? VoiceIds.EnemyDefeated : VoiceIds.AllyDefeated);
        }
    }

    /// <summary>New strategic launches: an enemy's = "nuclear launch detected", ours or an ally's = "missile launched".
    /// Launches before the first snapshot seen (a loaded game) set the baseline only.</summary>
    private void VoiceLaunches(SimSnapshot cur, int me)
    {
        if (_voiceLaunchSerial == int.MinValue || cur.LaunchSerial < _voiceLaunchSerial) { _voiceLaunchSerial = cur.LaunchSerial; return; }
        if (cur.LaunchSerial == _voiceLaunchSerial) return;
        foreach (var l in cur.Launches)
        {
            if (l.Serial <= _voiceLaunchSerial) continue;
            Say(l.Army != me && _ctx.IsEnemy(l.Army) ? VoiceIds.NukeLaunchDetected : VoiceIds.StrategicMissileLaunched);
        }
        _voiceLaunchSerial = cur.LaunchSerial;
    }

    private void VoiceEffect(SimSnapshot? prev, SimSnapshot cur, in EffectEvent e, int me)
    {
        var db = _ctx.Db;
        if ((uint)e.BlueprintIndex >= (uint)db.Count) return;
        var bp = db[e.BlueprintIndex];
        switch (e.Kind)
        {
            case EffectKind.UnitDeath when bp.Has(UnitCategory.Commander) && e.Army != me && e.Army >= 0:
                _commanderSeenDead[e.Army] = Raylib.GetTime();
                Say(_ctx.IsEnemy(e.Army) ? VoiceIds.EnemyCommanderDestroyed : VoiceIds.AllyCommanderDestroyed);
                break;
            case EffectKind.MissileLaunch when e.Size < 2f && e.Army >= 0 && e.Army != me && _ctx.IsEnemy(e.Army):
                Say(VoiceIds.TacticalLaunchDetected); // Size 1.2 = tactical, 3 = strategic (VoiceLaunches)
                break;
            case EffectKind.BuildComplete when e.Army == me:
                bool swapped = prev != null && FindOwnAt(cur, e.Pos, e.BlueprintIndex, me, out var v)
                               && prev.TryGetUnit(v.Index, v.Gen, out var pv) && pv.Bp != v.Bp;
                if (bp.Has(UnitCategory.Commander) && swapped) Say(VoiceIds.EnhancementComplete);
                else if (bp.Has(UnitCategory.Experimental) && !swapped) Say(VoiceIds.ExperimentalComplete);
                break;
        }
    }

    /// <summary>The army's unit of that blueprint nearest to pos (within 3 units).</summary>
    private static bool FindOwnAt(SimSnapshot s, Vector3 pos, int bp, int army, out UnitView found)
    {
        found = default;
        float best = 3f * 3f;
        bool ok = false;
        for (int k = 0; k < s.UnitCount; k++)
        {
            ref var v = ref s.Units[k];
            if (v.Bp != bp || v.Army != army) continue;
            float d = (v.Pos.X - pos.X) * (v.Pos.X - pos.X) + (v.Pos.Z - pos.Z) * (v.Pos.Z - pos.Z);
            if (d < best) { best = d; found = v; ok = true; }
        }
        return ok;
    }
}
