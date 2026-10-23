using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Pathfinding;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Moves mobile units.
/// <list type="bullet">
/// <item>Surface units (land, hover, amphibious, naval): follow the PathService waypoints with speed-dependent
///   corner lookahead, brake along the remaining path length, turn-rate limited; collide with impassable cells
///   (axis sliding); escape cells that became blocked under them; stuck/blocked detection requests a repath and
///   gives up near a crowded destination.</item>
/// <item>Separation with priorities (SupCom-like): units with a goal push through idle friendly units, idle units
///   yield sideways out of the way of moving friends; air units only separate from air units.</item>
/// <item>Formation groups (same order) share a pace: every member moves at remaining/T where T is the time the
///   slowest member needs, so the group arrives together at the slowest member's speed.</item>
/// <item>Air units never stop: they fly to the goal, then orbit it (radius from speed and turn rate); altitude
///   eases towards the blueprint flight altitude; no terrain collision.</item>
/// </list>
/// PARALLEL, double-buffered: new positions go to UnitStore.ScratchPos, then are committed, so every unit
/// reads its neighbours' positions (and last tick's velocity/moving snapshot) from the same moment.
/// </summary>
public static partial class MovementSystem
{
    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        ref var mv = ref w.Units.Move[i];
        bool fresh = mv.OrbitDir == 0f; // UnitStore.Allocate zeroes Move; a blueprint swap re-runs this (OrbitDir = ±1)
        mv.PathId = -1;
        mv.HasGoal = false;
        mv.OrbitCenter = w.Units.Pos[i];
        mv.OrbitDir = (i & 1) == 0 ? 1f : -1f;
        // Subs start submerged (spawn and factory release put them at their running depth); StartSurfaced divers
        // (Atlantis) start at the surface. A blueprint swap keeps the dive state and the current depth.
        if (fresh && bp.Movement.Layer == MotionLayer.Sub && bp.IsMobile)
        {
            if (Naval.Submarines.StartsSurfaced(bp)) w.Units.Flags[i] |= UnitFlags.Surfaced;
            w.Units.Elevation[i] = (w.Units.Flags[i] & UnitFlags.Surfaced) != 0
                ? w.Terrain.WaterLevel : w.Terrain.WaterLevel - Naval.NavalMotion.SubDepth;
        }
    }

    /// <summary>
    /// Called by CommandSystem when a (non-queued) order is issued to a set of units: formation orders given to
    /// several surface units form a group that keeps a common pace. Other orders clear the group. SEQUENTIAL.
    /// </summary>
    public static void AssignGroup(SimWorld w, List<int> units, bool formation)
    {
        int members = 0;
        foreach (int i in units) if (Groupable(w.BpOf(i))) members++;
        int gid = formation && members > 1 ? w.Paths.NewGroupId() : 0;
        foreach (int i in units) w.Units.Move[i].GroupId = Groupable(w.BpOf(i)) ? gid : 0;
    }

    private static bool Groupable(UnitBlueprint bp) => bp.IsMobile && bp.Movement.Layer is not (MotionLayer.Air or MotionLayer.None);

    /// <summary>True when the last movement step arrived at this goal (also when the goal was unreachable and the
    /// unit stopped at the nearest reachable point). OrderSystem uses it to complete orders.</summary>
    public static bool ArrivedAt(in MoveState mv, Vector2 dest) => mv.Arrived && mv.Goal == dest;

    /// <summary>Idle orbit radius of an air unit (comfortably above its minimum turning radius).</summary>
    public static float OrbitRadius(MovementBlueprint mb)
    {
        float omega = MathF.Max(0.1f, mb.TurnRate * DMath.Deg2Rad);
        return MathF.Max(3f, 1.25f * (mb.MaxSpeed * AirOrbitSpeedFactor) / omega);
    }

    private const float AirOrbitSpeedFactor = 0.8f;

    public static void Run(SimWorld w)
    {
        long sw = Stopwatch.GetTimestamp(); // (no Stopwatch object per tick)
        var u = w.Units;
        var paths = w.Paths;
        paths.EnsureGrids();
        var sep = paths.Sep;
        var navHeading = Naval.NavalMotion.HeadingSnapshot(w);
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            ref var mv = ref u.Move[i];
            var bp = w.BpOf(i);
            ref var s = ref sep[i];
            s.Pos = u.Pos[i];
            s.Vel = mv.Velocity;
            s.Radius = bp.Radius;
            s.Team = w.Armies[u.Army[i]].Team;
            s.Kind = bp.IsStructure || !u.IsComplete(i) || bp.Movement.Layer == MotionLayer.None ? Naval.NavalMotion.KindNone
                : Naval.NavalMotion.KindOf(u, i, bp);
            s.Moving = mv.HasGoal ? (byte)1 : (byte)0;
            if (s.Kind >= Naval.NavalMotion.KindShip) navHeading[i] = u.Heading[i];
        }
        ComputeGroupPace(w);

        Par.For(u.AliveCount, 256, w, static (w, _, start, end) =>
        {
            var u = w.Units;
            const float dt = SimWorld.Dt;
            for (int k = start; k < end; k++)
            {
                int i = u.AliveList[k];
                var bp = w.BpOf(i);
                if (bp.IsStructure || !u.IsComplete(i) || bp.Movement.Layer == MotionLayer.None
                    || (u.Flags[i] & UnitFlags.Stunned) != 0 && Combat.EmpStun.HoldStill(u, i)) // EMP stun: stands still
                {
                    u.ScratchPos[i] = u.Pos[i];
                    continue;
                }
                if (bp.Movement.Layer == MotionLayer.Air) StepAir(w, i, bp, dt);
                else StepSurface(w, i, bp, dt);
            }
        });
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            u.Pos[i] = u.ScratchPos[i];
        }
        paths.Stats.LastMoveMs = Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
        paths.Stats.TotalMoveMs += paths.Stats.LastMoveMs;
        paths.Stats.Ticks++;
    }

    /// <summary>Per group: the time the slowest member needs to arrive, and the slowest member speed. Sequential;
    /// max/min are order-independent so the result is deterministic.</summary>
    private static void ComputeGroupPace(SimWorld w)
    {
        var u = w.Units;
        var groups = w.Paths.Groups;
        groups.Clear();
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            ref var mv = ref u.Move[i];
            if (mv.GroupId == 0 || !mv.HasGoal || !u.IsComplete(i)) continue;
            if (mv.StuckTime > 2f || mv.BlockedTime > 1f) continue; // don't let a stuck member freeze the group
            var bp = w.BpOf(i);
            float speed = MathF.Max(0.1f, bp.Movement.MaxSpeed);
            float rem = MathF.Max(0, w.Paths.RemainingDistance(i, mv, u.Pos[i]) - mv.ArriveRadius);
            float t = rem / speed;
            if (groups.TryGetValue(mv.GroupId, out var g))
                groups[mv.GroupId] = new PathService.GroupPace { Time = MathF.Max(g.Time, t), MinSpeed = MathF.Min(g.MinSpeed, speed) };
            else
                groups[mv.GroupId] = new PathService.GroupPace { Time = t, MinSpeed = speed };
        }
    }

    private static void Arrive(ref MoveState mv)
    {
        mv.Arrived = true;
        mv.HasGoal = false;
        mv.StuckTime = 0;
        mv.BlockedTime = 0;
    }

    private static void StepSurface(SimWorld w, int i, UnitBlueprint bp, float dt)
    {
        var u = w.Units;
        var paths = w.Paths;
        ref var mv = ref u.Move[i];
        var mb = bp.Movement;
        var grid = paths.GridFor(mb.Layer)!;
        var pos = u.Pos[i];
        float heading = u.Heading[i];
        float desiredSpeed = 0;
        float desiredHeading = heading;
        bool wantsMove = false;
        float remaining = 0;
        mv.Arrived = false;
        bool onBlocked = !grid.CanStand(pos);

        if (mv.HasGoal)
        {
            var end = paths.PathEnd(i, mv, pos);
            remaining = paths.RemainingDistance(i, mv, pos);
            // Unreachable goal (e.g. a builder walking to a water site): the arrive radius is meant around the goal,
            // so get as close as it allows instead of stopping a full radius short of the nearest reachable point.
            float arrive = mv.ArriveRadius;
            if (mv.PathToNearest && end != mv.Goal) arrive = MathF.Max(MathF.Min(arrive, 1f), arrive - Vector2.Distance(end, mv.Goal));
            if (!mv.PathPartial && Vector2.DistanceSquared(pos, end) <= arrive * arrive)
            {
                Arrive(ref mv);
            }
            else
            {
                var wp = paths.CurrentWaypoint(i, ref mv, pos);
                var to = wp - pos;
                if (to.LengthSquared() > 1e-6f)
                {
                    desiredHeading = DMath.HeadingOf(to);
                    float cap = mb.MaxSpeed;
                    if (mv.GroupId != 0 && paths.Groups.TryGetValue(mv.GroupId, out var gp) && gp.Time > 0.05f)
                    {
                        float sync = MathF.Max(0, remaining - mv.ArriveRadius) / gp.Time;
                        cap = MathF.Min(MathF.Max(sync, 0.5f * gp.MinSpeed), mb.MaxSpeed);
                    }
                    // Brake along the remaining path: v = sqrt(2 a d).
                    float brake = mv.PathPartial
                        ? float.MaxValue
                        : DMath.Sqrt(2f * mb.Acceleration * MathF.Max(0, remaining - mv.ArriveRadius * 0.5f)) + 0.2f;
                    desiredSpeed = MathF.Min(cap, brake);
                    wantsMove = true;
                }
            }
        }
        else if (onBlocked)
        {
            // A structure was placed on this unit (or it was pushed into a blocked cell): walk out.
            int cx = (int)MathF.Floor(pos.X), cz = (int)MathF.Floor(pos.Y);
            if (grid.FindNearestPassable(cx, cz, 1, 8, out int ex, out int ez))
            {
                var to = new Vector2(ex + 0.5f, ez + 0.5f) - pos;
                if (to.LengthSquared() > 1e-6f)
                {
                    desiredHeading = DMath.HeadingOf(to);
                    desiredSpeed = mb.MaxSpeed * 0.6f;
                    wantsMove = true;
                }
            }
        }

        if (mv.LeaveTime > 0) FactoryExitMotion.Steer(w, i, bp, ref mv, pos, onBlocked, dt, ref desiredHeading, ref desiredSpeed, ref wantsMove);

        // Turn; vehicles must roughly face the direction before driving at full speed.
        float maxTurn = mb.TurnRate * DMath.Deg2Rad * dt;
        heading = DMath.RotateTowards(heading, desiredHeading, maxTurn);
        float facing = MathF.Cos(DMath.AngleDiff(heading, desiredHeading));
        desiredSpeed *= DMath.Clamp01((facing - 0.2f) / 0.8f);

        // Accelerate / brake.
        float speed = mv.Speed;
        float dv = mb.Acceleration * dt;
        speed = speed < desiredSpeed ? MathF.Min(desiredSpeed, speed + dv) : MathF.Max(desiredSpeed, speed - dv * 2f);
        mv.Speed = speed;

        var vel = DMath.Dir(heading) * speed;
        var push = paths.Sep[i].Kind >= Naval.NavalMotion.KindShip
            ? Naval.NavalMotion.Separation(w, i, bp, pos, mv.HasGoal)
            : Separation(w, i, bp, pos, mv.HasGoal);
        vel += push * MathF.Max(1f, mb.MaxSpeed * 0.5f);
        var newPos = pos + vel * dt;

        bool blocked = false;
        if (!onBlocked && !grid.CanStand(newPos))
        {
            var sx = new Vector2(newPos.X, pos.Y);
            var sz = new Vector2(pos.X, newPos.Y);
            newPos = grid.CanStand(sx) ? sx : grid.CanStand(sz) ? sz : pos;
            blocked = true;
        }
        newPos = w.Terrain.Clamp(newPos);

        // Stuck handling.
        if (mv.HasGoal && wantsMove)
        {
            if (blocked) mv.BlockedTime += dt;
            else mv.BlockedTime = MathF.Max(0, mv.BlockedTime - dt);
            if (mv.BlockedTime > 1.0f)
            {
                // Pushed against terrain/structures: the path is probably stale (or we were shoved off it).
                if (mv.StuckRepaths < 8) { mv.RepathRequested = true; mv.StuckRepaths++; }
                mv.BlockedTime = 0;
            }
            float moved = Vector2.Distance(newPos, pos);
            if (moved < 0.15f * mb.MaxSpeed * dt) mv.StuckTime += dt;
            else mv.StuckTime = MathF.Max(0, mv.StuckTime - 0.5f * dt);
            if (mv.StuckTime > 3f)
            {
                mv.StuckTime = 0;
                float crowdR = mb.Layer is MotionLayer.Water or MotionLayer.Sub ? Naval.NavalMotion.CrowdRadius(bp) : bp.Radius;
                if (remaining < mv.ArriveRadius + 4f + 2f * crowdR) Arrive(ref mv); // crowded destination: good enough
                else if (mv.StuckRepaths < 8) { mv.RepathRequested = true; mv.StuckRepaths++; }
            }
        }
        else
        {
            mv.BlockedTime = 0;
            mv.StuckTime = 0;
        }

        mv.Velocity = (newPos - pos) / dt;
        u.ScratchPos[i] = newPos;
        u.Heading[i] = heading;
        if (speed > 0.05f) u.Flags[i] |= UnitFlags.Moving; else u.Flags[i] &= ~UnitFlags.Moving;

        u.Elevation[i] = mb.Layer switch
        {
            MotionLayer.Land or MotionLayer.Amphibious => w.Terrain.GetHeight(newPos),
            MotionLayer.Sub => Naval.NavalMotion.SubElevation(w, i, u.Elevation[i]),
            _ => w.Terrain.GetSurfaceHeight(newPos),
        };
    }

    private static void StepAir(SimWorld w, int i, UnitBlueprint bp, float dt)
    {
        if (bp.IsVtol) { StepVtol(w, i, bp, dt); return; } // transports hover/land (MovementSystem.Vtol.cs)
        var u = w.Units;
        ref var mv = ref u.Move[i];
        var mb = bp.Movement;
        var pos = u.Pos[i];
        float heading = u.Heading[i];
        mv.Arrived = false;
        float orbitR = OrbitRadius(mb);
        Vector2 target = pos + DMath.Dir(heading);
        float desiredSpeed = mb.MaxSpeed * AirOrbitSpeedFactor;

        if (mv.HasGoal)
        {
            mv.OrbitCenter = mv.Goal;
            float arrive = MathF.Max(mv.ArriveRadius, 0.35f * orbitR);
            if (Vector2.DistanceSquared(pos, mv.Goal) <= arrive * arrive)
            {
                Arrive(ref mv);
            }
            else
            {
                target = mv.Goal;
                desiredSpeed = mb.MaxSpeed;
            }
        }
        if (!mv.HasGoal)
        {
            // Orbit: steer at a point on the circle a bit ahead of our angular position.
            var rel = pos - mv.OrbitCenter;
            float d = rel.Length();
            if (d > orbitR * 3f) target = mv.OrbitCenter;
            else
            {
                float ang = d > 1e-3f ? DMath.HeadingOf(rel) : heading;
                target = mv.OrbitCenter + DMath.Dir(ang + mv.OrbitDir * 0.8f) * orbitR;
            }
        }

        var to = target - pos;
        float desiredHeading = to.LengthSquared() > 1e-6f ? DMath.HeadingOf(to) : heading;
        heading = DMath.RotateTowards(heading, desiredHeading, mb.TurnRate * DMath.Deg2Rad * dt);

        desiredSpeed *= Transport.TransportSystem.AirSpeedFactor(w, i, bp); // out of fuel = slow
        float speed = mv.Speed;
        float dv = mb.Acceleration * dt;
        speed = speed < desiredSpeed ? MathF.Min(desiredSpeed, speed + dv) : MathF.Max(desiredSpeed, speed - dv);
        mv.Speed = speed;

        var vel = DMath.Dir(heading) * speed + Separation(w, i, bp, pos, true) * MathF.Max(1f, mb.MaxSpeed * 0.25f);
        var newPos = w.Terrain.Clamp(pos + vel * dt);
        mv.Velocity = (newPos - pos) / dt;
        mv.StuckTime = 0;
        u.ScratchPos[i] = newPos;
        u.Heading[i] = heading;
        u.Flags[i] |= UnitFlags.Moving;

        float targetAlt = w.Terrain.GetSurfaceHeight(newPos) + mb.Altitude;
        float climb = MathF.Max(4f, mb.Altitude * 0.5f) * dt;
        u.Elevation[i] += Math.Clamp(targetAlt - u.Elevation[i], -climb, climb);
    }

    /// <summary>True if a unit of this layer may stand at p (terrain + structures). Thread-safe.</summary>
    public static bool CanStand(SimWorld w, MotionLayer layer, Vector2 p) => w.Paths.CanStand(layer, p);

    private struct SeparationVisitor : IUnitVisitor
    {
        public PathService.SepInfo[] Sep;
        public int Self;
        public int Team;
        public byte Kind;
        public Vector2 Pos;
        public float Radius;
        public float MaxReach2;
        public bool SelfMoving;
        public Vector2 Push;

        public bool Visit(int j)
        {
            ref var o = ref Sep[j];
            var d = Pos - o.Pos;
            float d2 = d.LengthSquared();
            if (d2 >= MaxReach2 || j == Self) return true; // cheap rejects first
            // Land-kind units also collide with ships (circle model); ships/subs use NavalMotion.Separation.
            if (o.Kind != Kind && !(Kind == Naval.NavalMotion.KindSurface && o.Kind == Naval.NavalMotion.KindShip)) return true;
            bool air = Kind == 2;
            bool jMoving = o.Moving != 0;
            bool friendly = o.Team == Team;
            float reach = Radius + o.Radius;
            if (air) reach += 1f;
            else if (!SelfMoving && jMoving && friendly) reach += 0.8f; // make way early for moving friends
            if (d2 >= reach * reach) return true;
            Vector2 n;
            float dist;
            if (d2 < 1e-8f)
            {
                // Exactly overlapping: deterministic direction from the unit index.
                n = DMath.Dir(Self * 2.399963f);
                dist = 0;
            }
            else
            {
                dist = MathF.Sqrt(d2);
                n = d / dist;
            }
            float overlap = (reach - dist) / reach;
            if (air) Push += n * (overlap * 0.5f);
            else AddSurfacePush(ref Push, n, d, overlap, friendly, SelfMoving, jMoving, o.Vel);
            return true;
        }
    }

    /// <summary>
    /// Surface separation weighting (priorities): movers push through idle friends (0.25), friends both moving 0.6,
    /// idle units yield strongly (1.6) and sideways out of a moving friend's way; enemies push 1:1.
    /// <paramref name="d"/> = self − neighbour center offset (side choice).
    /// </summary>
    internal static void AddSurfacePush(ref Vector2 push, Vector2 n, Vector2 d, float overlap, bool friendly,
        bool selfMoving, bool jMoving, Vector2 jVel)
    {
        float weight = 1f;
        if (friendly)
        {
            if (selfMoving && !jMoving) weight = 0.25f; // push through idle friends; they step aside
            else if (selfMoving && jMoving) weight = 0.6f;
            else if (!selfMoving && jMoving)
            {
                weight = 1.6f;
                float vl = jVel.Length();
                if (vl > 0.1f)
                {
                    var side = new Vector2(-jVel.Y, jVel.X) / vl;
                    if (Vector2.Dot(side, d) < 0) side = -side;
                    push += side * (overlap * 1.2f);
                }
            }
        }
        push += n * (overlap * weight);
    }

    private static Vector2 Separation(SimWorld w, int i, UnitBlueprint bp, Vector2 pos, bool selfMoving)
    {
        var paths = w.Paths;
        ref var self = ref paths.Sep[i];
        float maxReach = bp.Radius + paths.MaxMobileRadius + 1f;
        var v = new SeparationVisitor
        {
            Sep = paths.Sep,
            Self = i,
            Team = self.Team,
            Kind = self.Kind,
            Pos = pos,
            Radius = bp.Radius,
            MaxReach2 = maxReach * maxReach,
            SelfMoving = selfMoving,
        };
        w.Grid.ForEachInRadius(pos, maxReach, ref v);
        return DMath.ClampLength(v.Push, 1.5f);
    }
}
