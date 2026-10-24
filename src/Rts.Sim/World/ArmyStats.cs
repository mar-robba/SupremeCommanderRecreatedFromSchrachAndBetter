namespace Rts.Sim.World;

/// <summary>
/// Per-army game statistics for the end-of-game screen. Updated in sequential sim code only (deterministic):
/// <see cref="Systems.ConstructionSystem.CompleteUnit"/> (built), <see cref="Systems.DamageSystem"/> (killed),
/// <see cref="Systems.CleanupSystem"/> (lost), <see cref="Systems.EconomySystem"/> (collected/spent).
/// Not part of the gameplay state hash.
/// </summary>
public sealed class ArmyStats
{
    /// <summary>Mobile units finished (factories and engineers; the starting commander is not counted).</summary>
    public int UnitsBuilt;
    /// <summary>Structures finished.</summary>
    public int StructuresBuilt;
    /// <summary>Own finished units/structures destroyed (reclaimed or cancelled ones are not counted).</summary>
    public int UnitsLost;
    /// <summary>Enemy units/structures destroyed by this army's weapons.</summary>
    public int UnitsKilled;
    /// <summary>Total mass/energy income (production + reclaim) over the game.</summary>
    public float MassCollected;
    public float EnergyCollected;
    /// <summary>Total mass/energy consumed (construction, upkeep).</summary>
    public float MassSpent;
    public float EnergySpent;
    /// <summary>(6b) Mass/energy given to allies (GiveResourcesCommand + storage overflow shared by EconomyShare).</summary>
    public float MassGiven, EnergyGiven;
    /// <summary>(6b) Mass/energy received from allies (gifts + their overflow).</summary>
    public float MassReceived, EnergyReceived;
    /// <summary>(6b, G6) Score history: one sample every <see cref="ScoreSample.Interval"/> ticks, recorded by
    /// <see cref="Systems.CleanupSystem"/> for every army (defeated ones too, so all histories share the time axis).</summary>
    public readonly List<ScoreSample> History = new();

    /// <summary>
    /// (6b, G6) The score shown in the diplomacy panel and the end-screen graph: a simple sum,
    /// <c>mass collected / 10 + energy collected / 100 + 10 per kill + 2 per unit or structure built</c> (rounded down).
    /// </summary>
    public static int Score(float massCollected, float energyCollected, int kills, int built)
        => (int)MathF.Floor(massCollected / 10f + energyCollected / 100f + 10f * kills + 2f * built);

    /// <summary>The current score of this army (<see cref="Score(float, float, int, int)"/>).</summary>
    public int Score() => Score(MassCollected, EnergyCollected, UnitsKilled, UnitsBuilt + StructuresBuilt);

    /// <summary>Appends this army's sample for <paramref name="tick"/> (sequential sim code).</summary>
    public void Record(int tick, EconomyState eco, int unitCount) => History.Add(Sample(tick, eco, unitCount));

    /// <summary>This army's sample now (also the end screen's live last point).</summary>
    public ScoreSample Sample(int tick, EconomyState eco, int unitCount) => new()
    {
        Tick = tick, MassIncome = eco.MassIncome, EnergyIncome = eco.EnergyIncome, Units = unitCount, Kills = UnitsKilled,
        MassCollected = MassCollected, Score = Score(),
    };
}

/// <summary>(6b, G6) One point of an army's score history (<see cref="ArmyStats.History"/>). Rates per second.</summary>
public struct ScoreSample
{
    /// <summary>Ticks between samples (30 s of game time).</summary>
    public const int Interval = 300;
    public int Tick;
    public float MassIncome, EnergyIncome;
    public int Units, Kills;
    public float MassCollected;
    public int Score;
}
