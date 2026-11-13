using System.Numerics;
using System.Runtime.CompilerServices;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Pathfinding;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Naval;

/// <summary>
/// Naval movement helpers used by <see cref="MovementSystem"/>:
/// <list type="bullet">
/// <item>Separation kinds: surface ships (Water layer and SURFACED subs) collide with each other (every army: it's
///   physical) and, as before, with land-kind surface units (hover/amphibious, circle model); submerged subs only
///   collide with other submerged subs; ships and submerged subs pass over/under each other.</item>
/// <item>Hulls: ships and subs are long, so ship-ship and sub-sub separation uses capsules (segment along the heading,
///   half-width from the visual hull) instead of the collision circle, so hulls don't overlap.</item>
/// <item>Sub depth: submerged subs run <see cref="SubDepth"/> below the water surface, surfaced ones at the surface,
///   with a smooth dive/surface transition (<see cref="DiveRate"/>).</item>
/// </list>
/// Parallel-safe: neighbours are read from <see cref="PathService.SepInfo"/> and a heading snapshot taken in the
/// sequential prepare loop, so every unit only writes its own data.
/// </summary>
public static class NavalMotion
{
    /// <summary>Separation kinds stored in <see cref="PathService.SepInfo.Kind"/>.</summary>
    public const byte KindNone = 0, KindSurface = 1, KindAir = 2, KindShip = 3, KindSubmerged = 4;
    /// <summary>Depth of a submerged sub below the water surface.</summary>
    public const float SubDepth = 1.5f;
    /// <summary>Vertical dive/surface speed (units per second).</summary>
    public const float DiveRate = 1.2f;
    /// <summary>Extra gap kept between hulls.</summary>
    public const float HullGap = 0.3f;

    private sealed class State
    {
        public float[] Heading = [];
        public float[] HalfSeg = [], HalfWidth = [];
        public float MaxHalfLength;
    }

    private static readonly ConditionalWeakTable<SimWorld, State> States = new();

    private static State Of(SimWorld w) => States.GetValue(w, Create);

    private static State Create(SimWorld w)
    {
        var s = new State { Heading = new float[w.Units.Capacity] };
        int n = w.Blueprints.All.Count;
        s.HalfSeg = new float[n];
        s.HalfWidth = new float[n];
        foreach (var bp in w.Blueprints.All)
        {
            Hull(bp, out float seg, out float width);
            s.HalfSeg[bp.Index] = seg;
            s.HalfWidth[bp.Index] = width;
            if (IsNaval(bp)) s.MaxHalfLength = MathF.Max(s.MaxHalfLength, seg + width);
        }
        return s;
    }

    public static bool IsNaval(UnitBlueprint bp) =>
        bp.IsMobile && (bp.Movement.Layer is MotionLayer.Water or MotionLayer.Sub || WalkingShips.IsWalkingShip(bp));

    /// <summary>
    /// Hull capsule of a naval unit: half-length of the center segment (along the heading) and half-width, from the
    /// visual hull size clamped to the collision radius (width ≤ Radius).
    /// </summary>
    public static void Hull(UnitBlueprint bp, out float halfSegment, out float halfWidth)
    {
        var size = bp.Visual.Size;
        halfWidth = Math.Clamp(size.X * 0.5f, bp.Radius * 0.5f, bp.Radius);
        float halfLength = MathF.Max(size.Z * 0.5f, halfWidth);
        halfSegment = IsNaval(bp) ? halfLength - halfWidth : 0;
    }

    /// <summary>Separation kind of a mobile, complete unit (structures/unfinished are <see cref="KindNone"/>).</summary>
    public static byte KindOf(UnitStore u, int i, UnitBlueprint bp) => bp.Movement.Layer switch
    {
        MotionLayer.Air => KindAir,
        MotionLayer.Water => KindShip,
        MotionLayer.Sub => (u.Flags[i] & UnitFlags.Surfaced) != 0 ? KindShip : KindSubmerged,
        MotionLayer.Hover when WalkingShips.IsWalkingShip(bp) => KindShip, // a hull on water and on land
        _ => KindSurface,
    };

    /// <summary>Sequential prepare step (MovementSystem): snapshot of the headings of naval units.</summary>
    internal static float[] HeadingSnapshot(SimWorld w) => Of(w).Heading;

    /// <summary>Distance at which a naval unit stuck near a crowded destination counts as arrived.</summary>
    public static float CrowdRadius(UnitBlueprint bp)
    {
        Hull(bp, out float seg, out float width);
        return MathF.Max(bp.Radius, seg + width);
    }

    /// <summary>Elevation of a sub this tick: eases towards the surface (surfaced) or <see cref="SubDepth"/> below it.</summary>
    public static float SubElevation(SimWorld w, int i, float current)
    {
        float water = w.Terrain.WaterLevel;
        float target = (w.Units.Flags[i] & UnitFlags.Surfaced) != 0 ? water : water - SubDepth;
        if (current < water - SubDepth - 0.01f || current > water + 0.01f) return target; // not yet placed (spawn)
        float step = DiveRate * SimWorld.Dt;
        return current + Math.Clamp(target - current, -step, step);
    }

    /// <summary>
    /// Separation push of ship/sub i (unscaled, length ≤ 1.5; same weighting as land units). Thread-safe: reads only
    /// the separation snapshot, the heading snapshot and immutable blueprint data.
    /// </summary>
    internal static Vector2 Separation(SimWorld w, int i, UnitBlueprint bp, Vector2 pos, bool selfMoving)
    {
        var s = Of(w);
        var paths = w.Paths;
        ref var self = ref paths.Sep[i];
        float seg = s.HalfSeg[bp.Index], width = s.HalfWidth[bp.Index];
        float maxReach = MathF.Max(seg + width + s.MaxHalfLength + HullGap, bp.Radius + paths.MaxMobileRadius) + 1f + HeadOnLook; // (11a-1) + channel rule lookahead
        var dir = DMath.Dir(s.Heading[i]);
        var v = new HullVisitor
        {
            Sep = paths.Sep,
            Bp = w.Units.Bp,
            S = s,
            Self = i,
            Team = self.Team,
            Kind = self.Kind,
            Pos = pos,
            A = pos - dir * seg,
            B = pos + dir * seg,
            Fwd = dir,
            Seg = seg,
            Width = width,
            Radius = bp.Radius,
            MaxReach2 = maxReach * maxReach,
            SelfMoving = selfMoving,
        };
        w.Grid.ForEachInRadius(pos, maxReach, ref v);
        return DMath.ClampLength(v.Push, 1.5f);
    }

    /// <summary>(11a-1) Channel rule weights (separation push units, see HullVisitor.HeadOn).</summary>
    private const float HeadOnSteer = 0.8f, HeadOnYield = 0.9f, HeadOnLook = 6f;

    private struct HullVisitor : IUnitVisitor
    {
        public PathService.SepInfo[] Sep;
        public int[] Bp;
        public State S;
        public int Self;
        public int Team;
        public byte Kind;
        public Vector2 Pos, A, B, Fwd;
        public float Width, Radius, Seg;
        public float MaxReach2;
        public bool SelfMoving;
        public Vector2 Push;

        public bool Visit(int j)
        {
            ref var o = ref Sep[j];
            var d = Pos - o.Pos;
            float d2 = d.LengthSquared();
            if (d2 >= MaxReach2 || j == Self) return true;
            Vector2 diff;
            float reach;
            if (o.Kind == Kind)
            {
                // Hull vs hull: closest points of the two capsule segments.
                int bj = Bp[j];
                float segJ = S.HalfSeg[bj];
                var dj = DMath.Dir(S.Heading[j]) * segJ;
                ClosestPoints(A, B, o.Pos - dj, o.Pos + dj, out var ci, out var cj);
                diff = ci - cj;
                reach = Width + S.HalfWidth[bj] + HullGap;
                if (SelfMoving && o.Moving != 0) HeadOn(j, o.Pos, segJ, reach);
            }
            else if (Kind == KindShip && o.Kind == KindSurface)
            {
                // Hover/amphibious units on the water: the old circle model.
                diff = d;
                reach = Radius + o.Radius;
            }
            else return true;
            float dist2 = diff.LengthSquared();
            if (dist2 >= reach * reach) return true;
            Vector2 n;
            float dist;
            if (dist2 < 1e-8f)
            {
                // Touching axes: push apart along the center offset, else a deterministic direction.
                dist = 0;
                n = d2 > 1e-8f ? d / MathF.Sqrt(d2) : DMath.Dir(Self * 2.399963f);
            }
            else
            {
                dist = MathF.Sqrt(dist2);
                n = diff / dist;
            }
            float overlap = (reach - dist) / reach;
            MovementSystem.AddSurfacePush(ref Push, n, d, overlap, o.Team == Team, SelfMoving, o.Moving != 0, o.Vel);
            return true;
        }

        /// <summary>
        /// (11a-1) Channel rule: a moving hull j ahead that comes the other way on a colliding line makes this ship
        /// steer to its own right (both do, so they pass port to port where the channel allows two); the ship of
        /// lower priority (the higher unit index) also brakes, so in a one-lane channel it queues instead of the two
        /// pushing bow to bow. Deterministic and local: only the separation snapshot and this unit's push.
        /// </summary>
        private void HeadOn(int j, Vector2 oPos, float segJ, float reach)
        {
            var fj = DMath.Dir(S.Heading[j]);
            if (Vector2.Dot(Fwd, fj) > -0.5f) return; // not oncoming
            var rel = oPos - Pos;
            float ahead = Vector2.Dot(rel, Fwd);
            float look = Seg + segJ + 2f * reach + 4f;
            if (ahead <= 0 || ahead >= look) return;
            var right = new Vector2(Fwd.Y, -Fwd.X);
            float lat = Vector2.Dot(rel, right);
            if (MathF.Abs(lat) >= reach) return; // lines already clear of each other
            float close = 1f - ahead / look;
            Push += right * (HeadOnSteer * close);
            if (Self > j) Push -= Fwd * (HeadOnYield * close);
        }
    }

    /// <summary>Closest points between segments p1-q1 and p2-q2 (Ericson, Real-Time Collision Detection 5.1.9).</summary>
    public static void ClosestPoints(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2, out Vector2 c1, out Vector2 c2)
    {
        const float eps = 1e-6f;
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        float a = Vector2.Dot(d1, d1), e = Vector2.Dot(d2, d2), f = Vector2.Dot(d2, r);
        float s, t;
        if (a <= eps && e <= eps) { s = 0; t = 0; }
        else if (a <= eps) { s = 0; t = DMath.Clamp01(f / e); }
        else
        {
            float c = Vector2.Dot(d1, r);
            if (e <= eps) { t = 0; s = DMath.Clamp01(-c / a); }
            else
            {
                float b = Vector2.Dot(d1, d2);
                float denom = a * e - b * b;
                s = denom > eps ? DMath.Clamp01((b * f - c * e) / denom) : 0;
                t = (b * s + f) / e;
                if (t < 0) { t = 0; s = DMath.Clamp01(-c / a); }
                else if (t > 1) { t = 1; s = DMath.Clamp01((b - c) / a); }
            }
        }
        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
    }
}
