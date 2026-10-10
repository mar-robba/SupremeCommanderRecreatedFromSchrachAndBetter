using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Moves mobile units toward their current waypoint (path from PathService, else the goal directly) with
/// acceleration, turn rate, unit separation and terrain checks.
/// PARALLEL, double-buffered: new positions go to UnitStore.ScratchPos, then are committed, so every unit
/// reads its neighbours' positions from the same moment (deterministic).
/// </summary>
public static class MovementSystem
{
    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        ref var mv = ref w.Units.Move[i];
        mv.PathId = -1;
        mv.HasGoal = false;
    }

    public static void Run(SimWorld w)
    {
        var u = w.Units;
        float dt = SimWorld.Dt;
        Par.For(u.AliveCount, 256, (_, start, end) =>
        {
            for (int k = start; k < end; k++)
            {
                int i = u.AliveList[k];
                var bp = w.BpOf(i);
                if (bp.IsStructure || !u.IsComplete(i)) { u.ScratchPos[i] = u.Pos[i]; continue; }
                Step(w, i, bp, dt);
            }
        });
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            u.Pos[i] = u.ScratchPos[i];
        }
    }

    private static void Step(SimWorld w, int i, UnitBlueprint bp, float dt)
    {
        var u = w.Units;
        ref var mv = ref u.Move[i];
        var mb = bp.Movement;
        var pos = u.Pos[i];
        float heading = u.Heading[i];
        float desiredSpeed = 0;
        float desiredHeading = heading;
        mv.Arrived = false;

        if (mv.HasGoal)
        {
            var wp = w.Paths.CurrentWaypoint(i, ref mv, pos);
            var to = wp - pos;
            float distGoal = Vector2.Distance(pos, mv.Goal);
            if (distGoal <= mv.ArriveRadius)
            {
                mv.Arrived = true;
                mv.HasGoal = false;
            }
            else if (to.LengthSquared() > 1e-6f)
            {
                desiredHeading = DMath.HeadingOf(to);
                // Slow down approaching the final goal: v = sqrt(2 a d).
                desiredSpeed = MathF.Min(mb.MaxSpeed, DMath.Sqrt(2f * mb.Acceleration * MathF.Max(0, distGoal - mv.ArriveRadius * 0.5f)) + 0.2f);
            }
        }

        // Turn.
        float maxTurn = mb.TurnRate * DMath.Deg2Rad * dt;
        heading = DMath.RotateTowards(heading, desiredHeading, maxTurn);
        // Vehicles must roughly face the direction before driving at full speed.
        float facing = MathF.Cos(DMath.AngleDiff(heading, desiredHeading));
        if (mb.Layer != MotionLayer.Air) desiredSpeed *= DMath.Clamp01((facing - 0.2f) / 0.8f);

        // Accelerate.
        float speed = mv.Speed;
        float dv = mb.Acceleration * dt;
        speed = speed < desiredSpeed ? MathF.Min(desiredSpeed, speed + dv) : MathF.Max(desiredSpeed, speed - dv * 2f);
        mv.Speed = speed;

        var vel = DMath.Dir(heading) * speed;
        vel += Separation(w, i, bp, pos) * MathF.Max(1f, mb.MaxSpeed * 0.5f);
        var newPos = pos + vel * dt;

        if (mb.Layer != MotionLayer.Air && !CanStand(w, mb.Layer, newPos))
        {
            // Try sliding along each axis.
            var sx = new Vector2(newPos.X, pos.Y);
            var sz = new Vector2(pos.X, newPos.Y);
            newPos = CanStand(w, mb.Layer, sx) ? sx : CanStand(w, mb.Layer, sz) ? sz : pos;
            if (newPos == pos) mv.StuckTime += dt; else mv.StuckTime = 0;
        }
        else mv.StuckTime = 0;

        newPos = w.Terrain.Clamp(newPos);
        mv.Velocity = (newPos - pos) / dt;
        u.ScratchPos[i] = newPos;
        u.Heading[i] = heading;
        if (speed > 0.05f) u.Flags[i] |= UnitFlags.Moving; else u.Flags[i] &= ~UnitFlags.Moving;

        u.Elevation[i] = mb.Layer switch
        {
            MotionLayer.Air => w.Terrain.GetSurfaceHeight(newPos) + mb.Altitude,
            MotionLayer.Land or MotionLayer.Amphibious => w.Terrain.GetHeight(newPos),
            MotionLayer.Sub => w.Terrain.WaterLevel - 1.5f,
            _ => w.Terrain.GetSurfaceHeight(newPos),
        };
    }

    public static bool CanStand(SimWorld w, MotionLayer layer, Vector2 p)
    {
        int cx = (int)p.X, cz = (int)p.Y;
        if (!w.Terrain.CanTraverse(layer, cx, cz)) return false;
        return !w.Occupancy.IsBlocked(cx, cz);
    }

    private struct SeparationVisitor : IUnitVisitor
    {
        public SimWorld W;
        public int Self;
        public Vector2 Pos;
        public float Radius;
        public bool Air;
        public Vector2 Push;

        public bool Visit(int j)
        {
            if (j == Self) return true;
            var bpj = W.BpOf(j);
            if (bpj.IsStructure || (bpj.Movement.Layer == MotionLayer.Air) != Air) return true;
            var d = Pos - W.Units.Pos[j];
            float minDist = Radius + bpj.Radius;
            float d2 = d.LengthSquared();
            if (d2 >= minDist * minDist) return true;
            if (d2 < 1e-6f)
            {
                // Exactly overlapping: deterministic tie-break by index.
                d = DMath.Dir(Self * 2.399963f);
                d2 = 1e-6f;
            }
            float dist = MathF.Sqrt(d2);
            Push += d / dist * ((minDist - dist) / minDist);
            return true;
        }
    }

    private static Vector2 Separation(SimWorld w, int i, UnitBlueprint bp, Vector2 pos)
    {
        var v = new SeparationVisitor { W = w, Self = i, Pos = pos, Radius = bp.Radius, Air = bp.Movement.Layer == MotionLayer.Air };
        w.Grid.ForEachInRadius(pos, bp.Radius + 3f, ref v);
        return DMath.ClampLength(v.Push, 1.5f);
    }
}
