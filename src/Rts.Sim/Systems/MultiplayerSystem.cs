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
            if (!army.Name.EndsWith(" (AI)", StringComparison.Ordinal)) army.Name += " (AI)";
            return;
        }
        // Defeat: the army is defeated now and all its units are destroyed (every victory mode).
        CleanupSystem.Defeat(w, c.Army);
    }
}
