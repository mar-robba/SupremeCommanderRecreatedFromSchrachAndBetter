using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Mass/energy flow economy (SupCom style). Runs once per tick, before the consumers:
/// <list type="number">
/// <item>Sums per army (in AliveList order, per-chunk partial sums merged in chunk order = deterministic):
///   storage capacity of complete units, production of complete non-paused units, reclaim income, the requests
///   consumers made LAST tick (<see cref="EconState.MassRequest"/>/<see cref="EconState.EnergyRequest"/>) and
///   upkeep (<c>MaintenanceEnergy</c>) of complete non-paused units.</item>
/// <item>Energy efficiency = min(1, (stored + income·dt) / (demand·dt)). Production of units that need energy upkeep
///   (mass extractors) is scaled by it; then mass efficiency is computed the same way.</item>
/// <item>Each unit gets <see cref="EconState.Efficiency"/> = min of the efficiencies of the resources it requested;
///   consumers apply their work scaled by it, and exactly request·efficiency is deducted from storage.</item>
/// </list>
/// Pay-first model: builders request at tick T, pay at T+1 (here) and apply the work at T+1 (ConstructionSystem),
/// so resources spent always equal work done.
/// </summary>
public static class EconomySystem
{
    // Large chunks: the per-unit work is tiny, so below ~8k units a single inline chunk beats thread dispatch.
    private const int ChunkSize = 8192;

    /// <summary>Per-army partial sums of one chunk.</summary>
    private struct Sums
    {
        public float StorageMass, StorageEnergy;
        public float ProdMass, ProdEnergy;
        /// <summary>Mass production of units with energy upkeep (scaled by energy efficiency).</summary>
        public float ProdMassUpkeep;
        public float DemandMass, DemandEnergy;
        /// <summary>Requests by what the unit asks for (its efficiency depends on it):
        /// mass only, energy only (incl. upkeep), both.</summary>
        public float MassOnly, EnergyOnly, BothMass, BothEnergy;
    }

    private sealed class Scratch
    {
        public Sums[] Partial = [];
        public float[] EffMass = [];
        public float[] EffEnergy = [];
        /// <summary>Units that requested something (need a per-unit efficiency), per chunk.</summary>
        public readonly ChunkedBuffer<int> Consumers = new();
    }

    // One scratch per world (systems are static; worlds may coexist, e.g. in tests).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SimWorld, Scratch> _scratch = new();

    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        ref var e = ref w.Units.Econ[i];
        e = default;
        e.Efficiency = 1;
    }

    /// <summary>True if the unit currently produces / pays upkeep (complete and not paused). A paused structure with
    /// an in-place upgrade in progress only pauses the upgrade: it keeps producing (SupCom).</summary>
    public static bool IsActiveProducer(UnitStore u, int i)
    {
        var f = u.Flags[i];
        if ((f & (UnitFlags.UnderConstruction | UnitFlags.Dead)) != 0) return false;
        return (f & UnitFlags.Paused) == 0 || u.Upgrade[i].Active;
    }

    /// <summary>Energy upkeep (per second) the unit pays this tick: blueprint MaintenanceEnergy, none while a shield
    /// is collapsed/recharging (SupCom: a down shield costs nothing), reduced by adjacency (<see cref="EconState.AdjEnergyUse"/>).</summary>
    public static float Upkeep(SimWorld w, Strategic.StrategicState strat, int i, UnitBlueprint bp)
    {
        float upkeep = bp.Economy.MaintenanceEnergy;
        if (upkeep <= 0) return 0;
        if (bp.Shield != null && !strat.Shield[i].Up) return 0;
        return upkeep * (1f - w.Units.Econ[i].AdjEnergyUse);
    }

    public static void Run(SimWorld w)
    {
        var u = w.Units;
        int armies = w.Armies.Length;
        int n = u.AliveCount;
        int chunks = Par.ChunkCount(n, ChunkSize);
        var s = _scratch.GetValue(w, _ => new Scratch());
        int need = Math.Max(1, chunks) * armies;
        if (s.Partial.Length < need) s.Partial = new Sums[need];
        if (s.EffMass.Length < armies) { s.EffMass = new float[armies]; s.EffEnergy = new float[armies]; }
        var partial = s.Partial;
        Array.Clear(partial, 0, need);
        var consumers = s.Consumers;
        consumers.Reset(chunks);
        var strat = Strategic.StrategicState.Of(w);

        // Pass 1 (parallel, each unit writes only its own Econ): storage, production, demand.
        Par.For(n, ChunkSize, (c, start, end) =>
        {
            int baseIdx = c * armies;
            var list = consumers[c];
            for (int k = start; k < end; k++)
            {
                int i = u.AliveList[k];
                ref var sum = ref partial[baseIdx + u.Army[i]];
                ref var e = ref u.Econ[i];
                var bpi = w.BpOf(i);
                var eco = bpi.Economy;
                float upkeep = 0;
                float prodMass = 0, prodEnergy = 0;
                if ((u.Flags[i] & UnitFlags.UnderConstruction) == 0)
                {
                    sum.StorageMass += eco.StorageMass;
                    sum.StorageEnergy += eco.StorageEnergy;
                    if (IsActiveProducer(u, i))
                    {
                        upkeep = Upkeep(w, strat, i, bpi);
                        prodEnergy = eco.ProductionEnergy * (1f + e.AdjEnergyProd);
                        if (eco.MaintenanceEnergy > 0) sum.ProdMassUpkeep += eco.ProductionMass * (1f + e.AdjMassProd);
                        else prodMass = eco.ProductionMass * (1f + e.AdjMassProd);
                    }
                }
                prodMass += e.ReclaimMass;
                prodEnergy += e.ReclaimEnergy;
                sum.ProdMass += prodMass;
                sum.ProdEnergy += prodEnergy;
                e.MassProduction = prodMass; // upkeep-dependent production is added in pass 2
                e.EnergyProduction = prodEnergy;

                float reqM = e.MassRequest;
                float reqE = e.EnergyRequest * (1f - e.AdjEnergyUse) + upkeep; // adjacency: pgens cut energy use
                sum.DemandMass += reqM;
                sum.DemandEnergy += reqE;
                if (reqM > 0 && reqE > 0) { sum.BothMass += reqM; sum.BothEnergy += reqE; }
                else if (reqM > 0) sum.MassOnly += reqM;
                else if (reqE > 0) sum.EnergyOnly += reqE;
                else { e.Efficiency = 1; continue; }
                list.Add(i);
            }
        });

        float dt = SimWorld.Dt;
        for (int a = 0; a < armies; a++)
        {
            var t = Merge(partial, chunks, armies, a);
            var eco = w.Armies[a].Eco;
            eco.MassMax = t.StorageMass;
            eco.EnergyMax = t.StorageEnergy;
            float effE = Efficiency(eco.Energy, t.ProdEnergy, t.DemandEnergy, dt);
            float incomeMass = t.ProdMass + t.ProdMassUpkeep * effE;
            float effM = Efficiency(eco.Mass, incomeMass, t.DemandMass, dt);
            float effBoth = MathF.Min(effM, effE);
            eco.EnergyIncome = t.ProdEnergy;
            eco.MassIncome = incomeMass;
            eco.EnergyDemand = t.DemandEnergy;
            eco.MassDemand = t.DemandMass;
            eco.EnergyEfficiency = effE;
            eco.MassEfficiency = effM;
            s.EffMass[a] = effM;
            s.EffEnergy[a] = effE;
            float consumedMass = t.MassOnly * effM + t.BothMass * effBoth;
            float consumedEnergy = t.EnergyOnly * effE + t.BothEnergy * effBoth;
            eco.Mass = Math.Clamp(eco.Mass + (incomeMass - consumedMass) * dt, 0, eco.MassMax);
            eco.Energy = Math.Clamp(eco.Energy + (t.ProdEnergy - consumedEnergy) * dt, 0, eco.EnergyMax);
            var stats = w.Armies[a].Stats;
            stats.MassCollected += incomeMass * dt;
            stats.EnergyCollected += t.ProdEnergy * dt;
            stats.MassSpent += consumedMass * dt;
            stats.EnergySpent += consumedEnergy * dt;
        }

        // Pass 2 (only units that requested something): per-unit efficiency, upkeep-scaled production.
        for (int c = 0; c < consumers.ChunkCount; c++)
        {
            foreach (int i in consumers[c])
            {
                int a = u.Army[i];
                ref var e = ref u.Econ[i];
                float eff = 1;
                if (e.MassRequest > 0) eff = s.EffMass[a];
                var eco = w.BpOf(i).Economy;
                bool active = eco.MaintenanceEnergy > 0 && IsActiveProducer(u, i);
                bool upkeep = active && Upkeep(w, strat, i, w.BpOf(i)) > 0;
                if (e.EnergyRequest > 0 || upkeep) eff = MathF.Min(eff, s.EffEnergy[a]);
                e.Efficiency = eff;
                if (active) e.MassProduction += eco.ProductionMass * (1f + e.AdjMassProd) * s.EffEnergy[a];
            }
        }
    }

    /// <summary>min(1, (stored + income·dt) / (demand·dt)); 1 when there is no demand.</summary>
    public static float Efficiency(float stored, float income, float demand, float dt)
    {
        if (demand <= 0) return 1;
        float avail = stored + income * dt;
        return avail >= demand * dt ? 1 : MathF.Max(0, avail / (demand * dt));
    }

    private static Sums Merge(Sums[] partial, int chunks, int armies, int a)
    {
        var t = new Sums();
        for (int c = 0; c < chunks; c++)
        {
            ref var p = ref partial[c * armies + a];
            t.StorageMass += p.StorageMass;
            t.StorageEnergy += p.StorageEnergy;
            t.ProdMass += p.ProdMass;
            t.ProdEnergy += p.ProdEnergy;
            t.ProdMassUpkeep += p.ProdMassUpkeep;
            t.DemandMass += p.DemandMass;
            t.DemandEnergy += p.DemandEnergy;
            t.MassOnly += p.MassOnly;
            t.EnergyOnly += p.EnergyOnly;
            t.BothMass += p.BothMass;
            t.BothEnergy += p.BothEnergy;
        }
        return t;
    }
}
