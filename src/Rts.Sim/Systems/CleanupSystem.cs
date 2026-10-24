using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// End of tick: frees dead units, updates army unit counts, the defeat rule of <see cref="GameOptions.Victory"/>
/// (assassination: commander death defeats the army and destroys its units; supremacy / annihilation: no finished
/// structure+engineer+commander / no finished unit left; sandbox: never) and victory detection. Sequential.
/// </summary>
public static class CleanupSystem
{
    /// <summary>Per army scratch of the supremacy / annihilation scan (consumed in the same call).</summary>
    [ThreadStatic, Persistence.NoSave] private static bool[]? _hasUnits;

    public static void Run(SimWorld w)
    {
        var u = w.Units;
        var victory = w.Options.Victory;

        if (victory == VictoryCondition.Assassination)
        {
            // Assassination: an army whose commander died is defeated; all its units self-destruct.
            foreach (var army in w.Armies)
            {
                if (army.Defeated || army.Commander.IsNone) continue;
                if (u.IsActive(army.Commander)) continue;
                army.Defeated = true;
                for (int k = 0; k < u.AliveCount; k++)
                {
                    int i = u.AliveList[k];
                    if (u.Army[i] == army.Index) w.KillUnit(i, UnitHandle.None);
                }
            }
        }
        else if (victory is VictoryCondition.Supremacy or VictoryCondition.Annihilation)
            DefeatEmptyArmies(w, victory == VictoryCondition.Supremacy);

        // Free dead units (in the order they died: deterministic).
        foreach (int i in w.PendingDeaths)
        {
            if (!u.Alive[i]) continue;
            var bp = w.BpOf(i);
            if (bp.IsStructure) w.Occupancy.Clear(bp, u.Pos[i], i);
            if (u.BuildFraction[i] >= 1f) w.Armies[u.Army[i]].Stats.UnitsLost++;
            w.Armies[u.Army[i]].UnitCount--;
            u.Free(i);
        }
        w.PendingDeaths.Clear();

        // (6b, G6) Score history: one sample per army every ScoreSample.Interval ticks (not hashed).
        if (w.Tick % ScoreSample.Interval == 0)
            foreach (var army in w.Armies) army.Stats.Record(w.Tick, army.Eco, army.UnitCount);

        // Victory: one team left standing (never in sandbox mode).
        if (w.WinningTeam < 0 && victory != VictoryCondition.Sandbox)
        {
            int team = -1;
            bool multiple = false;
            foreach (var army in w.Armies)
            {
                if (army.Defeated) continue;
                if (team < 0) team = army.Team;
                else if (team != army.Team) multiple = true;
            }
            if (!multiple && team >= 0 && w.Armies.Length > 1) w.WinningTeam = team;
        }
    }

    /// <summary>
    /// Supremacy / annihilation: an army with no finished unit that counts (supremacy: structures, engineers and
    /// commanders incl. SCUs; annihilation: any unit) is defeated and its remaining units destroyed. Scans every
    /// slot, so units carried in transports and units spawned this tick count too.
    /// </summary>
    private static void DefeatEmptyArmies(SimWorld w, bool supremacy)
    {
        var u = w.Units;
        int n = w.Armies.Length;
        var has = _hasUnits is { } arr && arr.Length >= n ? _hasUnits : _hasUnits = new bool[n];
        Array.Clear(has, 0, n);
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || (u.Flags[i] & UnitFlags.Dead) != 0 || u.BuildFraction[i] < 1f) continue;
            int a = u.Army[i];
            if (has[a]) continue;
            if (supremacy)
            {
                var bp = w.BpOf(i);
                if (!bp.IsStructure && !bp.HasAny(UnitCategory.Engineer | UnitCategory.Commander)) continue;
            }
            has[a] = true;
        }
        for (int a = 0; a < n; a++)
            if (!has[a] && !w.Armies[a].Defeated) Defeat(w, a);
    }

    /// <summary>
    /// Defeats an army now (any victory mode): <c>Defeated = true</c> and every unit of it destroyed, carried ones
    /// included. Sequential. Used by supremacy/annihilation and a multiplayer drop with <c>DropAction.Defeat</c>.
    /// </summary>
    public static void Defeat(SimWorld w, int army)
    {
        if ((uint)army >= (uint)w.Armies.Length) return;
        w.Armies[army].Defeated = true;
        var u = w.Units;
        for (int k = 0; k < u.Capacity; k++)
            if (u.Alive[k] && u.Army[k] == army) w.KillUnit(k, UnitHandle.None);
    }
}
