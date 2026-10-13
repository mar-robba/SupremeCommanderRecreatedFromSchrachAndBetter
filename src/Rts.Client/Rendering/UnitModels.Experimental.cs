using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;

namespace Rts.Client.Rendering;

/// <summary>
/// Models of the experimental-tier units (Fatboy mobile factory, Mavor artillery), the quantum gateway, the support
/// commander, and the commander enhancement attachments (one per installed slot, drawn on the torso).
/// </summary>
public static partial class UnitModels
{
    private static readonly Color GlowW = Mat.G(205, 238, 255);
    private static readonly Color GlowGate = Mat.G(120, 190, 255);

    private static Matrix4x4 RotY(float a) => Matrix4x4.CreateRotationY(a);

    /// <summary>Horizontal ring of n box segments (radius r around c), alternating colors if 'alt' is given.</summary>
    private static void Ring(MeshBuilder b, Vector3 c, float r, float thick, float height, int n, Color col, Color? alt = null)
    {
        float seg = MathF.Tau * r / n * 1.06f;
        for (int k = 0; k < n; k++)
            b.Box(V(0, 0, r), V(seg, height, thick), alt is Color a && (k & 1) == 1 ? a : col, null, RotY(k * MathF.Tau / n) * T(c.X, c.Y, c.Z));
    }

    /// <summary>
    /// Called once after the shape switch: commander enhancement attachments (on the torso turret) and a larger
    /// culling radius for models whose parts reach far outside Visual.Size (the Mavor barrel).
    /// </summary>
    private static void FinishModel(UnitBlueprint bp, MeshBuilder b, MeshBuilder? t, float w, float h, float l, ref float radius)
    {
        if (bp.Visual.Shape == "mavor") radius = MathF.Max(radius, 1.9f * l);
        if (bp.Installed.Length > 0) AddEnhancementAttachments(bp, b, t, w, h, l);
    }

    // ================================================================ Fatboy

    /// <summary>Track pod at a corner: track slab, sloped armored housing, team stripe, road-wheel hubs on the outer face.</summary>
    private static void TrackPod(MeshBuilder b, float cx, float cz, int side, float pw, float pl, float ph)
    {
        Vector2[] prof = [V2(cz - 0.5f * pl, 0.45f * ph), V2(cz - 0.4f * pl, 0), V2(cz + 0.4f * pl, 0), V2(cz + 0.5f * pl, 0.45f * ph), V2(cz + 0.45f * pl, 0.8f * ph), V2(cz - 0.45f * pl, 0.8f * ph)];
        b.SideSlab(prof, cx - 0.5f * pw, cx + 0.5f * pw, Black);
        b.Loft(MeshBuilder.Chamfered(1, 1, 0.2f),
            [new Station(cz - 0.5f * pl, 0.94f * pw, 0.22f * ph, cx, 0.9f * ph), new Station(cz - 0.36f * pl, 1.08f * pw, 0.44f * ph, cx, 0.86f * ph),
             new Station(cz + 0.36f * pl, 1.08f * pw, 0.44f * ph, cx, 0.86f * ph), new Station(cz + 0.5f * pl, 0.94f * pw, 0.22f * ph, cx, 0.9f * ph)],
            Axis.Z, HullDark, Hull);
        b.Box(V(cx + side * 0.545f * pw, 0.86f * ph, cz), V(0.02f * pw, 0.14f * ph, 0.62f * pl), Team);
        b.Box(V(cx, 1.085f * ph, cz), V(0.5f * pw, 0.01f * ph, 0.5f * pl), Dark);
        for (int i = -1; i <= 1; i++)
            b.Cylinder(V(cx + side * 0.5f * pw - 0.03f * pw, 0.36f * ph, cz + i * 0.27f * pl), Axis.X, 0.06f * pw, 0.17f * ph, 0.17f * ph, 8, HullDark, Dark);
    }

    /// <summary>
    /// UEF Fatboy: four corner track pods under a massive boxy hull, a factory bay with a lowered ramp at the REAR,
    /// riot guns on the front sponsons, flak mounts on the rear deck, a shield emitter mast; turret = heavy gauss cannon (x2).
    /// </summary>
    private static MeshBuilder Fatboy(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        float pw = 0.25f * w, pl = 0.36f * l, ph = 0.36f * h;
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
        foreach (int sz in (ReadOnlySpan<int>)[-1, 1])
            TrackPod(b, sx * (0.5f * w - pw / 2), sz * 0.31f * l, sx, pw, pl, ph);

        // Chassis between the pods + massive main hull with a sloped glacis.
        b.Loft(MeshBuilder.Chamfered(1, 1, 0.15f), [new Station(-0.47f * l, 0.56f * w, 0.26f * h, 0, 0.3f * h), new Station(0.4f * l, 0.56f * w, 0.26f * h, 0, 0.3f * h), new Station(0.48f * l, 0.5f * w, 0.14f * h, 0, 0.32f * h)], Axis.Z, HullDark);
        b.Loft(MeshBuilder.Chamfered(1, 1, 0.12f), [new Station(-0.48f * l, 0.7f * w, 0.26f * h, 0, 0.51f * h), new Station(0.3f * l, 0.72f * w, 0.26f * h, 0, 0.51f * h), new Station(0.47f * l, 0.64f * w, 0.12f * h, 0, 0.44f * h)], Axis.Z, Hull, HullLight);
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
        {
            // Side sponsons between front and rear pods, hull stripes, vents.
            b.BevelBox(V(sx * 0.4f * w, 0.3f * h, 0), V(0.18f * w, 0.2f * h, 0.26f * l), 0.03f * w, HullDark, Hull);
            b.Box(V(sx * 0.49f * w, 0.3f * h, 0), V(0.01f * w, 0.05f * h, 0.2f * l), GlowO);
            b.Box(V(sx * 0.362f * w, 0.56f * h, -0.05f * l), V(0.012f * w, 0.06f * h, 0.72f * l), Team);
            // Riot guns on the front sponsons (twin short barrels).
            b.BevelBox(V(sx * 0.38f * w, 0.45f * h, 0.36f * l), V(0.12f * w, 0.12f * h, 0.12f * l), 0.02f * w, HullDark, Hull);
            foreach (int sy in (ReadOnlySpan<int>)[-1, 1])
                b.Cylinder(V(sx * 0.38f * w, 0.45f * h + sy * 0.025f * h, 0.42f * l), Axis.Z, 0.08f * l, 0.022f * w, 0.02f * w, 6, Dark, Black);
            // Flak mounts on the rear deck: base, housing, twin barrels pitched up.
            float fx = sx * 0.29f * w, fz = -0.36f * l;
            b.Cylinder(V(fx, 0.63f * h, fz), Axis.Y, 0.04f * h, 0.07f * w, 0.065f * w, 8, Dark);
            b.BevelBox(V(fx, 0.71f * h, fz), V(0.1f * w, 0.07f * h, 0.1f * w), 0.015f * w, HullLight, null);
            var flak = PitchUp(1.0f) * T(fx, 0.72f * h, fz + 0.02f * l);
            foreach (int k in (ReadOnlySpan<int>)[-1, 1])
                b.Cylinder(V(k * 0.025f * w, 0, 0), Axis.Z, 0.11f * l, 0.012f * w, 0.012f * w, 5, Dark, null, flak);
            // Headlights / rear warning lights.
            b.Box(V(sx * 0.24f * w, 0.47f * h, 0.475f * l), V(0.07f * w, 0.03f * h, 0.01f * l), GlowC);
            b.Box(V(sx * 0.33f * w, 0.62f * h, -0.482f * l), V(0.04f * w, 0.03f * h, 0.01f * l), GlowR);
        }
        b.Box(V(0, 0.645f * h, 0.12f * l), V(0.1f * w, 0.01f * h, 0.34f * l), Team);

        // Raised factory roof / superstructure (rear-center) with roof grille and team chevrons.
        b.Loft(MeshBuilder.Chamfered(1, 1, 0.18f), [new Station(-0.44f * l, 0.46f * w, 0.16f * h, 0, 0.7f * h), new Station(-0.05f * l, 0.46f * w, 0.16f * h, 0, 0.7f * h), new Station(0.03f * l, 0.4f * w, 0.08f * h, 0, 0.66f * h)], Axis.Z, Hull, HullLight);
        b.Box(V(0, 0.785f * h, -0.32f * l), V(0.22f * w, 0.012f * h, 0.14f * l), Dark);
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sx * 0.17f * w, 0.785f * h, -0.2f * l), V(0.06f * w, 0.01f * h, 0.3f * l), Team);

        // Shield generator emitter mast: collar, glowing orb, three prongs.
        float ez = -0.14f * l;
        b.Cylinder(V(0, 0.78f * h, ez), Axis.Y, 0.12f * h, 0.06f * w, 0.04f * w, 6, HullDark);
        b.Cylinder(V(0, 0.86f * h, ez), Axis.Y, 0.03f * h, 0.1f * w, 0.09f * w, 8, Team);
        b.Sphere(V(0, 0.95f * h, ez), V(0.065f * w, 0.065f * w, 0.065f * w), 5, 8, GlowC);
        for (int k = 0; k < 3; k++)
            b.Cylinder(V(0, 0.86f * h, 0.1f * w), Axis.Y, 0.15f * h, 0.016f * w, 0.012f * w, 5, HullLight, GlowC, RotY(k * MathF.Tau / 3f) * T(0, 0, ez));

        // Rear factory bay: dark opening framed by hazard-striped posts, a lintel and work lights; lowered ramp.
        b.Box(V(0, 0.4f * h, -0.483f * l), V(0.4f * w, 0.38f * h, 0.03f * l), Black);
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Box(V(sx * 0.22f * w, 0.4f * h, -0.49f * l), V(0.04f * w, 0.4f * h, 0.04f * l), HullDark);
            for (int k = 0; k < 4; k++)
                b.Box(V(sx * 0.22f * w, 0.24f * h + k * 0.1f * h, -0.511f * l), V(0.042f * w, 0.04f * h, 0.004f * l), Hazard);
            b.Box(V(sx * 0.17f * w, 0.56f * h, -0.5f * l), V(0.04f * w, 0.025f * h, 0.01f * l), GlowY);
        }
        b.Box(V(0, 0.6f * h, -0.49f * l), V(0.48f * w, 0.05f * h, 0.045f * l), HullDark);
        float rdz = 0.15f * l, rdy = 0.21f * h, rl = MathF.Sqrt(rdz * rdz + rdy * rdy);
        var ramp = PitchUp(MathF.Atan2(rdy, rdz)) * T(0, 0.5f * rdy, -0.475f * l - 0.5f * rdz);
        b.Box(V(0, 0, 0), V(0.38f * w, 0.03f * h, rl), HullDark, Pad, ramp);
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sx * 0.175f * w, 0.02f * h, 0), V(0.03f * w, 0.02f * h, rl * 0.92f), Hazard, null, ramp);
        b.Box(V(0, 0.018f * h, 0), V(0.02f * w, 0.01f * h, rl * 0.8f), GlowY, null, ramp);

        // Turret: heavy gauss cannon house with a long coil-ringed barrel (two pivots, front deck).
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.04f * h, 0.13f * w, 0.12f * w, 10, Dark);
        t.Loft(MeshBuilder.Chamfered(1, 1, 0.25f), [new Station(-0.12f * l, 0.2f * w, 0.1f * h, 0, 0.1f * h), new Station(0.04f * l, 0.24f * w, 0.13f * h, 0, 0.1f * h), new Station(0.1f * l, 0.17f * w, 0.08f * h, 0, 0.095f * h)], Axis.Z, Hull, HullLight);
        t.Box(V(0, 0.168f * h, -0.03f * l), V(0.1f * w, 0.008f * h, 0.12f * l), Team);
        t.Box(V(0, 0.1f * h, -0.14f * l), V(0.15f * w, 0.07f * h, 0.05f * l), HullDark);
        t.Box(V(0.08f * w, 0.15f * h, 0.07f * l), V(0.03f * w, 0.025f * h, 0.01f * l), GlowC);
        t.Box(V(0, 0.1f * h, 0.1f * l), V(0.1f * w, 0.08f * h, 0.04f * l), HullDark);
        t.Cylinder(V(0, 0.1f * h, 0.1f * l), Axis.Z, 0.1f * l, 0.045f * w, 0.04f * w, 8, HullDark);
        t.Cylinder(V(0, 0.1f * h, 0.2f * l), Axis.Z, 0.24f * l, 0.03f * w, 0.027f * w, 8, HullLight);
        foreach (float z in (ReadOnlySpan<float>)[0.24f, 0.3f, 0.36f])
            t.Cylinder(V(0, 0.1f * h, z * l), Axis.Z, 0.02f * l, 0.042f * w, 0.042f * w, 8, GlowC);
        t.Cylinder(V(0, 0.1f * h, 0.43f * l), Axis.Z, 0.045f * l, 0.042f * w, 0.04f * w, 8, Dark, Black);
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
            pivots.Add(V(sx * 0.18f * w, 0.64f * h, 0.22f * l));
        return t;
    }

    // ================================================================ Mavor

    /// <summary>
    /// UEF Mavor: wide octagonal armored base with buttresses and a team ring; turret = rotating gun house with
    /// trunnion cheeks and an enormous barrel (~1.7 x length) elevated about 26 degrees.
    /// </summary>
    private static MeshBuilder Mavor(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        Foundation(b, w, l, 0.04f * h, HullDark);
        b.Cylinder(V(0, 0, 0), Axis.Y, 0.12f * h, 0.5f * w, 0.46f * w, 8, Hull, HullLight);
        for (int k = 0; k < 8; k++)
        {
            var rot = RotY(k * MathF.Tau / 8f + MathF.PI / 8f);
            b.Frustum(V(0, 0.1f * h, 0.38f * w), 0.14f * w, 0.12f * w, 0.08f * w, 0.05f * w, 0.1f * h, HullDark, Hull, rot, 0.2f);
            b.Box(V(0, 0.122f * h, 0.47f * w), V(0.05f * w, 0.006f * h, 0.03f * w), (k & 1) == 0 ? GlowY : GlowR, null, rot);
        }
        b.Cylinder(V(0, 0.12f * h, 0), Axis.Y, 0.07f * h, 0.4f * w, 0.36f * w, 8, HullDark, Dark);
        b.Cylinder(V(0, 0.155f * h, 0), Axis.Y, 0.025f * h, 0.405f * w, 0.39f * w, 16, Team);
        b.Cylinder(V(0, 0.19f * h, 0), Axis.Y, 0.03f * h, 0.34f * w, 0.33f * w, 16, Dark);
        // Ammunition hoppers at the rear corners of the base.
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
        {
            b.BevelBox(V(sx * 0.3f * w, 0.17f * h, -0.3f * l), V(0.14f * w, 0.1f * h, 0.14f * l), 0.02f * w, Hull, HullLight);
            b.Box(V(sx * 0.3f * w, 0.222f * h, -0.3f * l), V(0.08f * w, 0.006f * h, 0.08f * l), Team);
        }

        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.04f * h, 0.32f * w, 0.31f * w, 12, Hull, HullLight);
        // Gun house: wide chamfered body, counterweight at the back, vents.
        t.Loft(MeshBuilder.Chamfered(1, 1, 0.22f), [new Station(-0.34f * l, 0.36f * w, 0.14f * h, 0, 0.12f * h), new Station(-0.02f * l, 0.44f * w, 0.2f * h, 0, 0.14f * h), new Station(0.2f * l, 0.34f * w, 0.14f * h, 0, 0.12f * h)], Axis.Z, Hull, HullLight);
        t.BevelBox(V(0, 0.13f * h, -0.38f * l), V(0.32f * w, 0.14f * h, 0.1f * l), 0.03f * w, HullDark, Dark);
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Box(V(sx * 0.1f * w, 0.13f * h, -0.432f * l), V(0.08f * w, 0.05f * h, 0.006f * l), GlowO);
            // Trunnion cheeks holding the barrel, with team panels on the outside.
            t.BevelBox(V(sx * 0.14f * w, 0.24f * h, 0.03f * l), V(0.06f * w, 0.2f * h, 0.24f * l), 0.02f * w, HullDark, Hull);
            t.Box(V(sx * 0.172f * w, 0.24f * h, 0.03f * l), V(0.006f * w, 0.08f * h, 0.16f * l), Team);
            t.Cylinder(V(sx > 0 ? 0.17f * w : -0.21f * w, 0.3f * h, 0.03f * l), Axis.X, 0.04f * w, 0.05f * w, 0.05f * w, 8, Dark, HullLight);
        }
        t.Box(V(0, 0.222f * h, -0.16f * l), V(0.16f * w, 0.008f * h, 0.16f * l), Team);
        t.Box(V(0.07f * w, 0.12f * h, 0.203f * l), V(0.05f * w, 0.03f * h, 0.006f * l), GlowC);

        // The barrel (built along +Z, then elevated and placed on the trunnion axis).
        var barrel = PitchUp(0.45f) * T(0, 0.3f * h, 0.03f * l);
        t.Cylinder(V(0, 0, -0.22f * l), Axis.Z, 0.4f * l, 0.11f * w, 0.1f * w, 8, HullDark, Dark, barrel);
        t.Cylinder(V(0, 0, -0.02f * l), Axis.Z, 0.04f * l, 0.113f * w, 0.113f * w, 8, Team, null, barrel);
        t.Cylinder(V(0, 0, 0.18f * l), Axis.Z, 0.32f * l, 0.08f * w, 0.072f * w, 8, Hull, null, barrel);
        t.Cylinder(V(0, 0, 0.5f * l), Axis.Z, 1.12f * l, 0.058f * w, 0.048f * w, 8, HullLight, null, barrel);
        foreach (float z in (ReadOnlySpan<float>)[0.5f, 0.78f, 1.05f, 1.32f])
            t.Cylinder(V(0, 0, z * l), Axis.Z, 0.05f * l, 0.068f * w, 0.066f * w, 8, Dark, null, barrel);
        t.Cylinder(V(0, 0, 1.6f * l), Axis.Z, 0.11f * l, 0.072f * w, 0.075f * w, 8, Dark, Black, barrel);
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Box(V(sx * 0.058f * w, 0, 1.655f * l), V(0.02f * w, 0.1f * w, 0.08f * l), HullDark, null, barrel); // muzzle brake vents
            t.Cylinder(V(sx * 0.05f * w, -0.075f * w, 0.05f * l), Axis.Z, 0.5f * l, 0.022f * w, 0.022f * w, 6, Dark, HullLight, barrel); // recoil cylinders
        }
        t.Box(V(0, 0.105f * w, -0.05f * l), V(0.04f * w, 0.02f * w, 0.3f * l), Team, null, barrel);
        pivots.Add(V(0, 0.22f * h, 0));
        return t;
    }

    // ================================================================ Quantum gateway

    /// <summary>
    /// Quantum gateway: square armored platform, four corner pylons with inward arms, a glowing blue-white gate disc
    /// on a raised dais; turret = slowly spinning gate ring with glowing nodes held between the arms.
    /// </summary>
    private static MeshBuilder QuantumGateway(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        Foundation(b, w, l, 0.04f * h, HullDark);
        b.Frustum(V(0, 0.02f * h, 0), 0.94f * w, 0.94f * l, 0.86f * w, 0.86f * l, 0.1f * h, Hull, HullLight, null, 0.12f);
        for (int k = 0; k < 4; k++)
        {
            var rot = RotY(k * MathF.PI * 0.5f);
            b.Box(V(0, 0.115f * h, 0.405f * l), V(0.36f * w, 0.012f * h, 0.03f * l), Team, null, rot);
            foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
                b.Box(V(sx * 0.24f * w, 0.115f * h, 0.405f * l), V(0.05f * w, 0.014f * h, 0.03f * l), Hazard, null, rot);
        }
        // Dais with the glowing gate disc and a team/dark segmented ring.
        b.Cylinder(V(0, 0.12f * h, 0), Axis.Y, 0.05f * h, 0.35f * w, 0.33f * w, 16, HullDark, Dark);
        b.Cylinder(V(0, 0.17f * h, 0), Axis.Y, 0.012f * h, 0.29f * w, 0.29f * w, 16, GlowGate);
        b.Cylinder(V(0, 0.18f * h, 0), Axis.Y, 0.012f * h, 0.17f * w, 0.17f * w, 16, GlowW);
        Ring(b, V(0, 0.18f * h, 0), 0.31f * w, 0.03f * w, 0.03f * h, 24, Team, Dark);
        // Corner pylons (rotated 45 degrees), team bands, glowing tips, inward arms holding the ring, floor conduits.
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
        foreach (int sz in (ReadOnlySpan<int>)[-1, 1])
        {
            float px = sx * 0.37f * w, pz = sz * 0.37f * l;
            var xf = RotY(MathF.PI / 4f) * T(px, 0, pz);
            b.Frustum(V(0, 0.1f * h, 0), 0.17f * w, 0.17f * w, 0.08f * w, 0.08f * w, 0.72f * h, Hull, HullLight, xf, 0.25f);
            b.Frustum(V(0, 0.4f * h, 0), 0.14f * w, 0.14f * w, 0.135f * w, 0.135f * w, 0.06f * h, Team, null, xf, 0.25f);
            b.Frustum(V(0, 0.15f * h, 0), 0.2f * w, 0.2f * w, 0.18f * w, 0.18f * w, 0.08f * h, HullDark, null, xf, 0.25f);
            b.Sphere(V(px, 0.84f * h, pz), V(0.045f * w, 0.045f * w, 0.045f * w), 4, 8, GlowC);
            var dir = RotY(MathF.Atan2(sx, sz));
            b.Box(V(0, 0.58f * h, 0.44f * w), V(0.05f * w, 0.06f * h, 0.2f * w), HullDark, Hull, dir);
            b.Box(V(0, 0.58f * h, 0.345f * w), V(0.07f * w, 0.08f * h, 0.03f * w), GlowC, null, dir);
            b.Box(V(0, 0.122f * h, 0.42f * w), V(0.03f * w, 0.01f * h, 0.16f * w), GlowGate, null, dir);
        }

        var t = new MeshBuilder();
        Ring(t, V(0, 0, 0), 0.3f * w, 0.045f * w, 0.06f * h, 24, Hull, HullLight);
        Ring(t, V(0, 0, 0), 0.27f * w, 0.015f * w, 0.035f * h, 24, GlowGate);
        for (int k = 0; k < 6; k++)
        {
            var rot = RotY(k * MathF.Tau / 6f);
            t.Box(V(0, 0, 0.3f * w), V(0.06f * w, 0.09f * h, 0.07f * w), Team, null, rot);
            t.Box(V(0, 0.047f * h, 0.3f * w), V(0.03f * w, 0.006f * h, 0.03f * w), GlowW, null, rot);
        }
        pivots.Add(V(0, 0.58f * h, 0));
        return t;
    }

    // ================================================================ Support commander

    /// <summary>Support commander: sturdier mech legs with knee guards; turret = boxy torso, big shoulders, backpack, gun right arm.</summary>
    private static MeshBuilder Scu(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        MechLegs(b, w, h, l, 0.48f);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.BevelBox(V(sd * 0.2f * w, 0.31f * h, 0.13f * l), V(0.19f * w, 0.1f * h, 0.06f * l), 0.03f * w, Team);

        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.01f * h, 0), Axis.Y, 0.06f * h, 0.13f * w, 0.13f * w, 8, Dark);
        t.Loft(MeshBuilder.Chamfered(1, 1, 0.16f), [new Station(0.04f * h, 0.42f * w, 0.32f * l), new Station(0.18f * h, 0.62f * w, 0.46f * l, 0, 0.02f * l), new Station(0.3f * h, 0.56f * w, 0.4f * l)], Axis.Y, Hull, HullLight);
        t.Box(V(0, 0.2f * h, 0.252f * l), V(0.3f * w, 0.06f * h, 0.02f * l), Team);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Box(V(sd * 0.09f * w, 0.12f * h, 0.225f * l), V(0.08f * w, 0.025f * h, 0.02f * l), GlowC);
        // Low, wide head sunk between the shoulders with a full-width visor and an antenna.
        t.BevelBox(V(0, 0.32f * h, 0.06f * l), V(0.22f * w, 0.06f * h, 0.17f * l), 0.03f * w, HullLight);
        t.Box(V(0, 0.322f * h, 0.15f * l), V(0.19f * w, 0.022f * h, 0.012f * l), GlowC);
        t.Cylinder(V(0.07f * w, 0.35f * h, -0.02f * l), Axis.Y, 0.1f * h, 0.012f * w, 0.008f * w, 4, Dark);
        // Big backpack with two exhaust stacks.
        t.BevelBox(V(0, 0.2f * h, -0.27f * l), V(0.46f * w, 0.24f * h, 0.18f * l), 0.04f * w, HullDark, Hull);
        t.Box(V(0, 0.24f * h, -0.362f * l), V(0.3f * w, 0.04f * h, 0.01f * l), Team);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Cylinder(V(sd * 0.15f * w, 0.26f * h, -0.31f * l), Axis.Y, 0.12f * h, 0.045f * w, 0.04f * w, 6, Dark, GlowO);
            // Bulky shoulder blocks with dark armor plates, upper arms.
            t.BevelBox(V(sd * 0.4f * w, 0.27f * h, 0), V(0.26f * w, 0.15f * h, 0.36f * l), 0.05f * w, Team);
            t.Box(V(sd * 0.42f * w, 0.35f * h, 0), V(0.2f * w, 0.012f * h, 0.28f * l), HullDark);
            t.Box(V(sd * 0.4f * w, 0.15f * h, 0), V(0.13f * w, 0.14f * h, 0.15f * l), Dark);
        }
        // Right arm (+X): heavy plasma gun; left arm (-X): manipulator with nanolathe emitter.
        t.BevelBox(V(0.4f * w, 0.06f * h, 0.12f * l), V(0.17f * w, 0.13f * h, 0.4f * l), 0.03f * w, Hull);
        t.Box(V(0.4f * w, 0.128f * h, 0.1f * l), V(0.11f * w, 0.012f * h, 0.22f * l), TeamDim);
        t.Cylinder(V(0.4f * w, 0.06f * h, 0.32f * l), Axis.Z, 0.22f * l, 0.05f * w, 0.045f * w, 6, Dark);
        t.Cylinder(V(0.4f * w, 0.06f * h, 0.54f * l), Axis.Z, 0.04f * l, 0.055f * w, 0.055f * w, 6, Dark, GlowO);
        t.BevelBox(V(-0.4f * w, 0.06f * h, 0.1f * l), V(0.16f * w, 0.13f * h, 0.34f * l), 0.03f * w, Hull);
        t.Box(V(-0.4f * w, 0.06f * h, 0.285f * l), V(0.07f * w, 0.07f * h, 0.04f * l), GlowY);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Box(V(-0.4f * w + sd * 0.055f * w, 0.06f * h, 0.32f * l), V(0.025f * w, 0.05f * h, 0.09f * l), HullLight);
        pivots.Add(V(0, 0.48f * h, 0));
        return t;
    }

    // ================================================================ enhancements

    /// <summary>Attachment points on a commander-type torso (turret space).</summary>
    private readonly record struct MechAnchor(float ArmX, float ArmY, float GunZ, float ShoulderY, float BackZ, float BackY);

    private static MechAnchor AnchorsFor(string shape, float w, float h, float l) => shape == "scu"
        ? new MechAnchor(0.4f * w, 0.06f * h, 0.32f * l, 0.355f * h, -0.27f * l, 0.32f * h)
        : new MechAnchor(0.37f * w, 0.07f * h, 0.3f * l, 0.31f * h, -0.25f * l, 0.3f * h);

    /// <summary>Draws the installed enhancement of every slot (its Visual hint) on the torso turret (or the body).</summary>
    private static void AddEnhancementAttachments(UnitBlueprint bp, MeshBuilder b, MeshBuilder? t, float w, float h, float l)
    {
        var m = t ?? b;
        var saved = m.Transform;
        if (t == null) m.Transform = T(0, 0.5f * h, 0);
        var a = AnchorsFor(bp.Visual.Shape, w, h, l);
        for (int slot = 0; slot < bp.Installed.Length; slot++)
        {
            int e = bp.Installed[slot];
            if ((uint)e >= (uint)bp.Enhancements.Length) continue;
            var en = bp.Enhancements[e];
            float s = en.Slot == EnhancementSlot.LeftArm ? -1 : 1;
            switch (en.Visual)
            {
                case "cannon": EnhCannon(m, a, s, w, h, l); break;
                case "amplifier": EnhAmplifier(m, a, s, w, h, l); break;
                case "engineering": EnhEngineering(m, a, s, w, h, l); break;
                case "engineering3": EnhEngineering3(m, a, s, w, h, l); break;
                case "resource": EnhResource(m, a, s, w, h, l); break;
                case "shield": EnhShield(m, a, w, h, l); break;
                case "shield_field": EnhShieldField(m, a, w, h, l); break;
                case "missile_pack": EnhMissilePack(m, a, w, h, l); break;
            }
        }
        m.Transform = saved;
    }

    /// <summary>Heavy antimatter cannon / HE ordnance: big armored housing and a much longer, thicker barrel.</summary>
    private static void EnhCannon(MeshBuilder m, MechAnchor a, float s, float w, float h, float l)
    {
        float x = s * a.ArmX, y = a.ArmY + 0.02f * h;
        m.BevelBox(V(x, y, 0.16f * l), V(0.21f * w, 0.18f * h, 0.38f * l), 0.04f * w, HullDark, Hull);
        m.Box(V(x + s * 0.106f * w, y, 0.16f * l), V(0.01f * w, 0.07f * h, 0.26f * l), Team);
        m.Box(V(x, y + 0.092f * h, 0.14f * l), V(0.06f * w, 0.01f * h, 0.26f * l), GlowO);
        m.Cylinder(V(x, y, 0.3f * l), Axis.Z, 0.12f * l, 0.09f * w, 0.085f * w, 8, Dark);
        m.Cylinder(V(x, y, 0.42f * l), Axis.Z, 0.38f * l, 0.065f * w, 0.055f * w, 8, Hull);
        m.Cylinder(V(x, y, 0.58f * l), Axis.Z, 0.03f * l, 0.075f * w, 0.075f * w, 8, Dark);
        m.Cylinder(V(x, y, 0.8f * l), Axis.Z, 0.08f * l, 0.085f * w, 0.08f * w, 8, Dark, GlowO);
    }

    /// <summary>Zephyr amplifier: glowing coils around the gun barrel, an extended barrel tip and capacitor pods.</summary>
    private static void EnhAmplifier(MeshBuilder m, MechAnchor a, float s, float w, float h, float l)
    {
        float x = s * a.ArmX, y = a.ArmY;
        for (int i = 0; i < 4; i++)
            m.Cylinder(V(x, y, a.GunZ + (0.01f + i * 0.06f) * l), Axis.Z, 0.03f * l, 0.085f * w, 0.085f * w, 8, (i & 1) == 0 ? GlowC : Dark);
        m.Cylinder(V(x, y, a.GunZ + 0.22f * l), Axis.Z, 0.14f * l, 0.04f * w, 0.03f * w, 6, HullLight, GlowC);
        foreach (int k in (ReadOnlySpan<int>)[-1, 1])
            m.Cylinder(V(x + k * 0.1f * w, y + 0.05f * h, 0.02f * l), Axis.Z, 0.22f * l, 0.03f * w, 0.03f * w, 6, HullDark, GlowC);
    }

    /// <summary>Advanced engineering: sleeve with hazard stripes, yellow claw prongs and an emitter on the arm.</summary>
    private static void EnhEngineering(MeshBuilder m, MechAnchor a, float s, float w, float h, float l)
    {
        float x = s * a.ArmX, y = a.ArmY;
        m.BevelBox(V(x, y, 0.12f * l), V(0.19f * w, 0.15f * h, 0.3f * l), 0.03f * w, HullLight);
        for (int i = 0; i < 3; i++)
            m.Box(V(x, y + 0.076f * h, (0.02f + i * 0.08f) * l), V(0.19f * w, 0.006f * h, 0.035f * l), Hazard);
        foreach (int k in (ReadOnlySpan<int>)[-1, 1])
            m.Box(V(x + k * 0.065f * w, y, 0.33f * l), V(0.035f * w, 0.08f * h, 0.16f * l), Hazard);
        m.Box(V(x, y, 0.29f * l), V(0.07f * w, 0.07f * h, 0.03f * l), GlowY);
    }

    /// <summary>T3 engineering / rapid fabricator: large fabricator housing, nanolathe dish, three-prong claw, coil.</summary>
    private static void EnhEngineering3(MeshBuilder m, MechAnchor a, float s, float w, float h, float l)
    {
        float x = s * a.ArmX, y = a.ArmY + 0.01f * h;
        m.BevelBox(V(x, y, 0.1f * l), V(0.25f * w, 0.2f * h, 0.42f * l), 0.05f * w, Hull, HullLight);
        for (int i = 0; i < 4; i++)
            m.Box(V(x + s * 0.126f * w, y, (-0.04f + i * 0.07f) * l), V(0.004f * w, 0.18f * h, 0.03f * l), Hazard);
        m.Box(V(x, y + 0.101f * h, 0.06f * l), V(0.12f * w, 0.006f * h, 0.22f * l), Team);
        m.Cylinder(V(x, y, 0.31f * l), Axis.Z, 0.08f * l, 0.1f * w, 0.07f * w, 8, Dark, GlowY);
        for (int k = 0; k < 3; k++)
        {
            float ang = MathF.PI / 2 + k * MathF.Tau / 3f;
            m.Box(V(x + MathF.Cos(ang) * 0.1f * w, y + MathF.Sin(ang) * 0.1f * w, 0.42f * l), V(0.035f * w, 0.035f * w, 0.16f * l), Hazard);
        }
        m.Cylinder(V(x, y + 0.1f * h, -0.06f * l), Axis.Y, 0.1f * h, 0.035f * w, 0.025f * w, 6, HullDark, GlowY);
        m.Cylinder(V(x - s * 0.13f * w, y + 0.03f * h, -0.08f * l), Axis.Z, 0.34f * l, 0.025f * w, 0.025f * w, 6, HullDark);
    }

    /// <summary>Resource allocation system: generator box on the shoulder with a glowing green core and yellow lights.</summary>
    private static void EnhResource(MeshBuilder m, MechAnchor a, float s, float w, float h, float l)
    {
        float x = s * a.ArmX, y = a.ShoulderY;
        m.BevelBox(V(x, y + 0.04f * h, -0.02f * l), V(0.24f * w, 0.08f * h, 0.28f * l), 0.03f * w, HullDark, Hull);
        m.Cylinder(V(x, y + 0.08f * h, -0.02f * l), Axis.Y, 0.08f * h, 0.075f * w, 0.075f * w, 8, GlowG);
        m.Cylinder(V(x, y + 0.16f * h, -0.02f * l), Axis.Y, 0.025f * h, 0.1f * w, 0.06f * w, 8, Hull, Team);
        foreach (int k in (ReadOnlySpan<int>)[-1, 1])
        {
            m.Box(V(x, y + 0.11f * h, -0.02f * l + k * 0.11f * l), V(0.16f * w, 0.05f * h, 0.015f * l), Dark);
            m.Box(V(x + k * 0.11f * w, y + 0.06f * h, 0.1f * l), V(0.025f * w, 0.025f * h, 0.03f * l), GlowY);
        }
    }

    /// <summary>Personal shield: mast with a small flared emitter dish and a glowing tip on the backpack.</summary>
    private static void EnhShield(MeshBuilder m, MechAnchor a, float w, float h, float l)
    {
        m.Cylinder(V(0, a.BackY, a.BackZ), Axis.Y, 0.16f * h, 0.035f * w, 0.025f * w, 6, HullDark);
        m.Cylinder(V(0, a.BackY + 0.16f * h, a.BackZ), Axis.Y, 0.04f * h, 0.05f * w, 0.15f * w, 8, HullLight, Team);
        m.Sphere(V(0, a.BackY + 0.22f * h, a.BackZ), V(0.045f * w, 0.045f * w, 0.045f * w), 4, 6, GlowC);
        foreach (int k in (ReadOnlySpan<int>)[-1, 1])
            m.Box(V(k * 0.17f * w, a.BackY - 0.02f * h, a.BackZ), V(0.05f * w, 0.05f * h, 0.05f * l), GlowC);
    }

    /// <summary>Shield generator field / bubble shield: tall emitter tower with prongs around a big glowing orb.</summary>
    private static void EnhShieldField(MeshBuilder m, MechAnchor a, float w, float h, float l)
    {
        float y = a.BackY, z = a.BackZ;
        m.BevelBox(V(0, y + 0.03f * h, z), V(0.36f * w, 0.07f * h, 0.2f * l), 0.03f * w, Hull, Team);
        m.Cylinder(V(0, y + 0.06f * h, z), Axis.Y, 0.26f * h, 0.075f * w, 0.045f * w, 6, HullDark);
        m.Cylinder(V(0, y + 0.22f * h, z), Axis.Y, 0.04f * h, 0.11f * w, 0.1f * w, 8, Team);
        m.Sphere(V(0, y + 0.42f * h, z), V(0.11f * w, 0.11f * w, 0.11f * w), 5, 8, GlowC);
        for (int k = 0; k < 3; k++)
            m.Cylinder(V(0, y + 0.25f * h, 0.14f * w), Axis.Y, 0.2f * h, 0.022f * w, 0.014f * w, 5, HullLight, GlowC, RotY(k * MathF.Tau / 3f) * T(0, 0, z));
    }

    /// <summary>Tactical missile pack: launcher box on the back with 2 x 3 tubes and red warhead tips.</summary>
    private static void EnhMissilePack(MeshBuilder m, MechAnchor a, float w, float h, float l)
    {
        float y = a.BackY, z = a.BackZ - 0.03f * l;
        m.BevelBox(V(0, y + 0.07f * h, z), V(0.46f * w, 0.17f * h, 0.24f * l), 0.03f * w, Hull, HullLight);
        foreach (int k in (ReadOnlySpan<int>)[-1, 1])
            m.Box(V(k * 0.232f * w, y + 0.07f * h, z), V(0.01f * w, 0.1f * h, 0.18f * l), Team);
        for (int i = -1; i <= 1; i++)
        foreach (int k in (ReadOnlySpan<int>)[-1, 1])
        {
            var p = V(i * 0.13f * w, y + 0.155f * h, z + k * 0.06f * l);
            m.Cylinder(p, Axis.Y, 0.02f * h, 0.05f * w, 0.05f * w, 6, Black);
            m.Cylinder(p + V(0, 0.02f * h, 0), Axis.Y, 0.03f * h, 0.03f * w, 0.004f * w, 6, GlowR);
        }
    }
}
