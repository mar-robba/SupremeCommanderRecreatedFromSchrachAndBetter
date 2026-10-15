using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Air transport side of a brain: drops of land units on islands the army can't walk to (Archipelago) and harass
/// drops on enemy extractors, plus air staging for the air force. Hooks: <see cref="ChooseAir"/> (air factory
/// production, from AiEconomy.ChooseAir), <see cref="TryBuild"/> (engineer jobs, from AiEconomy.TryAssign),
/// <see cref="Think"/> (from AiMilitary.Think).
/// </summary>
public sealed class AiTransport
{
    private readonly AiBrain _b;

    public AiTransport(AiBrain b) => _b = b;

    /// <summary>A transport blueprint the air factory <paramref name="factory"/> should build now, or -1.</summary>
    public int ChooseAir(int factory) => -1;

    /// <summary>Engineer job: air staging. Returns true if an order was issued.</summary>
    public bool TryBuild(int unit, bool isCommander) => false;

    /// <summary>Drops and transport orders (military think).</summary>
    public void Think() { }
}
