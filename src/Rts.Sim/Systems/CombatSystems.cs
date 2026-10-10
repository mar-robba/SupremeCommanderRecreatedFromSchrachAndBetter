using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>Weapons acquire targets. STUB. TODO(combat): see docs/ROADMAP.md.</summary>
public static class TargetingSystem
{
    public static void Run(SimWorld w) { }
}

/// <summary>Turret aiming and firing; spawns projectiles. STUB. TODO(combat).</summary>
public static class WeaponSystem
{
    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        var ws = w.Units.WeaponsOf(i);
        for (int k = 0; k < ws.Length; k++)
        {
            ws[k].TurretHeading = w.Units.Heading[i];
            ws[k].RetargetTimer = i % 3;
        }
    }

    public static void Run(SimWorld w) { }
}

/// <summary>Moves projectiles, detects impacts, emits damage events. STUB. TODO(combat).</summary>
public static class ProjectileSystem
{
    public static void Run(SimWorld w) { }
}
