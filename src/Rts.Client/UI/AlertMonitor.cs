using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.UI;

/// <summary>
/// (7b) Notification pass: turns snapshot changes into alerts (<see cref="AlertLog"/>: minimap flash + Backspace jump)
/// and toasts / alert sounds (through <see cref="Raise"/>). Runs every frame whether or not an audio device exists:
/// <list type="bullet">
/// <item>own units losing health (prev → cur): "Units under attack" (toast + alarm only off screen, 25 s cooldown),
/// commander under attack (20 s); every attacked area flashes on the minimap (<see cref="AlertLog.Add"/> merges);</item>
/// <item>allied units losing health: "Ally under attack" (30 s);</item>
/// <item>own complete structure destroyed (UnitDeath effect): "Structure lost" (not when the player self-destructed it);</item>
/// <item>own structure finished (BuildComplete): "Construction complete", or "Upgrade complete" when the same unit
/// had another blueprint in the previous snapshot (upgrades keep the unit handle);</item>
/// <item>unit cap reached (<see cref="UnitCapCounter.ShouldAlert"/>).</item>
/// </list>
/// </summary>
public sealed class AlertMonitor
{
    public delegate void Notify(AlertKind kind, string text, Color color);

    /// <summary>The toast of an UnderAttack alert about structures (the voice says "base under attack" for it, 11b-1).</summary>
    public const string StructuresUnderAttackText = "Structures under attack";

    private readonly BlueprintDb _db;
    private readonly AlertLog _log;
    private readonly UnitCapCounter _cap = new();
    private readonly Dictionary<string, (int count, double time)> _repeat = new();
    private int _lastTick = -1, _lastPing = -1;

    public AlertMonitor(BlueprintDb db, AlertLog log)
    {
        _db = db;
        _log = log;
    }

    public static readonly Color AttackColor = new(255, 170, 90, 255), CommanderColor = new(255, 110, 90, 255),
        AllyColor = new(255, 200, 110, 255), LostColor = new(255, 120, 110, 255), DoneColor = new(150, 230, 160, 255);

    /// <summary>Checks a new snapshot (no-op while the tick is unchanged). <paramref name="onScreen"/>: is a world point
    /// visible; <paramref name="raise"/> receives toasts + sounds (already rate-limited).</summary>
    public void Poll(SimSnapshot? prev, SimSnapshot cur, IReadOnlyList<EffectEvent> effects, int localArmy,
        Func<int, bool> isEnemy, Func<Vector3, bool> onScreen, double now, Notify raise)
    {
        if (cur.Tick == _lastTick) return;
        _lastTick = cur.Tick;
        if ((uint)localArmy >= (uint)cur.Armies.Length) return;
        if (prev != null && prev.Tick < cur.Tick) CheckDamage(prev, cur, localArmy, isEnemy, onScreen, now, raise);
        foreach (var e in effects) CheckEffect(prev, cur, e, localArmy, now, raise);
        CheckPings(cur, localArmy, now, raise);
        var a = cur.Armies[localArmy];
        if (cur.Sandbox == null && a.UnitCap > 0 && _cap.ShouldAlert(a.UnitCount, a.UnitCap))
            raise(AlertKind.UnitCapReached, "Unit cap reached: factories and engineers wait for free slots", LostColor);
    }

    private void CheckDamage(SimSnapshot prev, SimSnapshot cur, int me, Func<int, bool> isEnemy, Func<Vector3, bool> onScreen,
        double now, Notify raise)
    {
        for (int k = 0; k < cur.UnitCount; k++)
        {
            ref var v = ref cur.Units[k];
            bool own = v.Army == me;
            if (!own && (v.Army < 0 || isEnemy(v.Army))) continue;
            if (!prev.TryGetUnit(v.Index, v.Gen, out var pv) || pv.HealthFrac - v.HealthFrac < 0.002f) continue;
            var bp = _db[v.Bp];
            if (!own)
            {
                if (_log.Add(AlertKind.AllyUnderAttack, v.Pos, now, "Ally under attack") && _log.ShouldNotify(AlertKind.AllyUnderAttack, now, 30))
                    raise(AlertKind.AllyUnderAttack, $"Ally under attack: {cur.Armies[v.Army].Name}", AllyColor);
                continue;
            }
            if (bp.Has(UnitCategory.Commander))
            {
                if (_log.Add(AlertKind.CommanderUnderAttack, v.Pos, now, "Commander under attack") && _log.ShouldNotify(AlertKind.CommanderUnderAttack, now, 20))
                    raise(AlertKind.CommanderUnderAttack, "Commander under attack!", CommanderColor);
                continue;
            }
            string what = bp.IsStructure ? StructuresUnderAttackText : "Units under attack";
            if (_log.Add(AlertKind.UnderAttack, v.Pos, now, what) && !onScreen(v.Pos) && _log.ShouldNotify(AlertKind.UnderAttack, now, 25))
                raise(AlertKind.UnderAttack, what, AttackColor);
        }
    }

    private void CheckEffect(SimSnapshot? prev, SimSnapshot cur, in EffectEvent e, int me, double now, Notify raise)
    {
        if (e.Army != me || (uint)e.BlueprintIndex >= (uint)_db.Count) return;
        var bp = _db[e.BlueprintIndex];
        if (!bp.IsStructure) return;
        if (e.Kind == EffectKind.UnitDeath)
        {
            // Skip cancelled / destroyed construction sites (still under construction in the previous snapshot).
            if (prev != null && FindAt(prev, e.Pos, e.BlueprintIndex, me, out var pv)
                && ((pv.Flags & UnitFlags.UnderConstruction) != 0 || _log.TakeSelfDestructed(new Rts.Sim.Core.UnitHandle(pv.Index, pv.Gen))))
                return; // (10b-2) self-destructed by the player: no toast
            _log.Add(AlertKind.StructureLost, e.Pos, now, "Structure lost: " + bp.Name);
            raise(AlertKind.StructureLost, Counted("Structure lost: " + bp.Name, now), LostColor);
        }
        else if (e.Kind == EffectKind.BuildComplete)
        {
            bool upgrade = prev != null && FindAt(cur, e.Pos, e.BlueprintIndex, me, out var v)
                           && prev.TryGetUnit(v.Index, v.Gen, out var pv) && pv.Bp != v.Bp;
            var kind = upgrade ? AlertKind.UpgradeDone : AlertKind.ConstructionDone;
            string text = (upgrade ? "Upgrade complete: " : "Construction complete: ") + bp.Name;
            _log.Add(kind, e.Pos, now, text);
            raise(kind, Counted(text, now), DoneColor);
        }
    }

    /// <summary>New map pings (snapshot Pings are the viewer's team only): sound, minimap flash + jump target; an
    /// ally's ping also toasts "Name: alert". The first snapshot (a loaded game) only sets the baseline.</summary>
    private void CheckPings(SimSnapshot cur, int me, double now, Notify raise)
    {
        if (_lastPing < 0 || cur.PingSerial < _lastPing) { _lastPing = cur.PingSerial; return; } // start / replay seek back
        for (int i = 0; i < cur.PingCount; i++)
        {
            ref var p = ref cur.Pings[i];
            if (p.Id <= _lastPing) continue;
            string who = (uint)p.Army < (uint)cur.Armies.Length ? cur.Armies[p.Army].Name : "?";
            _log.Add(AlertKind.Ping, p.Pos, now, $"{who}: {PingMarkers.KindName(p.Kind)}");
            raise(AlertKind.Ping, p.Army == me ? "" : $"{who}: {PingMarkers.KindName(p.Kind)}", PingMarkers.KindColor(p.Kind));
        }
        _lastPing = cur.PingSerial;
    }

    /// <summary>"text" or, repeated within 4 s, "text ×N" (walls, storages...).</summary>
    private string Counted(string text, double now)
    {
        int n = _repeat.TryGetValue(text, out var r) && now - r.time < 4 ? r.count + 1 : 1;
        _repeat[text] = (n, now);
        return n > 1 ? $"{text} ×{n}" : text;
    }

    /// <summary>The army's unit of that blueprint nearest to pos (within 1.5 units).</summary>
    private static bool FindAt(SimSnapshot s, Vector3 pos, int bp, int army, out UnitView found)
    {
        found = default;
        float best = 1.5f * 1.5f;
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
