using System.Numerics;
using Rts.Sim.Data;

namespace Rts.Client.Rendering;

/// <summary>
/// Cybran experimentals (shapes "cy_monkeylord", "cy_soul_ripper", "cy_scathis", dispatched by <c>CybranShape</c> in
/// UnitModels.Cybran.cs): the Monkeylord spider walker, the Soul Ripper heavy gunship and the Scathis rapid-fire
/// artillery crawler. Same palette and conventions as the other Cybran models: dark faceted hulls, team plates and
/// joints, red glowing seams/lenses; one shared turret mesh per model (origin = pivot, barrels along +Z).
/// Parts that reach outside Visual.Size (legs, the Scathis barrels) are covered by <c>CybranCullRadius</c>.
/// </summary>
public static partial class UnitModels
{
    // ================================================================ Monkeylord

    /// <summary>
    /// One Monkeylord leg from the hip, along the ground direction <paramref name="dir"/> (unit, xz): an armored upper
    /// segment arching to a high knee, a long lower segment down to the ankle and a black claw spike to the foot.
    /// Distances are fractions of w (x) and l (z); heights of h.
    /// </summary>
    private static void CyMonkeyLeg(MeshBuilder b, Vector3 hip, Vector2 dir, float w, float h, float l, float r)
    {
        Vector3 At(float reach, float y) => V(hip.X + dir.X * reach * w, y * h, hip.Z + dir.Y * reach * l);
        var knee = At(0.25f, 1.0f);
        var ankle = At(0.45f, 0.3f);
        var foot = At(0.52f, 0);
        b.BevelBox(hip, V(r * 2.6f, r * 2.4f, r * 2.6f), r * 0.5f, CyHullDark);
        Limb(b, hip, knee, r, CyHull);
        // Armor plate on top of the upper segment and a red seam along it.
        Limb(b, Vector3.Lerp(hip, knee, 0.2f) + V(0, r * 0.9f, 0), Vector3.Lerp(hip, knee, 0.8f) + V(0, r * 0.9f, 0), r * 0.55f, CyHullLight);
        Limb(b, Vector3.Lerp(hip, knee, 0.3f) + V(0, r * 1.25f, 0), Vector3.Lerp(hip, knee, 0.7f) + V(0, r * 1.25f, 0), r * 0.18f, CyRedDim);
        Limb(b, knee, ankle, r * 0.75f, CyHullDark);
        Spike(b, ankle, foot, r * 0.6f, CyBlack);
        b.BevelBox(knee, V(r * 2.4f, r * 2.2f, r * 2.4f), r * 0.4f, CyTeam);
        Spike(b, knee + V(0, r, 0), At(0.3f, 1.16f), r * 0.45f, CyHullLight);
        b.Box(Vector3.Lerp(knee, ankle, 0.08f) + V(0, -r * 0.9f, 0), V(r * 0.9f, r * 0.5f, r * 0.9f), CyRed);
        b.Box(ankle, V(r * 1.7f, r * 1.4f, r * 1.7f), CyHullDark);
    }

    /// <summary>
    /// Monkeylord: giant spider. Body: eight long jointed legs arching high over a low faceted thorax (team plate, back
    /// spikes), a raised hexagonal abdomen with red flank slits and two nanodart AA racks. Turret = the head: faceted
    /// wedge with spider eyes and a red visor, the heavy microwave laser under the chin (red lens, three focusing prongs),
    /// an electron bolter on each cheek, mandible spikes and a raked antenna.
    /// </summary>
    private static MeshBuilder CyMonkeylord(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        // Legs: four per side, hips along the thorax flank, fanned from forward to backward.
        ReadOnlySpan<float> hipZ = [0.17f, 0.07f, -0.03f, -0.13f];
        ReadOnlySpan<float> phi = [0.95f, 0.32f, -0.32f, -0.95f];
        float r = 0.034f * w;
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            for (int i = 0; i < 4; i++)
                CyMonkeyLeg(b, V(sd * 0.19f * w, 0.42f * h, hipZ[i] * l), new Vector2(sd * MathF.Cos(phi[i]), MathF.Sin(phi[i])), w, h, l, r);

        // Thorax: low faceted carapace along Z.
        Vector2[] cara = [V2(-0.5f, 0.35f), V2(-0.32f, 0), V2(0.32f, 0), V2(0.5f, 0.35f), V2(0.3f, 1), V2(-0.3f, 1)];
        b.Loft(cara, [
            new Station(-0.16f * l, 0.38f * w, 0.24f * h, 0, 0.32f * h), new Station(-0.02f * l, 0.5f * w, 0.32f * h, 0, 0.28f * h),
            new Station(0.14f * l, 0.48f * w, 0.3f * h, 0, 0.29f * h), new Station(0.27f * l, 0.3f * w, 0.18f * h, 0, 0.32f * h)], Axis.Z, CyHull, CyHullDark);
        b.Box(V(0, 0.28f * h, 0), V(0.3f * w, 0.03f * h, 0.3f * l), CyBlack);
        b.Box(V(0, 0.603f * h, 0.02f * l), V(0.15f * w, 0.012f * h, 0.2f * l), CyTeam);
        b.Box(V(0, 0.612f * h, 0.0f), V(0.02f * w, 0.012f * h, 0.26f * l), CyRedDim);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Box(V(sd * 0.252f * w, 0.39f * h, 0.03f * l), V(0.01f * w, 0.025f * h, 0.24f * l), CyRed);
            b.Box(V(sd * 0.2f * w, 0.5f * h, 0.05f * l), V(0.08f * w, 0.008f * h, 0.16f * l), CyTeamDim, null, CyCant(sd * 0.2f * w, 0.5f * h, sd * 0.55f));
            Spike(b, V(sd * 0.09f * w, 0.58f * h, -0.05f * l), V(sd * 0.15f * w, 0.8f * h, -0.16f * l), 0.024f * w, CyHullLight);
            Spike(b, V(sd * 0.05f * w, 0.56f * h, -0.12f * l), V(sd * 0.08f * w, 0.7f * h, -0.22f * l), 0.016f * w, CyHullDark);
        }

        // Abdomen: raised faceted hexagonal pod behind the thorax.
        b.Loft(MeshBuilder.NGon(6, 1, 0), [
            new Station(-0.1f * l, 0.15f * w, 0.11f * h, 0, 0.5f * h), new Station(-0.26f * l, 0.22f * w, 0.16f * h, 0, 0.56f * h),
            new Station(-0.42f * l, 0.17f * w, 0.12f * h, 0, 0.58f * h), new Station(-0.5f * l, 0.05f * w, 0.04f * h, 0, 0.56f * h)], Axis.Z, CyHullDark, CyBlack);
        b.Box(V(0, 0.703f * h, -0.28f * l), V(0.1f * w, 0.01f * h, 0.14f * l), CyTeam);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Box(V(sd * 0.224f * w, 0.56f * h, -0.27f * l), V(0.012f * w, 0.035f * h, 0.13f * l), CyRed);
            // Nanodart AA rack on the abdomen shoulder: dark block with three upward tubes.
            var aa = V(sd * 0.14f * w, 0.66f * h, -0.19f * l);
            b.BevelBox(aa, V(0.07f * w, 0.06f * h, 0.1f * l), 0.01f * w, CyHull, CyHullDark);
            for (int k = -1; k <= 1; k++)
                b.Cylinder(aa + V(0, 0.03f * h, k * 0.03f * l), Axis.Y, 0.05f * h, 0.012f * w, 0.012f * w, 5, CyBlack, CyRedDim);
        }

        // Turret: the head.
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.02f * h, 0), Axis.Y, 0.05f * h, 0.12f * w, 0.12f * w, 6, CyBlack);
        t.Loft(MeshBuilder.Chamfered(0.3f * w, 0.18f * h, 0.035f * w), [
            new Station(-0.1f * l, 0.85f, 0.85f, 0, 0.1f * h), new Station(0.03f * l, 1, 1, 0, 0.1f * h),
            new Station(0.14f * l, 0.7f, 0.6f, 0, 0.07f * h)], Axis.Z, CyHull, CyHullLight);
        t.Box(V(0, 0.192f * h, -0.03f * l), V(0.13f * w, 0.008f * h, 0.1f * l), CyTeam);
        t.Box(V(0, 0.08f * h, 0.141f * l), V(0.12f * w, 0.016f * h, 0.006f * l), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            // Spider eyes: two red lenses per side on the brow.
            t.Box(V(sd * 0.035f * w, 0.112f * h, 0.138f * l), V(0.022f * w, 0.02f * h, 0.008f * l), CyRed);
            t.Box(V(sd * 0.08f * w, 0.1f * h, 0.13f * l), V(0.016f * w, 0.016f * h, 0.008f * l), CyRedDim);
            // Electron bolter on each cheek.
            t.BevelBox(V(sd * 0.17f * w, 0.06f * h, 0.02f * l), V(0.055f * w, 0.07f * h, 0.12f * l), 0.01f * w, CyHullDark, CyHull);
            t.Cylinder(V(sd * 0.17f * w, 0.06f * h, 0.08f * l), Axis.Z, 0.1f * l, 0.014f * w, 0.012f * w, 5, CyBlack, CyRedDim);
            t.Box(V(sd * 0.198f * w, 0.06f * h, 0.02f * l), V(0.004f * w, 0.02f * h, 0.08f * l), CyTeam);
            // Mandibles.
            Spike(t, V(sd * 0.09f * w, -0.01f * h, 0.12f * l), V(sd * 0.05f * w, -0.1f * h, 0.25f * l), 0.02f * w, CyHullLight);
        }
        // Heavy microwave laser under the chin: hex housing, emitter collar, red lens, three focusing prongs.
        t.Cylinder(V(0, 0.0f, 0.06f * l), Axis.Z, 0.14f * l, 0.075f * w, 0.065f * w, 6, CyHullDark);
        t.Cylinder(V(0, 0.0f, 0.2f * l), Axis.Z, 0.035f * l, 0.085f * w, 0.085f * w, 6, CyBlack, CyHullDark);
        t.Cylinder(V(0, 0.0f, 0.235f * l), Axis.Z, 0.008f * l, 0.055f * w, 0.055f * w, 6, CyRed);
        for (int k = 0; k < 3; k++)
        {
            float a = MathF.PI / 2 + k * MathF.Tau / 3, c = MathF.Cos(a), s = MathF.Sin(a);
            Spike(t, V(0.07f * w * c, 0.07f * w * s, 0.22f * l), V(0.035f * w * c, 0.035f * w * s, 0.31f * l), 0.012f * w, CyHullLight);
        }
        Spike(t, V(-0.06f * w, 0.18f * h, -0.06f * l), V(-0.1f * w, 0.4f * h, -0.17f * l), 0.012f * w, CyHullDark);
        t.Box(V(-0.095f * w, 0.37f * h, -0.16f * l), V(0.014f * w, 0.014f * w, 0.014f * w), CyRed);
        pivots.Add(V(0, 0.36f * h, 0.24f * l));
        return t;
    }

    // ================================================================ Soul Ripper

    /// <summary>
    /// Soul Ripper: huge heavy gunship. Body: broad faceted wedge hull with a dorsal spine, red visor and nose prongs,
    /// angular wings carrying two big lift pods at the tips and two smaller forward pods (team rings, red thrust discs
    /// underneath), iridium rocket packs under the wings, nanodart AA racks on the spine, canted tail fins and red stern
    /// glows. Turret = an electron bolter pod (twin barrels, rocket box) hanging under the belly at five pivots.
    /// </summary>
    private static MeshBuilder CySoulRipper(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        CyWedge(b, 0, 0.3f * h, 0.36f * w, 0.45f * h, -0.44f * l, 0.46f * l, CyHull, CyHullDark, 0.05f, 0.42f);
        CyFuselage(b, 0, [V4(-0.5f * l, 0.06f * w, 0.12f * h, 0.8f * h), V4(-0.3f * l, 0.14f * w, 0.22f * h, 0.8f * h),
                          V4(0.1f * l, 0.12f * w, 0.2f * h, 0.78f * h), V4(0.26f * l, 0.04f * w, 0.08f * h, 0.74f * h)], CyHullDark, CyHull);
        b.Box(V(0, 0.905f * h, -0.12f * l), V(0.05f * w, 0.012f * h, 0.24f * l), CyTeam);
        b.Box(V(0.02f * w, 0.44f * h, 0.462f * l), V(0.1f * w, 0.04f * h, 0.008f * l), CyRed);
        b.Box(V(0, 0.295f * h, 0.02f * l), V(0.12f * w, 0.012f * h, 0.42f * l), CyRedDim);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            float sx = sd * 0.183f * w;
            b.Box(V(sx, 0.36f * h, 0.0f), V(0.008f * w, 0.05f * h, 0.5f * l), CyTeam);
            b.Box(V(sx, 0.43f * h, 0.06f * l), V(0.008f * w, 0.022f * h, 0.36f * l), CyRed);
            Spike(b, V(sd * 0.1f * w, 0.36f * h, 0.38f * l), V(sd * 0.06f * w, 0.3f * h, 0.55f * l), 0.025f * w, CyHullLight);
            b.Box(V(sd * 0.07f * w, 0.45f * h, -0.442f * l), V(0.08f * w, 0.09f * h, 0.006f * l), CyRed);

            // Wings with a team stripe; big lift pods at the tips.
            CyWingPanel(b, sd, 0.15f * w, 0.18f * l, -0.22f * l, 0.44f * w, -0.02f * l, -0.24f * l, 0.5f * h, 0.58f * h, CyHull);
            CyWingPanel(b, sd, 0.2f * w, 0.06f * l, -0.02f * l, 0.4f * w, -0.08f * l, -0.12f * l, 0.58f * h, 0.59f * h, CyTeam);
            float px = sd * 0.44f * w, pz = -0.12f * l;
            b.Cylinder(V(px, 0.22f * h, pz), Axis.Y, 0.5f * h, 0.11f * w, 0.09f * w, 6, CyHull, CyBlack);
            b.Cylinder(V(px, 0.64f * h, pz), Axis.Y, 0.08f * h, 0.1f * w, 0.095f * w, 6, CyTeam);
            b.Cylinder(V(px, 0.21f * h, pz), Axis.Y, 0.012f * h, 0.075f * w, 0.075f * w, 6, CyRed);
            b.Box(V(px + sd * 0.1f * w, 0.42f * h, pz), V(0.012f * w, 0.05f * h, 0.12f * l), CyRedDim);
            Spike(b, V(px, 0.7f * h, pz - 0.04f * l), V(px + sd * 0.03f * w, 1.0f * h, pz - 0.14f * l), 0.02f * w, CyHullDark);
            // Forward lift pods on stub wings.
            float fx = sd * 0.27f * w, fz = 0.25f * l;
            b.Box(V(sd * 0.2f * w, 0.5f * h, fz), V(0.14f * w, 0.07f * h, 0.08f * l), CyHullDark);
            b.Cylinder(V(fx, 0.26f * h, fz), Axis.Y, 0.36f * h, 0.075f * w, 0.065f * w, 6, CyHullDark, CyBlack);
            b.Cylinder(V(fx, 0.55f * h, fz), Axis.Y, 0.06f * h, 0.07f * w, 0.066f * w, 6, CyTeam);
            b.Cylinder(V(fx, 0.25f * h, fz), Axis.Y, 0.012f * h, 0.05f * w, 0.05f * w, 6, CyRed);
            // Iridium rocket packs hanging under the wings (red tube tips).
            foreach (float z in (ReadOnlySpan<float>)[0.02f, -0.12f])
            {
                var rp = V(sd * 0.3f * w, 0.44f * h, z * l);
                b.BevelBox(rp, V(0.06f * w, 0.12f * h, 0.1f * l), 0.01f * w, CyHullDark, CyHull);
                for (int k = 0; k < 4; k++)
                    b.Box(rp + V(((k & 1) - 0.5f) * 0.026f * w, ((k >> 1) - 0.5f) * 0.05f * h, 0.051f * l), V(0.016f * w, 0.03f * h, 0.004f * l), CyRed);
            }
            // Nanodart AA racks on the spine.
            var aa = V(sd * 0.1f * w, 0.8f * h, -0.08f * l);
            b.BevelBox(aa, V(0.05f * w, 0.08f * h, 0.08f * l), 0.008f * w, CyHull, CyHullDark);
            b.Box(aa + V(0, 0.01f * h, 0.041f * l), V(0.03f * w, 0.03f * h, 0.004f * l), CyRed);
            // Canted tail fins with team tips.
            float nx = sd * 0.08f * w;
            Fin(b, nx, 0.02f * w, 0.74f * h, -0.5f * l, -0.34f * l, -0.5f * l, 1.15f * h, CyHullDark, CyCant(nx, 0.74f * h, sd * 0.35f));
            Fin(b, nx, 0.024f * w, 1.0f * h, -0.5f * l, -0.47f * l, -0.5f * l, 1.15f * h, CyTeam, CyCant(nx, 0.74f * h, sd * 0.35f));
        }

        // Turret: electron bolter pod hanging under the belly (origin = mount on the belly).
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.06f * h, 0), Axis.Y, 0.06f * h, 0.045f * w, 0.045f * w, 6, CyBlack);
        t.Loft(MeshBuilder.Chamfered(0.09f * w, 0.1f * h, 0.015f * w), [
            new Station(-0.05f * l, 0.9f, 0.9f, 0, -0.1f * h), new Station(0.03f * l, 1, 1, 0, -0.1f * h),
            new Station(0.07f * l, 0.7f, 0.7f, 0, -0.1f * h)], Axis.Z, CyHullDark, CyHull);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Cylinder(V(sd * 0.022f * w, -0.11f * h, 0.05f * l), Axis.Z, 0.08f * l, 0.011f * w, 0.009f * w, 5, CyBlack);
            t.Cylinder(V(sd * 0.022f * w, -0.11f * h, 0.12f * l), Axis.Z, 0.016f * l, 0.014f * w, 0.014f * w, 5, CyHullDark, CyRed);
            t.Box(V(sd * 0.046f * w, -0.1f * h, -0.005f * l), V(0.004f * w, 0.03f * h, 0.06f * l), CyTeam);
        }
        t.Box(V(0, -0.17f * h, 0), V(0.05f * w, 0.04f * h, 0.07f * l), CyHull);
        t.Box(V(0, -0.17f * h, 0.036f * l), V(0.04f * w, 0.025f * h, 0.004f * l), CyRed);
        pivots.Add(V(0, 0.3f * h, 0.33f * l));
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            pivots.Add(V(sd * 0.12f * w, 0.3f * h, 0.1f * l));
            pivots.Add(V(sd * 0.09f * w, 0.3f * h, -0.2f * l));
        }
        return t;
    }

    // ================================================================ Scathis

    /// <summary>
    /// Scathis: mobile rapid-fire artillery. Body: six heavy spider legs under a long faceted chassis with armored side
    /// skirts, a red prow slit, a rear power core with red vents and radiator fins, and the gun race ring. Turret = a
    /// faceted mount with team-plated trunnion cheeks and a rear counterweight carrying the huge rotary gun pitched up:
    /// an octagonal breech drum with red coils, six long barrels around a spindle held by two collars, red muzzles.
    /// </summary>
    private static MeshBuilder CyScathis(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        CyLegSet(b, 3, w, h, l, 0.3f, 0.5f, 0.8f, 0.26f, 0.48f, 0.64f, 0.05f, CyHull, CyHullDark, CyTeam);
        CyWedge(b, 0, 0.2f * h, 0.62f * w, 0.24f * h, -0.46f * l, 0.42f * l, CyHull, CyHullDark, 0, 0.55f);
        b.Box(V(0, 0.27f * h, 0.422f * l), V(0.2f * w, 0.025f * h, 0.006f * l), CyRed);
        b.Box(V(0, 0.443f * h, 0.27f * l), V(0.13f * w, 0.008f * h, 0.16f * l), CyTeam);
        b.Cylinder(V(0, 0.425f * h, -0.04f * l), Axis.Y, 0.025f * h, 0.28f * w, 0.27f * w, 8, CyBlack);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.BevelBox(V(sd * 0.3f * w, 0.33f * h, -0.03f * l), V(0.08f * w, 0.14f * h, 0.6f * l), 0.02f * w, CyHullDark, CyHull);
            b.Box(V(sd * 0.341f * w, 0.36f * h, -0.03f * l), V(0.004f * w, 0.04f * h, 0.44f * l), CyTeam);
            b.Box(V(sd * 0.341f * w, 0.3f * h, 0.0f), V(0.004f * w, 0.012f * h, 0.36f * l), CyRedDim);
            b.Box(V(sd * 0.08f * w, 0.48f * h, -0.442f * l), V(0.07f * w, 0.06f * h, 0.006f * l), CyRed);
            Fin(b, sd * 0.1f * w, 0.02f * w, 0.54f * h, -0.44f * l, -0.3f * l, -0.46f * l, 0.72f * h, CyHullLight);
        }
        b.BevelBox(V(0, 0.48f * h, -0.36f * l), V(0.36f * w, 0.14f * h, 0.16f * l), 0.03f * w, CyHullDark, CyHull);

        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.06f * h, 0.26f * w, 0.24f * w, 8, CyHullDark);
        Vector2[] prof = [V2(-0.5f, 0), V2(0.5f, 0), V2(0.5f, 0.55f), V2(0.25f, 1f), V2(-0.25f, 1f), V2(-0.5f, 0.55f)];
        t.Loft(prof, [new Station(-0.3f * l, 0.44f * w, 0.2f * h, 0, 0.05f * h), new Station(0.06f * l, 0.48f * w, 0.22f * h, 0, 0.05f * h),
                      new Station(0.2f * l, 0.34f * w, 0.14f * h, 0, 0.06f * h)], Axis.Z, CyHull, CyHullDark);
        t.BevelBox(V(0, 0.15f * h, -0.36f * l), V(0.3f * w, 0.12f * h, 0.1f * l), 0.02f * w, CyHullDark, CyHull);
        t.Box(V(0, 0.15f * h, -0.412f * l), V(0.18f * w, 0.04f * h, 0.006f * l), CyRed);
        Spike(t, V(0.15f * w, 0.26f * h, -0.2f * l), V(0.2f * w, 0.5f * h, -0.3f * l), 0.012f * w, CyHullDark);
        t.Box(V(0.195f * w, 0.47f * h, -0.29f * l), V(0.014f * w, 0.014f * w, 0.014f * w), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.BevelBox(V(sd * 0.2f * w, 0.3f * h, 0.04f * l), V(0.07f * w, 0.16f * h, 0.18f * l), 0.015f * w, CyHullDark, CyHull);
            t.Box(V(sd * 0.237f * w, 0.3f * h, 0.04f * l), V(0.006f * w, 0.08f * h, 0.12f * l), CyTeam);
        }
        // Rotary gun, pitched up about the trunnions.
        var pitch = PitchUp(0.3f) * T(0, 0.33f * h, 0.04f * l);
        t.Cylinder(V(0, 0, -0.24f * l), Axis.Z, 0.32f * l, 0.14f * w, 0.14f * w, 8, CyHullDark, CyHull, pitch);
        t.Cylinder(V(0, 0, -0.3f * l), Axis.Z, 0.06f * l, 0.1f * w, 0.13f * w, 8, CyBlack, CyRedDim, pitch);
        t.Cylinder(V(0, 0, 0.06f * l), Axis.Z, 0.7f * l, 0.035f * w, 0.035f * w, 6, CyHull, null, pitch);
        t.Cylinder(V(0, 0, 0.3f * l), Axis.Z, 0.04f * l, 0.13f * w, 0.13f * w, 6, CyHull, CyHullDark, pitch);
        t.Cylinder(V(0, 0, 0.56f * l), Axis.Z, 0.03f * l, 0.125f * w, 0.125f * w, 6, CyHullDark, null, pitch);
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.Tau / 6, x = MathF.Cos(a) * 0.085f * w, y = MathF.Sin(a) * 0.085f * w;
            t.Cylinder(V(x, y, 0.08f * l), Axis.Z, 0.66f * l, 0.03f * w, 0.026f * w, 6, CyBlack, null, pitch);
            t.Cylinder(V(x, y, 0.72f * l), Axis.Z, 0.05f * l, 0.034f * w, 0.034f * w, 6, CyHullDark, CyRed, pitch);
        }
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Box(V(sd * 0.142f * w, 0, -0.1f * l), V(0.01f * w, 0.04f * w, 0.22f * l), CyRed, null, pitch);
        t.Box(V(0, 0.142f * w, -0.1f * l), V(0.05f * w, 0.01f * w, 0.24f * l), CyTeam, null, pitch);
        pivots.Add(V(0, 0.44f * h, -0.04f * l));
        return t;
    }
}
