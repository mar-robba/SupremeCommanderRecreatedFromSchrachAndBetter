namespace Rts.Sim.AI;

/// <summary>Navy knobs (<see cref="AiNavy"/>). Per-difficulty differences are derived from <see cref="Difficulty"/> in
/// AiNavy (<c>Diff(easy, normal, hard)</c>) on top of these bases, so <c>AI_CFG</c> overrides still apply.</summary>
public sealed partial class AiConfig
{
    /// <summary>Master switch of the navy (naval factories, ships, torpedo defense, sonar).</summary>
    public bool NavyEnabled = false;
    /// <summary>Coast search radius around the base for the naval factory (scaled by sqrt(map size / 256)).</summary>
    public float NavalSiteRadius = 70f;
    /// <summary>Earliest first naval factory when the enemy is also reachable by land (Lakes). Without a land path
    /// (Archipelago) it comes right after the opening.</summary>
    public float NavalFactoryTime = 420f;
    /// <summary>Ships needed before the fleet sails (Easy -1, Hard +1).</summary>
    public int MinFleet = 4;
    /// <summary>Ship cap while the map has naval targets (Easy x0.4, Hard x1.3); 6 when there is nothing to attack.</summary>
    public int MaxFleet = 30;
    /// <summary>The fleet sails after this long at home with at least MinFleet ships, whatever the strength.</summary>
    public float FleetAssembleTimeout = 150f;
    /// <summary>Strength over the estimated enemy naval strength + defenses at the objective needed to sail.</summary>
    public float FleetAttackMargin = 1.2f;
    /// <summary>The fleet goes home when it is below this share of its strength at departure and outgunned.</summary>
    public float FleetRetreatShare = 0.3f;
    /// <summary>Torpedo launchers at the base coast once enemy ships came near (Easy -1, Hard +1).</summary>
    public int MaxTorpedoDefenses = 2;
}
