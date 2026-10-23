namespace Rts.Sim.AI;

/// <summary>Navy knobs (<see cref="AiNavy"/>). Per-difficulty differences are derived from <see cref="Difficulty"/> in
/// AiNavy (<c>Diff(easy, normal, hard)</c>) on top of these bases, so <c>AI_CFG</c> overrides still apply.</summary>
public sealed partial class AiConfig
{
    /// <summary>Master switch of the navy (naval factories, ships, torpedo defense, sonar).</summary>
    public bool NavyEnabled = true;
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

    // ---- drops (AiTransport; MaxTransports is in AiConfig.cs)
    /// <summary>Idle land units needed before the first transport is built (island maps only).</summary>
    public int DropMinUnits = 6;
    /// <summary>One more transport (up to DropTransportCap x MaxTransports) per this many idle land units.</summary>
    public int DropUnitsPerTransport = 12;
    public int DropTransportCap = 3;
    /// <summary>Island maps: above this many idle land units the land and air factories get no budget.</summary>
    public int DropIdleCap = 60;
    /// <summary>Pool units that stay home (base defense) instead of boarding.</summary>
    public int DropKeepHome = 4;
    /// <summary>The landed group attacks with at least DropWave units and DropStrengthShare x the enemy's estimated land
    /// strength, or after DropGatherTime seconds at the landing zone with half a wave.</summary>
    public int DropWave = 16;
    public float DropStrengthShare = 0.7f;
    public float DropGatherTime = 240f;
    /// <summary>Enemy threat in the base (DefenseRadius) that sends the units waiting for a transport back to defend.</summary>
    public float DropDefendThreat = 150f;
    /// <summary>Earliest air staging for the transports (T2 engineers).</summary>
    public float DropStagingTime = 600f;
}
