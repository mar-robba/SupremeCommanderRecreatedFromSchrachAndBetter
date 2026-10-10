using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Moves projectiles, detects impacts and emits damage events.
/// PARALLEL phase over Projectiles.AliveList (projectiles alive at the start of the tick; the ones fired this tick
/// start moving next tick): each projectile integrates its own state (gravity, missile homing) and records its
/// end of life into per-chunk buffers. SEQUENTIAL phase: impacts are applied in chunk order (damage events,
/// effects) and slots freed, then the alive list is rebuilt so snapshots include this tick's new shots.
/// Impact rules:
/// - Straight shots/missiles hit the first enemy collision sphere along their path (any enemy on a layer the
///   weapon can target), else stop at the ground/water surface.
/// - Ballistic shells and bombs explode on ground/water contact (or on their target's sphere).
/// - Flak (area weapon fired at an aircraft) bursts at its closest approach when the target is inside the blast,
///   else at its aim point (predicted intercept), else when its lifetime runs out.
/// - Missiles steer (limited turn rate) toward the predicted intercept point of their target.
/// - DamageRadius &gt; 0 → AreaDamageEvent (layers = weapon TargetMask), else DamageEvent to the unit hit
///   (ground hits damage the nearest enemy whose sphere contains the impact point, if any).
/// </summary>
public static class ProjectileSystem
{
    /// <summary>Missile turn rate in radians per second.</summary>
    public const float HomingTurnRate = 5f;
    /// <summary>Straight shots only collide with terrain this far below the surface (grazing shots fly on).</summary>
    public const float StraightGroundTolerance = 0.3f;
    private const int Chunk = 256;
    private static readonly float HomingCos = MathF.Cos(HomingTurnRate * SimWorld.Dt);
    private static readonly float HomingSin = MathF.Sin(HomingTurnRate * SimWorld.Dt);

    public static void Run(SimWorld w)
    {
        var s = CombatState.Of(w);
        s.Prepare(w);
        var P = w.Projectiles;
        int n = P.AliveCount;
        s.Impacts.Reset(Par.ChunkCount(n, Chunk));
        Par.For(n, Chunk, (c, start, end) =>
        {
            var impacts = s.Impacts[c];
            for (int k = start; k < end; k++)
            {
                int p = P.AliveList[k];
                if (P.Alive[p]) Step(w, s, p, impacts);
            }
        });

        for (int c = 0; c < s.Impacts.ChunkCount; c++)
        {
            var impacts = s.Impacts[c];
            for (int k = 0; k < impacts.Count; k++)
            {
                Apply(w, impacts[k]);
                P.Free(impacts[k].Projectile);
            }
            impacts.Clear();
        }
        P.RebuildAliveList();
    }

    private static void Step(SimWorld w, CombatState s, int p, List<ProjectileImpact> impacts)
    {
        var P = w.Projectiles;
        var u = w.Units;
        var terrain = w.Terrain;
        const float dt = SimWorld.Dt;
        var flags = P.Flags[p];
        var pos0 = P.Pos[p];
        var vel = P.Vel[p];
        var th = P.Target[p];
        int tj = u.IsActive(th) ? th.Index : -1;

        if ((flags & ProjectileFlags.Homing) != 0 && tj >= 0)
        {
            // Lead pursuit: steer at the predicted intercept point (handles circling aircraft).
            var aim = Ballistics.LeadStraight(pos0, s.Center(u, tj), u.Move[tj].Velocity, s.Omega[tj], MathF.Max(1f, vel.Length()), out _);
            vel = Ballistics.Steer(vel, aim - pos0, HomingCos, HomingSin);
        }
        float g = P.Gravity[p];
        if (g != 0) vel.Y -= g * dt;
        var pos1 = pos0 + vel * dt;
        float life = P.TimeLeft[p] - dt;
        P.Pos[p] = pos1;
        P.Vel[p] = vel;
        P.TimeLeft[p] = life;

        bool ballistic = (flags & ProjectileFlags.Ballistic) != 0;

        // Ground / water surface crossing along the segment (param in [0,1], 2 = none).
        float tol = ballistic ? 0f : StraightGroundTolerance;
        float h1 = pos1.Y - terrain.GetSurfaceHeight(new Vector2(pos1.X, pos1.Z)) + tol;
        float groundT = 2f;
        if (h1 < 0)
        {
            float h0 = pos0.Y - terrain.GetSurfaceHeight(new Vector2(pos0.X, pos0.Z)) + tol;
            groundT = h0 <= 0 ? 0 : h0 / (h0 - h1);
        }

        // Flak fuse: burst at the closest approach to the air target if it is inside the blast, or when passing the
        // aim point (timed fuse set to the predicted intercept); if both happen this tick, the burst nearer the target.
        if ((flags & ProjectileFlags.ProximityFuse) != 0)
        {
            bool burst = false;
            var at = pos1;
            float best = float.MaxValue;
            bool air = tj >= 0 && s.Layer[tj] == LayerMask.Air;
            var c = air ? s.Center(u, tj) : P.TargetPos[p];
            if (air)
            {
                float t = Ballistics.ClosestParam(pos0, pos1, c);
                var pt = Vector3.Lerp(pos0, pos1, t);
                float d2 = Vector3.DistanceSquared(pt, c);
                var ti = s.Catalog.Units[u.Bp[tj]];
                float lethal = P.DamageRadius[p] + ti.TargetRadius;
                if (t <= groundT && (t < 1f || d2 <= ti.HitRadius * ti.HitRadius) && d2 <= lethal * lethal)
                {
                    burst = true; at = pt; best = d2;
                }
            }
            float ta = Ballistics.ClosestParam(pos0, pos1, P.TargetPos[p]);
            if (ta < 1f && ta <= groundT)
            {
                var pa = Vector3.Lerp(pos0, pos1, ta);
                float d2 = Vector3.DistanceSquared(pa, c);
                if (!burst || d2 < best) { burst = true; at = pa; }
            }
            if (burst)
            {
                impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Burst, Unit = -1, Pos = at });
                return;
            }
        }

        // Direct hits on unit collision spheres.
        int hit = -1;
        float hitT = 2f;
        var mask = WeaponMask(w, p);
        if (!ballistic)
        {
            var v = new HitVisitor
            {
                W = w, S = s, Army = P.Army[p], Mask = mask, A = pos0, B = pos1, BestT = 2f, Best = -1,
            };
            var mid = new Vector2(pos0.X + pos1.X, pos0.Z + pos1.Z) * 0.5f;
            float half = new Vector2(pos1.X - pos0.X, pos1.Z - pos0.Z).Length() * 0.5f;
            s.ForEachInRadius(w, mid, half + s.Catalog.MaxRadius + CombatState.GridSlack, s.EnemyArmies[P.Army[p]], ref v);
            hit = v.Best;
            hitT = v.BestT;
        }
        else if (tj >= 0)
        {
            float t = Ballistics.SegmentSphere(pos0, pos1, s.Center(u, tj), s.Catalog.Units[u.Bp[tj]].HitRadius);
            if (t >= 0) { hit = tj; hitT = t; }
        }
        if (hit >= 0 && hitT <= groundT)
        {
            impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Unit, Unit = hit, Pos = Vector3.Lerp(pos0, pos1, hitT) });
            return;
        }

        if (groundT <= 1f)
        {
            var gp = Vector3.Lerp(pos0, pos1, groundT);
            int victim = P.DamageRadius[p] > 0 ? -1 : NearestEnemyAt(w, s, P.Army[p], mask, gp);
            impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Ground, Unit = victim, Pos = gp });
            return;
        }

        if (life <= 0)
        {
            var kind = (flags & ProjectileFlags.ProximityFuse) != 0 ? ImpactKind.Burst : ImpactKind.Expire;
            impacts.Add(new ProjectileImpact { Projectile = p, Kind = kind, Unit = -1, Pos = pos1 });
            return;
        }

        if (pos1.X < -8 || pos1.Z < -8 || pos1.X > terrain.Width + 8 || pos1.Z > terrain.Height + 8)
            impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Expire, Unit = -1, Pos = pos1 });
    }

    private static LayerMask WeaponMask(SimWorld w, int p)
    {
        var P = w.Projectiles;
        var weapons = w.Blueprints[P.SourceBp[p]].Weapons;
        int wi = P.WeaponIndex[p];
        return (uint)wi < (uint)weapons.Length ? weapons[wi].TargetMask : LayerMask.All;
    }

    /// <summary>Sequential: damage + effects for one finished projectile.</summary>
    private static void Apply(SimWorld w, in ProjectileImpact imp)
    {
        if (imp.Kind == ImpactKind.Expire) return;
        var P = w.Projectiles;
        int p = imp.Projectile;
        float radius = P.DamageRadius[p];
        int army = P.Army[p];
        int bp = P.SourceBp[p];
        if (radius > 0)
        {
            w.Events.AreaDamage.Add(new AreaDamageEvent
            {
                Center = new Vector2(imp.Pos.X, imp.Pos.Z),
                Radius = radius,
                Amount = P.Damage[p],
                Source = P.Source[p],
                SourceArmy = army,
                Layers = WeaponMask(w, p),
            });
            CombatFx.Add(w, EffectKind.Explosion, imp.Pos, radius, army, bp);
            return;
        }
        if (imp.Unit >= 0 && w.Units.IsActive(imp.Unit))
        {
            w.Events.Damage.Add(new DamageEvent
            {
                Target = w.Units.HandleOf(imp.Unit),
                Source = P.Source[p],
                SourceArmy = army,
                Amount = P.Damage[p],
            });
        }
        CombatFx.Add(w, EffectKind.Impact, imp.Pos, 0.5f, army, bp);
    }

    private static int NearestEnemyAt(SimWorld w, CombatState s, int army, LayerMask mask, Vector3 pos)
    {
        var v = new NearestVisitor { W = w, S = s, Army = army, Mask = mask, Pos = new Vector2(pos.X, pos.Z), Best = -1, BestD2 = float.MaxValue };
        s.ForEachInRadius(w, v.Pos, s.Catalog.MaxRadius + 0.5f + CombatState.GridSlack, s.EnemyArmies[army], ref v);
        return v.Best;
    }

    /// <summary>Earliest enemy collision sphere crossed by segment A→B (ties: lowest index).</summary>
    private struct HitVisitor : IUnitVisitor
    {
        public SimWorld W;
        public CombatState S;
        public int Army;
        public LayerMask Mask;
        public Vector3 A, B;
        public float BestT;
        public int Best;

        public bool Visit(int j)
        {
            var u = W.Units;
            if (!S.IsEnemyArmy(Army, u.Army[j])) return true;
            if ((S.Layer[j] & Mask) == 0) return true;
            if (!u.IsActive(j)) return true;
            float t = Ballistics.SegmentSphere(A, B, S.Center(u, j), S.Catalog.Units[u.Bp[j]].HitRadius);
            if (t < 0 || t > BestT || (t == BestT && j > Best)) return true;
            BestT = t;
            Best = j;
            return true;
        }
    }

    /// <summary>Nearest enemy whose radius (+0.5) contains the point (ground hits of single-target weapons).</summary>
    private struct NearestVisitor : IUnitVisitor
    {
        public SimWorld W;
        public CombatState S;
        public int Army;
        public LayerMask Mask;
        public Vector2 Pos;
        public int Best;
        public float BestD2;

        public bool Visit(int j)
        {
            var u = W.Units;
            if (!S.IsEnemyArmy(Army, u.Army[j])) return true;
            if ((S.Layer[j] & Mask) == 0) return true;
            if (!u.IsActive(j)) return true;
            float d2 = Vector2.DistanceSquared(u.Pos[j], Pos);
            float r = S.Catalog.Units[u.Bp[j]].TargetRadius + 0.5f;
            if (d2 > r * r || d2 > BestD2 || (d2 == BestD2 && j > Best)) return true;
            Best = j;
            BestD2 = d2;
            return true;
        }
    }
}
