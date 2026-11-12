using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Strategic;

/// <summary>
/// Strategic missiles and missile defense. Missiles are ordinary projectiles (ProjectileStore) registered in
/// <see cref="StrategicState"/>; ProjectileSystem hands their per-tick step to <see cref="Step"/> (parallel, each
/// projectile writes only its own slot) and their impacts to <see cref="ApplyIntercept"/> / <see cref="Detonate"/>.
/// <list type="bullet">
/// <item>Tactical missile: scripted cruise path (vertical boost, cruise at CruiseAltitude above the highest terrain on
///   the way, terminal dive), stopped by enemy shields like any projectile, area damage at the target.</item>
/// <item>Strategic missile (nuke): high arc to any point; ignores shields; on impact a two-ring blast
///   (<see cref="ApplyDetonations"/>, in DamageSystem) that also knocks down every shield it reaches.</item>
/// <item>Interceptor (TMD shot / anti-nuke missile): homes on its target missile using the target's start-of-tick
///   position/velocity (copied sequentially by <see cref="PrepareInterceptors"/>), destroys it on contact.</item>
/// <item>Missile defense (sequential): every RateOfFire period, fires at the nearest enemy missile of its kind within
///   Radius that no interceptor is chasing yet (anti-nuke: consumes one missile of its stockpile per shot).</item>
/// </list>
/// </summary>
public static class MissileSystem
{
    /// <summary>Interceptor contact distance.</summary>
    public const float InterceptRadius = 2.5f;
    /// <summary>Interceptor turn rate (rad/s).</summary>
    public const float InterceptorTurnRate = 6f;
    /// <summary>Default cruise height of tactical missiles above the highest terrain on their way.</summary>
    public const float DefaultCruise = 16f;
    private static readonly float TurnCos = DMath.Cos(InterceptorTurnRate * SimWorld.Dt);
    private static readonly float TurnSin = DMath.Sin(InterceptorTurnRate * SimWorld.Dt);
    // Scratch, cleared before use. Per thread: several SimWorlds may step at once in one process (multiplayer loopback tests).
    [ThreadStatic, Persistence.NoSave] private static List<int>? t_tmp;
    private static List<int> _tmp => t_tmp ??= new();

    internal static void InitUnit(StrategicState s, SimWorld w, int i, UnitBlueprint bp) =>
        s.Defense[i] = new DefenseState { Cooldown = 0 };

    // ------------------------------------------------------------------ launch

    /// <summary>Launches the silo's missile at a ground point. Sequential. Returns the projectile slot or -1.</summary>
    public static int Launch(StrategicState s, SimWorld w, int unit, Vector2 target)
    {
        var u = w.Units;
        var bp = w.BpOf(unit);
        var sb = bp.Silo!;
        var wb = bp.Weapons[sb.WeaponIndex];
        var from = u.Pos[unit];
        if (wb.Spread > 0)
        {
            var rng = w.RandomFor(unit, 0x5170);
            float r = wb.Spread * MathF.Sqrt(rng.NextFloat());
            float a = rng.NextFloat() * DMath.TwoPi;
            target = w.Terrain.Clamp(target + new Vector2(DMath.Cos(a), DMath.Sin(a)) * r);
        }
        return Spawn(s, w, u.Army[unit], u.HandleOf(unit), bp, from, u.Elevation[unit] + bp.Visual.Size.Y * 0.9f + 0.5f, target);
    }

    /// <summary>
    /// Spawns the silo blueprint's missile for <paramref name="army"/> from a ground point (no launcher unit needed:
    /// <paramref name="source"/> may be <see cref="UnitHandle.None"/>). Sequential. Returns the projectile slot or -1.
    /// </summary>
    internal static int Spawn(StrategicState s, SimWorld w, int army, UnitHandle source, UnitBlueprint bp, Vector2 from, float startY, Vector2 target)
    {
        var sb = bp.Silo!;
        var wb = bp.Weapons[sb.WeaponIndex];
        var terrain = w.Terrain;
        var start = new Vector3(from.X, startY, from.Y);
        var end = new Vector3(target.X, terrain.GetSurfaceHeight(target), target.Y);
        float dist = Vector2.Distance(from, target);

        // Highest terrain along the way, so cruise missiles clear hills.
        float top = MathF.Max(start.Y, end.Y);
        for (int k = 1; k < 24; k++)
            top = MathF.Max(top, terrain.GetSurfaceHeight(DMath.Lerp(from, target, k / 24f)));
        bool strategic = sb.Kind == SiloKind.Strategic;
        float cruise = sb.CruiseAltitude > 0 ? sb.CruiseAltitude : strategic ? Math.Clamp(dist * 0.3f, 60f, 220f) : DefaultCruise;
        float apex = top + cruise;
        float speed = MathF.Max(5f, wb.MuzzleVelocity);
        float duration = (dist + 2f * (apex - MathF.Min(start.Y, end.Y))) / speed;

        var P = w.Projectiles;
        int p = P.Spawn(start, new Vector3(0, speed, 0), army, source, bp.Index, sb.WeaponIndex,
            UnitHandle.None, end, wb.Damage, wb.DamageRadius, duration + 5f, 0f);
        if (p < 0) return -1;
        Register(s, P, p, strategic ? MissileRole.Strategic : MissileRole.Tactical);
        s.Start[p] = start;
        s.Duration[p] = MathF.Max(1f, duration);
        s.Elapsed[p] = 0;
        s.Apex[p] = apex;
        CombatFx.Add(w, EffectKind.MissileLaunch, start, strategic ? 3f : 1.2f, army, bp.Index, sb.WeaponIndex);
        if (strategic)
        {
            s.LaunchSerial++;
            s.Launches.Add(new LaunchRecord { Serial = s.LaunchSerial, Tick = w.Tick, Army = army, Kind = sb.Kind, From = from, Target = target });
            if (s.Launches.Count > StrategicState.MaxLaunchRecords) s.Launches.RemoveAt(0);
        }
        return p;
    }

    private static void Register(StrategicState s, ProjectileStore P, int p, MissileRole role)
    {
        s.MissileId[p] = P.Id[p];
        s.Role[p] = role;
        s.Engaged[p] = 0;
        s.TargetSlot[p] = -1;
        s.Missiles.Add(p);
    }

    /// <summary>Position along a missile's scripted path at normalized time u (0 = launch, 1 = impact).</summary>
    public static Vector3 PathPoint(MissileRole role, Vector3 start, Vector3 end, float apex, float u)
    {
        u = DMath.Clamp01(u);
        float h, bump;
        if (role == MissileRole.Strategic)
        {
            h = Smooth(Smooth(u));
            bump = DMath.Pow(DMath.Sin(MathF.PI * u), 0.6f);
        }
        else
        {
            h = 0.35f * u + 0.65f * Smooth(u);
            bump = Smooth(u / 0.2f) * (1f - Smooth((u - 0.72f) / 0.28f));
        }
        var flat = DMath.Lerp(start, end, h);
        flat.Y += (apex - flat.Y) * bump;
        return flat;
    }

    private static float Smooth(float x)
    {
        x = DMath.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    // ------------------------------------------------------------------ per tick (sequential, before combat)

    /// <summary>Prunes stale registry entries, copies interceptor aims and counts engaged missiles.</summary>
    internal static void PrepareInterceptors(StrategicState s, SimWorld w)
    {
        var P = w.Projectiles;
        int n = 0;
        for (int k = 0; k < s.Missiles.Count; k++)
        {
            int p = s.Missiles[k];
            if (!s.IsMissile(P, p)) { s.MissileId[p] = 0; continue; }
            s.Missiles[n++] = p;
            s.Engaged[p] = 0;
        }
        s.Missiles.RemoveRange(n, s.Missiles.Count - n);
        foreach (int p in s.Missiles)
        {
            if (s.Role[p] != MissileRole.Interceptor) continue;
            int t = s.TargetSlot[p];
            if (t >= 0 && P.Alive[t] && P.Id[t] == s.TargetId[p])
            {
                s.AimPos[p] = P.Pos[t];
                s.AimVel[p] = P.Vel[t];
                s.Engaged[t]++;
            }
            else s.TargetSlot[p] = -1;
        }
    }

    /// <summary>Missile defenses fire interceptors. Sequential (spawns projectiles).</summary>
    internal static void UpdateDefenses(StrategicState s, SimWorld w)
    {
        var u = w.Units;
        var P = w.Projectiles;
        const float dt = SimWorld.Dt;
        foreach (var h in s.Units)
        {
            if (!u.IsActive(h)) continue;
            int i = h.Index;
            var bp = w.BpOf(i);
            var md = bp.MissileDefense;
            if (md == null) continue;
            ref var d = ref s.Defense[i];
            d.Cooldown -= dt;
            if (d.Cooldown < -dt) d.Cooldown = -dt;
            if (d.Cooldown > 1e-4f || !u.IsComplete(i) || (u.Flags[i] & (UnitFlags.Paused | UnitFlags.Stunned)) != 0) continue;
            if (md.UsesStockpile && s.Silo[i].Stock <= 0) continue;
            var want = md.Intercepts == InterceptKind.Strategic ? MissileRole.Strategic : MissileRole.Tactical;
            var pos = u.Pos[i];
            int army = u.Army[i];
            int best = -1;
            float bestD2 = md.Radius * md.Radius;
            foreach (int p in s.Missiles)
            {
                if (s.Role[p] != want || s.Engaged[p] > 0 || !P.Alive[p] || !s.IsEnemy(army, P.Army[p])) continue;
                float d2 = Vector2.DistanceSquared(pos, new Vector2(P.Pos[p].X, P.Pos[p].Z));
                if (d2 > bestD2 || (d2 == bestD2 && best >= 0 && P.Id[p] > P.Id[best])) continue;
                best = p;
                bestD2 = d2;
            }
            if (best < 0) continue;
            if (FireInterceptor(s, w, i, bp, md, best) < 0) continue;
            d.Cooldown += 1f / MathF.Max(0.01f, md.RateOfFire);
            if (md.UsesStockpile) s.Silo[i].Stock--;
            s.Engaged[best]++;
        }
    }

    private static int FireInterceptor(StrategicState s, SimWorld w, int unit, UnitBlueprint bp, MissileDefenseBlueprint md, int target)
    {
        var u = w.Units;
        var P = w.Projectiles;
        var wb = (uint)md.WeaponIndex < (uint)bp.Weapons.Length ? bp.Weapons[md.WeaponIndex] : null;
        float speed = MathF.Max(10f, wb?.MuzzleVelocity ?? 60f);
        var muzzle = new Vector3(u.Pos[unit].X, u.Elevation[unit] + bp.Visual.Size.Y * 0.9f + 0.3f, u.Pos[unit].Y);
        var tp = P.Pos[target];
        var tv = P.Vel[target];
        float tLead = Vector3.Distance(tp, muzzle) / speed;
        var aim = tp + tv * tLead;
        var dir = aim - muzzle;
        dir = dir.LengthSquared() > 1e-6f ? Vector3.Normalize(dir) : Vector3.UnitY;
        // Launched upward first (silo / vertical launcher), then turns toward the target.
        var vel = Vector3.Normalize(dir + new Vector3(0, 0.6f, 0)) * speed;
        float life = md.Radius * 2.5f / speed + 3f;
        int p = P.Spawn(muzzle, vel, u.Army[unit], u.HandleOf(unit), bp.Index, Math.Max(0, md.WeaponIndex),
            UnitHandle.None, tp, 0f, 0f, life, 0f);
        if (p < 0) return -1;
        Register(s, P, p, MissileRole.Interceptor);
        s.TargetSlot[p] = target;
        s.TargetId[p] = P.Id[target];
        s.AimPos[p] = tp;
        s.AimVel[p] = tv;
        // Turn the launcher toward the target (cosmetic turret yaw).
        if (bp.Weapons.Length > 0)
            u.Weapons[unit * UnitStore.MaxWeapons].TurretHeading = DMath.HeadingOf(new Vector2(tp.X - muzzle.X, tp.Z - muzzle.Z));
        CombatFx.Add(w, EffectKind.MuzzleFlash, muzzle, 0.8f, u.Army[unit], bp.Index, Math.Max(0, md.WeaponIndex));
        return p;
    }

    // ------------------------------------------------------------------ flight (parallel, called by ProjectileSystem)

    internal static void Step(SimWorld w, StrategicState s, int p, List<ProjectileImpact> impacts)
    {
        var P = w.Projectiles;
        const float dt = SimWorld.Dt;
        var pos0 = P.Pos[p];
        float life = P.TimeLeft[p] - dt;
        P.TimeLeft[p] = life;

        if (s.Role[p] == MissileRole.Interceptor)
        {
            StepInterceptor(w, s, p, pos0, life, impacts);
            return;
        }

        float t1 = s.Elapsed[p] + dt;
        s.Elapsed[p] = t1;
        float u1 = t1 / s.Duration[p];
        var end = P.TargetPos[p];
        var pos1 = u1 >= 1f ? end : PathPoint(s.Role[p], s.Start[p], end, s.Apex[p], u1);
        P.Pos[p] = pos1;
        P.Vel[p] = (pos1 - pos0) / dt;
        bool nuke = s.Role[p] == MissileRole.Strategic;

        // Terrain on the way (hills): explode where the path dips under the surface (not right after launch).
        float groundT = 2f;
        if (u1 >= 1f) groundT = 1f;
        else if (t1 > 1f)
        {
            float h1 = pos1.Y - w.Terrain.GetSurfaceHeight(new Vector2(pos1.X, pos1.Z));
            if (h1 < 0)
            {
                float h0 = pos0.Y - w.Terrain.GetSurfaceHeight(new Vector2(pos0.X, pos0.Z));
                groundT = h0 <= 0 ? 0 : h0 / (h0 - h1);
            }
        }

        if (!nuke)
        {
            int e = ShieldSystem.FirstEntry(s, P.Army[p], pos0, pos1, out float st);
            if (e >= 0 && st <= groundT)
            {
                impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Shield, Unit = s.Index.Entries[e].Unit, Pos = DMath.Lerp(pos0, pos1, st) });
                return;
            }
        }
        if (groundT <= 1f)
        {
            var gp = DMath.Lerp(pos0, pos1, groundT);
            impacts.Add(new ProjectileImpact { Projectile = p, Kind = nuke ? ImpactKind.Detonation : ImpactKind.Ground, Unit = -1, Pos = gp });
            return;
        }
        if (life <= 0) impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Expire, Unit = -1, Pos = pos1 });
    }

    private static void StepInterceptor(SimWorld w, StrategicState s, int p, Vector3 pos0, float life, List<ProjectileImpact> impacts)
    {
        var P = w.Projectiles;
        const float dt = SimWorld.Dt;
        var vel = P.Vel[p];
        if (s.TargetSlot[p] < 0)
        {
            // Target gone: fly on and fizzle.
            P.Pos[p] = pos0 + vel * dt;
            if (life <= 0) impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Expire, Unit = -1, Pos = P.Pos[p] });
            return;
        }
        var tp0 = s.AimPos[p];
        var tv = s.AimVel[p];
        var tp1 = tp0 + tv * dt;
        float speed = MathF.Max(1f, vel.Length());
        float tLead = MathF.Min(3f, Vector3.Distance(tp1, pos0) / speed);
        vel = Ballistics.Steer(vel, tp1 + tv * tLead - pos0, TurnCos, TurnSin);
        var pos1 = pos0 + vel * dt;
        P.Pos[p] = pos1;
        P.Vel[p] = vel;
        // Closest approach in the target's frame (both move during the tick).
        var r0 = pos0 - tp0;
        var r1 = pos1 - tp1;
        float t = Ballistics.ClosestParam(r0, r1, Vector3.Zero);
        if (DMath.Lerp(r0, r1, t).LengthSquared() <= InterceptRadius * InterceptRadius)
        {
            impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Intercept, Unit = -1, Pos = DMath.Lerp(pos0, pos1, t) });
            return;
        }
        if (life <= 0) impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Expire, Unit = -1, Pos = pos1 });
    }

    // ------------------------------------------------------------------ impacts (sequential, ProjectileSystem.Apply)

    /// <summary>An interceptor reached its missile: both are destroyed.</summary>
    internal static void ApplyIntercept(SimWorld w, int p, Vector3 pos)
    {
        var s = StrategicState.Of(w);
        var P = w.Projectiles;
        int t = s.TargetSlot[p];
        if (t < 0 || !P.Alive[t] || P.Id[t] != s.TargetId[p]) return;
        CombatFx.Add(w, EffectKind.MissileIntercepted, pos, s.Role[t] == MissileRole.Strategic ? 3f : 1.2f, P.Army[t], P.SourceBp[t], P.WeaponIndex[t]);
        P.Free(t);
        s.MissileId[t] = 0;
    }

    /// <summary>A strategic missile reached the ground: queue the blast for DamageSystem.</summary>
    internal static void Detonate(SimWorld w, int p, Vector3 pos)
    {
        var P = w.Projectiles;
        var bp = w.Blueprints[P.SourceBp[p]];
        var sb = bp.Silo;
        QueueDetonation(w, P.Army[p], P.Source[p], new Vector2(pos.X, pos.Z),
            P.DamageRadius[p], P.Damage[p], MathF.Max(P.DamageRadius[p], sb?.OuterRadius ?? 0), sb?.OuterDamage ?? 0,
            bp.Index, P.WeaponIndex[p]);
    }

    /// <summary>
    /// Queues a strategic blast at <paramref name="center"/> (applied by <see cref="ApplyDetonations"/> at the start of
    /// this tick's DamageSystem) and raises the <see cref="EffectKind.Nuke"/> effect (Size = outer radius). Used by
    /// missile impacts and by <see cref="StrategicApi.Detonate"/> (sandbox instant nuke). Sequential.
    /// </summary>
    internal static void QueueDetonation(SimWorld w, int army, UnitHandle source, Vector2 center,
        float innerRadius, float innerDamage, float outerRadius, float outerDamage, int blueprint = -1, int weapon = 0)
    {
        var s = StrategicState.Of(w);
        outerRadius = MathF.Max(innerRadius, outerRadius);
        s.Detonations.Add(new PendingDetonation
        {
            Center = center,
            InnerRadius = innerRadius, InnerDamage = innerDamage,
            OuterRadius = outerRadius, OuterDamage = outerDamage,
            Source = source, Army = army,
        });
        var ground = new Vector3(center.X, w.Terrain.GetSurfaceHeight(center), center.Y);
        CombatFx.Add(w, EffectKind.Nuke, ground, outerRadius, army, blueprint, weapon);
    }

    /// <summary>
    /// Removes every prop (wrecks included) inside the inner ring of the blasts applied last tick: like SupCom, a nuke
    /// leaves a clean crater. Runs one tick later (StrategicSystem.Run) so the wrecks of the units the blast killed are
    /// gone too. Sequential.
    /// </summary>
    internal static void VaporizeCraters(StrategicState s, SimWorld w)
    {
        var props = w.Props;
        for (int k = s.Craters.Count - 1; k >= 0; k--)
        {
            var (tick, center, radius) = s.Craters[k];
            if (tick >= w.Tick) continue;
            s.Craters.RemoveAt(k);
            _tmp.Clear();
            props.EnsureGrid();
            props.QueryRadius(center, radius + props.MaxRadius, _tmp); // ascending, like the old AliveList scan
            foreach (int i in _tmp)
                if (Vector2.Distance(props.Pos[i], center) <= radius + props.Radius[i]) props.Free(i);
        }
    }

    /// <summary>
    /// Applies queued strategic blasts (start of DamageSystem). Inner ring: InnerDamage + OuterDamage, outer ring:
    /// OuterDamage, to land/naval/air units (subs are spared). Ignores shields and knocks down every shield whose dome
    /// reaches the outer ring.
    /// </summary>
    public static void ApplyDetonations(SimWorld w)
    {
        var s = StrategicState.Of(w);
        if (s.Detonations.Count == 0) return;
        var u = w.Units;
        foreach (var d in s.Detonations)
        {
            foreach (var h in s.Units)
            {
                if (!u.IsActive(h)) continue;
                var sb = w.BpOf(h.Index).Shield;
                if (sb != null && Vector2.Distance(u.Pos[h.Index], d.Center) <= d.OuterRadius + sb.Radius) ShieldSystem.KnockDown(w, h.Index);
            }
            _tmp.Clear();
            w.Grid.QueryRadius(u, d.Center, d.OuterRadius + 10f, _tmp);
            foreach (int j in _tmp)
            {
                if (!u.IsActive(j)) continue;
                if (OrderSystem.LayerOf(w, j) == LayerMask.Sub) continue;
                float r = w.BpOf(j).Radius;
                float dist = Vector2.Distance(u.Pos[j], d.Center);
                float dmg = 0;
                if (dist <= d.InnerRadius + r) dmg += d.InnerDamage;
                if (dist <= d.OuterRadius + r) dmg += d.OuterDamage;
                if (dmg > 0) DamageSystem.Apply(w, j, dmg, d.Source);
            }
            s.Craters.Add((w.Tick, d.Center, d.InnerRadius));
        }
        s.Detonations.Clear();
    }
}
