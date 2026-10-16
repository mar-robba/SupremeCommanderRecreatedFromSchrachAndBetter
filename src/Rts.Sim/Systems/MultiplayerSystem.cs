using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>Sim side of multiplayer: applies <see cref="PlayerLeftCommand"/> (called by CommandSystem, sequential).</summary>
public static class MultiplayerSystem
{
    public static void Apply(SimWorld w, PlayerLeftCommand c)
    {
        if ((uint)c.Army >= (uint)w.Armies.Length) return;
        var army = w.Armies[c.Army];
        if (army.Defeated) return;
        if (c.Action == DropAction.AiTakeover)
        {
            // The skirmish AI creates its brain lazily on its next think and continues from the current army.
            SandboxSystem.SetAi(w, c.Army, true, army.AiDifficulty);
            if (!army.Name.EndsWith(" (AI)")) army.Name += " (AI)";
            return;
        }
        // Defeat: the assassination rule (CleanupSystem) destroys the army at the end of this tick.
        var u = w.Units;
        if (!army.Commander.IsNone && u.IsActive(army.Commander))
        {
            w.KillUnit(army.Commander.Index, UnitHandle.None);
            return;
        }
        army.Defeated = true;
        for (int k = 0; k < u.Capacity; k++)
            if (u.Alive[k] && u.Army[k] == c.Army) w.KillUnit(k, UnitHandle.None);
    }
}
