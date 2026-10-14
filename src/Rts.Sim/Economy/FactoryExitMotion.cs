using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Units leaving their factory (SupCom roll-off): a finished surface unit starts where it was built and drives
/// straight to the exit point chosen by <see cref="FactorySystem"/>, crossing the factory's own footprint (units on
/// blocked cells move freely in MovementSystem), then follows its rally orders. While leaving it ignores its path;
/// when done it asks for a new path from where it actually is. Aircraft just take off from the pad.
/// </summary>
public static class FactoryExitMotion
{
    /// <summary>Safety cap on the drive out (crowded exit, pushed around): seconds.</summary>
    public const float MaxLeaveTime = 8f;

    /// <summary>Release position of a finished unit: where it was built, i.e. the factory center (clamped to the map).</summary>
    public static Vector2 Start(SimWorld w, int factory) => w.Terrain.Clamp(w.Units.Pos[factory]);

    /// <summary>
    /// MovementSystem hook (PARALLEL, writes only unit i's MoveState): steer at the exit until the unit is off any
    /// blocked cell and close to it (or the time cap runs out).
    /// </summary>
    internal static void Steer(SimWorld w, int i, UnitBlueprint bp, ref MoveState mv, Vector2 pos, bool onBlocked, float dt,
        ref float desiredHeading, ref float desiredSpeed, ref bool wantsMove)
    {
        mv.LeaveTime -= dt;
        var to = mv.FactoryExit - pos;
        float done = 0.5f + 0.5f * bp.Radius;
        if (mv.LeaveTime <= 0 || (!onBlocked && to.LengthSquared() <= done * done))
        {
            mv.LeaveTime = 0;
            if (mv.HasGoal) mv.RepathRequested = true; // the path was planned from inside the factory
            return;
        }
        if (to.LengthSquared() < 1e-6f) return;
        desiredHeading = DMath.HeadingOf(to);
        desiredSpeed = bp.Movement.MaxSpeed * 0.75f;
        wantsMove = true;
    }
}
