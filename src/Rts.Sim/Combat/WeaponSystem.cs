using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Turret aiming and firing. PARALLEL decision phase (each unit writes only its own WeaponState) that appends
/// <see cref="ShotRequest"/>s to per-chunk buffers, then a SEQUENTIAL phase that spawns projectiles (or applies
/// beam damage) in chunk order, so projectile slots/ids are identical for any thread count.
/// - Turret yaw (WeaponState.TurretHeading, world space) turns at TurretTurnRate; fires when the target is within
///   [MinRange, Range + target radius] horizontally and the yaw error is within FiringTolerance.
/// - Moving targets are led using their MoveState.Velocity. Ballistic weapons solve a gravity arc for their
///   MuzzleVelocity (high arc for indirect-fire units). Bombs (Ballistic + MuzzleVelocity 0) are released when
///   the predicted impact point (plane velocity, free fall) lands within Range of the predicted target position.
///   Beams (MuzzleVelocity 0, not ballistic) hit instantly.
/// - Cooldown = 1 / RateOfFire (fractional remainders carried over so average rates are exact).
/// </summary>
public static class WeaponSystem
{
    private const int Chunk = 128;
    private const float CooldownEps = 1e-4f;

    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        var ws = w.Units.WeaponsOf(i);
        for (int k = 0; k < ws.Length; k++)
        {
            ws[k].TurretHeading = w.Units.Heading[i];
            ws[k].TurretPitch = 0;
            ws[k].Cooldown = 0;
            ws[k].RetargetTimer = (i + k) % 3;
        }
    }

    public static void Run(SimWorld w)
    {
        var s = CombatState.Of(w);
        s.Prepare(w);
        var u = w.Units;
        int n = u.AliveCount;
        s.Shots.Reset(Par.ChunkCount(n, Chunk));
        Par.For(n, Chunk, (c, start, end) =>
        {
            var shots = s.Shots[c];
            for (int k = start; k < end; k++) UpdateUnit(w, s, u.AliveList[k], shots);
        });

        // Sequential: spawn in chunk order (deterministic slot allocation).
        for (int c = 0; c < s.Shots.ChunkCount; c++)
        {
            var shots = s.Shots[c];
            for (int k = 0; k < shots.Count; k++) Emit(w, shots[k]);
            shots.Clear();
        }

        for (int k = 0; k < n; k++)
        {
            int i = u.AliveList[k];
            if (s.Engaged[i] != 0) u.Flags[i] |= UnitFlags.Firing;
            else u.Flags[i] &= ~UnitFlags.Firing;
        }
    }

    private static void UpdateUnit(SimWorld w, CombatState s, int i, List<ShotRequest> shots)
    {
        var u = w.Units;
        s.Engaged[i] = 0;
        if (!u.IsActive(i) || !u.IsComplete(i)) return;
        var info = s.Catalog.Units[u.Bp[i]];
        if (info.Weapons.Length == 0) return;
        var bp = w.Blueprints[u.Bp[i]];
        const float dt = SimWorld.Dt;
        var pos = u.Pos[i];
        float elev = u.Elevation[i];
        bool engaged = false;

        for (int wi = 0; wi < info.Weapons.Length; wi++)
        {
            ref var ws = ref u.Weapons[i * UnitStore.MaxWeapons + wi];
            ref readonly var winfo = ref info.Weapons[wi];
            var wbp = bp.Weapons[wi];
            ws.Cooldown -= dt;
            if (ws.Cooldown < -dt) ws.Cooldown = -dt; // no burst after idling

            // What are we shooting at?
            int tj = -1;
            Vector3 tc;
            Vector2 tv = Vector2.Zero;
            float tom = 0, tr = 0;
            if (u.IsActive(ws.Target))
            {
                tj = ws.Target.Index;
                tc = s.Center(u, tj);
                tv = u.Move[tj].Velocity;
                tom = s.Omega[tj];
                tr = s.Catalog.Units[u.Bp[tj]].TargetRadius;
            }
            else if (ws.HasGroundTarget)
            {
                var g = ws.GroundTarget;
                tc = new Vector3(g.X, w.Terrain.GetSurfaceHeight(g), g.Y);
            }
            else
            {
                // Idle: turret returns to the hull heading.
                ws.TurretHeading = DMath.RotateTowards(ws.TurretHeading, u.Heading[i], winfo.TurnRate * dt);
                ws.TurretPitch = 0;
                continue;
            }

            if (winfo.Kind == WeaponKind.Bomb)
            {
                if (UpdateBomb(w, s, i, wi, ref ws, winfo, wbp, tj, tc, tv, tom, tr, shots)) engaged = true;
                continue;
            }

            var muzzle = new Vector3(pos.X, elev + info.MuzzleHeight, pos.Y);
            Vector3 aim, launch;
            float flight;
            bool solution = true;
            switch (winfo.Kind)
            {
                case WeaponKind.Ballistic:
                    solution = Ballistics.SolveArc(muzzle, tc, wbp.MuzzleVelocity, Ballistics.Gravity, winfo.HighArc, out launch, out flight);
                    aim = tc;
                    if (tv != Vector2.Zero)
                    {
                        var tp = new Vector2(tc.X, tc.Z);
                        for (int it = 0; it < 3; it++)
                        {
                            var pp = Ballistics.PredictPosition(tp, tv, tom, flight);
                            aim = new Vector3(pp.X, tc.Y, pp.Y);
                            solution = Ballistics.SolveArc(muzzle, aim, wbp.MuzzleVelocity, Ballistics.Gravity, winfo.HighArc, out launch, out flight);
                        }
                    }
                    break;
                case WeaponKind.Beam:
                    aim = tc;
                    launch = Vector3.Zero;
                    flight = 0;
                    break;
                default:
                    aim = Ballistics.LeadStraight(muzzle, tc, tv, tom, wbp.MuzzleVelocity, out flight);
                    launch = SafeDir(aim - muzzle, ws.TurretHeading) * wbp.MuzzleVelocity;
                    break;
            }

            var toAim = new Vector2(aim.X - pos.X, aim.Z - pos.Y);
            float desiredYaw = toAim.LengthSquared() > 1e-6f ? DMath.HeadingOf(toAim) : ws.TurretHeading;
            ws.TurretHeading = DMath.RotateTowards(ws.TurretHeading, desiredYaw, winfo.TurnRate * dt);
            ws.TurretPitch = winfo.Kind == WeaponKind.Ballistic
                ? DMath.Atan2(launch.Y, new Vector2(launch.X, launch.Z).Length())
                : DMath.Atan2(aim.Y - muzzle.Y, MathF.Max(1e-3f, toAim.Length()));

            float d2 = Vector2.DistanceSquared(pos, new Vector2(tc.X, tc.Z));
            float maxR = wbp.Range + tr;
            if (d2 > maxR * maxR || d2 < wbp.MinRange * wbp.MinRange) continue;
            engaged = true;
            if (ws.Cooldown > CooldownEps || !solution) continue;
            if (MathF.Abs(DMath.AngleDiff(ws.TurretHeading, desiredYaw)) > winfo.Tolerance) continue;

            ws.Cooldown += winfo.Period;
            var flashPos = muzzle + new Vector3(MathF.Cos(ws.TurretHeading), 0, MathF.Sin(ws.TurretHeading)) * (bp.Radius * 0.6f);
            int count = Math.Max(1, wbp.ProjectilesPerShot);
            for (int p = 0; p < count; p++)
            {
                var a = aim;
                var vel = launch;
                if (wbp.Spread > 0)
                {
                    var rng = w.RandomFor(i, 1 + wi * 64 + p);
                    float r = wbp.Spread * MathF.Sqrt(rng.NextFloat());
                    float ang = rng.NextFloat() * DMath.TwoPi;
                    a.X += MathF.Cos(ang) * r;
                    a.Z += MathF.Sin(ang) * r;
                    if (winfo.Kind == WeaponKind.Ballistic)
                    {
                        if (!Ballistics.SolveArc(muzzle, a, wbp.MuzzleVelocity, Ballistics.Gravity, winfo.HighArc, out vel, out _)) vel = launch;
                    }
                    else if (winfo.Kind != WeaponKind.Beam)
                    {
                        vel = SafeDir(a - muzzle, ws.TurretHeading) * wbp.MuzzleVelocity;
                    }
                }

                var flags = ProjectileFlags.None;
                float life;
                switch (winfo.Kind)
                {
                    case WeaponKind.Ballistic:
                        flags |= ProjectileFlags.Ballistic;
                        life = flight * 1.5f + 2f;
                        break;
                    case WeaponKind.Homing:
                        flags |= ProjectileFlags.Homing;
                        life = flight * 2f + 1.5f;
                        break;
                    default:
                        life = MathF.Max(flight * 1.5f, wbp.Range * 1.25f / MathF.Max(1f, wbp.MuzzleVelocity)) + 0.3f;
                        break;
                }
                if (winfo.ProximityFuse && tj >= 0 && s.Layer[tj] == LayerMask.Air)
                {
                    // Flak: proximity fuse + timed fuse at the predicted intercept point (TargetPos).
                    flags |= ProjectileFlags.ProximityFuse;
                    if (winfo.Kind == WeaponKind.Direct) life = flight + 0.5f;
                }

                shots.Add(new ShotRequest
                {
                    Shooter = i,
                    Weapon = wi,
                    Bp = bp.Index,
                    Army = u.Army[i],
                    Source = u.HandleOf(i),
                    Target = tj >= 0 ? ws.Target : UnitHandle.None,
                    Pos = muzzle,
                    Vel = vel,
                    TargetPos = a,
                    Gravity = winfo.Kind == WeaponKind.Ballistic ? Ballistics.Gravity : 0f,
                    Life = life,
                    Damage = wbp.Damage,
                    DamageRadius = wbp.DamageRadius,
                    Flags = flags,
                    Instant = winfo.Kind == WeaponKind.Beam,
                    Flash = p == 0,
                    FlashPos = flashPos,
                });
            }
        }

        if (engaged) s.Engaged[i] = 1;
    }

    /// <summary>Bomber logic. Returns true while engaging (target within acquisition range).</summary>
    private static bool UpdateBomb(SimWorld w, CombatState s, int i, int wi, ref WeaponState ws, in WeaponInfo winfo,
        WeaponBlueprint wbp, int tj, Vector3 tc, Vector2 tv, float tom, float tr, List<ShotRequest> shots)
    {
        var u = w.Units;
        var pos = u.Pos[i];
        float elev = u.Elevation[i];
        ws.TurretHeading = u.Heading[i];
        ws.TurretPitch = -DMath.Pi * 0.5f;

        var tpos = new Vector2(tc.X, tc.Z);
        float acq = winfo.AcquireRange + tr;
        bool engaged = Vector2.DistanceSquared(pos, tpos) <= acq * acq;
        if (elev - w.Terrain.GetSurfaceHeight(pos) < 1.5f) return engaged; // landed aircraft can't bomb

        var release = new Vector3(pos.X, elev - 0.3f, pos.Y);
        float groundY = tj >= 0 ? u.Elevation[tj] : tc.Y;
        float h = release.Y - groundY;
        float tFall = h > 0 ? MathF.Sqrt(2f * h / Ballistics.Gravity) : 0f;
        var v = u.Move[i].Velocity;
        var impact = pos + v * tFall;
        var predicted = Ballistics.PredictPosition(tpos, tv, tom, tFall);
        // Release when the predicted impact lands within the blast (capped by Range) of the predicted target
        // position, at the best moment: while the target is still ahead by more than one tick of flight, wait.
        float tol = MathF.Min(wbp.Range, MathF.Max(wbp.DamageRadius, 0.5f)) + tr;
        var miss = predicted - impact;
        if (miss.LengthSquared() > tol * tol) return engaged;
        float speed = v.Length();
        if (speed > 0.5f && Vector2.Dot(miss, v) / speed > speed * SimWorld.Dt) return engaged;
        if (ws.Cooldown > CooldownEps) return engaged;

        ws.Cooldown += winfo.Period;
        int count = Math.Max(1, wbp.ProjectilesPerShot);
        for (int p = 0; p < count; p++)
        {
            var vel = new Vector3(v.X, 0, v.Y);
            if (wbp.Spread > 0)
            {
                var rng = w.RandomFor(i, 1 + wi * 64 + p);
                float r = wbp.Spread * MathF.Sqrt(rng.NextFloat());
                float ang = rng.NextFloat() * DMath.TwoPi;
                float inv = 1f / MathF.Max(0.5f, tFall);
                vel.X += MathF.Cos(ang) * r * inv;
                vel.Z += MathF.Sin(ang) * r * inv;
            }
            shots.Add(new ShotRequest
            {
                Shooter = i,
                Weapon = wi,
                Bp = u.Bp[i],
                Army = u.Army[i],
                Source = u.HandleOf(i),
                Target = tj >= 0 ? ws.Target : UnitHandle.None,
                Pos = release,
                Vel = vel,
                TargetPos = new Vector3(predicted.X, groundY, predicted.Y),
                Gravity = Ballistics.Gravity,
                Life = tFall * 2f + 2f,
                Damage = wbp.Damage,
                DamageRadius = wbp.DamageRadius,
                Flags = ProjectileFlags.Ballistic | ProjectileFlags.Bomb,
                Flash = false,
            });
        }
        return true;
    }

    private static Vector3 SafeDir(Vector3 v, float fallbackYaw)
    {
        float l = v.Length();
        return l > 1e-4f ? v / l : new Vector3(MathF.Cos(fallbackYaw), 0, MathF.Sin(fallbackYaw));
    }

    /// <summary>Sequential: turns a shot request into a projectile (or instant beam damage) plus effects.</summary>
    private static void Emit(SimWorld w, in ShotRequest shot)
    {
        if (shot.Flash)
            CombatFx.Add(w, EffectKind.MuzzleFlash, shot.FlashPos, 0.4f + MathF.Min(shot.Damage, 200f) / 250f, shot.Army, shot.Bp);

        if (shot.Instant)
        {
            var u = w.Units;
            var mask = w.Blueprints[shot.Bp].Weapons[shot.Weapon].TargetMask;
            var hitPos = u.IsActive(shot.Target) ? CombatState.Of(w).Center(u, shot.Target.Index) : shot.TargetPos;
            if (shot.DamageRadius > 0)
            {
                w.Events.AreaDamage.Add(new AreaDamageEvent
                {
                    Center = new Vector2(hitPos.X, hitPos.Z), Radius = shot.DamageRadius, Amount = shot.Damage,
                    Source = shot.Source, SourceArmy = shot.Army, Layers = mask,
                });
                CombatFx.Add(w, EffectKind.Explosion, hitPos, shot.DamageRadius, shot.Army, shot.Bp);
            }
            else
            {
                if (u.IsActive(shot.Target))
                    w.Events.Damage.Add(new DamageEvent { Target = shot.Target, Source = shot.Source, SourceArmy = shot.Army, Amount = shot.Damage });
                CombatFx.Add(w, EffectKind.Impact, hitPos, 0.5f, shot.Army, shot.Bp);
            }
            return;
        }

        var P = w.Projectiles;
        int idx = P.Spawn(shot.Pos, shot.Vel, shot.Army, shot.Source, shot.Bp, shot.Weapon, shot.Target, shot.TargetPos,
            shot.Damage, shot.DamageRadius, shot.Life, shot.Gravity);
        if (idx >= 0) P.Flags[idx] = shot.Flags;
    }
}
