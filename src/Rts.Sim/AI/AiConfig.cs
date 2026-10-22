using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Tuning knobs of the skirmish AI, derived from <see cref="AiDifficulty"/>. Difficulty scales reaction speed
/// (think intervals), build efficiency (engineer/factory caps, idle pickup), army handling (assembling before an
/// attack, reinforcing, raids, retreats, commander snipes) and intel use (scouts, radar). Every difficulty plays by
/// the fog-of-war rules and none gets free resources: Hard is stronger through better macro and decisions only.
/// </summary>
public sealed partial class AiConfig
{
    public AiDifficulty Difficulty;

    // ---- reaction speed
    /// <summary>Ticks between economy thinks (engineers, factories).</summary>
    public int EconInterval = 5;
    /// <summary>Ticks between military thinks (pool, army, defense).</summary>
    public int MilitaryInterval = 10;
    /// <summary>Chance per economy think that an idle engineer gets a new job (build efficiency; 1 = always).</summary>
    public float IdlePickupChance = 1f;

    // ---- economy
    public int MaxEngineers = 10;
    public int EarlyEngineers = 4;
    public int MaxLandFactories = 6;
    /// <summary>Land factory cap while mass piles up in storage and the income can feed them (~4.5 mass/s each).</summary>
    public int MaxLandFactoriesFloat = 14;
    public int MaxAirFactories = 1;
    /// <summary>Game time (s) from which an air factory may be built (earlier if the enemy has air).</summary>
    public float AirFactoryTime = 360;
    /// <summary>Multiplier on the projected energy need (higher = more power headroom).</summary>
    public float PowerMargin = 0.8f;
    /// <summary>Extra deposits queued after the first one when an engineer expands (mex chains).</summary>
    public int MexChain = 1;
    public int MaxMassStorage = 2;
    public int MaxEnergyStorage = 2;
    public int BasePointDefense = 2;
    public int BaseAntiAir = 3;
    public int ExpansionPointDefense = 3;
    /// <summary>Factory queue length the AI keeps topped up.</summary>
    public int QueueDepth = 2;
    /// <summary>Max distance from the base center the commander builds at after the opening.</summary>
    public float CommanderRadius = 32;
    /// <summary>Mass spending the AI plans for, as a multiple of its mass income (factories beyond it pause).</summary>
    public float SpendRatio = 1.15f;

    // ---- intel
    /// <summary>Land scouts kept alive (rebuilt when lost, at most one every <see cref="ScoutRebuildTime"/> s).</summary>
    public int LandScouts = 1;
    /// <summary>Air scouts kept alive once an air factory exists.</summary>
    public int AirScouts = 1;
    public float ScoutRebuildTime = 90;
    /// <summary>Radars (base first, then expansions not covered yet) and the game time from which they are built.</summary>
    public int MaxRadars = 2;
    public float RadarTime = 200;

    // ---- military
    /// <summary>Earliest game time (s) of the first attack.</summary>
    public float FirstAttackTime = 240;
    /// <summary>Armed land units needed to form the army; the minimum attack size grows by WaveGrowth per attack up to MaxWave.</summary>
    public int FirstWave = 8;
    public int WaveGrowth = 3;
    public int MaxWave = 40;
    /// <summary>Army size at which it attacks whatever the estimated enemy strength.</summary>
    public int MaxArmy = 110;
    /// <summary>The army attacks when its strength is this multiple of the estimated enemy strength at the objective.</summary>
    public float AttackMargin = 1.3f;
    /// <summary>Share of the estimated enemy land army (away from the objective) the attack must also outweigh: it
    /// may come to help the defenders.</summary>
    public float EnemyArmyShare = 0.35f;
    /// <summary>The army gathers this far from its objective (outside the known defenses) before attacking (0 = no staging).</summary>
    public float StageDistance = 45f;
    /// <summary>Seconds the army may wait at the staging point for stragglers/strength before attacking anyway.</summary>
    public float AssembleTimeout = 150f;
    /// <summary>The attacking army retreats when its strength drops below this fraction of the initial one (0 = never).</summary>
    public float RetreatFraction = 0f;
    /// <summary>Units under this health fraction leave their group and go home (0 = off).</summary>
    public float PullBackHealth;
    /// <summary>Enemies closer than this to the base center are a base threat.</summary>
    public float DefenseRadius = 55;
    /// <summary>Commander retreats to the base when under this health fraction and enemies are close.</summary>
    public float CommanderRetreatHealth = 0.45f;
    /// <summary>Game time (s) after which the army goes for the enemy base/commander rather than outlying structures.</summary>
    public float PushTime = 900;
    /// <summary>Game time (s) after which the AI plays to end the game: no retreats, bigger staging tolerance, all-in.</summary>
    public float LateGameTime = 1320;
    public int BomberGroup = 4;
    /// <summary>Send fresh pool units to the army while it attacks (otherwise they wait for it to assemble again).</summary>
    public bool Reinforce = true;
    /// <summary>Reinforcements for an attacking army leave home in batches of at least this many units, or this
    /// fraction of the army, or after ReinforceWait seconds.</summary>
    public int ReinforceBatch = 6;
    public float ReinforceBatchFraction;
    public float ReinforceWait = 35f;
    /// <summary>An attack on a position (not a unit) moves on after this many seconds when the group dominates
    /// there (0 = only when it idles there).</summary>
    public float PositionTargetTime = 60f;
    /// <summary>Share of artillery in the land mix (after the opening).</summary>
    public float ArtilleryShare = 0.25f;
    /// <summary>Raiding parties against weakly defended enemy extractors (size, max simultaneous; 0 = none).</summary>
    public int RaidSize = 0;
    public int MaxRaids = 0;
    /// <summary>Attack the enemy commander directly when it is seen near the army and the army can take it.</summary>
    public bool CommanderSnipe;
    /// <summary>Snipes look for the enemy commander this far from the army.</summary>
    public float SnipeRadius = 40f;
    /// <summary>Indirect-fire members (missile launchers, artillery) join the snipe too (wave 5k: enhanced commanders
    /// with shields and engineering HP outlast the few direct-fire units of an artillery-heavy army).</summary>
    public bool SnipeIndirect;
    /// <summary>Micro: direct-fire units of a fighting group spread their fire over the weakest enemies in range
    /// (enough damage on each to kill it, then the next) instead of each shooting the nearest one.</summary>
    public bool FocusFire;
    /// <summary>Seconds of a shooter's damage counted against a focus target before the next shooter picks another.</summary>
    public float FocusHorizon = 3f;

    // ---- tech, strategic, navy, transports (wave 5k). Times are game seconds.
    /// <summary>Highest tech level the AI goes for (factories, engineers, structures).</summary>
    public int MaxTier = 3;
    /// <summary>Extractor upgrades start (T1→T2, T2→T3) once the game is this old.</summary>
    public float MexT2Time = 330, MexT3Time = 900;
    /// <summary>Factory upgrades (first factory of the tier; more follow with income).</summary>
    public float LandT2Time = 480, LandT3Time = 1020, AirT2Time = 720, AirT3Time = 1200;
    /// <summary>Share of the mass income that running upgrades may take.</summary>
    public float UpgradeShare = 0.35f;
    /// <summary>Share of the mass income kept for the factories: eco/tech commitments wait while the rest is used
    /// (AiEconomy.Budget.cs).</summary>
    public float ArmyShare = 0.5f;
    /// <summary>ArmyShare while pressured: own land strength below PressureRatio x the enemy's, or enemies at the base.</summary>
    public float PressureArmyShare = 0f, PressureRatio = 0.6f; // off: 0.85 made NvN worse (40/96 vs 58), AI_TUNING
    /// <summary>More land factories (up to MaxLandFactoriesFloat) after CapacityTime while their capacity is below
    /// ArmyShare x CapacityMargin x mass income.</summary>
    public float CapacityTime = 420, CapacityMargin = 1.2f;
    /// <summary>Floating also when stored mass exceeds max(FloatMass, FloatSeconds x income) (big storages).</summary>
    public float FloatMass = 1200, FloatSeconds = 30;
    /// <summary>Highest tier of combat units the factories make (experiments; engineers/structures use MaxTier).</summary>
    public int MaxUnitTier = 3;
    /// <summary>Highest tier of the artillery role (T2 missile launchers and T3 artillery outrange the rest of the army).</summary>
    public int ArtilleryTier = 3;
    /// <summary>Attrition stall (AiMilitary.FallBack): an attacking army that has not closed in on its objective for
    /// StallTime seconds while weaker than StallMargin x the strength the objective needs gathers again. 0 = off.</summary>
    public float StallTime = 0, StallMargin = 1f;
    public int MaxMexUpgrades = 2;
    public int MaxFactoryUpgrades = 1;
    /// <summary>Commander enhancements start (engineering first, then shield).</summary>
    public float EnhanceTime = 540;
    /// <summary>Base shields, TML, artillery installations, mass storage next to upgraded extractors.</summary>
    public float ShieldTime = 780;
    public int MaxShields = 3;
    public float TmlTime = 840;
    public int MaxTml = 1;
    /// <summary>Nukes, experimentals, quantum gateway (also need a big mass income).</summary>
    public float StrategicTime = 1260;
    public int MaxScus = 2;
    /// <summary>Navy: frigates/subs vs destroyers/cruisers mix cap; 0 = no navy.</summary>
    public int MaxNavalFactories = 2;
    /// <summary>Transport drops (0 = never).</summary>
    public int MaxTransports = 2;

    public static AiConfig For(AiDifficulty d) => d switch
    {
        AiDifficulty.Easy => new AiConfig
        {
            Difficulty = d,
            EconInterval = 10, MilitaryInterval = 25, IdlePickupChance = 0.5f,
            MaxEngineers = 5, EarlyEngineers = 2, MaxLandFactories = 2, MaxLandFactoriesFloat = 3, MaxAirFactories = 1, AirFactoryTime = 720,
            PowerMargin = 0.7f, MexChain = 0, MaxMassStorage = 1, MaxEnergyStorage = 1,
            BasePointDefense = 1, BaseAntiAir = 1, ExpansionPointDefense = 0, QueueDepth = 1,
            LandScouts = 1, AirScouts = 0, ScoutRebuildTime = 240, MaxRadars = 1, RadarTime = 480,
            FirstAttackTime = 480, FirstWave = 10, WaveGrowth = 2, MaxWave = 24, MaxArmy = 30,
            AttackMargin = 1f, StageDistance = 0, AssembleTimeout = 60,
            RetreatFraction = 0, PullBackHealth = 0, DefenseRadius = 40, CommanderRetreatHealth = 0,
            PushTime = 1500, LateGameTime = 1800, BomberGroup = 3, Reinforce = false,
            MaxTier = 2, MexT2Time = 600, MexT3Time = 99999, LandT2Time = 900, AirT2Time = 1500, UpgradeShare = 0.25f,
            MaxMexUpgrades = 1, EnhanceTime = 1200, ShieldTime = 1500, MaxShields = 1, TmlTime = 99999, MaxTml = 0,
            StrategicTime = 99999, MaxScus = 0, MaxNavalFactories = 1, MaxTransports = 0,
        },
        AiDifficulty.Hard => new AiConfig
        {
            // Hard = faster reactions, a stronger opening, an artillery-heavy mix, raids, snipes and micro - no bonuses.
            Difficulty = d,
            EconInterval = 3, MilitaryInterval = 6, IdlePickupChance = 1f,
            MaxEngineers = 12, EarlyEngineers = 5, MaxLandFactories = 7, MaxLandFactoriesFloat = 16, MaxAirFactories = 1, AirFactoryTime = 330,
            PowerMargin = 0.8f, MexChain = 1, MaxMassStorage = 2, MaxEnergyStorage = 1,
            BasePointDefense = 2, BaseAntiAir = 3, ExpansionPointDefense = 4, QueueDepth = 2, SpendRatio = 1.1f,
            LandScouts = 2, AirScouts = 1, ScoutRebuildTime = 60, MaxRadars = 3, RadarTime = 150,
            FirstAttackTime = 200, FirstWave = 8, WaveGrowth = 3, MaxWave = 45, MaxArmy = 130,
            AttackMargin = 1.25f, StageDistance = 45f, AssembleTimeout = 120,
            RetreatFraction = 0, PullBackHealth = 0.25f, DefenseRadius = 65, CommanderRetreatHealth = 0.5f,
            PushTime = 840, LateGameTime = 1140, BomberGroup = 5, Reinforce = true,
            RaidSize = 6, MaxRaids = 1, CommanderSnipe = true, FocusFire = true,
            MexT2Time = 300, MexT3Time = 840, LandT2Time = 420, LandT3Time = 900, AirT2Time = 660, AirT3Time = 1080,
            UpgradeShare = 0.4f, MaxMexUpgrades = 3, MaxFactoryUpgrades = 2, EnhanceTime = 480, ShieldTime = 720,
            MaxShields = 4, TmlTime = 720, MaxTml = 2, StrategicTime = 1140, MaxScus = 3, MaxNavalFactories = 3, MaxTransports = 3,
            ArtilleryShare = 0.55f, // matrix: 0.15 -> 36 % wins vs Normal, 0.25 -> 57 %, 0.4 -> 78 %, 0.55 -> 86 %
        },
        _ => new AiConfig { Difficulty = AiDifficulty.Normal },
    };
}
