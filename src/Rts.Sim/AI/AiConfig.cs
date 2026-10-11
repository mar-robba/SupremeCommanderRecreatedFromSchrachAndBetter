using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Tuning knobs of the skirmish AI, derived from <see cref="AiDifficulty"/>. Difficulty scales reaction speed
/// (think intervals), build efficiency (engineer/factory caps, idle pickup), attack timing (wave sizes) and micro
/// (retreats, pulling back damaged units). The AI never cheats on resources.
/// </summary>
public sealed class AiConfig
{
    public AiDifficulty Difficulty;

    // ---- reaction speed
    /// <summary>Ticks between economy thinks (engineers, factories).</summary>
    public int EconInterval = 5;
    /// <summary>Ticks between military thinks (pool, waves, defense).</summary>
    public int MilitaryInterval = 10;
    /// <summary>Chance per economy think that an idle engineer gets a new job (build efficiency; 1 = always).</summary>
    public float IdlePickupChance = 1f;

    // ---- economy
    public int MaxEngineers = 10;
    public int EarlyEngineers = 4;
    public int MaxLandFactories = 6;
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

    // ---- military
    /// <summary>Earliest game time (s) of the first attack wave.</summary>
    public float FirstAttackTime = 240;
    /// <summary>Armed land units needed for the first wave; later waves grow by WaveGrowth up to MaxWave.</summary>
    public int FirstWave = 8;
    public int WaveGrowth = 3;
    public int MaxWave = 40;
    /// <summary>Groups retreat when their strength drops below this fraction of the initial one (0 = never).</summary>
    public float RetreatFraction = 0.3f;
    /// <summary>Units under this health fraction leave their group and go home (0 = off).</summary>
    public float PullBackHealth;
    /// <summary>Enemies closer than this to the base center are a base threat.</summary>
    public float DefenseRadius = 55;
    /// <summary>Commander retreats to the base when under this health fraction and enemies are close.</summary>
    public float CommanderRetreatHealth = 0.45f;
    /// <summary>Game time (s) after which waves aim for the enemy base/commander rather than outlying structures.</summary>
    public float PushTime = 900;
    public int BomberGroup = 4;
    /// <summary>Send fresh pool units to reinforce an attacking wave that has lost strength (concentration of force).</summary>
    public bool Reinforce;

    public static AiConfig For(AiDifficulty d) => d switch
    {
        AiDifficulty.Easy => new AiConfig
        {
            Difficulty = d,
            EconInterval = 10, MilitaryInterval = 25, IdlePickupChance = 0.5f,
            MaxEngineers = 5, EarlyEngineers = 2, MaxLandFactories = 2, MaxAirFactories = 1, AirFactoryTime = 720,
            PowerMargin = 0.7f, MexChain = 0, MaxMassStorage = 1, MaxEnergyStorage = 1,
            BasePointDefense = 1, BaseAntiAir = 1, ExpansionPointDefense = 0, QueueDepth = 1,
            FirstAttackTime = 480, FirstWave = 10, WaveGrowth = 2, MaxWave = 24,
            RetreatFraction = 0, PullBackHealth = 0, DefenseRadius = 40, CommanderRetreatHealth = 0,
            PushTime = 1500, BomberGroup = 3,
        },
        AiDifficulty.Hard => new AiConfig
        {
            // Hard = faster reactions, longer mex chains, earlier/steadier pressure and micro - not a bigger base.
            Difficulty = d,
            EconInterval = 3, MilitaryInterval = 6, IdlePickupChance = 1f,
            MaxEngineers = 12, EarlyEngineers = 4, MaxLandFactories = 7, MaxAirFactories = 1, AirFactoryTime = 360,
            PowerMargin = 0.8f, MexChain = 2, MaxMassStorage = 2, MaxEnergyStorage = 1,
            BasePointDefense = 2, BaseAntiAir = 3, ExpansionPointDefense = 4, QueueDepth = 2,
            FirstAttackTime = 200, FirstWave = 8, WaveGrowth = 3, MaxWave = 50,
            RetreatFraction = 0.35f, PullBackHealth = 0.25f, DefenseRadius = 65, CommanderRetreatHealth = 0.5f,
            PushTime = 900, BomberGroup = 5, Reinforce = true,
        },
        _ => new AiConfig { Difficulty = AiDifficulty.Normal },
    };
}
