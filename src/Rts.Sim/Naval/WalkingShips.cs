using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Naval;

/// <summary>
/// Ships that also walk on land (the Cybran Salem destroyer): a Naval-category unit with <c>Movement.Layer == Hover</c>.
/// It is a ship for separation (hull capsule, <see cref="NavalMotion.KindShip"/>) on both terrains, a Water-layer
/// target on water and a Land-layer target on land (<see cref="Systems.OrderSystem.LayerOf"/>), drives at
/// <c>MaxSpeed × Movement.LandSpeedFactor</c> on dry ground, holds its torpedo weapons there and is never transportable.
/// All helpers read only immutable data and the unit they are given (thread-safe).
/// </summary>
public static class WalkingShips
{
    /// <summary>A ship that can leave the water: mobile, Naval category, Hover layer.</summary>
    public static bool IsWalkingShip(UnitBlueprint bp) =>
        bp.IsMobile && bp.Movement.Layer == MotionLayer.Hover && bp.HasAny(UnitCategory.Naval);

    /// <summary>The unit stands on dry ground (terrain at or above the water level).</summary>
    public static bool OnLand(SimWorld w, int i) => !w.Terrain.IsUnderwater(w.Units.Pos[i]);

    /// <summary>At sea for the AI's land/naval split: a ship or sub, or a walking ship that is in the water now.</summary>
    public static bool AtSea(SimWorld w, int i, UnitBlueprint bp) =>
        bp.Movement.Layer is MotionLayer.Water or MotionLayer.Sub || (IsWalkingShip(bp) && !OnLand(w, i));

    /// <summary>Max ground speed at <paramref name="pos"/>: <c>MaxSpeed</c>, times <c>LandSpeedFactor</c> on dry ground.</summary>
    public static float MaxSpeedAt(SimWorld w, MovementBlueprint mb, Vector2 pos) =>
        mb.LandSpeedFactor != 1f && !w.Terrain.IsUnderwater(pos) ? mb.MaxSpeed * mb.LandSpeedFactor : mb.MaxSpeed;

    /// <summary>
    /// Torpedo and torpedo-defense weapons need water under the shooter: a mobile non-air unit standing on dry ground
    /// holds them (a Salem or a seabed walker that walked ashore). Aircraft drop torpedoes from the air and structures
    /// stand where they were built, so they are not affected.
    /// </summary>
    public static bool TorpedoesHeld(SimWorld w, int i, UnitBlueprint bp, Combat.WeaponKind kind) =>
        kind is Combat.WeaponKind.Torpedo or Combat.WeaponKind.AntiTorpedo
        && bp.IsMobile && bp.Movement.Layer != MotionLayer.Air && OnLand(w, i);
}
