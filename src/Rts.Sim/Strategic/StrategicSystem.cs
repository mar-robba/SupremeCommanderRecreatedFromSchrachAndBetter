using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Strategic;

/// <summary>
/// Strategic &amp; defensive structures: shields (<see cref="ShieldSystem"/>), missile silos (<see cref="SiloSystem"/>),
/// strategic missiles and missile defense (<see cref="MissileSystem"/>). State lives in <see cref="StrategicState"/>.
/// Pipeline step (sequential, after MovementSystem and before TargetingSystem):
/// prune/aim interceptors → silo stockpiles + launch orders → shields (state + index) → missile defenses.
/// Hooks elsewhere: ProjectileSystem (shield interception, missile flight/impacts), WeaponSystem (beams vs shields,
/// silo/defense weapons never auto-fire: <c>WeaponKind.Manual</c>), DamageSystem (nuke blasts, area damage vs domes),
/// CommandSystem (Launch orders for silos), UnitLifecycle (InitUnit), snapshot (StrategicSnapshotWriter).
/// Cost with no strategic units: one empty list walk.
/// </summary>
public static class StrategicSystem
{
    public static bool HasModule(UnitBlueprint bp) => bp.Shield != null || bp.Silo != null || bp.MissileDefense != null;

    /// <summary>Spawn hook (UnitLifecycle.OnSpawn). Sequential.</summary>
    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        if (!HasModule(bp)) return;
        var s = StrategicState.Of(w);
        ShieldSystem.InitUnit(s, w, i, bp);
        SiloSystem.InitUnit(s, w, i, bp);
        MissileSystem.InitUnit(s, w, i, bp);
        s.Units.Add(w.Units.HandleOf(i));
    }

    public static void Run(SimWorld w)
    {
        var s = StrategicState.Of(w);
        if (s.Units.Count == 0 && s.Missiles.Count == 0 && s.Index.Count == 0) return;
        // Prune dead units (keeps spawn order).
        int n = 0;
        for (int k = 0; k < s.Units.Count; k++)
            if (w.Units.IsActive(s.Units[k])) s.Units[n++] = s.Units[k];
        s.Units.RemoveRange(n, s.Units.Count - n);

        s.RefreshEnemies(w);
        MissileSystem.PrepareInterceptors(s, w);
        SiloSystem.Update(s, w);
        ShieldSystem.Update(s, w);
        MissileSystem.UpdateDefenses(s, w);
    }

    /// <summary>Hash of the strategic state (mixed into Simulation.StateHash).</summary>
    public static ulong Hash(SimWorld w)
    {
        var s = StrategicState.Of(w);
        ulong h = 14695981039346656037UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        foreach (var uh in s.Units)
        {
            int i = uh.Index;
            if (!w.Units.IsActive(uh)) continue;
            Mix((ulong)i);
            Mix(BitConverter.SingleToUInt32Bits(s.Shield[i].Hp));
            Mix(s.Shield[i].Up ? 1UL : 0UL);
            Mix((ulong)s.Silo[i].Stock);
            Mix(BitConverter.SingleToUInt32Bits(s.Silo[i].Progress));
        }
        Mix((ulong)s.LaunchSerial);
        return h;
    }

    /// <summary>Checks blueprint references of the strategic modules (throws InvalidDataException).</summary>
    public static void Validate(BlueprintDb db)
    {
        foreach (var bp in db.All)
        {
            if (bp.Silo is { } sb && (uint)sb.WeaponIndex >= (uint)bp.Weapons.Length)
                throw new InvalidDataException($"Blueprint '{bp.Id}': Silo.WeaponIndex {sb.WeaponIndex} out of range");
            if (bp.MissileDefense is { } md && bp.Weapons.Length > 0 && (uint)md.WeaponIndex >= (uint)bp.Weapons.Length)
                throw new InvalidDataException($"Blueprint '{bp.Id}': MissileDefense.WeaponIndex {md.WeaponIndex} out of range");
            if (bp.MissileDefense is { UsesStockpile: true } && bp.Silo == null)
                throw new InvalidDataException($"Blueprint '{bp.Id}': MissileDefense.UsesStockpile needs a Silo");
            if (bp.Shield is { } sh && (sh.Radius <= 0 || sh.MaxHealth <= 0))
                throw new InvalidDataException($"Blueprint '{bp.Id}': Shield needs Radius > 0 and MaxHealth > 0");
        }
    }

    /// <summary>Is weapon <paramref name="wi"/> of the blueprint driven by the strategic module (never auto-fires)?</summary>
    public static bool IsManualWeapon(UnitBlueprint bp, int wi) =>
        (bp.Silo != null && bp.Silo.WeaponIndex == wi) || (bp.MissileDefense != null && bp.MissileDefense.WeaponIndex == wi);
}
