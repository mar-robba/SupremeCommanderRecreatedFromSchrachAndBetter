using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// Cybran economy, factory and intel structures (shapes "cy_factory" … "cy_stealth_field", dispatched by
/// <c>CybranShape</c> in UnitModels.Cybran.cs): chamfered dark plinths, faceted halls lofted from angular profiles, raked
/// claw buttresses and spires with red glowing seams, team bands on ridges and collars. Same conventions as the UEF
/// structures: y = 0 is the ground, factories open on +Z (door, rollout apron or air pad there); spinning parts
/// (mex rotor, radar array, gateway halo …) are the turret mesh, origin = pivot.
/// </summary>
public static partial class UnitModels
{
    /// <summary>Cybran plinth: a chamfered (octagonal) block sunk into the ground with its deck at y = top.</summary>
    private static void CyFoundation(MeshBuilder b, float w, float l, float top, Color col, float chamfer = 0.16f)
        => b.Frustum(V(0, -0.35f, 0), w, l, w * 0.96f, l * 0.96f, 0.35f + top, col, CyPad, null, chamfer);

    /// <summary>A point rotated about Y by <paramref name="a"/> rad (a = 0: unchanged).</summary>
    private static Vector3 CyRot(Vector3 p, float a) => Vector3.Transform(p, RotY(a));

    /// <summary>Faceted Cybran hall block lofted along X: vertical front (+Z) up to 0.72 of the height, a ridge set
    /// back, a steep rear slope; chamfered ends. (hz, hd) = depth center/extent, y0 = base, hw = half width.</summary>
    private static void CyHall(MeshBuilder b, float hw, float y0, float hH, float hz, float hd, Color side, Color cap)
    {
        Vector2[] prof = [V2(-0.5f, 0), V2(0.5f, 0), V2(0.5f, 0.72f), V2(0.16f, 1f), V2(-0.3f, 1f), V2(-0.5f, 0.62f)];
        b.Loft(prof, [new Station(-hw, 0.86f * hd, 0.8f * hH, hz, y0), new Station(-0.8f * hw, hd, hH, hz, y0),
                      new Station(0.8f * hw, hd, hH, hz, y0), new Station(hw, 0.86f * hd, 0.8f * hH, hz, y0)], Axis.X, side, cap);
    }

    // ================================================================ factories

    /// <summary>
    /// Cybran land/air factory (T1-T3): faceted hall at the back with a black door and red lintel facing +Z, raked blade
    /// buttresses, back corner spires with red lights, two nanolathe arms reaching over the apron. Land: a pad apron with
    /// red guide lights and blade fins; air: a hex landing pad with red corner lights and a raked control spire. Each
    /// tier stacks one more faceted block on the ridge (T3 also a central antenna spike and a third arm pair).
    /// </summary>
    private static void CyFactory(MeshBuilder b, float w, float h, float l, bool air, int tier)
    {
        float y0 = 0.08f * h;
        CyFoundation(b, w, l, y0, CyHullDark, 0.1f);
        float hz = -0.15f * l, hd = 0.6f * l, hH = 0.58f * h, front = hz + 0.5f * hd, hw = 0.42f * w;
        CyHall(b, hw, y0, hH, hz, hd, CyHull, CyHullDark);
        // Door: dark opening, red lintel, angular jambs.
        b.Box(V(0, y0 + 0.19f * h, front + 0.003f * l), V(0.44f * w, 0.38f * h, 0.006f * l), CyBlack);
        b.Box(V(0, y0 + 0.395f * h, front + 0.005f * l), V(0.5f * w, 0.025f * h, 0.01f * l), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.BevelBox(V(sd * 0.25f * w, y0 + 0.2f * h, front + 0.015f * l), V(0.05f * w, 0.4f * h, 0.04f * l), 0.012f * w, CyHullDark, CyTeamDim);
            // Flank buttress blades and red slits on the chamfered ends.
            Fin(b, sd * (hw + 0.005f * w), 0.018f * w, y0, -0.42f * l, -0.02f * l, -0.3f * l, y0 + 0.52f * h, CyHullDark);
            for (int k = 0; k < 3; k++)
                b.Box(V(sd * (hw + 0.002f * w), y0 + 0.14f * h, (-0.3f + 0.1f * k) * l), V(0.006f * w, 0.1f * h, 0.025f * l), CyRedDim);
            b.Box(V(sd * (0.8f * hw + 0.01f * w), y0 + 0.5f * hH, front - 0.004f * l), V(0.02f * w, 0.05f * h, 0.006f * l), CyTeam);
            // Back corner spires with red lights.
            float sx = sd * 0.41f * w, sz = -0.42f * l, st = y0 + (0.8f + 0.08f * tier) * h;
            b.Frustum(V(sx, y0, sz), 0.1f * w, 0.1f * l, 0.05f * w, 0.05f * l, 0.3f * h, CyHullDark, null, null, 0.25f);
            Spike(b, V(sx, y0 + 0.28f * h, sz), V(sx + sd * 0.025f * w, st, sz - 0.02f * l), 0.025f * w, CyHull);
            b.Box(V(sx + sd * 0.014f * w, y0 + 0.28f * h + 0.62f * (st - y0 - 0.28f * h), sz - 0.012f * l), V(0.022f * w, 0.022f * w, 0.022f * w), CyRed);
        }
        // Roof vents on the main ridge (beside the stacked blocks), ridge team band + stacked tiers (each a smaller hall on the previous ridge).
        float by = y0 + hH, bz = hz - 0.07f * hd, bd = 0.42f * hd, bw = 0.62f * hw, bh = 0.2f * h;
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            for (int k = 0; k < 2; k++)
            {
                var vp = V(sd * 0.3f * w, by - 0.01f * h, bz + (k == 0 ? -0.12f : 0.12f) * hd);
                b.Cylinder(vp, Axis.Y, 0.07f * h, 0.035f * w, 0.026f * w, 6, CyHullDark, CyRedDim);
            }
        for (int k = 2; k <= tier; k++)
        {
            CyHall(b, bw, by - 0.01f * h, bh, bz, bd, k == 2 ? CyHullLight : CyHull, CyHullDark);
            b.Box(V(0, by + 0.1f * bh, bz + 0.5f * bd + 0.003f * l), V(1.2f * bw, 0.025f * h, 0.006f * l), CyRed);
            by += bh - 0.01f * h; bz -= 0.07f * bd; bd *= 0.46f; bw *= 0.7f; bh *= 0.7f;
        }
        foreach (float dz in (ReadOnlySpan<float>)[-0.22f, 0.22f])
            b.Box(V(0, by + 0.004f * h, bz + dz * bd), V(1.5f * bw, 0.008f * h, 0.1f * bd), CyTeam);
        Spike(b, V(-0.5f * bw, by, bz), V(-0.55f * bw, by + (0.2f + 0.08f * tier) * h, bz - 0.03f * l), 0.018f * w, CyHullDark);
        if (tier >= 3)
        {
            Spike(b, V(0.15f * bw, by, bz), V(0.2f * bw, by + 0.5f * h, bz - 0.04f * l), 0.04f * w, CyHull, 6);
            b.Box(V(0.18f * bw, by + 0.3f * h, bz - 0.025f * l), V(0.03f * w, 0.03f * w, 0.03f * w), CyRed);
        }
        // Nanolathe arms from the front roof edge over the apron.
        float eY = y0 + 0.72f * hH;
        for (int k = 0; k < (tier >= 3 ? 2 : 1); k++)
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            {
                float ax = sd * (0.32f - 0.12f * k) * w;
                var sh = V(ax, eY, front - 0.03f * l);
                var el = V(ax * 0.95f, eY + (0.16f - 0.04f * k) * h, front + 0.09f * l);
                var wr = V(ax * 0.55f, y0 + 0.5f * h, front + (0.2f + 0.04f * k) * l);
                b.BevelBox(sh, V(0.07f * w, 0.06f * h, 0.07f * l), 0.012f * w, CyHullDark, CyTeam);
                Limb(b, sh, el, 0.016f * w, CyHull);
                Limb(b, el, wr, 0.012f * w, CyHullDark);
                b.Box(el, V(0.035f * w, 0.035f * w, 0.035f * w), CyTeam);
                foreach (int c in (ReadOnlySpan<int>)[-1, 1])
                    Spike(b, wr, wr + V(-sd * 0.015f * w, -0.12f * h, c * 0.018f * l), 0.01f * w, CyBlack);
                b.Box(wr + V(0, -0.015f * h, 0), V(0.022f * w, 0.022f * w, 0.022f * w), CyRed);
            }
        if (air)
        {
            // Hex landing pad with red corner lights and a team ring; raked control spire with a red visor.
            float pz = 0.33f * l;
            b.Cylinder(V(0, y0, pz), Axis.Y, 0.02f * h, 0.17f * w, 0.16f * w, 6, CyHullDark, CyPad);
            b.Cylinder(V(0, y0 + 0.02f * h, pz), Axis.Y, 0.004f * h, 0.13f * w, 0.13f * w, 6, CyTeamDim);
            b.Cylinder(V(0, y0 + 0.021f * h, pz), Axis.Y, 0.005f * h, 0.11f * w, 0.11f * w, 6, CyPad);
            for (int i = 0; i < 6; i++)
            {
                var p = V(0, y0 + 0.025f * h, pz) + CyRot(V(0, 0, 0.155f * w), i * MathF.Tau / 6 + MathF.PI / 6);
                b.Box(p, V(0.018f * w, 0.012f * h, 0.018f * w), CyRed);
            }
            var tb = V(0.38f * w, y0, 0.3f * l);
            b.Frustum(tb, 0.1f * w, 0.1f * l, 0.05f * w, 0.05f * l, 0.2f * h, CyHullDark, null, null, 0.25f);
            Spike(b, tb + V(0, 0.18f * h, 0), tb + V(0.02f * w, 0.75f * h, -0.03f * l), 0.03f * w, CyHull);
            b.BevelBox(tb + V(0.006f * w, 0.48f * h, -0.012f * l), V(0.07f * w, 0.07f * h, 0.07f * l), 0.015f * w, CyHullLight);
            b.Box(tb + V(0.006f * w, 0.48f * h, 0.0f), V(0.075f * w, 0.025f * h, 0.074f * l), CyRed);
            // Fuel pods on the left.
            foreach (float z in (ReadOnlySpan<float>)[0.24f, 0.38f])
                b.Cylinder(V(-0.38f * w, y0, z * l), Axis.Y, 0.14f * h, 0.04f * w, 0.035f * w, 6, CyHullDark, CyTeamDim);
        }
        else
        {
            // Apron plate with red guide lights and raked blade fins along the sides.
            b.Box(V(0, y0 + 0.004f * h, 0.32f * l), V(0.46f * w, 0.008f * h, 0.34f * l), CyPad);
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            {
                for (int k = 0; k < 4; k++)
                    b.Box(V(sd * 0.215f * w, y0 + 0.012f * h, (0.2f + 0.08f * k) * l), V(0.016f * w, 0.012f * h, 0.03f * l), CyRed);
                Fin(b, sd * 0.3f * w, 0.03f * w, y0, front, 0.46f * l, front + 0.03f * l, y0 + 0.2f * h, CyHullDark);
                b.Box(V(sd * 0.3f * w, y0 + 0.05f * h, 0.38f * l), V(0.032f * w, 0.03f * h, 0.06f * l), CyTeam);
            }
        }
    }

    /// <summary>
    /// Cybran quantum gateway: chamfered plinth, a hex dais with a red/violet gate disc and a team/black segmented ring,
    /// three huge raked claw pylons arching in over the disc. Turret = the spinning halo the claws hold (segment ring,
    /// red emitters pointing down at the disc).
    /// </summary>
    private static MeshBuilder CyGateway(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        float y0 = 0.05f * h;
        CyFoundation(b, w, l, y0, CyHullDark, 0.24f);
        b.Cylinder(V(0, y0, 0), Axis.Y, 0.06f * h, 0.43f * w, 0.4f * w, 6, CyHull, CyPad, null, 0);
        float dy = y0 + 0.06f * h;
        // Gate disc: black well with a thin red rim, red rune segments and a small violet core.
        b.Cylinder(V(0, dy, 0), Axis.Y, 0.008f * h, 0.3f * w, 0.3f * w, 12, CyRedDim);
        b.Cylinder(V(0, dy, 0), Axis.Y, 0.011f * h, 0.28f * w, 0.28f * w, 12, CyBlack);
        Ring(b, V(0, dy + 0.011f * h, 0), 0.2f * w, 0.012f * w, 0.003f * h, 12, CyRedDim, CyBlack);
        b.Cylinder(V(0, dy, 0), Axis.Y, 0.014f * h, 0.1f * w, 0.1f * w, 12, CyViolet);
        Ring(b, V(0, dy + 0.01f * h, 0), 0.33f * w, 0.03f * w, 0.025f * h, 18, CyTeam, CyBlack);
        // Plinth corner blades between the pylons, team marks on the dais edges.
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.Tau / 6;
            b.Box(V(0, dy + 0.002f * h, 0.36f * w), V(0.18f * w, 0.006f * h, 0.03f * w), (i & 1) == 0 ? CyTeam : CyHullDark, null, RotY(a));
        }
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF.Tau / 3;
            var r = RotY(a);
            b.Frustum(V(0, y0, 0.42f * w), 0.15f * w, 0.15f * w, 0.08f * w, 0.08f * w, 0.24f * h, CyHullDark, CyTeam, r, 0.28f);
            var p0 = CyRot(V(0, 0.27f * h, 0.42f * w), a);
            var p1 = CyRot(V(0, 0.62f * h, 0.44f * w), a);
            var p2 = CyRot(V(0, 0.88f * h, 0.27f * w), a);
            var p3 = CyRot(V(0, 0.76f * h, 0.15f * w), a);
            Limb(b, p0, p1, 0.04f * w, CyHull);
            Limb(b, p1, p2, 0.032f * w, CyHull);
            Spike(b, p2, p3, 0.028f * w, CyHullDark);
            b.Box(p1, V(0.07f * w, 0.07f * w, 0.07f * w), CyTeam);
            b.Box(p2, V(0.055f * w, 0.055f * w, 0.055f * w), CyHullDark);
            b.Box(CyRot(V(0, 0.45f * h, 0.455f * w), a), V(0.03f * w, 0.1f * h, 0.012f * w), CyRed, null, r);
            b.Box(CyRot(V(0, 0.2f * h, 0.5f * w), a), V(0.05f * w, 0.03f * h, 0.01f * w), CyRed, null, r);
            // Rear blade fin behind each pylon.
            Fin(b, 0, 0.025f * w, y0, 0.36f * w, 0.5f * w, 0.47f * w, 0.5f * h, CyHullDark, r);
        }
        var t = new MeshBuilder();
        Ring(t, V(0, 0, 0), 0.22f * w, 0.025f * w, 0.03f * h, 12, CyHullDark, CyHull);
        Ring(t, V(0, 0.02f * h, 0), 0.22f * w, 0.028f * w, 0.008f * h, 12, CyTeam);
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.Tau / 6 + MathF.PI / 6;
            var p = CyRot(V(0, -0.01f * h, 0.22f * w), a);
            Spike(t, p, CyRot(V(0, -0.12f * h, 0.17f * w), a), 0.014f * w, CyHullDark);
            t.Box(p + V(0, -0.02f * h, 0), V(0.025f * w, 0.025f * w, 0.025f * w), CyRed);
        }
        pivots.Add(V(0, 0.62f * h, 0));
        return t;
    }

    // ================================================================ economy

    /// <summary>
    /// Cybran mass extractor (T1-T3): chamfered plinth over the deposit, three raked claw legs gripping the ground, a
    /// faceted pump column with a team band and red slits; T2 adds green-glowing side tanks, T3 a second collar and
    /// spikes. Turret = the spinning rotor head (3 raked blades, 4 at T3, red tips, green mass core).
    /// </summary>
    private static MeshBuilder CyMex(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, int tier)
    {
        float top = 0.16f * h;
        b.Frustum(V(0, -0.3f, 0), 0.96f * w, 0.96f * l, 0.78f * w, 0.78f * l, 0.3f + top, CyHullDark, CyPad, null, 0.3f);
        b.Cylinder(V(0, top, 0), Axis.Y, 0.006f * h, 0.3f * w, 0.3f * w, 8, CyGreen);
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF.Tau / 3 + MathF.PI / 3;
            CyLeg(b, CyRot(V(0, top + 0.2f * h, 0.16f * w), a), CyRot(V(0, top + 0.34f * h, 0.36f * w), a),
                  CyRot(V(0, 0, 0.5f * w), a), 0.035f * w, CyHull, CyHullDark, CyTeam);
        }
        float colH = (0.36f + 0.06f * tier) * h;
        b.Frustum(V(0, top, 0), 0.4f * w, 0.4f * l, 0.24f * w, 0.24f * l, colH, CyHull, CyHullLight, null, 0.3f);
        b.Frustum(V(0, top + 0.1f * h, 0), 0.37f * w, 0.37f * l, 0.355f * w, 0.355f * l, 0.05f * h, CyTeam, null, null, 0.3f);
        for (int i = 0; i < 4; i++)
            b.Box(V(0, top + 0.26f * h, 0.165f * w), V(0.06f * w, 0.08f * h, 0.008f * w), CyRed, null, RotY(i * MathF.PI / 2));
        if (tier >= 2)
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            {
                b.Cylinder(V(sd * 0.3f * w, top, -0.05f * l), Axis.Y, 0.2f * h, 0.07f * w, 0.06f * w, 6, CyHullDark, CyHull);
                b.Box(V(sd * 0.3f * w, top + 0.1f * h, 0.015f * l), V(0.03f * w, 0.1f * h, 0.012f * w), CyGreen);
            }
        if (tier >= 3)
        {
            b.Frustum(V(0, top + colH - 0.12f * h, 0), 0.33f * w, 0.33f * l, 0.3f * w, 0.3f * l, 0.06f * h, CyHullDark, CyTeamDim, null, 0.3f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * MathF.PI / 2 + MathF.PI / 4;
                Spike(b, CyRot(V(0, top + colH - 0.1f * h, 0.15f * w), a), CyRot(V(0, top + colH + 0.12f * h, 0.3f * w), a), 0.02f * w, CyHullDark);
            }
        }
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.08f * h, 0.12f * w, 0.09f * w, 6, CyHullDark, CyGreen);
        int blades = tier >= 3 ? 4 : 3;
        for (int i = 0; i < blades; i++)
        {
            var r = RotY(i * MathF.Tau / blades);
            Vector2[] blade = [V2(0.06f * w, 0), V2(0.3f * w, 0.02f * h), V2(0.34f * w, 0.06f * h), V2(0.08f * w, 0.09f * h)];
            t.SideSlab(blade, -0.025f * w, 0.025f * w, CyHull, null, r);
            t.Box(V(0, 0.045f * h, 0.33f * w), V(0.04f * w, 0.03f * h, 0.03f * w), CyRed, null, r);
        }
        Spike(t, V(0, 0.08f * h, 0), V(0, 0.2f * h, 0), 0.05f * w, CyHull, 6);
        pivots.Add(V(0, top + colH, 0));
        return t;
    }

    /// <summary>Hex reactor core at base c: tapering faceted body, team collar, three red glow slits, a dark cap with a
    /// red disc and a short spike.</summary>
    private static void CyReactorCore(MeshBuilder b, Vector3 c, float r, float hgt, Color glow)
    {
        b.Cylinder(c, Axis.Y, 0.62f * hgt, r, 0.8f * r, 6, CyHull);
        b.Cylinder(c + V(0, 0.62f * hgt, 0), Axis.Y, 0.22f * hgt, 0.8f * r, 0.5f * r, 6, CyHullLight, CyHullDark);
        b.Cylinder(c + V(0, 0.12f * hgt, 0), Axis.Y, 0.08f * hgt, 1.02f * r, 1.0f * r, 6, CyTeam);
        for (int i = 0; i < 3; i++)
            b.Box(c + V(0, 0.4f * hgt, 0.86f * r), V(0.28f * r, 0.32f * hgt, 0.04f * r), glow, null, RotY(i * MathF.Tau / 3));
        b.Cylinder(c + V(0, 0.84f * hgt, 0), Axis.Y, 0.03f * hgt, 0.3f * r, 0.3f * r, 6, glow);
        Spike(b, c + V(0, 0.84f * hgt, 0), c + V(0, hgt, 0), 0.18f * r, CyHullDark);
    }

    /// <summary>Raked claw buttress (blade fin) at angle a around the center: base from radius r0 to r1 on y0, apex
    /// at radius ra / height ya; red light at the apex.</summary>
    private static void CyClawFin(MeshBuilder b, float a, float y0, float r0, float r1, float ra, float ya, float th, Color col)
    {
        var r = RotY(a);
        Fin(b, 0, th, y0, r0, r1, ra, ya, col, r);
        b.Box(CyRot(V(0, ya - 0.04f * (ya - y0), ra + 0.02f * (r1 - r0)), a), V(1.6f * th, 1.6f * th, 1.6f * th), CyRed, null, r);
    }

    /// <summary>
    /// Cybran power generator: T1 = one hex reactor core with three raked claw buttresses on a chamfered plinth; T2 = a
    /// larger core ringed by three satellite cores linked by conduits, six buttresses and two antenna spikes.
    /// </summary>
    private static void CyPowerGen(MeshBuilder b, float w, float h, float l, int tier)
    {
        float y0 = 0.06f * h;
        CyFoundation(b, w, l, y0, CyHullDark, 0.28f);
        if (tier <= 1)
        {
            CyReactorCore(b, V(0, y0, 0), 0.26f * w, 0.9f * h, CyRed);
            for (int i = 0; i < 3; i++)
                CyClawFin(b, i * MathF.Tau / 3 + MathF.PI / 3, y0, 0.18f * w, 0.46f * w, 0.24f * w, y0 + 0.62f * h, 0.035f * w, CyHullDark);
            return;
        }
        CyReactorCore(b, V(0, y0, 0), 0.2f * w, 0.92f * h, CyRed);
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF.Tau / 3;
            var c = CyRot(V(0, y0, 0.3f * w), a);
            CyReactorCore(b, c, 0.1f * w, 0.5f * h, CyRedDim);
            Limb(b, c + V(0, 0.3f * h, 0) - CyRot(V(0, 0, 0.06f * w), a), CyRot(V(0, y0 + 0.5f * h, 0.15f * w), a), 0.025f * w, CyHullDark);
            CyClawFin(b, a + MathF.PI / 3, y0, 0.18f * w, 0.47f * w, 0.24f * w, y0 + 0.58f * h, 0.025f * w, CyHullDark);
            CyClawFin(b, a + MathF.PI / 3 + 0.35f, y0, 0.26f * w, 0.44f * w, 0.3f * w, y0 + 0.3f * h, 0.02f * w, CyHull);
        }
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            Spike(b, V(sd * 0.1f * w, y0 + 0.7f * h, -0.06f * l), V(sd * 0.16f * w, y0 + 1.15f * h, -0.1f * l), 0.018f * w, CyHullDark);
    }

    /// <summary>
    /// Cybran T3 power generator: a hex dais with six raked buttresses, a tall stacked reactor (three tiers, red slits)
    /// and three satellite cores. Turret = the spinning crown on top (ring, three arms with red emitters, a spike).
    /// </summary>
    private static MeshBuilder CyPowerGen3(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        float y0 = 0.05f * h;
        CyFoundation(b, w, l, y0, CyHullDark, 0.28f);
        b.Cylinder(V(0, y0, 0), Axis.Y, 0.06f * h, 0.42f * w, 0.39f * w, 6, CyHull, CyPad, null, 0);
        float dy = y0 + 0.06f * h;
        Ring(b, V(0, dy + 0.005f * h, 0), 0.36f * w, 0.02f * w, 0.01f * h, 12, CyTeam, CyHullDark);
        CyReactorCore(b, V(0, dy, 0), 0.2f * w, 0.42f * h, CyRed);
        CyReactorCore(b, V(0, dy + 0.34f * h, 0), 0.15f * w, 0.32f * h, CyRed);
        b.Cylinder(V(0, dy + 0.6f * h, 0), Axis.Y, 0.1f * h, 0.1f * w, 0.07f * w, 6, CyHullDark, CyRed);
        for (int i = 0; i < 6; i++)
            CyClawFin(b, i * MathF.Tau / 6, dy, 0.17f * w, 0.42f * w, 0.2f * w, dy + ((i & 1) == 0 ? 0.62f : 0.45f) * h, 0.022f * w, (i & 1) == 0 ? CyHullDark : CyHull);
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF.Tau / 3 + MathF.PI / 6;
            var c = CyRot(V(0, dy, 0.32f * w), a);
            CyReactorCore(b, c, 0.065f * w, 0.32f * h, CyRedDim);
        }
        var t = new MeshBuilder();
        Ring(t, V(0, 0.02f * h, 0), 0.12f * w, 0.02f * w, 0.025f * h, 9, CyHullDark, CyTeam);
        for (int i = 0; i < 3; i++)
        {
            var r = RotY(i * MathF.Tau / 3);
            t.Box(V(0, 0.02f * h, 0.07f * w), V(0.025f * w, 0.02f * h, 0.12f * w), CyHull, null, r);
            Spike(t, CyRot(V(0, 0.02f * h, 0.12f * w), i * MathF.Tau / 3), CyRot(V(0, 0.16f * h, 0.17f * w), i * MathF.Tau / 3), 0.016f * w, CyHullDark);
            t.Box(V(0, 0.035f * h, 0.13f * w), V(0.03f * w, 0.03f * w, 0.03f * w), CyRed, null, r);
        }
        Spike(t, V(0, 0, 0), V(0, 0.25f * h, 0), 0.03f * w, CyHull, 6);
        pivots.Add(V(0, dy + 0.7f * h, 0));
        return t;
    }

    /// <summary>
    /// Cybran hydrocarbon plant: an amber-glowing vent well under a faceted housing held up by four raked claw legs;
    /// conduits to two side tanks and an offset exhaust stack with red/amber glow.
    /// </summary>
    private static void CyHydro(MeshBuilder b, float w, float h, float l)
    {
        float y0 = 0.05f * h;
        CyFoundation(b, w, l, y0, CyHullDark, 0.26f);
        b.Cylinder(V(0, y0, 0), Axis.Y, 0.02f * h, 0.2f * w, 0.2f * w, 8, CyBlack);
        b.Cylinder(V(0, y0 + 0.02f * h, 0), Axis.Y, 0.006f * h, 0.13f * w, 0.13f * w, 8, CyAmber);
        float hy = 0.42f * h;
        foreach (var (sx, sz) in (ReadOnlySpan<(int, int)>)[(-1, -1), (1, -1), (-1, 1), (1, 1)])
            CyLeg(b, V(sx * 0.14f * w, hy + 0.06f * h, sz * 0.14f * l), V(sx * 0.32f * w, hy + 0.14f * h, sz * 0.32f * l),
                  V(sx * 0.42f * w, y0, sz * 0.42f * l), 0.03f * w, CyHull, CyHullDark, CyTeam);
        b.Frustum(V(0, hy, 0), 0.22f * w, 0.22f * l, 0.36f * w, 0.36f * l, 0.12f * h, CyHullDark, null, null, 0.3f);
        b.Frustum(V(0, hy + 0.12f * h, 0), 0.36f * w, 0.36f * l, 0.2f * w, 0.2f * l, 0.2f * h, CyHull, CyHullLight, null, 0.3f);
        b.Frustum(V(0, hy + 0.14f * h, 0), 0.345f * w, 0.345f * l, 0.33f * w, 0.33f * l, 0.03f * h, CyTeam, null, null, 0.3f);
        for (int i = 0; i < 4; i++)
            b.Box(V(0, hy + 0.08f * h, 0.15f * w), V(0.08f * w, 0.04f * h, 0.008f * w), CyRed, null, RotY(i * MathF.PI / 2 + MathF.PI / 4));
        b.Cylinder(V(0, y0, 0), Axis.Y, hy - y0, 0.04f * w, 0.05f * w, 6, CyHullDark);
        // Exhaust stack (back right) and tanks (left/right front) fed by conduits.
        var st = V(0.3f * w, y0, -0.3f * l);
        b.Cylinder(st, Axis.Y, 0.95f * h, 0.07f * w, 0.045f * w, 6, CyHull, CyAmber);
        b.Box(st + V(0, 0.75f * h, 0.045f * w), V(0.03f * w, 0.12f * h, 0.01f * w), CyRed);
        Limb(b, V(0.12f * w, hy + 0.2f * h, -0.12f * l), st + V(0, 0.5f * h, 0), 0.02f * w, CyHullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            var tc = V(sd * 0.36f * w, y0, 0.06f * l);
            b.Cylinder(tc, Axis.Y, 0.24f * h, 0.07f * w, 0.06f * w, 6, CyHullDark, CyTeamDim);
            b.Box(tc + V(0, 0.12f * h, 0.068f * w), V(0.03f * w, 0.1f * h, 0.01f * w), CyAmber);
            Limb(b, tc + V(0, 0.22f * h, 0), V(sd * 0.12f * w, hy + 0.04f * h, 0.03f * l), 0.018f * w, CyHull);
        }
    }

    /// <summary>
    /// Cybran mass fabricator: a cage of raked pillars (4 at T2, 6 at T3) leaning in over a green-glowing core sphere
    /// (two stacked cores at T3), held by a top collar with red lights; team band on the plinth.
    /// </summary>
    private static void CyMassFab(MeshBuilder b, float w, float h, float l, int tier)
    {
        float y0 = 0.06f * h;
        CyFoundation(b, w, l, y0, CyHullDark, 0.28f);
        b.Cylinder(V(0, y0, 0), Axis.Y, 0.08f * h, 0.36f * w, 0.32f * w, 6, CyHull, CyPad, null, 0);
        b.Cylinder(V(0, y0 + 0.03f * h, 0), Axis.Y, 0.025f * h, 0.365f * w, 0.36f * w, 6, CyTeam, null, null, 0);
        float by = y0 + 0.08f * h, topY = 0.86f * h;
        int n = tier >= 3 ? 6 : 4;
        float cr = tier >= 3 ? 0.17f : 0.2f;
        if (tier >= 3)
        {
            b.Sphere(V(0, by + 0.2f * h, 0), V(cr * w, 0.16f * h, cr * w), 4, 8, CyGreen);
            b.Sphere(V(0, by + 0.52f * h, 0), V(0.8f * cr * w, 0.13f * h, 0.8f * cr * w), 4, 8, CyGreen);
            b.Cylinder(V(0, by + 0.33f * h, 0), Axis.Y, 0.05f * h, 0.16f * w, 0.16f * w, 6, CyHullDark, CyTeamDim);
        }
        else
            b.Sphere(V(0, by + 0.32f * h, 0), V(cr * w, 0.24f * h, cr * w), 4, 8, CyGreen);
        for (int i = 0; i < n; i++)
        {
            float a = i * MathF.Tau / n + MathF.PI / n;
            var p0 = CyRot(V(0, by, 0.3f * w), a);
            var p1 = CyRot(V(0, 0.5f * h, 0.38f * w), a);
            var p2 = CyRot(V(0, topY, 0.17f * w), a);
            Limb(b, p0, p1, 0.03f * w, CyHull);
            Limb(b, p1, p2, 0.025f * w, CyHullDark);
            b.Box(p1, V(0.06f * w, 0.06f * w, 0.06f * w), CyTeamDim);
            b.Box(CyRot(V(0, 0.32f * h, 0.36f * w), a), V(0.03f * w, 0.1f * h, 0.012f * w), CyRedDim, null, RotY(a));
        }
        b.Cylinder(V(0, topY - 0.03f * h, 0), Axis.Y, 0.06f * h, 0.2f * w, 0.14f * w, 6, CyHull, CyHullDark);
        b.Cylinder(V(0, topY - 0.06f * h, 0), Axis.Y, 0.03f * h, 0.14f * w, 0.17f * w, 6, CyRed);
        Spike(b, V(0, topY + 0.03f * h, 0), V(0, topY + (tier >= 3 ? 0.3f : 0.18f) * h, 0), 0.05f * w, CyHullDark, 6);
    }

    /// <summary>
    /// Cybran storage: mass = a low octagonal tank with green glow slits and a team band; energy = a taller faceted hex
    /// capsule with an amber glow band, three raked fins with red tips and a top spike.
    /// </summary>
    private static void CyStorage(MeshBuilder b, float w, float h, float l, bool energy)
    {
        float y0 = 0.08f * h;
        CyFoundation(b, w, l, y0, CyHullDark, 0.3f);
        if (!energy)
        {
            b.Frustum(V(0, y0, 0), 0.8f * w, 0.8f * l, 0.72f * w, 0.72f * l, 0.5f * h, CyHull, null, null, 0.3f);
            b.Frustum(V(0, y0 + 0.5f * h, 0), 0.72f * w, 0.72f * l, 0.46f * w, 0.46f * l, 0.14f * h, CyHullLight, CyHullDark, null, 0.3f);
            b.Frustum(V(0, y0 + 0.08f * h, 0), 0.805f * w, 0.805f * l, 0.8f * w, 0.8f * l, 0.08f * h, CyTeam, null, null, 0.3f);
            for (int i = 0; i < 4; i++)
                b.Box(V(0, y0 + 0.32f * h, 0.385f * w), V(0.2f * w, 0.12f * h, 0.01f * w), CyGreen, null, RotY(i * MathF.PI / 2));
            b.Cylinder(V(0, y0 + 0.64f * h, 0), Axis.Y, 0.06f * h, 0.12f * w, 0.1f * w, 6, CyHullDark, CyGreen);
            return;
        }
        b.Cylinder(V(0, y0, 0), Axis.Y, 0.15f * h, 0.3f * w, 0.34f * w, 6, CyHullDark);
        b.Cylinder(V(0, y0 + 0.15f * h, 0), Axis.Y, 0.45f * h, 0.34f * w, 0.3f * w, 6, CyHull);
        b.Cylinder(V(0, y0 + 0.6f * h, 0), Axis.Y, 0.15f * h, 0.3f * w, 0.14f * w, 6, CyHullLight, CyHullDark);
        b.Cylinder(V(0, y0 + 0.3f * h, 0), Axis.Y, 0.1f * h, 0.335f * w, 0.33f * w, 6, CyAmber);
        b.Cylinder(V(0, y0 + 0.16f * h, 0), Axis.Y, 0.05f * h, 0.345f * w, 0.34f * w, 6, CyTeam);
        for (int i = 0; i < 3; i++)
            CyClawFin(b, i * MathF.Tau / 3 + MathF.PI / 6, y0, 0.25f * w, 0.47f * w, 0.3f * w, y0 + 0.55f * h, 0.03f * w, CyHullDark);
        Spike(b, V(0, y0 + 0.74f * h, 0), V(0, 0.98f * h, 0), 0.05f * w, CyHullDark);
    }

    // ================================================================ intel

    /// <summary>
    /// Cybran radar (T1/T2): a tripod of raked claw legs around a faceted tapering mast with red slits and a team band
    /// (T2: taller, a second collar and raked spikes). Turret = the spinning asymmetric array at the mast top: a raked
    /// blade panel with a red emitter strip and a counterweight (T2: two back-to-back panels).
    /// </summary>
    private static MeshBuilder CyRadar(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, int tier)
    {
        float y0 = 0.03f * h, mastTop = 0.7f * h;
        CyFoundation(b, w, l, y0, CyHullDark, 0.3f);
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF.Tau / 3 + MathF.PI / 3;
            CyLeg(b, CyRot(V(0, 0.3f * h, 0.08f * w), a), CyRot(V(0, 0.22f * h, 0.36f * w), a), CyRot(V(0, y0, 0.47f * w), a),
                  0.05f * w, CyHull, CyHullDark, CyTeam);
        }
        b.Frustum(V(0, y0, 0), 0.3f * w, 0.3f * l, 0.12f * w, 0.12f * l, mastTop - y0, CyHull, CyHullDark, null, 0.3f);
        b.Frustum(V(0, 0.26f * h, 0), 0.26f * w, 0.26f * l, 0.25f * w, 0.25f * l, 0.05f * h, CyTeam, null, null, 0.3f);
        for (int k = 0; k < 3; k++)
        {
            float y = (0.36f + 0.1f * k) * h, f = (y - y0) / (mastTop - y0);
            b.Box(V(0, y, (0.15f - 0.09f * f) * w + 0.004f * w), V(0.05f * w, 0.04f * h, 0.008f * w), CyRed);
        }
        if (tier >= 2)
        {
            b.Frustum(V(0, 0.5f * h, 0), 0.2f * w, 0.2f * l, 0.17f * w, 0.17f * l, 0.04f * h, CyHullDark, CyTeamDim, null, 0.3f);
            for (int i = 0; i < 3; i++)
            {
                float a = i * MathF.Tau / 3;
                Spike(b, CyRot(V(0, 0.52f * h, 0.08f * w), a), CyRot(V(0, 0.64f * h, 0.3f * w), a), 0.025f * w, CyHullDark);
            }
        }
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.02f * h, 0), Axis.Y, 0.05f * h, 0.1f * w, 0.08f * w, 6, CyHullDark, CyTeam);
        float ph = (tier >= 2 ? 0.26f : 0.22f) * h;
        foreach (int sd in tier >= 2 ? (ReadOnlySpan<int>)[-1, 1] : (ReadOnlySpan<int>)[1])
        {
            // Panel profile (z, y): a raked trapezoid, slab across x.
            float x0 = sd * 0.06f * w;
            Vector2[] panel = [V2(-0.36f * w, 0.02f * h), V2(0.36f * w, 0.02f * h), V2(0.26f * w, ph), V2(-0.3f * w, 0.85f * ph)];
            t.SideSlab(panel, MathF.Min(x0, x0 + sd * 0.04f * w), MathF.Max(x0, x0 + sd * 0.04f * w), CyHull, null);
            t.Box(V(x0 + sd * 0.042f * w, 0.55f * ph, 0), V(0.006f * w, 0.03f * h, 0.5f * w), CyRed);
            t.Box(V(x0 + sd * 0.042f * w, 0.3f * ph, 0), V(0.006f * w, 0.012f * h, 0.56f * w), CyTeamDim);
        }
        if (tier < 2)
            t.BevelBox(V(-0.1f * w, 0.05f * h, 0), V(0.1f * w, 0.08f * h, 0.14f * w), 0.015f * w, CyHullDark);
        Spike(t, V(0, 0.03f * h, 0), V(0, ph + 0.08f * h, 0), 0.03f * w, CyHullDark);
        pivots.Add(V(0, mastTop, 0));
        return t;
    }

    /// <summary>
    /// Cybran omni sensor: three tall raked claw buttresses around a faceted spire with red slits and team collars.
    /// Turret = the spinning crown: a segmented ring with three claws arching up to a red sensor eye on a spike.
    /// </summary>
    private static MeshBuilder CyOmni(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        float y0 = 0.03f * h, top = 0.62f * h;
        CyFoundation(b, w, l, y0, CyHullDark, 0.3f);
        b.Cylinder(V(0, y0, 0), Axis.Y, 0.05f * h, 0.38f * w, 0.34f * w, 6, CyHull, CyPad, null, 0);
        for (int i = 0; i < 3; i++)
            CyClawFin(b, i * MathF.Tau / 3 + MathF.PI / 3, y0, 0.12f * w, 0.48f * w, 0.16f * w, 0.48f * h, 0.035f * w, CyHullDark);
        b.Cylinder(V(0, y0, 0), Axis.Y, top - y0, 0.2f * w, 0.09f * w, 6, CyHull, CyHullDark);
        foreach (float y in (ReadOnlySpan<float>)[0.12f, 0.4f])
        {
            float r = 0.2f * w - (0.11f * w) * (y * h - y0) / (top - y0);
            b.Cylinder(V(0, y * h, 0), Axis.Y, 0.03f * h, 1.06f * r, 1.03f * r, 6, CyTeam);
        }
        for (int i = 0; i < 3; i++)
            b.Box(CyRot(V(0, 0.27f * h, 0.15f * w), i * MathF.Tau / 3), V(0.05f * w, 0.1f * h, 0.008f * w), CyRed, null, RotY(i * MathF.Tau / 3));
        var t = new MeshBuilder();
        Ring(t, V(0, 0, 0), 0.16f * w, 0.025f * w, 0.025f * h, 9, CyHullDark, CyTeam);
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF.Tau / 3;
            var p0 = CyRot(V(0, 0.01f * h, 0.16f * w), a);
            var p1 = CyRot(V(0, 0.14f * h, 0.24f * w), a);
            var p2 = CyRot(V(0, 0.26f * h, 0.07f * w), a);
            Limb(t, p0, p1, 0.022f * w, CyHull);
            Spike(t, p1, p2, 0.02f * w, CyHullDark);
            t.Box(p1, V(0.045f * w, 0.045f * w, 0.045f * w), CyRed);
        }
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.2f * h, 0.05f * w, 0.025f * w, 6, CyHullDark);
        t.Sphere(V(0, 0.24f * h, 0), V(0.07f * w, 0.05f * h, 0.07f * w), 3, 6, CyRed);
        Spike(t, V(0, 0.27f * h, 0), V(0, 0.36f * h, 0), 0.02f * w, CyHullDark);
        pivots.Add(V(0, top, 0));
        return t;
    }

    /// <summary>
    /// Cybran stealth field generator: hex plinth, a faceted core column with violet glow slits and a team collar, three
    /// raked spikes. Turret = three swept vanes with violet emitter tips around a hub, topped by a violet orb.
    /// </summary>
    private static MeshBuilder CyStealthField(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        float y0 = 0.04f * h, top = 0.56f * h;
        CyFoundation(b, w, l, y0, CyHullDark, 0.3f);
        b.Cylinder(V(0, y0, 0), Axis.Y, 0.08f * h, 0.4f * w, 0.36f * w, 6, CyHull, CyPad, null, 0);
        b.Cylinder(V(0, y0 + 0.08f * h, 0), Axis.Y, top - y0 - 0.08f * h, 0.22f * w, 0.13f * w, 6, CyHull, CyHullDark);
        b.Cylinder(V(0, y0 + 0.12f * h, 0), Axis.Y, 0.04f * h, 0.215f * w, 0.21f * w, 6, CyTeam);
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF.Tau / 3;
            b.Box(CyRot(V(0, 0.33f * h, 0.17f * w), a), V(0.06f * w, 0.14f * h, 0.008f * w), CyViolet, null, RotY(a));
            Spike(b, CyRot(V(0, y0 + 0.06f * h, 0.3f * w), a + MathF.PI / 3), CyRot(V(0, 0.42f * h, 0.42f * w), a + MathF.PI / 3), 0.035f * w, CyHullDark);
        }
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.02f * h, 0), Axis.Y, 0.08f * h, 0.12f * w, 0.09f * w, 6, CyHullDark, CyTeam);
        for (int i = 0; i < 3; i++)
        {
            var r = RotY(i * MathF.Tau / 3);
            Vector2[] vane = [V2(0.08f * w, 0), V2(0.4f * w, 0.1f * h), V2(0.42f * w, 0.18f * h), V2(0.1f * w, 0.1f * h)];
            t.SideSlab(vane, -0.02f * w, 0.02f * w, CyHull, null, Matrix4x4.CreateRotationY(0.5f) * r);
            t.Box(V(0, 0.15f * h, 0.41f * w), V(0.05f * w, 0.05f * w, 0.05f * w), CyViolet, null, Matrix4x4.CreateRotationY(0.5f) * r);
        }
        Spike(t, V(0, 0.06f * h, 0), V(0, 0.3f * h, 0), 0.04f * w, CyHullDark);
        t.Sphere(V(0, 0.22f * h, 0), V(0.08f * w, 0.06f * h, 0.08f * w), 3, 6, CyViolet);
        pivots.Add(V(0, top, 0));
        return t;
    }
}
