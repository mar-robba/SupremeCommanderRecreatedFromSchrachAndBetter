using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>
/// SupCom1-style veterancy. A unit gains a level every <see cref="KillsPerLevel"/> kills (3 × tech level;
/// commanders and experimentals 20), up to <see cref="MaxLevel"/>. Each level gives +<see cref="HealthPerLevel"/>
/// of the blueprint max health (the gained max health is also healed) and +<see cref="RegenPerLevel"/> HP/s of
/// regeneration (T1 1, T2 3, T3 6, commander 10, experimental 25 per level).
/// Pure functions of (blueprint, kills): no extra per-unit state. Used by the sim (DamageSystem regen, repair,
/// snapshot) and by the client (unit panel stars / health).
/// </summary>
public static class Veterancy
{
    public const int MaxLevel = 5;
    public const float HealthPerLevel = 0.1f;

    public static int KillsPerLevel(UnitBlueprint bp) =>
        bp.Has(UnitCategory.Commander) || bp.Has(UnitCategory.Experimental) ? 20 : 3 * Math.Max(1, bp.Tech);

    public static int Level(UnitBlueprint bp, int kills) => Math.Clamp(kills / KillsPerLevel(bp), 0, MaxLevel);

    public static float HealthMultiplier(int level) => 1f + HealthPerLevel * level;

    /// <summary>Effective max health of a unit of this blueprint with this many kills.</summary>
    public static float MaxHealth(UnitBlueprint bp, int kills) => bp.MaxHealth * HealthMultiplier(Level(bp, kills));

    public static float MaxHealth(SimWorld w, int i) => MaxHealth(w.BpOf(i), w.Units.Kills[i]);

    /// <summary>Extra regeneration (HP/s) per veterancy level.</summary>
    public static float RegenPerLevel(UnitBlueprint bp) =>
        bp.Has(UnitCategory.Experimental) ? 25f
        : bp.Has(UnitCategory.Commander) ? 10f
        : bp.Tech >= 3 ? 6f : bp.Tech == 2 ? 3f : 1f;

    /// <summary>Total regeneration (blueprint RegenRate + veterancy bonus), HP/s.</summary>
    public static float Regen(UnitBlueprint bp, int kills) => bp.RegenRate + Level(bp, kills) * RegenPerLevel(bp);

    /// <summary>Credits a kill to unit <paramref name="killer"/>; on a level up the added max health is healed.
    /// SEQUENTIAL (called from SimWorld.KillUnit).</summary>
    public static void AddKill(SimWorld w, int killer)
    {
        var u = w.Units;
        var bp = w.BpOf(killer);
        int before = Level(bp, u.Kills[killer]);
        u.Kills[killer]++;
        int after = Level(bp, u.Kills[killer]);
        if (after > before && u.IsComplete(killer))
            u.Health[killer] = MathF.Min(MaxHealth(bp, u.Kills[killer]), u.Health[killer] + bp.MaxHealth * HealthPerLevel * (after - before));
    }
}
