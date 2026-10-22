namespace Rts.Sim.AI;

/// <summary>
/// Army-first mass budget (wave 5k): the factories get at least <see cref="AiConfig.ArmyShare"/> of the mass income,
/// and new eco/tech commitments (upgrades, enhancements, assisting upgrades, big projects) start only while what the
/// army already spends outside its factories leaves room. Without it, T2/T3 engineers (build rate 12.5 / 30),
/// extractor and factory upgrades and commander enhancements took ~60 % of the income and the armies stayed half
/// the size of the T1 AI's, so games stalled (docs/AI_TUNING.md "Tech AI stalemates").
/// </summary>
public sealed partial class AiEconomy
{
    /// <summary>Mass per second busy factories draw (estimate: unit mass per build time is ~0.2 for every tier).</summary>
    public float FactorySpend;
    /// <summary>Mass per second requested by everything else (engineers, upgrades, enhancements): smoothed demand
    /// minus <see cref="FactorySpend"/>.</summary>
    public float EcoSpend;
    /// <summary>Mass per second the land and air factories could draw when all are busy.</summary>
    public float FactoryCapacity;

    private void UpdateBudget()
    {
        float fac = 0;
        foreach (int f in V.LandFactories) fac += FactoryDraw(f);
        foreach (int f in V.AirFactories) fac += FactoryDraw(f);
        foreach (int f in V.NavalFactories) fac += FactoryDraw(f);
        foreach (int f in V.Gateways) fac += FactoryDraw(f);
        FactorySpend = fac;
        float cap = 0;
        foreach (int f in V.LandFactories) cap += FactoryWeight(f) * 4f;
        foreach (int f in V.AirFactories) cap += FactoryWeight(f) * 4f;
        FactoryCapacity = cap;
        EcoSpend = MathF.Max(0f, MassDemand - fac);
        // Losing the land war (or enemies at the base): tech waits, the mass goes to the army.
        float enemy = MathF.Max(V.EnemyLandStrength, _b.Perception.EnemyLandEstimate);
        Pressured = _b.GameTime > 300 && (V.OwnLandStrength < Cfg.PressureRatio * enemy
                                          || V.ThreatAt(_b.BasePos, Cfg.DefenseRadius) > V.OwnLandStrength * 0.5f + 50f);
    }

    /// <summary>The enemy land army looks much stronger than ours, or threatens the base (see UpdateBudget).</summary>
    public bool Pressured;

    /// <summary>Share of the income kept for the factories now.</summary>
    public float ArmyShareNow => Pressured ? MathF.Max(Cfg.ArmyShare, Cfg.PressureArmyShare) : Cfg.ArmyShare;

    private float FactoryDraw(int f)
    {
        var u = W.Units;
        if (!u.IsComplete(f) || u.Upgrade[f].Active || u.Factory[f].Queue is not { Count: > 0 }) return 0f;
        return FactoryWeight(f) * 4f;
    }

    /// <summary>Mass per second still free for eco/tech after the army's share of the income (stored mass above a
    /// reserve adds to it, so a full storage gets spent).</summary>
    public float EcoRoom
    {
        get
        {
            float stored = W.Armies[_b.Army].Eco.Mass;
            return MassIncome * (1f - ArmyShareNow) + MathF.Max(0f, stored - 400f) / 60f - EcoSpend;
        }
    }
}
