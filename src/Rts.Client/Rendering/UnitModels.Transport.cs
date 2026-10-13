using System.Numerics;

namespace Rts.Client.Rendering;

/// <summary>
/// Cargo wave shapes: the UEF air transports (T1 C-6 Courier "transport", T2 C-14 Star Lifter "transport2"), the
/// T2 Air Staging Facility ("air_staging") and the experimental aircraft carrier Atlantis ("atlantis"). Same palette
/// and conventions as the other models (forward +Z, up +Y; ships: y = 0 is the water line). Transports keep their
/// belly clear (lowest point = the cargo clamps, see <see cref="UnitModel.MinY"/>): carried units hang under it
/// (<see cref="UnitRenderer"/>, UnitRenderer.Cargo.cs). Docked aircraft are parked on <see cref="StagingPad"/> /
/// <see cref="AtlantisSpot"/>. Dispatched from <see cref="Build"/>.
/// </summary>
public static partial class UnitModels
{
    // ================================================================ T1 transport (C-6 Courier)

    /// <summary>C-6 Courier: slim armoured spine with the cockpit at the nose, two clamp rails under it (the cargo
    /// hangs between them), four ducted lift fans on outrigger pylons, twin tail fins.</summary>
    private static void CourierTransport(MeshBuilder b, float w, float h, float l)
    {
        b.Loft(MeshBuilder.Chamfered(0.34f * w, 0.36f * h, 0.07f * w), [
            new Station(-0.5f * l, 0.7f, 0.6f, 0, 0.64f * h), new Station(-0.36f * l, 1, 1, 0, 0.6f * h),
            new Station(0.3f * l, 1, 1, 0, 0.6f * h), new Station(0.44f * l, 0.82f, 0.8f, 0, 0.56f * h),
            new Station(0.5f * l, 0.45f, 0.42f, 0, 0.52f * h)], Axis.Z, Hull, HullDark);
        b.Sphere(V(0, 0.7f * h, 0.33f * l), V(0.11f * w, 0.14f * h, 0.13f * l), 4, 6, Glass, null, 0.5f);
        b.Box(V(0, 0.785f * h, -0.08f * l), V(0.1f * w, 0.012f * h, 0.5f * l), Team);
        b.Box(V(0, 0.45f * h, -0.5f * l), V(0.12f * w, 0.1f * h, 0.012f * l), GlowO);

        // Cargo frame: cross beams under the spine, two rails, clamp arms reaching down.
        foreach (float z in (ReadOnlySpan<float>)[-0.3f, -0.02f, 0.26f])
            b.Box(V(0, 0.4f * h, z * l), V(0.84f * w, 0.06f * h, 0.05f * l), HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            float rx = sd * 0.4f * w;
            b.Box(V(rx, 0.32f * h, -0.04f * l), V(0.05f * w, 0.1f * h, 0.8f * l), HullDark, Dark);
            b.Box(V(rx + sd * 0.026f * w, 0.33f * h, -0.04f * l), V(0.004f * w, 0.04f * h, 0.7f * l), Team);
            foreach (float z in (ReadOnlySpan<float>)[-0.32f, -0.04f, 0.24f])
            {
                b.Box(V(rx, 0.17f * h, z * l), V(0.035f * w, 0.22f * h, 0.035f * l), Hull);
                b.Box(V(rx - sd * 0.035f * w, 0.07f * h, z * l), V(0.1f * w, 0.035f * h, 0.05f * l), Dark);
            }
        }

        // Four ducted lift fans on pylons (fan = dark caps), thrust glow underneath.
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        foreach (float z in (ReadOnlySpan<float>)[-0.32f, 0.27f])
        {
            float px = sd * 0.37f * w;
            b.Box(V(sd * 0.27f * w, 0.62f * h, z * l), V(0.2f * w, 0.06f * h, 0.07f * l), HullDark);
            b.Cylinder(V(px, 0.48f * h, z * l), Axis.Y, 0.24f * h, 0.13f * w, 0.13f * w, 10, Hull, Black);
            b.Cylinder(V(px, 0.58f * h, z * l), Axis.Y, 0.06f * h, 0.134f * w, 0.134f * w, 10, Team);
            b.Cylinder(V(px, 0.72f * h, z * l), Axis.Y, 0.02f * h, 0.03f * w, 0.03f * w, 6, Dark);
            b.Cylinder(V(px, 0.47f * h, z * l), Axis.Y, 0.01f * h, 0.09f * w, 0.09f * w, 10, GlowC);
        }
        b.Box(V(-0.5f * w + 0.02f * w, 0.6f * h, -0.32f * l), V(0.03f * w, 0.04f * h, 0.03f * w), GlowR);
        b.Box(V(0.5f * w - 0.02f * w, 0.6f * h, -0.32f * l), V(0.03f * w, 0.04f * h, 0.03f * w), GlowG);

        // Twin tail fins.
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            Vector2[] fin = [V2(-0.5f * l, 0.72f * h), V2(-0.33f * l, 0.72f * h), V2(-0.43f * l, 1.0f * h), V2(-0.51f * l, 1.0f * h)];
            b.SideSlab(fin, sd * 0.1f * w, sd * 0.12f * w, Hull, Team);
        }
    }

    // ================================================================ T2 transport (C-14 Star Lifter)

    /// <summary>C-14 Star Lifter: heavy flat-bellied cargo lifter (the cargo is clamped under the whole belly),
    /// raised cockpit, armoured side sponsons, two stub wings carrying four big ducted engines; turrets = two small
    /// twin-gun mounts on the spine.</summary>
    private static void StarLifterTransport(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, bool turrets, out MeshBuilder? t)
    {
        // Fuselage + belly plate.
        b.Loft(MeshBuilder.Chamfered(0.42f * w, 0.4f * h, 0.09f * w), [
            new Station(-0.5f * l, 0.66f, 0.6f, 0, 0.62f * h), new Station(-0.38f * l, 1, 1, 0, 0.58f * h),
            new Station(0.26f * l, 1, 1, 0, 0.58f * h), new Station(0.42f * l, 0.86f, 0.86f, 0, 0.56f * h),
            new Station(0.5f * l, 0.5f, 0.5f, 0, 0.53f * h)], Axis.Z, Hull, HullDark);
        b.BevelBox(V(0, 0.33f * h, -0.04f * l), V(0.86f * w, 0.1f * h, 0.84f * l), 0.03f * w, HullDark, Dark);
        b.BevelBox(V(0, 0.82f * h, 0.3f * l), V(0.22f * w, 0.16f * h, 0.16f * l), 0.04f * w, HullLight);
        b.Box(V(0, 0.84f * h, 0.38f * l + 0.001f), V(0.18f * w, 0.07f * h, 0.002f * l), Glass);
        b.Box(V(0, 0.792f * h, -0.12f * l), V(0.12f * w, 0.012f * h, 0.42f * l), Team);

        // Side sponsons with team stripes, clamp arms under the belly.
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.BevelBox(V(sd * 0.44f * w, 0.36f * h, -0.06f * l), V(0.1f * w, 0.26f * h, 0.7f * l), 0.03f * w, Hull, HullLight);
            b.Box(V(sd * 0.492f * w, 0.4f * h, -0.06f * l), V(0.006f * w, 0.06f * h, 0.6f * l), Team);
            foreach (float z in (ReadOnlySpan<float>)[-0.34f, -0.12f, 0.1f, 0.3f])
            {
                b.Box(V(sd * 0.4f * w, 0.17f * h, z * l), V(0.03f * w, 0.18f * h, 0.03f * l), Hull);
                b.Box(V(sd * 0.37f * w, 0.08f * h, z * l), V(0.08f * w, 0.03f * h, 0.04f * l), Dark);
            }
        }
        foreach (float z in (ReadOnlySpan<float>)[-0.23f, 0.0f, 0.2f])
            b.Box(V(0, 0.2f * h, z * l), V(0.025f * w, 0.18f * h, 0.03f * l), Hull);

        // Stub wings fore and aft, a big ducted engine at each tip.
        foreach (float z in (ReadOnlySpan<float>)[-0.33f, 0.18f])
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            Vector2[] wing = [V2(sd * 0.18f * w, (z + 0.07f) * l), V2(sd * 0.36f * w, (z + 0.05f) * l), V2(sd * 0.36f * w, (z - 0.06f) * l), V2(sd * 0.18f * w, (z - 0.08f) * l)];
            b.Slab(wing, 0.6f * h, 0.68f * h, Hull);
            float px = sd * 0.395f * w;
            b.Cylinder(V(px, 0.5f * h, z * l), Axis.Y, 0.32f * h, 0.105f * w, 0.105f * w, 12, Hull, Black);
            b.Cylinder(V(px, 0.62f * h, z * l), Axis.Y, 0.07f * h, 0.109f * w, 0.109f * w, 12, Team);
            b.Cylinder(V(px, 0.82f * h, z * l), Axis.Y, 0.02f * h, 0.025f * w, 0.025f * w, 6, Dark);
            b.Cylinder(V(px, 0.49f * h, z * l), Axis.Y, 0.01f * h, 0.075f * w, 0.075f * w, 10, GlowC);
        }
        b.Box(V(-0.495f * w, 0.62f * h, -0.33f * l), V(0.02f * w, 0.03f * h, 0.02f * w), GlowR);
        b.Box(V(0.495f * w, 0.62f * h, -0.33f * l), V(0.02f * w, 0.03f * h, 0.02f * w), GlowG);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            Vector2[] fin = [V2(-0.5f * l, 0.74f * h), V2(-0.36f * l, 0.74f * h), V2(-0.45f * l, 1.0f * h), V2(-0.51f * l, 1.0f * h)];
            b.SideSlab(fin, sd * 0.12f * w, sd * 0.135f * w, Hull, Team);
        }
        b.Box(V(0, 0.48f * h, -0.502f * l), V(0.16f * w, 0.12f * h, 0.01f * l), GlowO);

        t = null;
        if (!turrets) return;
        // Small twin-gun mounts on the spine (railgun front, riot gun aft); both follow TurretHeading.
        t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.05f * h, 0.07f * w, 0.065f * w, 8, HullDark, Hull);
        t.Sphere(V(0, 0.05f * h, 0), V(0.055f * w, 0.07f * h, 0.055f * w), 3, 8, Hull, null, 0.5f);
        t.Box(V(0, 0.06f * h, -0.035f * w), V(0.07f * w, 0.012f * h, 0.03f * w), Team);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Cylinder(V(sd * 0.022f * w, 0.07f * h, 0.03f * w), Axis.Z, 0.06f * l, 0.012f * w, 0.01f * w, 5, Dark);
        pivots.Add(V(0, 0.78f * h, 0.17f * l));
        pivots.Add(V(0, 0.78f * h, -0.3f * l));
    }

    // ================================================================ T2 air staging facility

    /// <summary>Local position (model space, top of the pad) where docked aircraft k parks on the air staging
    /// (4 pads: front-left, front-right, rear-left, rear-right). False for k beyond the pads.</summary>
    public static bool StagingPad(int k, Vector3 size, out Vector3 pos)
    {
        pos = default;
        if ((uint)k >= 4u) return false;
        float sx = (k & 1) == 0 ? -1 : 1, sz = k < 2 ? 1 : -1;
        pos = new Vector3(sx * 0.25f * size.X, StagingPadTop * size.Y + 0.004f, sz * 0.25f * size.Z);
        return true;
    }

    private const float StagingPadTop = 0.42f;

    /// <summary>Air staging: low plinth, four raised landing pads on pillars (team rings, landing lights, cross
    /// markings) linked by fuel booms to a central control tower with a glass cab, antenna and beacon.</summary>
    private static void AirStaging(MeshBuilder b, float w, float h, float l)
    {
        Foundation(b, w * 0.98f, l * 0.98f, 0.05f * h, HullDark);
        float padY = StagingPadTop * h;
        for (int k = 0; k < 4; k++)
        {
            StagingPad(k, new Vector3(w, h, l), out var p);
            float px = p.X, pz = p.Z;
            b.Cylinder(V(px, 0.05f * h, pz), Axis.Y, padY - 0.13f * h, 0.07f * w, 0.05f * w, 8, Hull, null, null, 0);
            b.Cylinder(V(px, padY - 0.1f * h, pz), Axis.Y, 0.1f * h, 0.19f * w, 0.2f * w, 8, Hull, Pad);
            b.Cylinder(V(px, padY - 0.085f * h, pz), Axis.Y, 0.04f * h, 0.204f * w, 0.204f * w, 8, Team);
            b.Box(V(px, padY + 0.003f, pz), V(0.03f * w, 0.006f, 0.26f * w), HullLight);
            b.Box(V(px, padY + 0.003f, pz), V(0.26f * w, 0.006f, 0.03f * w), HullLight);
            for (int j = 0; j < 4; j++)
            {
                var d = new Vector2(MathF.Cos(j * MathF.PI * 0.5f + MathF.PI * 0.25f), MathF.Sin(j * MathF.PI * 0.5f + MathF.PI * 0.25f));
                b.Box(V(px + d.X * 0.165f * w, padY + 0.01f * h, pz + d.Y * 0.165f * w), V(0.02f * w, 0.02f * h, 0.02f * w), GlowY);
            }
            // Fuel boom from the tower to the pad.
            var rot = Matrix4x4.CreateRotationY(MathF.Atan2(px, pz));
            b.Box(V(0, padY - 0.06f * h, 0.24f * w), V(0.05f * w, 0.05f * h, 0.17f * w), HullDark, null, rot);
            b.Cylinder(V(0.03f * w, padY - 0.02f * h, 0.12f * w), Axis.Z, 0.17f * w, 0.012f * w, 0.012f * w, 5, GlowO, null, rot);
        }
        // Control tower.
        b.Frustum(V(0, 0.05f * h, 0), 0.24f * w, 0.24f * l, 0.14f * w, 0.14f * l, 0.66f * h, Hull, HullLight, null, 0.2f);
        b.Box(V(0, 0.4f * h, 0), V(0.205f * w, 0.06f * h, 0.205f * l), Team);
        b.BevelBox(V(0, 0.8f * h, 0), V(0.22f * w, 0.18f * h, 0.22f * l), 0.03f * w, Hull, HullLight);
        b.Box(V(0, 0.81f * h, 0), V(0.225f * w, 0.07f * h, 0.225f * l), Glass);
        b.Cylinder(V(0.05f * w, 0.89f * h, 0.05f * l), Axis.Y, 0.35f * h, 0.012f * w, 0.006f * w, 5, HullDark);
        b.Box(V(0.05f * w, 1.25f * h, 0.05f * l), V(0.02f * w, 0.04f * h, 0.02f * w), GlowR);
        b.Cylinder(V(-0.05f * w, 0.89f * h, -0.04f * l), Axis.Y, 0.05f * h, 0.04f * w, 0.05f * w, 8, HullDark, Dark);
        // Corner beacons on the plinth edge.
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
        foreach (int sz in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sx * 0.47f * w, 0.07f * h, sz * 0.47f * l), V(0.025f * w, 0.04f * h, 0.025f * w), GlowY);
    }

    // ================================================================ experimental aircraft carrier (Atlantis)

    private const float AtlantisDeck = 0.42f, AtlantisDeckTop = 0.47f;
    private static readonly float[] AtlantisCols = [-0.38f, -0.193f, 0.18f];
    private const int AtlantisRows = 9;

    /// <summary>Local position (model space, top of the flight deck) of docked aircraft k on the Atlantis: three
    /// rows of parking spots from the stern forward, leaving a clear lane on the centre line (the factory builds
    /// there) and the island side free. False when k is beyond the spots.</summary>
    public static bool AtlantisSpot(int k, Vector3 size, out Vector3 pos)
    {
        pos = default;
        if ((uint)k >= (uint)(AtlantisCols.Length * AtlantisRows)) return false;
        int row = k / AtlantisCols.Length, col = k % AtlantisCols.Length;
        pos = new Vector3(AtlantisCols[col] * size.X, AtlantisDeckTop * size.Y + 0.004f, (-0.4f + row * 0.095f) * size.Z);
        return true;
    }

    /// <summary>Atlantis: long carrier hull, full-length flight deck with markings and edge lights, island tower on
    /// +X with bridge, mast and radar dome, stern hangar door, bow torpedo tubes. Turrets = four SAM launchers on deck
    /// edge sponsons.</summary>
    private static MeshBuilder? Atlantis(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, bool turrets)
    {
        Vector2[] prof = [V2(-0.5f, 1.0f), V2(-0.47f, 0.4f), V2(-0.25f, 0.05f), V2(0, 0), V2(0.25f, 0.05f), V2(0.47f, 0.4f), V2(0.5f, 1.0f)];
        float bottom = -0.2f * h, deck = AtlantisDeck * h, hh = deck - bottom, top = AtlantisDeckTop * h;
        b.Loft(prof, [
            new Station(-0.5f * l, 0.8f * w, hh * 0.92f, 0, bottom + hh * 0.08f), new Station(-0.42f * l, 0.86f * w, hh, 0, bottom),
            new Station(0.2f * l, 0.86f * w, hh, 0, bottom), new Station(0.36f * l, 0.72f * w, hh * 1.01f, 0, bottom),
            new Station(0.46f * l, 0.38f * w, hh * 1.03f, 0, bottom), new Station(0.5f * l, 0.04f * w, hh * 1.05f, 0, bottom + hh * 0.03f)],
            Axis.Z, Hull, HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.43f * w, 0.1f * h, -0.04f * l), V(0.012f * w, 0.05f * h, 0.62f * l), Team);

        // Flight deck (overhangs the hull), markings, edge lights and team edge stripes.
        Vector2[] fd = [V2(-0.5f * w, -0.49f * l), V2(0.5f * w, -0.49f * l), V2(0.5f * w, 0.3f * l), V2(0.3f * w, 0.48f * l), V2(-0.3f * w, 0.48f * l), V2(-0.5f * w, 0.3f * l)];
        b.Slab(fd, deck, top, HullDark, Dark);
        for (int k = 0; k < 9; k++)
            b.Box(V(0, top + 0.002f, (-0.42f + k * 0.1f) * l), V(0.012f * w, 0.004f, 0.05f * l), HullLight);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Box(V(sd * 0.075f * w, top + 0.002f, 0.32f * l), V(0.01f * w, 0.004f, 0.24f * l), HullLight);
            b.Box(V(sd * 0.5f * w, (deck + top) * 0.5f, -0.1f * l), V(0.006f * w, (top - deck) * 0.6f, 0.76f * l), Team);
            for (int k = 0; k < 8; k++)
                b.Box(V(sd * 0.485f * w, top + 0.008f * h, (-0.46f + k * 0.1f) * l), V(0.012f * w, 0.012f * h, 0.006f * l), GlowY);
        }
        b.Box(V(0, top + 0.003f, -0.47f * l), V(0.3f * w, 0.004f, 0.01f * l), GlowY);
        b.Box(V(0, top + 0.002f, 0), V(0.14f * w, 0.004f, 0.08f * l), HullLight); // elevator on the centre lane
        b.Box(V(0, (deck + bottom) * 0.5f + 0.1f * h, -0.502f * l), V(0.42f * w, 0.2f * h, 0.006f * l), Dark); // hangar door

        // Island: base with team band, bridge tier with glass, mast, radar dome, beacon.
        float ix = 0.39f * w, iz = -0.07f * l;
        b.BevelBox(V(ix, top + 0.15f * h, iz), V(0.18f * w, 0.3f * h, 0.2f * l), 0.03f * w, HullLight, Hull);
        b.Box(V(ix, top + 0.24f * h, iz), V(0.182f * w, 0.04f * h, 0.202f * l), Team);
        b.BevelBox(V(ix, top + 0.36f * h, iz + 0.02f * l), V(0.15f * w, 0.12f * h, 0.12f * l), 0.025f * w, Hull);
        b.Box(V(ix, top + 0.37f * h, iz + 0.08f * l + 0.001f), V(0.13f * w, 0.05f * h, 0.004f * l), Glass);
        b.Box(V(ix - 0.075f * w - 0.001f, top + 0.37f * h, iz + 0.02f * l), V(0.004f * w, 0.05f * h, 0.1f * l), Glass);
        b.Frustum(V(ix, top + 0.42f * h, iz - 0.01f * l), 0.06f * w, 0.03f * l, 0.025f * w, 0.012f * l, 0.3f * h, HullDark);
        b.Box(V(ix, top + 0.6f * h, iz - 0.01f * l), V(0.16f * w, 0.015f * h, 0.012f * l), HullDark);
        b.Sphere(V(ix, top + 0.48f * h, iz - 0.07f * l), V(0.05f * w, 0.05f * w, 0.05f * w), 4, 8, HullLight);
        b.Box(V(ix, top + 0.74f * h, iz - 0.01f * l), V(0.02f * w, 0.02f * w, 0.02f * w), GlowR);
        b.Box(V(ix - 0.08f * w, top + 0.6f * h, iz - 0.01f * l), V(0.015f * w, 0.015f * w, 0.015f * w), GlowR);
        b.Box(V(ix + 0.08f * w, top + 0.6f * h, iz - 0.01f * l), V(0.015f * w, 0.015f * w, 0.015f * w), GlowG);

        // Bow torpedo tubes, flare racks aft.
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Cylinder(V(sd * 0.06f * w, 0.02f * h, 0.43f * l), Axis.Z, 0.03f * l, 0.025f * w, 0.025f * w, 8, Dark);
            b.Box(V(sd * 0.41f * w, deck - 0.06f * h, -0.44f * l), V(0.06f * w, 0.05f * h, 0.03f * l), HullDark, GlowO);
        }

        // SAM launcher sponsons at the deck edges (two per side).
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        foreach (float z in (ReadOnlySpan<float>)[0.24f, -0.36f])
        {
            b.Box(V(sd * 0.5f * w, deck - 0.03f * h, z * l), V(0.1f * w, 0.06f * h, 0.05f * l), HullDark, Pad);
            if (turrets) pivots.Add(V(sd * 0.5f * w, top - 0.01f * h, z * l));
        }
        if (!turrets) return null;
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.03f * h, 0.045f * w, 0.04f * w, 8, HullDark, Hull);
        var xf = PitchUp(0.45f) * T(0, 0.06f * h, 0);
        t.Box(V(0, 0, 0), V(0.07f * w, 0.05f * h, 0.035f * l), Hull, null, xf);
        t.Box(V(0, 0.026f * h, 0), V(0.072f * w, 0.006f * h, 0.03f * l), Team, null, xf);
        for (int k = 0; k < 4; k++)
            t.Box(V((-0.024f + (k & 1) * 0.048f) * w, ((k >> 1) - 0.5f) * 0.024f * h, 0.0177f * l), V(0.02f * w, 0.014f * h, 0.002f * l), GlowR, null, xf);
        return t;
    }
}
