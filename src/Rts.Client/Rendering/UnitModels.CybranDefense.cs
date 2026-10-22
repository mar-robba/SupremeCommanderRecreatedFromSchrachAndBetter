using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;

namespace Rts.Client.Rendering;

/// <summary>
/// Cybran defensive and strategic structures (shapes "cy_pd" … "cy_artillery_structure", dispatched by
/// <c>CybranShape</c> in UnitModels.Cybran.cs): point defense, AA, the ED shield chain, missile silos, the Zapper, the
/// anti-nuke and artillery installations. Same look as UnitModels.CybranStructures.cs (chamfered dark plinths, raked
/// claw buttresses, faceted heads, red glowing lenses/seams, team bands). y = 0 is the ground; turret meshes have their
/// origin at the pivot with barrels along +Z (yaw only: artillery and AA barrels are pitched up inside the mesh).
/// Silos and the anti-nuke fire fixed weapons (TurretTurnRate 0): no turret, like the UEF ones.
/// </summary>
public static partial class UnitModels
{
    /// <summary>Faceted defense drum on a chamfered plinth with <paramref name="claws"/> raked claw buttresses, a team
    /// band and red slits. Returns the drum top (the turret pivot height).</summary>
    private static float CyDefenseBase(MeshBuilder b, float w, float h, float l, float drumH, int claws, float drumR = 0.34f)
    {
        float top = 0.08f * h;
        CyFoundation(b, w, l, top, CyHullDark, 0.3f);
        b.Cylinder(V(0, top, 0), Axis.Y, drumH, drumR * w, 0.78f * drumR * w, 6, CyHull, CyHullLight);
        b.Cylinder(V(0, top + 0.2f * drumH, 0), Axis.Y, 0.16f * drumH, 1.03f * drumR * w, 0.99f * drumR * w, 6, CyTeam);
        for (int i = 0; i < 3; i++)
            b.Box(V(0, top + 0.62f * drumH, 0.75f * drumR * w + 0.004f * w),
                  V(0.07f * w, 0.22f * drumH, 0.01f * w), CyRed, null, RotY(i * MathF.Tau / 3 + MathF.PI / 6));
        for (int i = 0; i < claws; i++)
            CyClawFin(b, i * MathF.Tau / claws + MathF.PI / claws, top, 0.62f * drumR * w, 0.47f * w, 0.9f * drumR * w,
                      top + 0.85f * drumH, 0.035f * w, CyHullDark);
        return top + drumH;
    }

    // ================================================================ point defense

    /// <summary>
    /// Cybran point defense. T1: a faceted drum with three claw buttresses; turret = a raked wedge head with one pulse
    /// laser barrel (red muzzle), a side lens and a back fin. T2 "Cerberus": a taller drum with six buttresses; turret =
    /// a broad armored head with side cheek plates and three electron bolter barrels in a triangle, a sensor spike.
    /// </summary>
    private static MeshBuilder CyPointDefense(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, int tier)
    {
        float py = CyDefenseBase(b, w, h, l, (tier >= 2 ? 0.34f : 0.3f) * h, tier >= 2 ? 6 : 3, tier >= 2 ? 0.36f : 0.33f);
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.02f * h, 0), Axis.Y, 0.05f * h, 0.24f * w, 0.22f * w, 6, CyHullDark, CyHull);
        if (tier < 2)
        {
            CyWedge(t, 0, 0.03f * h, 0.38f * w, 0.24f * h, -0.26f * l, 0.2f * l, CyHull, CyHullLight, 0.08f, 0.55f);
            t.Box(V(0.03f * w, 0.27f * h, -0.06f * l), V(0.1f * w, 0.012f * h, 0.24f * l), CyTeam);
            float by = 0.13f * h;
            t.Box(V(0, by, 0.2f * l), V(0.11f * w, 0.1f * h, 0.12f * l), CyHullDark);
            t.Cylinder(V(0, by, 0.22f * l), Axis.Z, 0.3f * l, 0.045f * w, 0.035f * w, 6, CyHullDark, CyBlack);
            t.Cylinder(V(0, by, 0.5f * l), Axis.Z, 0.03f * l, 0.05f * w, 0.05f * w, 6, CyRed, CyRed);
            t.Box(V(0.19f * w, 0.14f * h, 0.06f * l), V(0.012f * w, 0.05f * h, 0.08f * l), CyRed);
            Fin(t, -0.05f * w, 0.025f * w, 0.2f * h, -0.24f * l, -0.04f * l, -0.3f * l, 0.38f * h, CyHullDark);
        }
        else
        {
            CyWedge(t, 0, 0.03f * h, 0.46f * w, 0.26f * h, -0.28f * l, 0.2f * l, CyHull, CyHullLight, 0, 0.6f);
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            {
                // Raked cheek plate (z, y) on each side with a red slit.
                Vector2[] cheek = [V2(-0.22f * l, 0.04f * h), V2(0.16f * l, 0.04f * h), V2(0.22f * l, 0.16f * h), V2(-0.12f * l, 0.3f * h)];
                t.SideSlab(cheek, sd * 0.22f * w, sd * 0.26f * w, CyHullDark);
                t.Box(V(sd * 0.262f * w, 0.14f * h, 0.02f * l), V(0.008f * w, 0.03f * h, 0.16f * l), CyRed);
            }
            t.Box(V(0, 0.29f * h, -0.08f * l), V(0.16f * w, 0.012f * h, 0.26f * l), CyTeam);
            t.Box(V(0, 0.14f * h, 0.2f * l), V(0.26f * w, 0.18f * h, 0.1f * l), CyHullDark);
            foreach (var (bx, byy) in (ReadOnlySpan<(float, float)>)[(-0.07f, 0.1f), (0.07f, 0.1f), (0, 0.19f)])
            {
                t.Cylinder(V(bx * w, byy * h, 0.24f * l), Axis.Z, 0.32f * l, 0.04f * w, 0.032f * w, 6, CyHullDark, CyBlack);
                t.Cylinder(V(bx * w, byy * h, 0.56f * l), Axis.Z, 0.025f * l, 0.042f * w, 0.042f * w, 6, CyRed, CyRed);
            }
            t.Box(V(0, 0.14f * h, 0.26f * l), V(0.22f * w, 0.025f * h, 0.03f * l), CyTeamDim);
            Spike(t, V(-0.12f * w, 0.28f * h, -0.18f * l), V(-0.14f * w, 0.5f * h, -0.3f * l), 0.025f * w, CyHullDark);
            t.Box(V(-0.14f * w, 0.5f * h, -0.3f * l), V(0.03f * w, 0.03f * w, 0.03f * w), CyRed);
        }
        pivots.Add(V(0, py, 0));
        return t;
    }

    // ================================================================ anti-air

    /// <summary>
    /// Cybran AA. T1 electron flak: faceted drum, turret = a wedge cradle with two short barrels pitched up and a red
    /// sensor lens. T2 flak: a taller drum, turret = four barrels in two pitched pairs between ammo drums. T3 SAM: a
    /// heavy drum with six buttresses, turret = two pitched missile pods (red cell caps) around a raked radar blade.
    /// </summary>
    private static MeshBuilder CyAaTurret(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, int tier)
    {
        float py = CyDefenseBase(b, w, h, l, (0.24f + 0.04f * tier) * h, tier >= 3 ? 6 : 3, 0.32f + 0.02f * tier);
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.02f * h, 0), Axis.Y, 0.06f * h, 0.26f * w, 0.24f * w, 6, CyHullDark, CyHull);
        if (tier < 3)
        {
            CyWedge(t, 0, 0.04f * h, (0.3f + 0.04f * tier) * w, 0.2f * h, -0.22f * l, 0.14f * l, CyHull, CyHullLight, -0.06f, 0.6f);
            t.Box(V(0, 0.24f * h, -0.06f * l), V(0.08f * w, 0.012f * h, 0.2f * l), CyTeam);
            t.Box(V(0, 0.16f * h, 0.145f * l), V(0.08f * w, 0.04f * h, 0.01f * l), CyRed);
            float elev = 0.8f;
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            {
                float x = sd * (0.2f + 0.02f * tier) * w;
                if (tier >= 2) // ammo drum on each flank
                    t.Cylinder(V(x + sd * 0.02f * w, 0.12f * h, -0.16f * l), Axis.Z, 0.22f * l, 0.08f * w, 0.08f * w, 6, CyHullDark, CyTeamDim);
                var xf = PitchUp(elev) * T(x, 0.14f * h, 0.02f * l);
                t.Box(V(0, 0, 0.04f * l), V(0.1f * w, 0.1f * w, 0.14f * l), CyHull, null, xf);
                int nb = tier >= 2 ? 2 : 1;
                for (int k = 0; k < nb; k++)
                {
                    float bx = nb == 1 ? 0 : (k - 0.5f) * 0.06f * w;
                    t.Cylinder(V(bx, 0, 0.1f * l), Axis.Z, 0.26f * l, 0.03f * w, 0.025f * w, 6, CyHullDark, CyBlack, xf);
                    t.Cylinder(V(bx, 0, 0.36f * l), Axis.Z, 0.02f * l, 0.032f * w, 0.032f * w, 6, CyRed, CyRed, xf);
                }
            }
        }
        else
        {
            t.BevelBox(V(0, 0.12f * h, -0.04f * l), V(0.3f * w, 0.18f * h, 0.3f * l), 0.03f * w, CyHull, CyHullLight);
            t.Box(V(0, 0.215f * h, -0.04f * l), V(0.12f * w, 0.01f * h, 0.26f * l), CyTeam);
            // Raked radar blade (z, y) on top with a red emitter strip.
            Vector2[] blade = [V2(-0.16f * l, 0.2f * h), V2(0.06f * l, 0.2f * h), V2(-0.02f * l, 0.42f * h), V2(-0.2f * l, 0.38f * h)];
            t.SideSlab(blade, -0.015f * w, 0.015f * w, CyHullDark);
            t.Box(V(0.018f * w, 0.33f * h, -0.08f * l), V(0.006f * w, 0.02f * h, 0.12f * l), CyRed);
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            {
                var xf = PitchUp(0.7f) * T(sd * 0.24f * w, 0.14f * h, -0.02f * l);
                t.BevelBox(V(0, 0, 0.04f * l), V(0.16f * w, 0.16f * w, 0.36f * l), 0.02f * w, CyHull, CyHullDark, xf);
                t.Box(V(-sd * 0.081f * w, 0, 0.04f * l), V(0.008f * w, 0.04f * w, 0.3f * l), CyTeamDim, null, xf);
                for (int cx = -1; cx <= 1; cx += 2)
                for (int cy = -1; cy <= 1; cy += 2)
                    t.Box(V(cx * 0.04f * w, cy * 0.04f * w, 0.222f * l), V(0.045f * w, 0.045f * w, 0.01f * l), CyRed, null, xf);
            }
        }
        pivots.Add(V(0, py, 0));
        return t;
    }

    // ================================================================ shield

    /// <summary>ED level (1..5) of a Cybran shield from the blueprint id suffix "_edN"; fallback: T3 = 4, else 1.</summary>
    private static int CyShieldLevel(UnitBlueprint bp)
    {
        int k = bp.Id.LastIndexOf("_ed", StringComparison.Ordinal);
        if (k >= 0 && int.TryParse(bp.Id.AsSpan(k + 3), out int n)) return Math.Clamp(n, 1, 5);
        return bp.Has(UnitCategory.Tech3) ? 4 : 1;
    }

    /// <summary>
    /// Cybran ED shield generator (one model for the ED1 → ED5 upgrade chain, growing with the level): a faceted pylon on
    /// a chamfered plinth, one red-slit collar per level, 3 claw buttresses (6 from ED3), raked spikes from ED4 and a red
    /// core lens at ED5. Turret = the spinning cage around the pylon top: 2 + level raked vanes with red emitter tips on a
    /// segmented ring (from ED3 a second, counter-raked tier; from ED4 a team crown and a top spike).
    /// </summary>
    private static MeshBuilder CyShield(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, int level)
    {
        float top = 0.07f * h, pylonTop = 0.78f * h;
        CyFoundation(b, w, l, top, CyHullDark, 0.3f);
        b.Cylinder(V(0, top, 0), Axis.Y, 0.08f * h, 0.42f * w, 0.36f * w, 6, CyHull, CyPad);
        b.Cylinder(V(0, top + 0.08f * h, 0), Axis.Y, pylonTop - top - 0.08f * h, 0.24f * w, 0.1f * w, 6, CyHull, CyHullLight);
        int claws = level >= 3 ? 6 : 3;
        for (int i = 0; i < claws; i++)
            CyClawFin(b, i * MathF.Tau / claws, top + 0.08f * h, 0.18f * w, 0.44f * w, 0.2f * w, (0.34f + 0.03f * level) * h, 0.03f * w, CyHullDark);
        for (int k = 0; k < level; k++)
        {
            float y = top + (0.14f + 0.1f * k) * h, f = (y - top - 0.08f * h) / (pylonTop - top - 0.08f * h);
            float r = (0.24f - 0.14f * f) * w;
            b.Cylinder(V(0, y, 0), Axis.Y, 0.03f * h, 1.06f * r, 1.02f * r, 6, k == 0 ? CyTeam : CyHullDark);
            b.Box(V(0, y + 0.06f * h, 0.95f * r), V(0.3f * r, 0.03f * h, 0.02f * w), CyRed, null, RotY(k * 1.05f));
        }
        if (level >= 4)
            for (int i = 0; i < 3; i++)
            {
                float a = i * MathF.Tau / 3 + MathF.PI / 3;
                Spike(b, CyRot(V(0, top + 0.08f * h, 0.3f * w), a), CyRot(V(0, 0.5f * h, 0.42f * w), a), 0.03f * w, CyHullDark);
            }
        if (level >= 5)
            b.Sphere(V(0, pylonTop, 0), V(0.08f * w, 0.05f * h, 0.08f * w), 3, 6, CyRed);

        var t = new MeshBuilder();
        float ringR = 0.2f * w;
        Ring(t, V(0, -0.02f * h, 0), ringR, 0.04f * w, 0.04f * h, 6 + level, CyHullDark, CyTeamDim);
        int vanes = 2 + level;
        for (int tierK = 0; tierK < (level >= 3 ? 2 : 1); tierK++)
            for (int i = 0; i < vanes; i++)
            {
                float a = i * MathF.Tau / vanes + tierK * MathF.PI / vanes;
                var r = RotY(a);
                float y0 = tierK == 0 ? 0 : -0.16f * h, rise = tierK == 0 ? 0.2f * h : -0.12f * h;
                // Vane profile (z, y): a raked parallelogram from the ring out and up (lower tier: out and down).
                Vector2[] vane = [V2(ringR, y0 - 0.03f * h), V2(ringR, y0 + 0.03f * h), V2(0.42f * w, y0 + rise + 0.015f * h), V2(0.42f * w, y0 + rise - 0.015f * h)];
                t.SideSlab(vane, -0.02f * w, 0.02f * w, CyHull, null, r);
                t.Box(V(0, y0 + rise, 0.43f * w), V(0.045f * w, 0.045f * w, 0.045f * w), CyRed, null, r);
            }
        if (level >= 4)
        {
            t.Cylinder(V(0, 0.02f * h, 0), Axis.Y, 0.05f * h, 0.12f * w, 0.08f * w, 6, CyTeam, CyHullDark);
            Spike(t, V(0, 0.07f * h, 0), V(0, 0.24f * h, 0), 0.04f * w, CyHullDark, 6);
        }
        pivots.Add(V(0, 0.62f * h, 0));
        return t;
    }

    // ================================================================ missiles

    /// <summary>
    /// Cybran missile silos (fixed weapons, no turret; launches are drawn by the effects). Tactical: a faceted hall at
    /// the back and a raked launch rail with a red-nosed cruise missile between team guard rails. Strategic
    /// "Liberator": a broad octagonal bunker with split blast doors over the silo (red seam, team rim), four raked claw
    /// buttresses, a control spire and two red-glowing vent stacks.
    /// </summary>
    private static void CyMissileSilo(MeshBuilder b, float w, float h, float l, bool nuke)
    {
        if (!nuke)
        {
            float top = 0.08f * h;
            CyFoundation(b, w, l, top, CyHullDark, 0.3f);
            CyHall(b, 0.42f * w, top, 0.42f * h, -0.22f * l, 0.48f * l, CyHull, CyHullLight);
            b.Box(V(0, top + 0.3f * h, 0.025f * l), V(0.6f * w, 0.03f * h, 0.01f * l), CyRed);
            b.Box(V(0, top + 0.43f * h, -0.24f * l), V(0.5f * w, 0.012f * h, 0.12f * l), CyTeam);
            var rail = PitchUp(1.0f) * T(0, top + 0.06f * h, -0.02f * l);
            b.BevelBox(V(0, 0, 0.24f * l), V(0.3f * w, 0.1f * h, 0.5f * l), 0.03f * w, CyHullDark, null, rail);
            b.Cylinder(V(0, 0.09f * h, 0.04f * l), Axis.Z, 0.36f * l, 0.055f * w, 0.055f * w, 6, CyHullLight, null, rail);
            Spike(b, Vector3.Transform(V(0, 0.09f * h, 0.4f * l), rail), Vector3.Transform(V(0, 0.09f * h, 0.5f * l), rail), 0.055f * w, CyRed, 6);
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            {
                b.Box(V(sd * 0.17f * w, 0.06f * h, 0.22f * l), V(0.03f * w, 0.1f * h, 0.46f * l), CyTeam, null, rail);
                b.Box(V(sd * 0.06f * w, 0.09f * h, 0.08f * l), V(0.06f * w, 0.008f * h, 0.06f * l), CyHullDark, null, rail);
                CyClawFin(b, sd * MathF.PI / 2, top, 0.2f * w, 0.46f * w, 0.24f * w, top + 0.3f * h, 0.03f * w, CyHullDark);
            }
            return;
        }
        float t0 = 0.06f * h, deck = t0 + 0.32f * h;
        CyFoundation(b, w, l, t0, CyHullDark, 0.2f);
        b.Frustum(V(0, t0, 0), 0.86f * w, 0.86f * l, 0.72f * w, 0.72f * l, deck - t0, CyHull, CyHullLight, null, 0.3f);
        b.Frustum(V(0, t0 + 0.1f * h, 0), 0.836f * w, 0.836f * l, 0.83f * w, 0.83f * l, 0.04f * h, CyTeamDim, null, null, 0.3f);
        b.Frustum(V(0, deck, 0), 0.5f * w, 0.5f * l, 0.47f * w, 0.47f * l, 0.03f * h, CyTeam, CyBlack, null, 0.3f);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            // Raked blast door half (x, z slab) leaving a red seam along the middle.
            Vector2[] door = [V2(sd * 0.012f * w, -0.2f * l), V2(sd * 0.012f * w, 0.2f * l), V2(sd * 0.2f * w, 0.14f * l), V2(sd * 0.2f * w, -0.14f * l)];
            b.Slab(door, deck + 0.03f * h, deck + 0.07f * h, CyHullDark, CyHull);
            b.Box(V(sd * 0.21f * w, deck + 0.05f * h, 0), V(0.01f * w, 0.02f * h, 0.24f * l), CyRedDim);
        }
        b.Box(V(0, deck + 0.04f * h, 0), V(0.024f * w, 0.012f * h, 0.38f * l), CyRed);
        for (int i = 0; i < 4; i++)
            CyClawFin(b, MathF.PI / 4 + i * MathF.PI / 2, t0, 0.42f * w, 0.62f * w, 0.44f * w, deck + 0.14f * h, 0.03f * w, CyHullDark);
        var sp = V(-0.27f * w, deck, -0.27f * l);
        b.Cylinder(sp, Axis.Y, 0.3f * h, 0.07f * w, 0.04f * w, 6, CyHull, CyHullDark);
        b.Cylinder(sp + V(0, 0.08f * h, 0), Axis.Y, 0.04f * h, 0.075f * w, 0.07f * w, 6, CyTeam);
        Spike(b, sp + V(0, 0.3f * h, 0), sp + V(0, 0.6f * h, 0), 0.025f * w, CyHullDark);
        b.Box(sp + V(0, 0.32f * h, 0), V(0.03f * w, 0.03f * w, 0.03f * w), CyRed);
        foreach (var (vx, vz) in (ReadOnlySpan<(float, float)>)[(0.27f, -0.27f), (0.27f, 0.27f)])
        {
            b.Cylinder(V(vx * w, deck, vz * l), Axis.Y, 0.14f * h, 0.06f * w, 0.05f * w, 6, CyHullDark, CyRedDim);
            b.Box(V(vx * w, deck + 0.09f * h, vz * l + 0.045f * w), V(0.03f * w, 0.05f * h, 0.008f * w), CyRed);
        }
    }

    /// <summary>
    /// Cybran tactical missile defense "Zapper": a tripod of claw legs around a faceted mast with red slits and a team
    /// band. Turret = the laser emitter: a faceted housing with a long focusing rod (red coil rings) ending in a red
    /// crystal tip, two raked cooling fins and a rear sensor block.
    /// </summary>
    private static MeshBuilder CyZapper(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        float y0 = 0.06f * h, mastTop = 0.6f * h;
        CyFoundation(b, w, l, y0, CyHullDark, 0.3f);
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF.Tau / 3 + MathF.PI / 3;
            CyLeg(b, CyRot(V(0, 0.32f * h, 0.08f * w), a), CyRot(V(0, 0.26f * h, 0.34f * w), a), CyRot(V(0, y0, 0.46f * w), a),
                  0.05f * w, CyHull, CyHullDark, CyTeam);
        }
        b.Frustum(V(0, y0, 0), 0.32f * w, 0.32f * l, 0.14f * w, 0.14f * l, mastTop - y0, CyHull, CyHullDark, null, 0.3f);
        b.Frustum(V(0, 0.2f * h, 0), 0.28f * w, 0.28f * l, 0.27f * w, 0.27f * l, 0.05f * h, CyTeam, null, null, 0.3f);
        for (int k = 0; k < 2; k++)
        {
            float y = (0.34f + 0.1f * k) * h, f = (y - y0) / (mastTop - y0);
            b.Box(V(0, y, (0.16f - 0.09f * f) * w + 0.004f * w), V(0.05f * w, 0.05f * h, 0.008f * w), CyRed);
        }
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.02f * h, 0), Axis.Y, 0.05f * h, 0.12f * w, 0.1f * w, 6, CyHullDark, CyHull);
        CyWedge(t, 0, 0.02f * h, 0.26f * w, 0.16f * h, -0.2f * l, 0.12f * l, CyHull, CyHullLight, 0.1f, 0.5f);
        float ry = 0.1f * h;
        t.Cylinder(V(0, ry, 0.1f * l), Axis.Z, 0.3f * l, 0.035f * w, 0.025f * w, 6, CyHullDark);
        for (int k = 0; k < 3; k++)
            t.Cylinder(V(0, ry, (0.16f + 0.08f * k) * l), Axis.Z, 0.025f * l, 0.055f * w, 0.055f * w, 6, k == 2 ? CyRed : CyRedDim, CyBlack);
        Spike(t, V(0, ry, 0.4f * l), V(0, ry, 0.5f * l), 0.04f * w, CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            Vector2[] fin = [V2(-0.18f * l, 0.04f * h), V2(0.02f * l, 0.04f * h), V2(-0.2f * l, 0.26f * h)];
            t.SideSlab(fin, sd * 0.1f * w, sd * 0.12f * w, CyHullDark, null, Matrix4x4.CreateRotationZ(-sd * 0.35f));
            t.Box(V(sd * 0.135f * w, 0.1f * h, 0.04f * l), V(0.008f * w, 0.025f * h, 0.06f * l), CyRed);
        }
        t.Box(V(0, 0.16f * h, -0.16f * l), V(0.1f * w, 0.06f * h, 0.08f * l), CyHullDark, CyTeam);
        pivots.Add(V(0, mastTop, 0));
        return t;
    }

    /// <summary>
    /// Cybran strategic missile defense (fixed weapon, no turret): a chamfered hex bunker with four raked claw
    /// buttresses, a central launch tube (black mouth, red rim) with the team-coloured nose of the next interceptor in
    /// it, two smaller cell hatches and a raked tracking radar blade with a red emitter strip.
    /// </summary>
    private static MeshBuilder? CyAntiNuke(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        float t0 = 0.06f * h, deck = t0 + 0.3f * h;
        CyFoundation(b, w, l, t0, CyHullDark, 0.25f);
        b.Frustum(V(0, t0, 0), 0.82f * w, 0.82f * l, 0.66f * w, 0.66f * l, deck - t0, CyHull, CyHullLight, null, 0.32f);
        b.Frustum(V(0, t0 + 0.08f * h, 0), 0.8f * w, 0.8f * l, 0.79f * w, 0.79f * l, 0.04f * h, CyTeamDim, null, null, 0.32f);
        for (int i = 0; i < 4; i++)
            CyClawFin(b, MathF.PI / 4 + i * MathF.PI / 2, t0, 0.38f * w, 0.56f * w, 0.4f * w, deck + 0.1f * h, 0.03f * w, CyHullDark);
        var c = V(0.04f * w, deck, 0.04f * l);
        b.Cylinder(c, Axis.Y, 0.32f * h, 0.2f * w, 0.15f * w, 6, CyHull, CyBlack);
        b.Cylinder(c + V(0, 0.06f * h, 0), Axis.Y, 0.05f * h, 0.205f * w, 0.195f * w, 6, CyTeam);
        b.Cylinder(c + V(0, 0.31f * h, 0), Axis.Y, 0.025f * h, 0.16f * w, 0.155f * w, 6, CyRed, CyBlack);
        Spike(b, c + V(0, 0.2f * h, 0), c + V(0, 0.5f * h, 0), 0.09f * w, CyTeam, 6);
        b.Box(c + V(0, 0.18f * h, 0.13f * w), V(0.06f * w, 0.06f * h, 0.008f * w), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            var hc = V(sd * 0.2f * w, deck, -0.18f * l);
            b.Frustum(hc, 0.14f * w, 0.14f * l, 0.12f * w, 0.12f * l, 0.04f * h, CyHullDark, CyBlack, null, 0.3f);
            b.Box(hc + V(0, 0.045f * h, 0), V(0.1f * w, 0.006f * h, 0.012f * w), CyRedDim);
        }
        var rb = V(-0.24f * w, deck, 0.2f * l);
        b.Cylinder(rb, Axis.Y, 0.18f * h, 0.05f * w, 0.035f * w, 6, CyHullDark);
        Vector2[] blade = [V2(-0.12f * l, 0.14f * h), V2(0.1f * l, 0.14f * h), V2(0.06f * l, 0.42f * h), V2(-0.08f * l, 0.36f * h)];
        b.SideSlab(blade, -0.015f * w, 0.015f * w, CyHull, null, RotY(-0.6f) * T(rb.X, rb.Y, rb.Z));
        b.Box(V(0.018f * w, 0.3f * h, 0), V(0.006f * w, 0.02f * h, 0.14f * l), CyRed, null, RotY(-0.6f) * T(rb.X, rb.Y, rb.Z));
        return null;
    }

    // ================================================================ artillery

    /// <summary>
    /// Cybran artillery installation (T2) / "Disruptor" heavy artillery (T3): a stepped faceted base with claw
    /// buttresses (4, 6 at T3) and a team band. Turret = an angular gun house with a raked rear counterweight and a long
    /// barrel pitched up (recoil sleeve, red heat vents, a slotted muzzle brake); T3 adds coil rings along a longer
    /// barrel, side rails and a sensor spike.
    /// </summary>
    private static MeshBuilder CyArtilleryStructure(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, int tier)
    {
        bool heavy = tier >= 3;
        float t0 = 0.06f * h, step = t0 + 0.12f * h, py = step + 0.12f * h;
        CyFoundation(b, w, l, t0, CyHullDark, 0.25f);
        b.Frustum(V(0, t0, 0), 0.8f * w, 0.8f * l, 0.66f * w, 0.66f * l, step - t0, CyHull, CyPad, null, 0.3f);
        b.Frustum(V(0, step, 0), 0.54f * w, 0.54f * l, 0.42f * w, 0.42f * l, py - step, CyHull, CyHullLight, null, 0.3f);
        b.Frustum(V(0, step + 0.03f * h, 0), 0.52f * w, 0.52f * l, 0.51f * w, 0.51f * l, 0.03f * h, CyTeam, null, null, 0.3f);
        int claws = heavy ? 6 : 4;
        for (int i = 0; i < claws; i++)
            CyClawFin(b, MathF.PI / claws + i * MathF.Tau / claws, t0, 0.3f * w, 0.48f * w, 0.3f * w, py - 0.02f * h, 0.03f * w, CyHullDark);
        for (int i = 0; i < 4; i++)
            b.Box(V(0, t0 + 0.07f * h, 0.37f * l), V(0.12f * w, 0.025f * h, 0.01f * l), CyRedDim, null, RotY(i * MathF.PI / 2));

        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.02f * h, 0), Axis.Y, 0.05f * h, 0.24f * w, 0.22f * w, 6, CyHullDark, CyHull);
        CyWedge(t, 0, 0.03f * h, 0.4f * w, 0.26f * h, -0.3f * l, 0.16f * l, CyHull, CyHullLight, 0.06f, 0.7f);
        t.Box(V(0.02f * w, 0.29f * h, -0.08f * l), V(0.12f * w, 0.012f * h, 0.28f * l), CyTeam);
        // Raked rear counterweight (z, y) below the house tail.
        Vector2[] cw = [V2(-0.4f * l, 0.04f * h), V2(-0.24f * l, 0.04f * h), V2(-0.24f * l, 0.22f * h), V2(-0.36f * l, 0.18f * h)];
        t.SideSlab(cw, -0.14f * w, 0.14f * w, CyHullDark, CyHull);
        t.Box(V(0, 0.12f * h, -0.401f * l), V(0.2f * w, 0.03f * h, 0.008f * l), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Box(V(sd * 0.21f * w, 0.15f * h, 0.06f * l), V(0.04f * w, 0.12f * h, 0.14f * l), CyHullDark);
        float len = (heavy ? 0.78f : 0.6f) * l;
        var gun = PitchUp(heavy ? 0.5f : 0.42f) * T(0, 0.16f * h, 0.06f * l);
        t.Cylinder(V(0, 0, 0), Axis.Z, 0.22f * l, 0.1f * w, 0.085f * w, 6, CyHull, null, gun);
        t.Cylinder(V(0, 0, 0.22f * l), Axis.Z, len - 0.22f * l, 0.055f * w, 0.045f * w, 6, CyHullDark, null, gun);
        for (int k = 0; k < 2; k++)
            t.Box(V(0, 0.09f * w, (0.06f + 0.08f * k) * l), V(0.08f * w, 0.012f * w, 0.04f * l), CyRed, null, gun);
        t.BevelBox(V(0, 0, len + 0.03f * l), V(0.13f * w, 0.1f * w, 0.08f * l), 0.02f * w, CyHull, null, gun);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Box(V(sd * 0.066f * w, 0, len + 0.03f * l), V(0.006f * w, 0.05f * w, 0.05f * l), CyRed, null, gun);
        t.Cylinder(V(0, 0, len + 0.07f * l), Axis.Z, 0.01f * l, 0.04f * w, 0.04f * w, 6, CyBlack, CyBlack, gun);
        if (heavy)
        {
            for (int k = 0; k < 3; k++)
                t.Cylinder(V(0, 0, (0.32f + 0.12f * k) * l), Axis.Z, 0.025f * l, 0.07f * w, 0.07f * w, 6, k == 1 ? CyTeam : CyRedDim, null, gun);
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
                t.Box(V(sd * 0.075f * w, 0, 0.42f * l), V(0.02f * w, 0.04f * w, 0.36f * l), CyHullDark, null, gun);
            Spike(t, V(-0.12f * w, 0.28f * h, -0.2f * l), V(-0.14f * w, 0.48f * h, -0.3f * l), 0.025f * w, CyHullDark);
            t.Box(V(-0.14f * w, 0.48f * h, -0.3f * l), V(0.03f * w, 0.03f * w, 0.03f * w), CyRed);
        }
        pivots.Add(V(0, py, 0));
        return t;
    }
}
