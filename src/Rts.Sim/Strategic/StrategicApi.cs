using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Sim.Strategic;

/// <summary>
/// Public entry points of the strategic module for other sim code (sandbox cheats, scripted scenarios, tests).
/// All methods are SEQUENTIAL sim code: call them from a command handler (CommandSystem), another sequential phase, or
/// on a world that is not being stepped (never from the client thread while the runner is running).
/// </summary>
public static class StrategicApi
{
    /// <summary>Blueprint whose missile (damage, rings, visuals) a launcher-less nuke uses by default.</summary>
    public const string DefaultNukeSilo = "uef_t3_strategic_missile_launcher";

    /// <summary>
    /// Launches a strategic missile (nuke) owned by <paramref name="army"/> from ground point <paramref name="from"/> to
    /// <paramref name="target"/>, without needing a silo unit or stockpile. It behaves exactly like a silo launch: high arc,
    /// global "strategic launch" warning (<c>SimSnapshot.Launches</c>), interceptable by enemy anti-nukes, two-ring blast
    /// that ignores and knocks down shields. Damage/rings come from <paramref name="siloBlueprint"/> (must have a
    /// <c>Silo</c> of kind Strategic). Returns the projectile slot, or -1 (bad army/blueprint, projectile pool full).
    /// </summary>
    public static int LaunchNuke(SimWorld w, int army, Vector2 from, Vector2 target, string siloBlueprint = DefaultNukeSilo)
    {
        if ((uint)army >= (uint)w.Armies.Length) return -1;
        if (!w.Blueprints.TryGet(siloBlueprint, out var bp) || bp.Silo is not { Kind: Data.SiloKind.Strategic }) return -1;
        var terrain = w.Terrain;
        from = terrain.Clamp(from);
        target = terrain.Clamp(target);
        float startY = terrain.GetSurfaceHeight(from) + bp.Visual.Size.Y * 0.9f + 0.5f;
        return MissileSystem.Spawn(StrategicState.Of(w), w, army, UnitHandle.None, bp, from, startY, target);
    }

    /// <summary>
    /// <see cref="LaunchNuke(SimWorld,int,Vector2,Vector2,string)"/> launched from the army's commander (if alive), else
    /// from the map center.
    /// </summary>
    public static int LaunchNuke(SimWorld w, int army, Vector2 target)
    {
        if ((uint)army >= (uint)w.Armies.Length) return -1;
        var cmd = w.Armies[army].Commander;
        var from = w.Units.IsActive(cmd) ? w.Units.Pos[cmd.Index] : new Vector2(w.Terrain.Width * 0.5f, w.Terrain.Height * 0.5f);
        return LaunchNuke(w, army, from, target);
    }

    /// <summary>
    /// A strategic blast at <paramref name="center"/> without any missile (no flight, cannot be intercepted): exactly
    /// the detonation of a nuke — inner ring InnerDamage + OuterDamage, outer ring OuterDamage, ignores and knocks down
    /// shields, spares subs, vaporizes the wrecks in the inner ring next tick, one <see cref="EffectKind.Nuke"/> effect.
    /// Damage is applied by this tick's DamageSystem. Used by the sandbox instant nuke.
    /// </summary>
    public static void Detonate(SimWorld w, int army, Vector2 center, float innerRadius, float innerDamage, float outerRadius, float outerDamage)
    {
        if ((uint)army >= (uint)w.Armies.Length) return;
        MissileSystem.QueueDetonation(w, army, UnitHandle.None, w.Terrain.Clamp(center), innerRadius, innerDamage, outerRadius, outerDamage);
    }

    /// <summary>Blast rings of a strategic silo blueprint's missile (0, 0 if it is not a strategic silo).</summary>
    public static (float Inner, float Outer) NukeRings(Data.BlueprintDb db, string siloBlueprint = DefaultNukeSilo)
    {
        if (!db.TryGet(siloBlueprint, out var bp) || bp.Silo is not { Kind: Data.SiloKind.Strategic } sb) return (0, 0);
        float inner = bp.Weapons[sb.WeaponIndex].DamageRadius;
        return (inner, MathF.Max(inner, sb.OuterRadius));
    }
}
