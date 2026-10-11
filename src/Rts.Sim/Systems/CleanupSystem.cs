using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// End of tick: frees dead units, updates army unit counts, assassination rule (commander death defeats the
/// army and destroys its units) and victory detection. Sequential.
/// </summary>
public static class CleanupSystem
{
    public static void Run(SimWorld w)
    {
        var u = w.Units;

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

        // Victory: one team left standing.
        if (w.WinningTeam < 0)
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
}
