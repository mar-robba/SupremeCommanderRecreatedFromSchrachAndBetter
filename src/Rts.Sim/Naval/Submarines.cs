using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Naval;

/// <summary>
/// Submarine dive/surface toggle (SupCom). Units with <c>Movement.Layer == Sub</c> are submerged by default
/// (<see cref="MovementBlueprint.StartSurfaced"/>: surfaced, the Atlantis); <see cref="UnitFlags.Surfaced"/> puts them on the Water layer (targetable by surface weapons, seen by normal
/// vision/radar, drawn at the surface).
/// </summary>
public static class Submarines
{
    /// <summary>The unit has the dive/surface toggle (subs, the Atlantis): <c>Movement.Layer == Sub</c>.</summary>
    public static bool CanDive(UnitBlueprint bp) => bp.Movement.Layer == MotionLayer.Sub;

    /// <summary>Divers that spawn surfaced (<see cref="MovementBlueprint.StartSurfaced"/>, the Atlantis); other subs
    /// start submerged.</summary>
    public static bool StartsSurfaced(UnitBlueprint bp) => CanDive(bp) && bp.Movement.StartSurfaced;

    public static bool IsSurfaced(SimWorld w, int i) => (w.Units.Flags[i] & UnitFlags.Surfaced) != 0;

    /// <summary>
    /// A diver that is not up at the surface: dive ordered (even while still going down) or surfacing but not yet
    /// halfway up. Its deck is closed (no docking / launching aircraft, see Transport) and only underwater weapons
    /// fire (<see cref="FiresSubmerged"/>). False for units that can't dive. Reads only unit i (thread-safe).
    /// </summary>
    public static bool IsSubmerged(SimWorld w, int i) =>
        CanDive(w.BpOf(i)) && (!IsSurfaced(w, i) || LayerOf(w, i) == LayerMask.Sub);

    /// <summary>Weapons that work while their submerged carrier is under water (torpedoes, torpedo defense); guns
    /// and SAMs (the Atlantis') hold fire and drop their targets.</summary>
    public static bool FiresSubmerged(Combat.WeaponKind kind) => kind is Combat.WeaponKind.Torpedo or Combat.WeaponKind.AntiTorpedo;

    /// <summary>Kind-based rule above, or the weapon blueprint's <c>FiresSubmerged</c> flag (any kind).</summary>
    public static bool FiresSubmerged(Combat.WeaponKind kind, WeaponBlueprint wb) => FiresSubmerged(kind) || wb.FiresSubmerged;

    /// <summary>
    /// Seabed walker under water: a <c>Movement.Layer == Amphibious</c> unit (engineers, Cybran Wagner/Monkeylord)
    /// standing below the water level. Like a dived sub it is on the Sub layer (<see cref="OrderSystem.LayerOf"/>:
    /// only Sub-layer weapons such as torpedoes hit it, only sonar/omni see it) and only its
    /// <see cref="FiresSubmerged(Combat.WeaponKind, WeaponBlueprint)"/> weapons work. Reads only unit i (thread-safe).
    /// </summary>
    public static bool IsOnSeabed(SimWorld w, int i) =>
        w.BpOf(i).Movement.Layer == MotionLayer.Amphibious && w.Terrain.IsUnderwater(w.Units.Pos[i]);

    /// <summary>Under water for its weapons: a submerged diver (<see cref="IsSubmerged"/>) or a seabed walker
    /// (<see cref="IsOnSeabed"/>). Targeting and WeaponSystem then hold every weapon that doesn't fire submerged.</summary>
    public static bool WeaponsUnderwater(SimWorld w, int i) => IsSubmerged(w, i) || IsOnSeabed(w, i);

    /// <summary>Height above the water surface where a non-torpedo <c>FiresSubmerged</c> weapon of a unit under water
    /// fires from (the gun breaks the surface; shots never travel under water).</summary>
    public const float SurfaceMuzzleClearance = 0.5f;

    /// <summary>Muzzle height of a weapon of a unit under water: torpedoes stay at <paramref name="y"/>, any other
    /// (FiresSubmerged) weapon fires from just above the water surface.</summary>
    public static float UnderwaterMuzzleY(SimWorld w, float y, Combat.WeaponKind kind) =>
        FiresSubmerged(kind) ? y : MathF.Max(y, w.Terrain.WaterLevel + SurfaceMuzzleClearance);

    /// <summary>
    /// Layer of a sub for intel and targeting: decided by its depth, not by the toggle, so a dive/surface takes
    /// effect once the hull is halfway (<see cref="NavalMotion.SubElevation"/> eases it): a diving sub stays exposed
    /// to surface guns and radar for a moment, a surfacing one stays hidden until it breaks the surface.
    /// </summary>
    public static LayerMask LayerOf(SimWorld w, int i) =>
        w.Units.Elevation[i] > w.Terrain.WaterLevel - NavalMotion.SubDepth * 0.5f ? LayerMask.Water : LayerMask.Sub;

    public static void Apply(SimWorld w, SetSubmergedCommand c)
    {
        var u = w.Units;
        foreach (var h in c.Units)
        {
            if (!CommandSystem.Owns(w, c.Army, h) || !CanDive(w.BpOf(h.Index))) continue;
            if (c.Submerged) u.Flags[h.Index] &= ~UnitFlags.Surfaced;
            else u.Flags[h.Index] |= UnitFlags.Surfaced;
        }
    }
}
