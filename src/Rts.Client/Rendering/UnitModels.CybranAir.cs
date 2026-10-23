using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// Cybran aircraft and the air staging facility (shapes "cy_air_scout" … "cy_air_staging", dispatched by
/// <c>CybranShape</c> in UnitModels.Cybran.cs): faceted, blade-winged airframes in dark gunmetal with team-colored
/// wing tips, red visor slits and red exhausts; asymmetric sensor/gun pods. Bodies sit around y = 0.45 h like the
/// UEF aircraft; transports keep their clamp claws as the lowest point (the cargo hangs under the model MinY).
/// </summary>
public static partial class UnitModels
{
    /// <summary>Faceted fuselage cross-section (x, y): flat-ish belly, sloped flanks, narrow spine.</summary>
    private static readonly Vector2[] CyFacetProf = [V2(-0.5f, 0), V2(-0.3f, -0.4f), V2(0.3f, -0.4f), V2(0.5f, 0), V2(0.22f, 0.5f), V2(-0.22f, 0.5f)];

    /// <summary>Rotation about the Z axis by <paramref name="a"/> around the line through (x, y) (canted fins, gull wings).</summary>
    private static Matrix4x4 CyCant(float x, float y, float a) => T(-x, -y, 0) * Matrix4x4.CreateRotationZ(a) * T(x, y, 0);

    /// <summary>Flat wing panel on side <paramref name="sd"/>: root chord r0 (front) .. r1 (back) at |x| = xr, tip chord
    /// t0 .. t1 at |x| = xt, between heights y0 and y1.</summary>
    private static void CyWingPanel(MeshBuilder b, int sd, float xr, float r0, float r1, float xt, float t0, float t1, float y0, float y1,
                                    Color col, Matrix4x4? xf = null)
    {
        Vector2[] p = [V2(sd * xr, r0), V2(sd * xt, t0), V2(sd * xt, t1), V2(sd * xr, r1)];
        b.Slab(p, y0, y1, col, null, xf);
    }

    /// <summary>Faceted fuselage along Z through (z, width, height, center y) stations, centered at x.</summary>
    private static void CyFuselage(MeshBuilder b, float x, ReadOnlySpan<Vector4> st, Color side, Color cap)
    {
        Span<Station> s = stackalloc Station[st.Length];
        for (int i = 0; i < st.Length; i++) s[i] = new Station(st[i].X, st[i].Y, st[i].Z, x, st[i].W);
        b.Loft(CyFacetProf, s, Axis.Z, side, cap);
    }

    private static Vector4 V4(float z, float sw, float sh, float y) => new(z, sw, sh, y);

    // ================================================================ T1

    /// <summary>T1 air scout: tiny dart with delta blades, a red sensor eye in the nose, one dorsal fin and an
    /// antenna spike on the left blade only.</summary>
    private static void CyAirScout(MeshBuilder b, float w, float h, float l)
    {
        CyFuselage(b, 0, [V4(-0.5f * l, 0.1f * w, 0.18f * h, 0.48f * h), V4(-0.15f * l, 0.18f * w, 0.34f * h, 0.5f * h),
                          V4(0.25f * l, 0.14f * w, 0.26f * h, 0.5f * h), V4(0.5f * l, 0.02f * w, 0.05f * h, 0.46f * h)], CyHull, CyHullDark);
        b.Box(V(0, 0.5f * h, 0.42f * l), V(0.05f * w, 0.06f * h, 0.06f * l), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            CyWingPanel(b, sd, 0.06f * w, 0.18f * l, -0.36f * l, 0.5f * w, -0.3f * l, -0.4f * l, 0.43f * h, 0.48f * h, CyHull);
            CyWingPanel(b, sd, 0.36f * w, -0.17f * l, -0.36f * l, 0.5f * w, -0.3f * l, -0.4f * l, 0.481f * h, 0.49f * h, CyTeam);
            b.Box(V(sd * 0.04f * w, 0.48f * h, -0.5f * l), V(0.04f * w, 0.08f * h, 0.012f * l), CyRed);
        }
        Fin(b, 0, 0.02f * w, 0.56f * h, -0.5f * l, -0.26f * l, -0.46f * l, 1.0f * h, CyHullDark);
        Spike(b, V(-0.42f * w, 0.47f * h, -0.2f * l), V(-0.42f * w, 0.47f * h, 0.22f * l), 0.02f * w, CyRedDim);
    }

    /// <summary>Prowler interceptor: knife fuselage, forward-swept blade wings with team tips, twin canted fins,
    /// a gun pod under the right wing root and a sensor spike on the left tip.</summary>
    private static void CyProwler(MeshBuilder b, float w, float h, float l)
    {
        CyFuselage(b, 0, [V4(-0.5f * l, 0.12f * w, 0.16f * h, 0.46f * h), V4(-0.2f * l, 0.18f * w, 0.3f * h, 0.5f * h),
                          V4(0.18f * l, 0.15f * w, 0.24f * h, 0.5f * h), V4(0.5f * l, 0.02f * w, 0.04f * h, 0.44f * h)], CyHull, CyHullDark);
        b.Box(V(0, 0.6f * h, 0.16f * l), V(0.05f * w, 0.02f * h, 0.14f * l), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            CyWingPanel(b, sd, 0.07f * w, 0.0f, -0.32f * l, 0.5f * w, 0.1f * l, -0.02f * l, 0.42f * h, 0.47f * h, CyHull);
            CyWingPanel(b, sd, 0.32f * w, 0.05f * l, -0.13f * l, 0.49f * w, 0.09f * l, -0.01f * l, 0.471f * h, 0.476f * h, CyTeam);
            Fin(b, sd * 0.08f * w, 0.02f * w, 0.5f * h, -0.5f * l, -0.3f * l, -0.47f * l, 0.95f * h, CyHullDark, CyCant(sd * 0.08f * w, 0.5f * h, -sd * 0.45f));
            b.Box(V(sd * 0.05f * w, 0.46f * h, -0.5f * l), V(0.05f * w, 0.08f * h, 0.012f * l), CyRed);
        }
        b.Cylinder(V(0.13f * w, 0.38f * h, -0.06f * l), Axis.Z, 0.42f * l, 0.035f * w, 0.022f * w, 5, CyBlack, CyRedDim);
        Spike(b, V(-0.5f * w, 0.45f * h, 0.04f * l), V(-0.5f * w, 0.45f * h, 0.3f * l), 0.02f * w, CyRedDim);
    }

    /// <summary>Zeus bomber: hunched fuselage, straight blade wings carrying two faceted engine nacelles with red
    /// exhausts, twin tail booms with in-canted fins and a red-lit bomb bay seam.</summary>
    private static void CyZeus(MeshBuilder b, float w, float h, float l)
    {
        CyFuselage(b, 0, [V4(-0.5f * l, 0.12f * w, 0.2f * h, 0.5f * h), V4(-0.2f * l, 0.2f * w, 0.38f * h, 0.5f * h),
                          V4(0.25f * l, 0.18f * w, 0.32f * h, 0.48f * h), V4(0.5f * l, 0.05f * w, 0.1f * h, 0.44f * h)], CyHull, CyHullDark);
        b.Box(V(0.02f * w, 0.62f * h, 0.3f * l), V(0.08f * w, 0.025f * h, 0.12f * l), CyRed);
        b.Box(V(0, 0.33f * h, -0.02f * l), V(0.06f * w, 0.012f * h, 0.36f * l), CyRedDim);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            CyWingPanel(b, sd, 0.1f * w, 0.14f * l, -0.12f * l, 0.5f * w, -0.04f * l, -0.2f * l, 0.44f * h, 0.5f * h, CyHull);
            CyWingPanel(b, sd, 0.38f * w, 0.0f, -0.14f * l, 0.5f * w, -0.05f * l, -0.19f * l, 0.501f * h, 0.506f * h, CyTeam);
            float nx = sd * 0.27f * w;
            CyFuselage(b, nx, [V4(-0.3f * l, 0.1f * w, 0.18f * h, 0.42f * h), V4(-0.1f * l, 0.13f * w, 0.26f * h, 0.42f * h),
                               V4(0.16f * l, 0.08f * w, 0.14f * h, 0.42f * h)], CyHullDark, CyBlack);
            b.Box(V(nx, 0.42f * h, -0.302f * l), V(0.08f * w, 0.1f * h, 0.01f * l), CyRed);
            b.Box(V(nx, 0.46f * h, -0.4f * l), V(0.035f * w, 0.06f * h, 0.2f * l), CyHull);
            Fin(b, nx, 0.02f * w, 0.48f * h, -0.5f * l, -0.36f * l, -0.48f * l, 0.92f * h, CyHullDark, CyCant(nx, 0.48f * h, sd * 0.3f));
        }
    }

    // ================================================================ T2

    /// <summary>Renegade gunship: hovering wedge hull with two side lift pods (red thrust glow underneath), a tail boom
    /// with a fin; turret = chin rocket pack (2x2 tubes with red tips).</summary>
    private static MeshBuilder? CyRenegade(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, bool hasWeapon)
    {
        CyWedge(b, 0, 0.3f * h, 0.42f * w, 0.4f * h, -0.36f * l, 0.42f * l, CyHull, CyHullDark, 0.08f);
        b.Box(V(0.03f * w, 0.62f * h, 0.3f * l), V(0.12f * w, 0.03f * h, 0.05f * l), CyRed);
        CyFuselage(b, 0, [V4(-0.5f * l, 0.05f * w, 0.1f * h, 0.56f * h), V4(-0.3f * l, 0.1f * w, 0.18f * h, 0.52f * h)], CyHullDark, CyHull);
        Fin(b, 0, 0.02f * w, 0.6f * h, -0.5f * l, -0.38f * l, -0.5f * l, 0.98f * h, CyTeam);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            float px = sd * 0.36f * w;
            b.Box(V(sd * 0.26f * w, 0.5f * h, 0.02f * l), V(0.16f * w, 0.06f * h, 0.1f * l), CyHullDark);
            b.Cylinder(V(px, 0.32f * h, 0.02f * l), Axis.Y, 0.3f * h, 0.13f * w, 0.1f * w, 6, CyHull, CyBlack);
            b.Cylinder(V(px, 0.5f * h, 0.02f * l), Axis.Y, 0.06f * h, 0.12f * w, 0.115f * w, 6, CyTeam);
            b.Cylinder(V(px, 0.31f * h, 0.02f * l), Axis.Y, 0.01f * h, 0.09f * w, 0.09f * w, 6, CyRed);
            CyWingPanel(b, sd, 0.08f * w, -0.36f * l, -0.44f * l, 0.2f * w, -0.4f * l, -0.46f * l, 0.54f * h, 0.57f * h, CyHull);
        }
        if (!hasWeapon) return null;
        var t = new MeshBuilder();
        t.Box(V(0, 0, 0), V(0.22f * w, 0.16f * h, 0.16f * l), CyHullDark, CyHull);
        for (int i = 0; i < 4; i++)
        {
            float x = (i & 1) == 0 ? -0.05f * w : 0.05f * w, y = i < 2 ? -0.04f * h : 0.04f * h;
            t.Cylinder(V(x, y, 0.06f * l), Axis.Z, 0.08f * l, 0.03f * w, 0.03f * w, 5, CyBlack);
            t.Box(V(x, y, 0.141f * l), V(0.03f * w, 0.03f * w, 0.004f * l), CyRed);
        }
        pivots.Add(V(0, 0.26f * h, 0.3f * l));
        return t;
    }

    /// <summary>Cormorant torpedo bomber: long-nosed fuselage, inverted-gull wings (engines at the bend), a nanite
    /// torpedo with a red seeker under the belly, a chin sonar pod and a T-tail.</summary>
    private static void CyCormorant(MeshBuilder b, float w, float h, float l)
    {
        CyFuselage(b, 0, [V4(-0.5f * l, 0.08f * w, 0.14f * h, 0.56f * h), V4(-0.2f * l, 0.16f * w, 0.32f * h, 0.52f * h),
                          V4(0.2f * l, 0.15f * w, 0.28f * h, 0.5f * h), V4(0.5f * l, 0.03f * w, 0.06f * h, 0.46f * h)], CyHull, CyHullDark);
        b.Box(V(-0.02f * w, 0.62f * h, 0.24f * l), V(0.07f * w, 0.025f * h, 0.12f * l), CyRed);
        b.Cylinder(V(0, 0.28f * h, -0.22f * l), Axis.Z, 0.48f * l, 0.04f * w, 0.04f * w, 6, CyBlack);
        b.Cylinder(V(0, 0.28f * h, 0.26f * l), Axis.Z, 0.04f * l, 0.04f * w, 0.012f * w, 6, CyRed);
        b.Cylinder(V(0.03f * w, 0.36f * h, 0.3f * l), Axis.Z, 0.12f * l, 0.04f * w, 0.025f * w, 5, CyHullDark, CyRedDim);
        const float droop = 0.3f, rise = 0.18f;
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            float x0 = 0.08f * w, x1 = 0.28f * w, y0 = 0.5f * h;
            CyWingPanel(b, sd, x0, 0.12f * l, -0.12f * l, x1, 0.04f * l, -0.14f * l, y0 - 0.025f * h, y0 + 0.025f * h, CyHull, CyCant(sd * x0, y0, -sd * droop));
            float bx = sd * (x0 + (x1 - x0) * MathF.Cos(droop)), by = y0 - (x1 - x0) * MathF.Sin(droop);
            var outer = T(bx - sd * x1, by - y0, 0) * CyCant(bx, by, sd * rise);
            CyWingPanel(b, sd, x1, 0.04f * l, -0.14f * l, 0.5f * w, -0.04f * l, -0.16f * l, y0 - 0.025f * h, y0 + 0.025f * h, CyHull, outer);
            CyWingPanel(b, sd, 0.4f * w, -0.01f * l, -0.15f * l, 0.5f * w, -0.04f * l, -0.16f * l, y0 + 0.026f * h, y0 + 0.032f * h, CyTeam, outer);
            CyFuselage(b, bx, [V4(-0.2f * l, 0.07f * w, 0.12f * h, by), V4(0.0f, 0.1f * w, 0.2f * h, by), V4(0.14f * l, 0.06f * w, 0.1f * h, by)], CyHullDark, CyBlack);
            b.Box(V(bx, by, -0.202f * l), V(0.06f * w, 0.08f * h, 0.008f * l), CyRed);
            CyWingPanel(b, sd, 0.02f * w, -0.42f * l, -0.5f * l, 0.2f * w, -0.47f * l, -0.52f * l, 0.92f * h, 0.95f * h, CyHull);
        }
        Fin(b, 0, 0.02f * w, 0.58f * h, -0.5f * l, -0.32f * l, -0.47f * l, 0.95f * h, CyHullDark);
        b.Box(V(0, 0.94f * h, -0.47f * l), V(0.06f * w, 0.02f * h, 0.03f * l), CyTeam);
    }

    /// <summary>T2 transport (Dragon Fly): armored spine with a raised cockpit pod, four lift pods on pylons,
    /// four pairs of clamp claws (the lowest point); turrets = small twin-gun mounts on the spine.</summary>
    private static MeshBuilder? CyDragonFly(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, bool hasWeapon)
    {
        CyTransportBody(b, w, h, l, [-0.3f, -0.1f, 0.1f, 0.28f], [-0.3f, 0.24f], 0.34f);
        if (!hasWeapon) return null;
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.04f * h, 0.07f * w, 0.06f * w, 6, CyHullDark);
        t.Loft(CyFacetProf, [new Station(-0.04f * l, 0.1f * w, 0.08f * h, 0, 0.07f * h), new Station(0.04f * l, 0.08f * w, 0.06f * h, 0, 0.07f * h)], Axis.Z, CyHull, CyTeam);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Cylinder(V(sd * 0.025f * w, 0.07f * h, 0.03f * l), Axis.Z, 0.07f * l, 0.012f * w, 0.01f * w, 5, CyBlack);
        t.Box(V(0, 0.07f * h, -0.042f * l), V(0.05f * w, 0.02f * h, 0.006f * l), CyRed);
        pivots.Add(V(0.03f * w, 0.85f * h, 0.12f * l));
        pivots.Add(V(-0.03f * w, 0.85f * h, -0.24f * l));
        return t;
    }

    // ================================================================ T1 transport (shared body) + T3

    /// <summary>T1 transport (Skyhook): slim spine with cockpit pod, two lift pods, three pairs of clamp claws.</summary>
    private static void CySkyhook(MeshBuilder b, float w, float h, float l) =>
        CyTransportBody(b, w, h, l, [-0.24f, 0.0f, 0.22f], [0.02f], 0.4f);

    /// <summary>Cybran transport body: faceted spine at the top, cockpit pod with a red visor in front, lift pods at the
    /// given z fractions (x = ±<paramref name="podX"/> w), clamp claws (spine → elbow → claw tip at 0.05 h) at the
    /// given z fractions; tail fins canted outward and a team stripe on the spine.</summary>
    private static void CyTransportBody(MeshBuilder b, float w, float h, float l, ReadOnlySpan<float> clampZ, ReadOnlySpan<float> podZ, float podX)
    {
        CyFuselage(b, 0, [V4(-0.5f * l, 0.12f * w, 0.14f * h, 0.74f * h), V4(-0.3f * l, 0.22f * w, 0.26f * h, 0.72f * h),
                          V4(0.26f * l, 0.22f * w, 0.26f * h, 0.72f * h), V4(0.38f * l, 0.3f * w, 0.36f * h, 0.68f * h),
                          V4(0.5f * l, 0.12f * w, 0.14f * h, 0.62f * h)], CyHull, CyHullDark);
        b.Box(V(0.02f * w, 0.78f * h, 0.42f * l), V(0.16f * w, 0.03f * h, 0.06f * l), CyRed);
        b.Box(V(0, 0.84f * h, -0.06f * l), V(0.06f * w, 0.012f * h, 0.56f * l), CyTeam);
        b.Box(V(0, 0.6f * h, -0.06f * l), V(0.3f * w, 0.05f * h, 0.66f * l), CyHullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            foreach (float zf in clampZ)
            {
                float z = zf * l;
                var root = V(sd * 0.1f * w, 0.6f * h, z);
                var elbow = V(sd * 0.3f * w, 0.36f * h, z);
                var tip = V(sd * 0.22f * w, 0.05f * h, z + 0.02f * l);
                Limb(b, root, elbow, 0.025f * w, CyHull);
                b.Box(elbow, V(0.05f * w, 0.05f * w, 0.05f * w), CyHullDark);
                Spike(b, elbow, tip, 0.022f * w, CyBlack);
            }
            foreach (float zf in podZ)
            {
                float z = zf * l, px = sd * podX * w;
                b.Box(V(sd * (0.1f + podX) * 0.5f * w, 0.7f * h, z), V((podX - 0.1f) * w, 0.05f * h, 0.06f * l), CyHullDark);
                b.Cylinder(V(px, 0.5f * h, z), Axis.Y, 0.28f * h, 0.11f * w, 0.09f * w, 6, CyHull, CyBlack);
                b.Cylinder(V(px, 0.66f * h, z), Axis.Y, 0.05f * h, 0.1f * w, 0.097f * w, 6, CyTeam);
                b.Cylinder(V(px, 0.49f * h, z), Axis.Y, 0.01f * h, 0.075f * w, 0.075f * w, 6, CyRed);
            }
            Fin(b, sd * 0.08f * w, 0.02f * w, 0.78f * h, -0.5f * l, -0.36f * l, -0.48f * l, 1.0f * h, CyHullDark, CyCant(sd * 0.08f * w, 0.78f * h, -sd * 0.35f));
        }
    }

    /// <summary>Gemini air superiority fighter: twin faceted hulls joined by a center wing (cockpit only on the right
    /// hull, the left one shorter with a sensor nose), swept outer blades with team tips, canted fins, twin red exhausts.</summary>
    private static void CyGemini(MeshBuilder b, float w, float h, float l)
    {
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            float x = sd * 0.16f * w, nose = sd > 0 ? 0.5f : 0.42f;
            CyFuselage(b, x, [V4(-0.5f * l, 0.1f * w, 0.18f * h, 0.46f * h), V4(-0.1f * l, 0.14f * w, 0.3f * h, 0.5f * h),
                              V4((nose - 0.2f) * l, 0.1f * w, 0.2f * h, 0.48f * h), V4(nose * l, 0.02f * w, 0.04f * h, 0.45f * h)], CyHull, CyHullDark);
            CyWingPanel(b, sd, 0.2f * w, 0.05f * l, -0.3f * l, 0.5f * w, -0.24f * l, -0.38f * l, 0.44f * h, 0.48f * h, CyHull);
            CyWingPanel(b, sd, 0.4f * w, -0.15f * l, -0.33f * l, 0.5f * w, -0.24f * l, -0.38f * l, 0.481f * h, 0.486f * h, CyTeam);
            Fin(b, x, 0.02f * w, 0.52f * h, -0.5f * l, -0.32f * l, -0.48f * l, 0.98f * h, CyHullDark, CyCant(x, 0.52f * h, -sd * 0.3f));
            b.Box(V(x, 0.46f * h, -0.502f * l), V(0.07f * w, 0.1f * h, 0.008f * l), CyRed);
        }
        Vector2[] center = [V2(-0.16f * w, 0.12f * l), V2(0.16f * w, 0.12f * l), V2(0.16f * w, -0.32f * l), V2(-0.16f * w, -0.32f * l)];
        b.Slab(center, 0.45f * h, 0.49f * h, CyHullDark);
        b.Box(V(0, 0.492f * h, -0.1f * l), V(0.02f * w, 0.006f * h, 0.36f * l), CyRedDim);
        b.Box(V(0.16f * w, 0.62f * h, 0.16f * l), V(0.05f * w, 0.02f * h, 0.14f * l), CyRed);
        b.Box(V(-0.16f * w, 0.47f * h, 0.39f * l), V(0.04f * w, 0.05f * h, 0.03f * l), CyRed);
    }

    /// <summary>Revenant strategic bomber: bat-like flying wing with a wedge center body, serrated trailing edges, four
    /// red exhausts, team leading-edge stripes and a red bomb bay seam.</summary>
    private static void CyRevenant(MeshBuilder b, float w, float h, float l)
    {
        CyWedge(b, 0, 0.34f * h, 0.3f * w, 0.42f * h, -0.44f * l, 0.48f * l, CyHull, CyHullDark, -0.05f);
        b.Box(V(-0.01f * w, 0.72f * h, 0.3f * l), V(0.06f * w, 0.02f * h, 0.12f * l), CyRed);
        b.Box(V(0, 0.335f * h, -0.02f * l), V(0.08f * w, 0.01f * h, 0.4f * l), CyRedDim);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            CyWingPanel(b, sd, 0.1f * w, 0.3f * l, -0.38f * l, 0.5f * w, -0.14f * l, -0.3f * l, 0.42f * h, 0.5f * h, CyHull);
            CyWingPanel(b, sd, 0.32f * w, 0.05f * l, -0.01f * l, 0.5f * w, -0.14f * l, -0.18f * l, 0.501f * h, 0.506f * h, CyTeam);
            for (int k = 0; k < 3; k++)
            {
                float xa = 0.1f + 0.4f * k / 3f, xb = 0.1f + 0.4f * (k + 1) / 3f, xm = (xa + xb) * 0.5f;
                float za = -0.38f + 0.08f * (xa - 0.1f) / 0.4f, zb = -0.38f + 0.08f * (xb - 0.1f) / 0.4f;
                Vector2[] tooth = [V2(sd * xa * w, za * l), V2(sd * xb * w, zb * l), V2(sd * xm * w, ((za + zb) * 0.5f - 0.08f) * l)];
                b.Slab(tooth, 0.43f * h, 0.48f * h, CyHullDark);
            }
            foreach (float ex in (ReadOnlySpan<float>)[0.2f, 0.3f])
            {
                b.Box(V(sd * ex * w, 0.53f * h, -0.18f * l), V(0.06f * w, 0.06f * h, 0.18f * l), CyHullDark);
                b.Box(V(sd * ex * w, 0.53f * h, -0.272f * l), V(0.05f * w, 0.045f * h, 0.006f * l), CyRed);
            }
        }
    }

    /// <summary>Spook spy plane: long slim fuselage, long straight glider wings with team tips, a dorsal sensor pod
    /// offset to the right with a red lens, a V-tail and an antenna spike on the left wing.</summary>
    private static void CySpook(MeshBuilder b, float w, float h, float l)
    {
        CyFuselage(b, 0, [V4(-0.5f * l, 0.06f * w, 0.14f * h, 0.5f * h), V4(-0.1f * l, 0.12f * w, 0.3f * h, 0.52f * h),
                          V4(0.3f * l, 0.1f * w, 0.26f * h, 0.5f * h), V4(0.5f * l, 0.02f * w, 0.05f * h, 0.47f * h)], CyHull, CyHullDark);
        CyFuselage(b, 0.03f * w, [V4(-0.2f * l, 0.04f * w, 0.1f * h, 0.66f * h), V4(-0.05f * l, 0.08f * w, 0.24f * h, 0.68f * h),
                                  V4(0.12f * l, 0.06f * w, 0.18f * h, 0.66f * h)], CyHullDark, CyRed);
        b.Box(V(0, 0.6f * h, 0.3f * l), V(0.05f * w, 0.025f * h, 0.1f * l), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            CyWingPanel(b, sd, 0.05f * w, 0.08f * l, -0.08f * l, 0.5f * w, 0.0f, -0.06f * l, 0.47f * h, 0.5f * h, CyHull);
            CyWingPanel(b, sd, 0.42f * w, 0.012f * l, -0.065f * l, 0.5f * w, 0.0f, -0.06f * l, 0.501f * h, 0.506f * h, CyTeam);
            Fin(b, sd * 0.04f * w, 0.02f * w, 0.55f * h, -0.5f * l, -0.36f * l, -0.48f * l, 1.0f * h, CyHullDark, CyCant(sd * 0.04f * w, 0.55f * h, -sd * 0.6f));
        }
        b.Box(V(0, 0.5f * h, -0.502f * l), V(0.04f * w, 0.06f * h, 0.006f * l), CyRed);
        Spike(b, V(-0.3f * w, 0.5f * h, -0.02f * l), V(-0.3f * w, 0.95f * h, -0.06f * l), 0.015f * w, CyRedDim);
    }

    // ================================================================ T2 air staging facility

    /// <summary>Cybran air staging: angular plinth, four hexagonal landing pads (tops at <see cref="StagingPad"/>) on
    /// tapered struts with team rims and red landing lights, nanite repair arms reaching to each pad from a faceted
    /// central spire with a red crown; an antenna spike on one side.</summary>
    private static void CyAirStaging(MeshBuilder b, float w, float h, float l)
    {
        Foundation(b, w * 0.98f, l * 0.98f, 0.05f * h, CyHullDark);
        float padY = StagingPadTop * h;
        for (int k = 0; k < 4; k++)
        {
            StagingPad(k, new Vector3(w, h, l), out var p);
            float px = p.X, pz = p.Z;
            b.Frustum(V(px, 0.05f * h, pz), 0.16f * w, 0.16f * l, 0.08f * w, 0.08f * l, padY - 0.13f * h, CyHull, null, null, 0.3f);
            b.Cylinder(V(px, padY - 0.1f * h, pz), Axis.Y, 0.1f * h, 0.17f * w, 0.2f * w, 6, CyHull, CyPad, null, 0);
            b.Cylinder(V(px, padY - 0.085f * h, pz), Axis.Y, 0.04f * h, 0.19f * w, 0.195f * w, 6, CyTeam, null, null, 0);
            for (int j = 0; j < 3; j++)
            {
                float a = j * MathF.Tau / 3f;
                b.Box(V(px + MathF.Cos(a) * 0.17f * w, padY + 0.01f * h, pz + MathF.Sin(a) * 0.17f * w), V(0.02f * w, 0.02f * h, 0.02f * w), CyRed);
            }
            var rot = Matrix4x4.CreateRotationY(MathF.Atan2(px, pz));
            b.Box(V(0, padY + 0.08f * h, 0.12f * w), V(0.04f * w, 0.05f * h, 0.12f * w), CyHullDark, null, rot);
            Spike(b, Vector3.Transform(V(0, padY + 0.08f * h, 0.18f * w), rot), Vector3.Transform(V(0, padY + 0.03f * h, 0.27f * w), rot), 0.02f * w, CyRedDim);
        }
        b.Frustum(V(0, 0.05f * h, 0), 0.26f * w, 0.26f * l, 0.12f * w, 0.12f * l, 0.7f * h, CyHull, CyHullLight, null, 0.3f);
        b.Box(V(0, 0.36f * h, 0), V(0.2f * w, 0.05f * h, 0.2f * l), CyTeam);
        b.Box(V(0, 0.6f * h, 0.07f * l), V(0.1f * w, 0.03f * h, 0.012f * l), CyRed);
        Spike(b, V(0, 0.75f * h, 0), V(0, 1.15f * h, 0), 0.05f * w, CyHullDark, 6);
        b.Box(V(0, 0.95f * h, 0), V(0.03f * w, 0.04f * h, 0.03f * w), CyRed);
        Spike(b, V(0.06f * w, 0.6f * h, -0.04f * l), V(0.14f * w, 1.0f * h, -0.08f * l), 0.012f * w, CyRedDim);
    }
}
