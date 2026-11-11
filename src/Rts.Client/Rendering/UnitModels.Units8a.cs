using System.Numerics;

namespace Rts.Client.Rendering;

/// <summary>
/// (8a) UEF shapes: "mech_marine" (T1 light assault bot), "stealth_field" (T2 stealth field generator) and
/// "missile_sub" (T3 strategic missile submarine Ace). Same palette and conventions as <c>UnitModels.cs</c>
/// (forward +Z, origin at ground/water level). Dispatched from <see cref="Build"/>.
/// </summary>
public static partial class UnitModels
{
    /// <summary>Mech Marine: small bot on the shared mech legs, a slim torso (turret) with one riot gun arm and a
    /// backpack antenna.</summary>
    private static MeshBuilder MechMarine(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        MechLegs(b, w, h, l, 0.5f);
        pivots.Add(V(0, 0.5f * h, 0));
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.01f * h, 0), Axis.Y, 0.05f * h, 0.12f * w, 0.12f * w, 6, Dark);
        t.Loft(MeshBuilder.Chamfered(1, 1, 0.25f), [new Station(0.03f * h, 0.4f * w, 0.3f * l), new Station(0.24f * h, 0.52f * w, 0.4f * l),
                                                     new Station(0.34f * h, 0.36f * w, 0.28f * l)], Axis.Y, Hull, Team);
        t.Box(V(0, 0.27f * h, 0.19f * l), V(0.2f * w, 0.035f * h, 0.012f * l), GlowC);
        t.BevelBox(V(0, 0.2f * h, -0.22f * l), V(0.28f * w, 0.18f * h, 0.12f * l), 0.03f * w, HullDark);
        t.Cylinder(V(-0.08f * w, 0.28f * h, -0.24f * l), Axis.Y, 0.22f * h, 0.012f * w, 0.008f * w, 4, Dark);
        t.Box(V(0.32f * w, 0.17f * h, 0.08f * l), V(0.14f * w, 0.12f * h, 0.34f * l), HullDark);
        t.Cylinder(V(0.32f * w, 0.17f * h, 0.25f * l), Axis.Z, 0.22f * l, 0.045f * w, 0.04f * w, 6, Dark);
        t.Box(V(0.32f * w, 0.17f * h, 0.47f * l), V(0.06f * w, 0.06f * w, 0.01f * l), GlowO);
        t.BevelBox(V(-0.3f * w, 0.2f * h, 0), V(0.12f * w, 0.1f * h, 0.2f * l), 0.03f * w, Team);
        return t;
    }

    /// <summary>T2 stealth field generator: armoured plinth, tapered octagonal tower with team bands and violet field
    /// coils; the spinning head (turret, Spin) is a ring of emitter vanes around a glowing core.</summary>
    private static MeshBuilder StealthFieldGenerator(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        Foundation(b, w, l, 0.08f * h, HullDark);
        b.Frustum(V(0, 0.08f * h, 0), 0.7f * w, 0.7f * l, 0.5f * w, 0.5f * l, 0.12f * h, Hull, HullLight, null, 0.2f);
        b.Loft(MeshBuilder.NGon(8, 1, MathF.PI / 8), [new Station(0.2f * h, 0.26f * w, 0.26f * l), new Station(0.7f * h, 0.17f * w, 0.17f * l)], Axis.Y, Hull, HullDark);
        foreach (float y in (ReadOnlySpan<float>)[0.32f, 0.5f])
        {
            b.Cylinder(V(0, y * h, 0), Axis.Y, 0.05f * h, 0.24f * w, 0.23f * w, 8, Team);
            b.Cylinder(V(0, (y + 0.07f) * h, 0), Axis.Y, 0.025f * h, 0.225f * w, 0.215f * w, 8, Mat.G(170, 110, 255));
        }
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.BevelBox(V(sd * 0.3f * w, 0.17f * h, 0), V(0.12f * w, 0.14f * h, 0.4f * l), 0.03f * w, HullDark);
            b.Box(V(sd * 0.3f * w, 0.245f * h, 0), V(0.06f * w, 0.01f * h, 0.3f * l), GlowY);
        }
        pivots.Add(V(0, 0.72f * h, 0));
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.02f * h, 0), Axis.Y, 0.05f * h, 0.15f * w, 0.15f * w, 8, Dark);
        t.Sphere(V(0, 0.1f * h, 0), V(0.12f * w, 0.08f * h, 0.12f * l), 3, 8, Mat.G(190, 130, 255));
        for (int k = 0; k < 6; k++)
        {
            float a = k * MathF.Tau / 6;
            var xf = Matrix4x4.CreateRotationY(a);
            t.Box(V(0.3f * w, 0.1f * h, 0), V(0.05f * w, 0.18f * h, 0.16f * l), k % 2 == 0 ? HullLight : Hull, null, xf);
            t.Box(V(0.2f * w, 0.1f * h, 0), V(0.14f * w, 0.02f * h, 0.04f * l), HullDark, null, xf);
        }
        t.Cylinder(V(0, 0.2f * h, 0), Axis.Y, 0.04f * h, 0.06f * w, 0.02f * w, 6, Dark);
        return t;
    }

    /// <summary>Ace strategic missile submarine: long octagonal hull, a sail forward of a raised missile deck with two
    /// rows of hatches (team rims, amber launch lights), cruise missile cells fore and aft, a cruciform tail.</summary>
    private static void MissileSub(MeshBuilder b, float w, float h, float l)
    {
        var oct = MeshBuilder.NGon(8, 1, MathF.PI / 8);
        float cy = 0.1f * h;
        b.Loft(oct, [
            new Station(-0.5f * l, 0.06f * w, 0.06f * h, 0, cy), new Station(-0.4f * l, 0.34f * w, 0.3f * h, 0, cy),
            new Station(-0.2f * l, 0.5f * w, 0.42f * h, 0, cy), new Station(0.32f * l, 0.5f * w, 0.42f * h, 0, cy),
            new Station(0.45f * l, 0.34f * w, 0.3f * h, 0, cy), new Station(0.5f * l, 0.08f * w, 0.1f * h, 0, cy)], Axis.Z, HullDark, Dark);
        // Missile deck (hump) aft of the sail with 2 x 4 hatches.
        float deck = cy + 0.48f * h;
        b.BevelBox(V(0, cy + 0.36f * h, -0.08f * l), V(0.52f * w, 0.24f * h, 0.42f * l), 0.06f * w, Hull, HullLight);
        for (int k = 0; k < 8; k++)
        {
            float x = (k % 2 == 0 ? -0.12f : 0.12f) * w, z = (-0.24f + (k / 2) * 0.1f) * l;
            b.Cylinder(V(x, deck - 0.01f * h, z), Axis.Y, 0.02f * h, 0.1f * w, 0.1f * w, 8, Team);
            b.Cylinder(V(x, deck + 0.005f * h, z), Axis.Y, 0.012f * h, 0.08f * w, 0.08f * w, 8, Dark);
            b.Box(V(x + (x > 0 ? 0.09f : -0.09f) * w, deck + 0.012f * h, z), V(0.02f * w, 0.01f * h, 0.01f * l), GlowO);
        }
        // Sail with dive planes and periscope.
        b.BevelBox(V(0, cy + 0.6f * h, 0.2f * l), V(0.2f * w, 0.5f * h, 0.12f * l), 0.05f * w, Hull, Team);
        b.Box(V(0, cy + 0.8f * h, 0.262f * l), V(0.12f * w, 0.04f * h, 0.004f * l), Glass);
        b.Box(V(0, cy + 0.66f * h, 0.2f * l), V(0.7f * w, 0.03f * h, 0.05f * l), HullDark);
        b.Cylinder(V(0.04f * w, cy + 0.85f * h, 0.19f * l), Axis.Y, 0.25f * h, 0.015f * w, 0.012f * w, 5, Dark);
        // Cruise missile cells: fore deck and aft.
        foreach (float z in (ReadOnlySpan<float>)[0.33f, -0.33f])
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
                b.Box(V(sd * 0.1f * w, cy + 0.42f * h, z * l), V(0.12f * w, 0.02f * h, 0.04f * l), Black, Team);
        // Tail: horizontal and vertical fins, team tips.
        b.Box(V(0, cy, -0.45f * l), V(1.1f * w, 0.03f * h, 0.05f * l), HullDark);
        b.Box(V(0, cy, -0.45f * l), V(0.04f * w, 0.8f * h, 0.05f * l), HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.52f * w, cy, -0.45f * l), V(0.08f * w, 0.035f * h, 0.052f * l), Team);
        b.Cylinder(V(0, cy, -0.505f * l), Axis.Z, 0.01f * l, 0.05f * w, 0.04f * w, 6, GlowC);
    }
}
