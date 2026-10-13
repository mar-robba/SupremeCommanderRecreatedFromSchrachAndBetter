using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Transport;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// VTOL flight for air transports (<see cref="UnitBlueprint.IsVtol"/>): fly straight at the goal with a braking
/// profile (helicopter-like, no orbit), hover in place without a goal, and descend to the carrier's HoverHeight
/// while <see cref="CargoState.AirMode"/> is <see cref="AirModes.Land"/> (set by TransportSystem to load/unload).
/// Runs inside the parallel movement phase: writes only this unit's state.
/// </summary>
public static partial class MovementSystem
{
    private static void StepVtol(SimWorld w, int i, UnitBlueprint bp, float dt)
    {
        var u = w.Units;
        ref var mv = ref u.Move[i];
        var mb = bp.Movement;
        var pos = u.Pos[i];
        float heading = u.Heading[i];
        mv.Arrived = false;
        float maxSpeed = mb.MaxSpeed * TransportSystem.AirSpeedFactor(w, i, bp);
        var dir = DMath.Dir(heading);
        float desired = 0;
        if (mv.HasGoal)
        {
            var to = mv.Goal - pos;
            float d = to.Length();
            float arrive = MathF.Max(mv.ArriveRadius, 0.5f);
            if (d <= arrive) Arrive(ref mv);
            else
            {
                dir = to / d;
                // Brake so that we stop at the goal (v² = 2·a·d), and fly nose first: slow while turning.
                desired = MathF.Min(maxSpeed, MathF.Sqrt(2f * mb.Acceleration * MathF.Max(0f, d - arrive * 0.5f)) + 0.5f);
                heading = DMath.RotateTowards(heading, DMath.HeadingOf(to), mb.TurnRate * DMath.Deg2Rad * dt);
                float align = Vector2.Dot(DMath.Dir(heading), dir);
                desired *= Math.Clamp(0.3f + 0.7f * align, 0.3f, 1f);
            }
        }
        else if (mv.Velocity.LengthSquared() > 1e-4f) dir = Vector2.Normalize(mv.Velocity);

        float speed = mv.Speed;
        float dv = mb.Acceleration * dt;
        speed = speed < desired ? MathF.Min(desired, speed + dv) : MathF.Max(desired, speed - dv * 1.5f);
        mv.Speed = speed;

        bool land = w.Cargo.AirMode[i] == AirModes.Land;
        var vel = dir * speed + Separation(w, i, bp, pos, mv.HasGoal) * MathF.Max(1f, mb.MaxSpeed * 0.25f) * (land ? 0.3f : 1f);
        var newPos = w.Terrain.Clamp(pos + vel * dt);
        mv.Velocity = (newPos - pos) / dt;
        mv.StuckTime = 0;
        mv.OrbitCenter = newPos;
        u.ScratchPos[i] = newPos;
        u.Heading[i] = heading;
        if (speed > 0.05f) u.Flags[i] |= UnitFlags.Moving;
        else u.Flags[i] &= ~UnitFlags.Moving;

        float targetAlt = w.Terrain.GetSurfaceHeight(newPos) + (land && bp.Cargo != null ? bp.Cargo.HoverHeight : mb.Altitude);
        float climb = MathF.Max(4f, mb.Altitude * 0.5f) * dt;
        u.Elevation[i] += Math.Clamp(targetAlt - u.Elevation[i], -climb, climb);
    }
}
