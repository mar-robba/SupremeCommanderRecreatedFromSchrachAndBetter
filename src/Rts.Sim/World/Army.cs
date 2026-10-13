using Rts.Sim.Core;

namespace Rts.Sim.World;

/// <summary>Per-army resource state. Rates are per second; Mass/Energy are stored amounts.</summary>
public sealed class EconomyState
{
    public float Mass;
    public float MassMax;
    public float Energy;
    public float EnergyMax;
    /// <summary>Total production per second.</summary>
    public float MassIncome;
    public float EnergyIncome;
    /// <summary>Total requested consumption per second.</summary>
    public float MassDemand;
    public float EnergyDemand;
    /// <summary>Fraction (0..1) of demand that can be satisfied this tick. Consumers scale their work by it.</summary>
    public float MassEfficiency = 1;
    public float EnergyEfficiency = 1;
}

/// <summary>Skirmish AI difficulty (reaction speed, build efficiency, attack timing, micro).</summary>
public enum AiDifficulty { Easy, Normal, Hard }

/// <summary>A player (human or AI). Index is used everywhere as the army id.</summary>
public sealed class Army
{
    public int Index;
    public string Name = "";
    /// <summary>Armies with the same team are allies.</summary>
    public int Team;
    /// <summary>RGBA packed as 0xRRGGBBAA.</summary>
    public uint Color = 0xFFFFFFFF;
    /// <summary>Faction id (data/factions, <c>BlueprintDb.Factions</c>); set by GameSetup from the player slot.</summary>
    public string Faction = "uef";
    public bool IsAI;
    /// <summary>Difficulty of the skirmish AI controlling this army (only used when IsAI).</summary>
    public AiDifficulty AiDifficulty = AiDifficulty.Normal;
    public bool Defeated;
    public UnitHandle Commander = UnitHandle.None;
    public int UnitCap = 1000;
    /// <summary>Live units owned (updated each tick).</summary>
    public int UnitCount;
    public readonly EconomyState Eco = new();
    /// <summary>Game statistics (end screen).</summary>
    public readonly ArmyStats Stats = new();
}
