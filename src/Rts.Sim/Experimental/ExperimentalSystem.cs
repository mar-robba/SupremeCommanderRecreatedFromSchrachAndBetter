using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Experimental;

/// <summary>
/// Generic experimental mechanics (data: <see cref="UnitBlueprint.Experimental"/>, SupCom-like). SEQUENTIAL, runs
/// right after MovementSystem:
/// <list type="bullet">
/// <item>Crushing: a moving unit with <c>CrushRadius</c> deals <c>CrushDamage</c>/s to small (Tech ≤ CrushMaxTech,
///   not commander/experimental) complete land units of enemy armies whose centre is within the radius.</item>
/// <item>Death explosion: a complete unit with <c>DeathDamage</c> explodes <c>DeathDelay</c> s after it dies
///   (area damage + big Explosion effect). The wreck comes from ConstructionSystem (BuildCostMass × WreckMassFraction).</item>
/// <item>Mobile factories (Fatboy): the unit being built rides inside the factory (FactorySystem.Release lets it
///   out behind).</item>
/// <item>Energy per shot (Mavor): weapons with <c>EnergyPerShot</c> charge from the economy (pay first, through the
///   unit's EnergyRequest) and may only fire with a full charge (<see cref="WeaponEnergy.TryConsume"/>).</item>
/// </list>
/// </summary>
public static class ExperimentalSystem
{
    /// <summary>Minimum speed (units/s) at which a crusher crushes.</summary>
    public const float CrushMinSpeed = 0.2f;

    public static bool Needs(UnitBlueprint bp)
    {
        if (bp.Experimental != null || (bp.IsMobile && bp.Has(UnitCategory.Factory))) return true;
        foreach (var wb in bp.Weapons) if (wb.EnergyPerShot > 0) return true;
        return false;
    }

    /// <summary>Spawn hook (UnitLifecycle.OnSpawn; re-run on in-place blueprint changes, so idempotent). Sequential.</summary>
    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        bool needs = Needs(bp);
        ExperimentalState? s;
        if (needs) s = ExperimentalState.Of(w);
        else if (!ExperimentalState.TryGet(w, out s)) return;
        var h = w.Units.HandleOf(i);
        bool registered = s.Units.Contains(h);
        if (!needs)
        {
            if (registered) s.Units.Remove(h);
            return;
        }
        if (registered) return;
        s.Units.Add(h);
        int b = i * UnitStore.MaxWeapons;
        for (int k = 0; k < UnitStore.MaxWeapons; k++) { s.Charge[b + k] = 0; s.ChargeRequest[b + k] = 0; }
    }

    /// <summary>Death hook (UnitLifecycle.OnDeath): schedules the death explosion of complete experimentals.</summary>
    public static void OnUnitDeath(SimWorld w, int i, UnitBlueprint bp)
    {
        var x = bp.Experimental;
        if (x == null || x.DeathDamage <= 0 || x.DeathRadius <= 0 || !w.Units.IsComplete(i)) return;
        ExperimentalState.Of(w).Blasts.Add(new PendingBlast
        {
            Center = w.Units.Pos[i],
            Elevation = w.Units.Elevation[i],
            Radius = x.DeathRadius,
            Damage = x.DeathDamage,
            Army = w.Units.Army[i],
            DueTick = w.Tick + Math.Max(1, (int)MathF.Round(x.DeathDelay / SimWorld.Dt)),
            Bp = bp.Index,
        });
    }

    public static void Run(SimWorld w)
    {
        if (!ExperimentalState.TryGet(w, out var s)) return;
        var u = w.Units;
        if (s.Blasts.Count > 0) ApplyBlasts(w, s);
        if (s.Units.Count == 0) return;
        int n = 0;
        for (int k = 0; k < s.Units.Count; k++)
            if (u.IsActive(s.Units[k])) s.Units[n++] = s.Units[k];
        s.Units.RemoveRange(n, s.Units.Count - n);

        foreach (var h in s.Units)
        {
            int i = h.Index;
            var bp = w.BpOf(i);
            bool complete = u.IsComplete(i);
            if (complete && bp.IsMobile && bp.Has(UnitCategory.Factory)) CarryCurrent(w, i);
            if (complete && bp.Experimental is { CrushRadius: > 0 } x && u.Move[i].Speed > CrushMinSpeed) Crush(w, s, i, x);
            ChargeWeapons(w, s, i, bp, complete);
        }
    }

    private static void ApplyBlasts(SimWorld w, ExperimentalState s)
    {
        int n = 0;
        for (int k = 0; k < s.Blasts.Count; k++)
        {
            var b = s.Blasts[k];
            if (b.DueTick > w.Tick) { s.Blasts[n++] = b; continue; }
            w.Events.AreaDamage.Add(new AreaDamageEvent
            {
                Center = b.Center, Radius = b.Radius, Amount = b.Damage,
                Source = UnitHandle.None, SourceArmy = b.Army, Layers = LayerMask.None,
            });
            w.Events.Effects.Add(new EffectEvent
            {
                Kind = EffectKind.Explosion,
                Pos = DMath.To3(b.Center, b.Elevation),
                Size = b.Radius,
                Army = b.Army,
                BlueprintIndex = b.Bp,
            });
        }
        s.Blasts.RemoveRange(n, s.Blasts.Count - n);
    }

    /// <summary>The unit under construction inside a mobile factory moves with it.</summary>
    private static void CarryCurrent(SimWorld w, int i)
    {
        var u = w.Units;
        var c = u.Factory[i].Current;
        if (c.IsNone || !u.IsActive(c) || u.IsComplete(c.Index)) return;
        int j = c.Index;
        u.Pos[j] = u.Pos[i];
        u.Heading[j] = u.Heading[i];
        u.Elevation[j] = u.Elevation[i];
        u.Move[j].Velocity = default;
    }

    private static void Crush(SimWorld w, ExperimentalState s, int i, ExperimentalBlueprint x)
    {
        var u = w.Units;
        int army = u.Army[i];
        var list = s.Query;
        list.Clear();
        w.Grid.QueryRadius(u, u.Pos[i], x.CrushRadius, list);
        list.Sort(); // deterministic damage order regardless of grid layout
        var self = u.HandleOf(i);
        foreach (int j in list)
        {
            if (j == i || !u.IsActive(j) || !u.IsComplete(j)) continue;
            if (!x.CrushAllies && !w.IsEnemy(army, u.Army[j])) continue;
            var jbp = w.BpOf(j);
            if (!jbp.IsMobile || jbp.Tech > x.CrushMaxTech
                || jbp.HasAny(UnitCategory.Experimental | UnitCategory.Commander)) continue;
            if (OrderSystem.LayerOf(w, j) != LayerMask.Land) continue;
            w.Events.Damage.Add(new DamageEvent { Target = u.HandleOf(j), Source = self, SourceArmy = army, Amount = x.CrushDamage * SimWorld.Dt });
        }
    }

    private static void ChargeWeapons(SimWorld w, ExperimentalState s, int i, UnitBlueprint bp, bool complete)
    {
        var u = w.Units;
        float total = 0;
        bool any = false;
        bool working = complete && (u.Flags[i] & (UnitFlags.Paused | UnitFlags.Stunned)) == 0;
        int b = i * UnitStore.MaxWeapons;
        for (int wi = 0; wi < bp.Weapons.Length && wi < UnitStore.MaxWeapons; wi++)
        {
            var wb = bp.Weapons[wi];
            if (wb.EnergyPerShot <= 0) continue;
            any = true;
            float cost = wb.EnergyPerShot;
            ref float charge = ref s.Charge[b + wi];
            ref float req = ref s.ChargeRequest[b + wi];
            charge = MathF.Min(cost, charge + req * SimWorld.Dt * u.Econ[i].Efficiency);
            req = working ? MathF.Min((cost - charge) / SimWorld.Dt, cost * MathF.Max(wb.RateOfFire, 0.01f)) : 0;
            if (req < 1e-3f) req = 0;
            total += req;
        }
        if (!any) return;
        // Builders reset/add their own requests every tick; other units (the Mavor) only have this one.
        if (bp.Economy.BuildRate > 0) u.Econ[i].EnergyRequest += total;
        else u.Econ[i].EnergyRequest = total;
    }

    /// <summary>Unfinished allied mobile unit of blueprint <paramref name="bpIndex"/> standing at <paramref name="site"/>
    /// (so engineers ordered to build the same mobile experimental there assist one site), else -1.</summary>
    public static int FindMobileSite(SimWorld w, int army, int bpIndex, System.Numerics.Vector2 site)
    {
        var u = w.Units;
        for (int k = 0; k < u.AliveCount; k++)
        {
            int j = u.AliveList[k];
            if (u.Bp[j] != bpIndex || !u.IsActive(j) || u.IsComplete(j) || !w.IsAlly(army, u.Army[j])) continue;
            if (System.Numerics.Vector2.DistanceSquared(u.Pos[j], site) < 1f) return j;
        }
        return -1;
    }

    /// <summary>Hash of the experimental state (determinism checks).</summary>
    public static ulong Hash(SimWorld w)
    {
        if (!ExperimentalState.TryGet(w, out var s)) return 0;
        ulong h = 1469598103934665603UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        foreach (var b in s.Blasts) { Mix((ulong)b.DueTick); Mix(BitConverter.SingleToUInt32Bits(b.Center.X)); }
        foreach (var uh in s.Units)
        {
            Mix((ulong)uh.Index);
            int b = uh.Index * UnitStore.MaxWeapons;
            for (int k = 0; k < UnitStore.MaxWeapons; k++) Mix(BitConverter.SingleToUInt32Bits(s.Charge[b + k]));
        }
        return h;
    }
}

/// <summary>Energy-per-shot gate for WeaponSystem (parallel-safe: touches only the firing unit's own slots).</summary>
public static class WeaponEnergy
{
    public static bool TryConsume(SimWorld w, int i, int wi, float cost)
    {
        if (!ExperimentalState.TryGet(w, out var s)) return false;
        ref float c = ref s.Charge[i * UnitStore.MaxWeapons + wi];
        if (c < cost - 1e-3f) return false;
        c = MathF.Max(0, c - cost);
        return true;
    }

    /// <summary>Charge fraction 0..1 of weapon wi of unit i (1 for weapons without an energy cost).</summary>
    public static float Fraction(SimWorld w, int i, int wi)
    {
        var bp = w.BpOf(i);
        if ((uint)wi >= (uint)bp.Weapons.Length || bp.Weapons[wi].EnergyPerShot <= 0) return 1;
        if (!ExperimentalState.TryGet(w, out var s)) return 0;
        return s.Charge[i * UnitStore.MaxWeapons + wi] / bp.Weapons[wi].EnergyPerShot;
    }
}
