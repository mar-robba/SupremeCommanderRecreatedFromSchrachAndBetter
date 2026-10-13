using Rts.Client.Menus;
using Rts.Sim;

namespace Rts.Client;

/// <summary>
/// <c>--scene transport</c>: screenshot/test scene for transports, ferries, air staging and the Atlantis.
/// </summary>
public static class TransportScene
{
    public const string Name = "transport";

    /// <summary>Scene defaults for options not given on the command line (call after parsing).</summary>
    public static void ApplyDefaults(ClientOptions o)
    {
        if (o.Scene != Name) throw new ArgumentException("unknown scene " + o.Scene + " (transport)");
        o.Players = 2;
        o.NoFog = true;
    }

    /// <summary>Spawns the scene into a freshly created skirmish (before the runner starts) and sets
    /// <see cref="GameConfig.SceneCamera"/>.</summary>
    public static void Spawn(Simulation sim, GameConfig cfg)
    {
        // TODO (transport visuals agent)
    }
}
