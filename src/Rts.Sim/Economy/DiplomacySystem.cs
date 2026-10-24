using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// (6b) Team play commands (SupCom diplomacy), applied by CommandSystem (SEQUENTIAL, so replays and lockstep carry
/// them): give units / stored resources to a living ally, and the cheats option's "enable cheat tools".
/// </summary>
public static class DiplomacySystem
{
    /// <summary>Can <paramref name="army"/> give to <paramref name="to"/>? (another, living ally)</summary>
    public static bool CanGive(SimWorld w, int army, int to) =>
        (uint)army < (uint)w.Armies.Length && (uint)to < (uint)w.Armies.Length && to != army &&
        w.IsAlly(army, to) && !w.Armies[to].Defeated && !w.Armies[army].Defeated;

    /// <summary>
    /// Gives own finished units to an ally (<see cref="UnitTransfer.Transfer"/>: a factory's unfinished unit and a
    /// carrier's cargo go along). Refused: commanders, carriers holding one, units under construction, carried units
    /// (they go with their carrier) and units that would put the receiver over its unit cap (the others still go).
    /// </summary>
    public static void Apply(SimWorld w, GiveUnitsCommand c)
    {
        if (c.Units == null || !CanGive(w, c.Army, c.ToArmy)) return;
        var u = w.Units;
        var to = w.Armies[c.ToArmy];
        foreach (var h in c.Units)
        {
            if (!CommandSystem.Owns(w, c.Army, h)) continue;
            int i = h.Index;
            if (w.BpOf(i).Has(UnitCategory.Commander) || UnitTransfer.HoldsCommander(w, i)) continue;
            if (!u.IsComplete(i) || w.Cargo.Carrier[i] >= 0) continue;
            if (to.UnitCount + CountMoved(w, i) > to.UnitCap) continue;
            UnitTransfer.Transfer(w, i, c.ToArmy);
        }
    }

    /// <summary>Units <see cref="UnitTransfer.Transfer"/> moves with unit i (itself, its factory's unit, its cargo).</summary>
    public static int CountMoved(SimWorld w, int i)
    {
        var u = w.Units;
        if (!u.IsActive(i)) return 0;
        int n = 1;
        var cur = u.Factory[i].Current;
        if (u.IsActive(cur)) n += CountMoved(w, cur.Index);
        var holds = w.Cargo.Holds[i];
        if (holds != null)
            foreach (var h in holds)
                if (u.IsActive(h)) n += CountMoved(w, h.Index);
        return n;
    }

    /// <summary>Gives stored mass/energy to an ally, clamped to what is stored and to the receiver's free storage.</summary>
    public static void Apply(SimWorld w, GiveResourcesCommand c)
    {
        if (!CanGive(w, c.Army, c.ToArmy)) return;
        var from = w.Armies[c.Army];
        var to = w.Armies[c.ToArmy];
        float m = Amount(c.Mass, from.Eco.Mass, to.Eco.MassMax - to.Eco.Mass);
        float e = Amount(c.Energy, from.Eco.Energy, to.Eco.EnergyMax - to.Eco.Energy);
        if (m > 0)
        {
            from.Eco.Mass = MathF.Max(from.Eco.Mass - m, 0);
            to.Eco.Mass = MathF.Min(to.Eco.Mass + m, to.Eco.MassMax);
            from.Stats.MassGiven += m;
            to.Stats.MassReceived += m;
        }
        if (e > 0)
        {
            from.Eco.Energy = MathF.Max(from.Eco.Energy - e, 0);
            to.Eco.Energy = MathF.Min(to.Eco.Energy + e, to.Eco.EnergyMax);
            from.Stats.EnergyGiven += e;
            to.Stats.EnergyReceived += e;
        }
    }

    /// <summary>The amount actually given: requested (NaN/negative = 0), at most stored and the receiver's room.</summary>
    private static float Amount(float requested, float stored, float room)
    {
        if (!(requested > 0)) return 0;
        float v = MathF.Min(requested, MathF.Min(stored, room));
        return v > 0 ? v : 0;
    }

    /// <summary>
    /// Cheats option: turns the sandbox tools on in a normal game (only with <see cref="SimWorld.CheatsEnabled"/>, once).
    /// The sandbox keeps the victory rule (<c>SandboxState.Victory</c>) and the game's unit caps.
    /// </summary>
    public static void Apply(SimWorld w, EnableCheatToolsCommand c)
    {
        if (!w.CheatsEnabled || SandboxSystem.IsEnabled(w)) return;
        var s = SandboxSystem.Enable(w);
        s.Victory = true;
        foreach (var a in w.Armies) a.UnitCap = w.Options.UnitCap; // Enable lifts the caps
        s.Publish(w);
    }
}
