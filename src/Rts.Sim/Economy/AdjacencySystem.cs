using System.Runtime.CompilerServices;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// SupCom adjacency bonus (rules in <see cref="AdjacencyRules"/>): writes <see cref="EconState.AdjMassProd"/>,
/// <see cref="EconState.AdjEnergyProd"/> and <see cref="EconState.AdjEnergyUse"/> of every structure.
/// SEQUENTIAL, once per tick right before <see cref="EconomySystem"/>.
/// <para>Change detection (≈ zero cost while nothing changes): the list of alive structures that can give or
/// receive a bonus is rebuilt only when <see cref="OccupancyGrid.Version"/> changes (a structure was placed,
/// destroyed or re-stamped by an upgrade); every tick a 64-bit hash of (index, generation, blueprint, army,
/// active, complete) over that short list is compared with the last one. Only when it differs (build complete,
/// death, capture/give, upgrade blueprint swap, sandbox spawn) are all bonuses recomputed: for each complete
/// receiver, the ring of cells around its footprint (corners excluded) is scanned in a fixed order for distinct
/// complete same-army neighbours, whose <see cref="AdjacencyRules.Effect"/>s are summed (energy-use reduction
/// capped at <see cref="AdjacencyRules.MaxEnergyUseReduction"/>).</para>
/// The per-world state is a pure cache (rebuilt from scratch when missing, e.g. after loading a save).
/// </summary>
public static class AdjacencySystem
{
    private sealed class State
    {
        public int OccupancyVersion = -1;
        public ulong Hash;
        public bool Valid;
        public int Recomputes;
        /// <summary>Alive participating structures (any construction state), ascending index.</summary>
        public readonly List<int> Candidates = new();
        /// <summary>Distinct neighbours of the current receiver (scratch).</summary>
        public readonly List<int> Neighbours = new();
    }

    private static readonly ConditionalWeakTable<SimWorld, State> _states = new();

    /// <summary>Number of full recomputations so far for this world (tests / profiling).</summary>
    public static int RecomputeCount(SimWorld w) => _states.TryGetValue(w, out var s) ? s.Recomputes : 0;

    public static void Run(SimWorld w)
    {
        var s = _states.GetValue(w, static _ => new State());
        var u = w.Units;
        if (s.OccupancyVersion != w.Occupancy.Version)
        {
            s.OccupancyVersion = w.Occupancy.Version;
            s.Candidates.Clear();
            for (int k = 0; k < u.AliveCount; k++)
            {
                int i = u.AliveList[k];
                if (u.IsActive(i) && AdjacencyRules.Participates(w.BpOf(i))) s.Candidates.Add(i);
            }
        }

        ulong h = 1469598103934665603UL;
        foreach (int i in s.Candidates)
        {
            ulong v = (ulong)(uint)i | (ulong)(uint)u.Gen[i] << 32;
            h = (h ^ v) * 1099511628211UL;
            ulong flags = (u.IsActive(i) ? 1UL : 0) | (u.IsComplete(i) ? 2UL : 0);
            v = (ulong)(uint)u.Bp[i] | ((ulong)(uint)u.Army[i] << 32) | (flags << 62);
            h = (h ^ v) * 1099511628211UL;
        }
        if (s.Valid && h == s.Hash) return;
        s.Valid = true;
        s.Hash = h;
        Recompute(w, s);
    }

    private static void Recompute(SimWorld w, State s)
    {
        s.Recomputes++;
        var u = w.Units;
        foreach (int i in s.Candidates)
        {
            ref var e = ref u.Econ[i];
            e.AdjMassProd = e.AdjEnergyProd = e.AdjEnergyUse = 0;
        }
        foreach (int r in s.Candidates)
        {
            if (!Counts(u, r)) continue;
            var rbp = w.BpOf(r);
            var (x0, z0, x1, z1) = OccupancyGrid.Footprint(rbp, u.Pos[r]);
            var nb = s.Neighbours;
            nb.Clear();
            int army = u.Army[r];
            for (int x = x0; x <= x1; x++) { Consider(w, r, army, x, z0 - 1, nb); Consider(w, r, army, x, z1 + 1, nb); }
            for (int z = z0; z <= z1; z++) { Consider(w, r, army, x0 - 1, z, nb); Consider(w, r, army, x1 + 1, z, nb); }
            if (nb.Count == 0) continue;
            float mass = 0, energy = 0, use = 0;
            foreach (int g in nb)
            {
                var fx = AdjacencyRules.Effect(w.BpOf(g), u.Pos[g], rbp, u.Pos[r]);
                mass += fx.MassProd;
                energy += fx.EnergyProd;
                use += fx.EnergyUse;
            }
            ref var e = ref u.Econ[r];
            e.AdjMassProd = mass;
            e.AdjEnergyProd = energy;
            e.AdjEnergyUse = MathF.Min(use, AdjacencyRules.MaxEnergyUseReduction);
        }
    }

    private static bool Counts(UnitStore u, int i) => u.IsActive(i) && u.IsComplete(i);

    private static void Consider(SimWorld w, int r, int army, int x, int z, List<int> nb)
    {
        int j = w.Occupancy.At(x, z);
        if (j < 0 || j == r || nb.Contains(j)) return;
        var u = w.Units;
        if (!Counts(u, j) || u.Army[j] != army || !AdjacencyRules.Participates(w.BpOf(j))) return;
        nb.Add(j);
    }
}
