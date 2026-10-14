using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Systems;

namespace Rts.Client.Input;

/// <summary>One touching structure of the placement preview: what the ghost gives it and what it gives the ghost.</summary>
public readonly record struct AdjacencyLink(int Id, Vector2 Pos, UnitBlueprint Bp, AdjacencyEffect Gives, AdjacencyEffect Receives);

/// <summary>Bonuses of a structure about to be placed: the links to touching structures and the ghost's total.</summary>
public sealed class AdjacencyPreviewResult
{
    public readonly List<AdjacencyLink> Links = new();
    /// <summary>Total bonus the new structure would receive (energy-use reduction capped like the sim).</summary>
    public AdjacencyEffect Received;
    /// <summary>Touching structures that would receive a bonus from the new one.</summary>
    public int Boosted;

    public bool IsEmpty => Links.Count == 0;
}

/// <summary>
/// Placement preview of SupCom adjacency (<see cref="AdjacencyRules"/>): which own structures touching the ghost
/// would give it a bonus or get one from it. Pure (no raylib, no snapshot): the caller passes the own structures
/// near the ghost. Used by <see cref="BuildPlacement"/> (lines + summary near the ghost) and unit-tested.
/// </summary>
public static class AdjacencyPreview
{
    public static AdjacencyPreviewResult Compute(UnitBlueprint ghost, Vector2 ghostPos,
                                                 IEnumerable<(int Id, UnitBlueprint Bp, Vector2 Pos)> structures)
    {
        var r = new AdjacencyPreviewResult();
        if (!AdjacencyRules.Participates(ghost)) return r;
        float mass = 0, energy = 0, use = 0;
        foreach (var (id, bp, pos) in structures)
        {
            if (!AdjacencyRules.Participates(bp)) continue;
            int edge = AdjacencyRules.SharedEdge(ghost, ghostPos, bp, pos);
            if (edge <= 0) continue;
            var gives = AdjacencyRules.Effect(ghost, bp, edge);
            var receives = AdjacencyRules.Effect(bp, ghost, edge);
            if (gives.IsZero && receives.IsZero) continue;
            r.Links.Add(new AdjacencyLink(id, pos, bp, gives, receives));
            if (!gives.IsZero) r.Boosted++;
            mass += receives.MassProd;
            energy += receives.EnergyProd;
            use += receives.EnergyUse;
        }
        r.Received = new AdjacencyEffect(mass, energy, MathF.Min(use, AdjacencyRules.MaxEnergyUseReduction));
        return r;
    }

    /// <summary>"+25% mass · -20% energy use", or "" for no bonus.</summary>
    public static string Describe(AdjacencyEffect e)
    {
        var parts = new List<string>(3);
        if (e.MassProd > 0.001f) parts.Add($"+{e.MassProd * 100:0.#}% mass");
        if (e.EnergyProd > 0.001f) parts.Add($"+{e.EnergyProd * 100:0.#}% energy");
        if (e.EnergyUse > 0.001f) parts.Add($"-{e.EnergyUse * 100:0.#}% energy use");
        return string.Join(" · ", parts);
    }

    /// <summary>Summary lines for the ghost: what it receives and how many neighbours it boosts.</summary>
    public static List<string> Summary(AdjacencyPreviewResult r)
    {
        var lines = new List<string>(2);
        if (Describe(r.Received) is { Length: > 0 } got) lines.Add("Adjacency: " + got);
        if (r.Boosted > 0)
        {
            // Name the bonus kind the ghost gives (one kind per giver type).
            var first = r.Links.First(l => !l.Gives.IsZero).Gives;
            string kind = first.EnergyUse > 0 ? "energy use" : first.MassProd > 0 ? "mass" : "energy";
            string verb = first.EnergyUse > 0 ? "cuts" : "boosts";
            lines.Add($"{verb} {kind} of {r.Boosted} neighbour{(r.Boosted == 1 ? "" : "s")}");
        }
        return lines;
    }
}
