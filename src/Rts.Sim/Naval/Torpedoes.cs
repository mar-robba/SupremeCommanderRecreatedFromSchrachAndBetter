using System.Numerics;
using System.Runtime.CompilerServices;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Naval;

/// <summary>
/// Torpedoes and torpedo defense (SupCom-like naval combat).
/// <list type="bullet">
/// <item><b>Weapons</b>: <c>"ProjectileVisual": "torpedo"</c> (MuzzleVelocity &gt; 0) makes a
///   <see cref="WeaponKind.Torpedo"/>; give it <c>"TargetLayers": ["Water", "Sub"]</c>. Subs, ships, torpedo
///   launchers and torpedo bombers use it. <c>"ProjectileVisual": "antitorpedo"</c> (TargetLayers = the torpedo layers
///   it stops; it never targets units) is a
///   <see cref="WeaponKind.AntiTorpedo"/> (destroyers): Range + RateOfFire = interceptions per second.</item>
/// <item><b>Motion</b> (<see cref="Step"/>, called from the parallel ProjectileSystem step for projectiles with
///   <see cref="ProjectileFlags.Torpedo"/>): above the water (launched from a deck, dropped by a plane) the torpedo
///   falls with gravity keeping its horizontal speed; a splash-down on land or in shallows kills it. In the water it
///   dives to <see cref="RunDepth"/>, runs horizontally at its launch speed and steers (limited turn rate) toward the
///   predicted position of its target. It dies as soon as the sea floor comes within <see cref="MinClearance"/>
///   (it can't cross land or shallow water: "dies on shore").</item>
/// <item><b>Hits</b>: 2D swept circle test (the hit is decided on the ground plane: surface ships and subs at depth
///   are both reachable) against enemies on the weapon's layers (Water/Sub) → single-target damage.</item>
/// <item><b>Defense</b> (<see cref="Intercept"/>, sequential, before projectiles move): every torpedo in the water
///   within Range of an enemy unit whose anti-torpedo weapon is off cooldown is destroyed; that weapon's cooldown
///   restarts (limited interception rate). Projectile order then grid order: deterministic.</item>
/// </list>
/// </summary>
public static class Torpedoes
{
    /// <summary>Running depth below the water surface.</summary>
    public const float RunDepth = 0.7f;
    /// <summary>The sea floor must stay at least this far below the torpedo, else it hits the shore.</summary>
    public const float MinClearance = 0.25f;
    /// <summary>Homing turn rate (radians per second).</summary>
    public const float TurnRate = 2.4f;
    /// <summary>Dive speed from the surface to the running depth (units per second).</summary>
    public const float DiveSpeed = 3f;

    /// <summary>Torpedo defense (TargetLayers = the layers of the torpedoes it stops): never targets units.</summary>
    public static bool IsInterceptor(WeaponBlueprint wb) =>
        string.Equals(wb.ProjectileVisual, "antitorpedo", StringComparison.OrdinalIgnoreCase);

    /// <summary>Torpedo / anti-torpedo weapon kind from the blueprint (null = some other kind).</summary>
    public static WeaponKind? KindOf(WeaponBlueprint wb)
    {
        if (string.Equals(wb.ProjectileVisual, "antitorpedo", StringComparison.OrdinalIgnoreCase)) return WeaponKind.AntiTorpedo;
        if (string.Equals(wb.ProjectileVisual, "torpedo", StringComparison.OrdinalIgnoreCase) && wb.MuzzleVelocity > 0) return WeaponKind.Torpedo;
        return null;
    }

    /// <summary>Horizontal launch velocity toward the aim point (torpedoes swim at their muzzle speed).</summary>
    public static Vector3 LaunchVelocity(Vector3 muzzle, Vector3 aim, float speed, float fallbackYaw)
    {
        var d = new Vector2(aim.X - muzzle.X, aim.Z - muzzle.Z);
        float l = d.Length();
        var dir = l > 1e-4f ? d / l : DMath.Dir(fallbackYaw);
        return new Vector3(dir.X * speed, 0, dir.Y * speed);
    }

    /// <summary>Lifetime: the run over 1.6 × Range plus the fall from the launch height.</summary>
    public static float Lifetime(WeaponBlueprint wb, float heightAboveWater)
    {
        float fall = heightAboveWater > 0 ? MathF.Sqrt(2f * heightAboveWater / Ballistics.Gravity) : 0f;
        return wb.Range * 1.6f / MathF.Max(1f, wb.MuzzleVelocity) + fall + 1f;
    }

    /// <summary>True if a torpedo at height y over (x, z) has run aground (sea floor too close or dry land).</summary>
    public static bool Aground(Map.Terrain t, float x, float z, float y) => t.GetHeight(x, z) > y - MinClearance;

    // ------------------------------------------------------------------ motion (parallel, per projectile)

    internal static void Step(SimWorld w, CombatState s, int p, List<ProjectileImpact> impacts)
    {
        var P = w.Projectiles;
        var u = w.Units;
        var terrain = w.Terrain;
        const float dt = SimWorld.Dt;
        var pos0 = P.Pos[p];
        var vel = P.Vel[p];
        float water = terrain.WaterLevel;
        float runY = water - RunDepth;
        float life = P.TimeLeft[p] - dt;
        Vector3 pos1;
        bool inWater = pos0.Y <= water;

        if (!inWater)
        {
            // Falling (deck launch / air drop): gravity, horizontal speed kept.
            vel.Y -= Ballistics.Gravity * dt;
            pos1 = pos0 + vel * dt;
            if (pos1.Y <= water)
            {
                float t = (pos0.Y - water) / MathF.Max(1e-4f, pos0.Y - pos1.Y);
                var at = Vector3.Lerp(pos0, pos1, t);
                at.Y = water;
                if (Aground(terrain, at.X, at.Z, water))
                {
                    impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Ground, Unit = -1, Pos = new Vector3(at.X, terrain.GetSurfaceHeight(new Vector2(at.X, at.Z)), at.Z) });
                    return;
                }
                pos1.Y = water;
                vel.Y = 0;
                inWater = true;
            }
            else if (pos1.Y <= terrain.GetHeight(pos1.X, pos1.Z))
            {
                impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Ground, Unit = -1, Pos = pos1 });
                return;
            }
        }
        else
        {
            var flat = new Vector2(vel.X, vel.Z);
            float speed = flat.Length();
            var here = new Vector2(pos0.X, pos0.Z);
            var dir = speed > 1e-4f ? flat / speed : Vector2.UnitX;
            var th = P.Target[p];
            if (u.IsActive(th) && (s.Layer[th.Index] & (LayerMask.Water | LayerMask.Sub)) != 0)
            {
                // Lead pursuit on the ground plane.
                float tGo = Vector2.Distance(here, u.Pos[th.Index]) / MathF.Max(1f, speed);
                var to = s.Predict(u, th.Index, tGo) - here;
                if (to.LengthSquared() > 1e-6f)
                    dir = DMath.Dir(DMath.RotateTowards(DMath.HeadingOf(dir), DMath.HeadingOf(to), TurnRate * dt));
            }
            float y = pos0.Y + Math.Clamp(runY - pos0.Y, -DiveSpeed * dt, DiveSpeed * dt);
            pos1 = new Vector3(pos0.X + dir.X * speed * dt, y, pos0.Z + dir.Y * speed * dt);
            vel = new Vector3(dir.X * speed, (y - pos0.Y) / dt, dir.Y * speed);
        }
        P.Pos[p] = pos1;
        P.Vel[p] = vel;
        P.TimeLeft[p] = life;

        if (inWater)
        {
            var v = new HitVisitor
            {
                W = w, S = s, Army = P.Army[p], Mask = WeaponMask(w, p),
                A = new Vector2(pos0.X, pos0.Z), B = new Vector2(pos1.X, pos1.Z), BestT = 2f, Best = -1,
            };
            var mid = (v.A + v.B) * 0.5f;
            s.ForEachInRadius(w, mid, Vector2.Distance(v.A, v.B) * 0.5f + s.Catalog.MaxRadius + CombatState.GridSlack, s.EnemyArmies[P.Army[p]], ref v);
            if (v.Best >= 0)
            {
                impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Unit, Unit = v.Best, Pos = Vector3.Lerp(pos0, pos1, v.BestT) });
                return;
            }
            if (Aground(terrain, pos1.X, pos1.Z, pos1.Y))
            {
                // Ran into the shore / shallows: harmless splash.
                impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Ground, Unit = -1, Pos = new Vector3(pos1.X, water, pos1.Z) });
                return;
            }
        }

        if (life <= 0 || pos1.X < -8 || pos1.Z < -8 || pos1.X > terrain.Width + 8 || pos1.Z > terrain.Height + 8)
            impacts.Add(new ProjectileImpact { Projectile = p, Kind = ImpactKind.Expire, Unit = -1, Pos = pos1 });
    }

    private static LayerMask WeaponMask(SimWorld w, int p)
    {
        var P = w.Projectiles;
        var weapons = w.Blueprints[P.SourceBp[p]].Weapons;
        int wi = P.WeaponIndex[p];
        return (uint)wi < (uint)weapons.Length ? weapons[wi].TargetMask : LayerMask.Water | LayerMask.Sub;
    }

    /// <summary>First parameter t in [0, 1] where segment a→b enters the circle (c, r); 0 if it starts inside; -1 if none.</summary>
    public static float SegmentCircle(Vector2 a, Vector2 b, Vector2 c, float r)
    {
        var d = b - a;
        var f = a - c;
        float cc = Vector2.Dot(f, f) - r * r;
        if (cc <= 0) return 0f;
        float aa = Vector2.Dot(d, d);
        if (aa < 1e-10f) return -1f;
        float bb = 2f * Vector2.Dot(f, d);
        float disc = bb * bb - 4f * aa * cc;
        if (disc < 0) return -1f;
        float t = (-bb - MathF.Sqrt(disc)) / (2f * aa);
        return t >= 0f && t <= 1f ? t : -1f;
    }

    /// <summary>Earliest enemy (on the weapon's layers) whose collision circle the swim segment crosses; ties: lowest index.</summary>
    private struct HitVisitor : IUnitVisitor
    {
        public SimWorld W;
        public CombatState S;
        public int Army;
        public LayerMask Mask;
        public Vector2 A, B;
        public float BestT;
        public int Best;

        public bool Visit(int j)
        {
            var u = W.Units;
            if (!S.IsEnemyArmy(Army, u.Army[j])) return true;
            if ((S.Layer[j] & Mask) == 0) return true;
            if (!u.IsActive(j)) return true;
            float t = SegmentCircle(A, B, u.Pos[j], S.Catalog.Units[u.Bp[j]].HitRadius);
            if (t < 0 || t > BestT || (t == BestT && j > Best)) return true;
            BestT = t;
            Best = j;
            return true;
        }
    }

    // ------------------------------------------------------------------ torpedo defense (sequential)

    /// <summary>Per-blueprint torpedo-defense weapons (derived once per world from the CombatCatalog).</summary>
    private sealed class DefenseTable
    {
        private static readonly ConditionalWeakTable<SimWorld, DefenseTable> Table = new();
        public static DefenseTable Of(SimWorld w, CombatState s) => Table.GetValue(w, x => new DefenseTable(x, s.Catalog));

        /// <summary>Per blueprint: indices of its AntiTorpedo weapons (empty = none).</summary>
        public readonly int[][] Weapons;
        public readonly float MaxRange;
        public readonly bool Any;

        private DefenseTable(SimWorld w, CombatCatalog cat)
        {
            Weapons = new int[w.Blueprints.Count][];
            for (int b = 0; b < Weapons.Length; b++)
            {
                var list = new List<int>();
                var ws = cat.Units[b].Weapons;
                for (int k = 0; k < ws.Length; k++)
                {
                    if (ws[k].Kind != WeaponKind.AntiTorpedo) continue;
                    list.Add(k);
                    MaxRange = MathF.Max(MaxRange, w.Blueprints[b].Weapons[k].Range);
                }
                Weapons[b] = list.ToArray();
                Any |= list.Count > 0;
            }
        }
    }

    /// <summary>
    /// Torpedo defense: each torpedo in the water within Range of an enemy unit with a ready anti-torpedo weapon is
    /// destroyed and that weapon's cooldown restarts. SEQUENTIAL (writes other units' weapon cooldowns), called by
    /// ProjectileSystem before the parallel step. Cost: one flag test per projectile when no torpedo is in flight.
    /// </summary>
    internal static void Intercept(SimWorld w, CombatState s)
    {
        var table = DefenseTable.Of(w, s);
        if (!table.Any) return;
        var P = w.Projectiles;
        float water = w.Terrain.WaterLevel;
        for (int k = 0; k < P.AliveCount; k++)
        {
            int p = P.AliveList[k];
            if (!P.Alive[p] || (P.Flags[p] & ProjectileFlags.Torpedo) == 0 || P.Pos[p].Y > water) continue;
            var pos = new Vector2(P.Pos[p].X, P.Pos[p].Z);
            var v = new DefenderVisitor { W = w, S = s, Table = table, Army = P.Army[p], Pos = pos, Unit = -1, Weapon = -1 };
            s.ForEachInRadius(w, pos, table.MaxRange + CombatState.GridSlack, s.EnemyArmies[P.Army[p]], ref v);
            if (v.Unit < 0) continue;
            var u = w.Units;
            ref var ws = ref u.Weapons[v.Unit * UnitStore.MaxWeapons + v.Weapon];
            ws.Cooldown += s.Catalog.Units[u.Bp[v.Unit]].Weapons[v.Weapon].Period;
            CombatFx.Add(w, EffectKind.MuzzleFlash, s.Center(u, v.Unit), 0.5f, u.Army[v.Unit], u.Bp[v.Unit], v.Weapon);
            CombatFx.Add(w, EffectKind.Impact, P.Pos[p], 0.8f, u.Army[v.Unit], u.Bp[v.Unit], v.Weapon);
            P.Free(p);
        }
    }

    /// <summary>First (grid order) enemy unit with a ready anti-torpedo weapon in range of the torpedo.</summary>
    private struct DefenderVisitor : IUnitVisitor
    {
        public SimWorld W;
        public CombatState S;
        public DefenseTable Table;
        public int Army;
        public Vector2 Pos;
        public int Unit, Weapon;

        public bool Visit(int j)
        {
            var u = W.Units;
            var list = Table.Weapons[u.Bp[j]];
            if (list.Length == 0 || !S.IsEnemyArmy(Army, u.Army[j]) || !u.IsActive(j) || !u.IsComplete(j)) return true;
            var bp = W.Blueprints[u.Bp[j]];
            float d2 = Vector2.DistanceSquared(u.Pos[j], Pos);
            foreach (int wi in list)
            {
                float r = bp.Weapons[wi].Range;
                if (d2 > r * r || u.Weapons[j * UnitStore.MaxWeapons + wi].Cooldown > 1e-4f) continue;
                Unit = j;
                Weapon = wi;
                return false; // stop at the first defender
            }
            return true;
        }
    }
}
