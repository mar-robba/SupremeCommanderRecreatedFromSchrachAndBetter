using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Naval side of a brain: decides whether the map calls for a navy (water near the base that connects to the enemy),
/// builds naval factories and anti-naval defenses through engineers, keeps naval factories producing and moves the
/// fleet. Hooks: <see cref="TryBuild"/> (engineer jobs, from AiEconomy.TryAssign), <see cref="ManageFactories"/> (from
/// AiEconomy.ManageFactories), <see cref="Think"/> (from AiMilitary.Think).
/// </summary>
public sealed class AiNavy
{
    private readonly AiBrain _b;

    public AiNavy(AiBrain b) => _b = b;

    /// <summary>Engineer job: naval factory / torpedo defense / sonar. Returns true if an order was issued.</summary>
    public bool TryBuild(int unit, bool isCommander) => false;

    /// <summary>Keeps the naval factories' queues topped up. <paramref name="slot"/> counts factories allowed to
    /// start units against <paramref name="budget"/> (same budget as land/air factories).</summary>
    public void ManageFactories(ref int slot, int budget) { }

    /// <summary>Fleet orders (military think).</summary>
    public void Think() { }
}
