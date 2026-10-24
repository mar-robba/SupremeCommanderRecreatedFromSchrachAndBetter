using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// (6b) Team economy, run by <see cref="EconomySystem"/> after its per-army storage update (SEQUENTIAL):
/// <list type="bullet">
/// <item>Allied overflow (SupCom): what an army produced but could not store (<see cref="NoteOverflow"/>) goes to its
/// living allies (same team, not defeated) with free storage, split equally; what a full ally can't take is passed on
/// to those still with room; the rest is lost. Mass and energy separately, givers in army index order.</item>
/// <item>The <see cref="StartResources.High"/> start bank (<see cref="Army.StartBankMass"/>/<see cref="Army.StartBankEnergy"/>)
/// refills the army's own storage as it frees up (after the allied overflow, so the bank is never wasted).</item>
/// </list>
/// The overflow of a tick is scratch consumed in the same EconomySystem call; persistent state is only the Army bank
/// fields and the ArmyStats counters. With no bank and no allies this is a short no-op loop over the armies.
/// </summary>
public static class EconomyShare
{
    [ThreadStatic, NoSave] private static float[]? _overMass, _overEnergy;
    [ThreadStatic, NoSave] private static int[]? _receivers;

    /// <summary>Records what army a could not store this tick (raw minus storage; negative = nothing).</summary>
    public static void NoteOverflow(SimWorld w, int a, float mass, float energy)
    {
        int n = w.Armies.Length;
        if (_overMass == null || _overMass.Length < n) { _overMass = new float[n]; _overEnergy = new float[n]; }
        _overMass[a] = mass > 0 ? mass : 0;
        _overEnergy![a] = energy > 0 ? energy : 0;
    }

    /// <summary>Shares this tick's noted overflow among allies, then releases start banks. Clears the overflow.</summary>
    public static void Distribute(SimWorld w)
    {
        var armies = w.Armies;
        int n = armies.Length;
        var om = _overMass;
        var oe = _overEnergy;
        if (om != null && om.Length >= n)
        {
            for (int g = 0; g < n; g++)
            {
                float m = om[g], e = oe![g];
                om[g] = 0;
                oe[g] = 0;
                if ((m <= 0 && e <= 0) || armies[g].Defeated) continue;
                if (m > 0) Share(w, g, m, mass: true);
                if (e > 0) Share(w, g, e, mass: false);
            }
        }
        for (int a = 0; a < n; a++)
        {
            var army = armies[a];
            if (army.StartBankMass <= 0 && army.StartBankEnergy <= 0) continue;
            var eco = army.Eco;
            if (army.StartBankMass > 0)
            {
                float r = MathF.Min(army.StartBankMass, eco.MassMax - eco.Mass);
                if (r > 0) { eco.Mass = MathF.Min(eco.Mass + r, eco.MassMax); army.StartBankMass -= r; }
            }
            if (army.StartBankEnergy > 0)
            {
                float r = MathF.Min(army.StartBankEnergy, eco.EnergyMax - eco.Energy);
                if (r > 0) { eco.Energy = MathF.Min(eco.Energy + r, eco.EnergyMax); army.StartBankEnergy -= r; }
            }
        }
    }

    /// <summary>Gives <paramref name="amount"/> of army g's overflow to its allies with room (equal split, repeated).</summary>
    private static void Share(SimWorld w, int g, float amount, bool mass)
    {
        var armies = w.Armies;
        int n = armies.Length;
        var recv = _receivers is { } r0 && r0.Length >= n ? r0 : _receivers = new int[n];
        int count = 0;
        for (int a = 0; a < n; a++)
            if (a != g && !armies[a].Defeated && w.IsAlly(g, a) && Room(armies[a].Eco, mass) > 0) recv[count++] = a;
        float given = 0;
        while (count > 0 && amount > 1e-6f)
        {
            float share = amount / count;
            int keep = 0;
            for (int k = 0; k < count; k++)
            {
                var ra = armies[recv[k]];
                float room = Room(ra.Eco, mass);
                float give = MathF.Min(share, room);
                if (give > 0)
                {
                    if (mass) { ra.Eco.Mass = MathF.Min(ra.Eco.Mass + give, ra.Eco.MassMax); ra.Stats.MassReceived += give; }
                    else { ra.Eco.Energy = MathF.Min(ra.Eco.Energy + give, ra.Eco.EnergyMax); ra.Stats.EnergyReceived += give; }
                    amount -= give;
                    given += give;
                }
                if (give < room) recv[keep++] = recv[k]; // still has room: may take the next round
            }
            if (keep == count) break; // nobody got full: everything was handed out (up to rounding)
            count = keep;
        }
        if (given <= 0) return;
        if (mass) armies[g].Stats.MassGiven += given;
        else armies[g].Stats.EnergyGiven += given;
    }

    private static float Room(EconomyState e, bool mass) => mass ? e.MassMax - e.Mass : e.EnergyMax - e.Energy;
}
