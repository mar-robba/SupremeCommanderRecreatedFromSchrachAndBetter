using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>Models of the strategic/defensive structures: shield generators, missile silos/defenses, artillery.</summary>
public static partial class UnitModels
{
    private static readonly Color Hazard = Mat.M(196, 160, 40);

    // ================================================================ shields

    /// <summary>Shield generator: octagonal plinth, buttressed column, glowing emitter; turret = spinning prong ring.</summary>
    private static MeshBuilder ShieldGenerator(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        b.Cylinder(V(0, -0.35f, 0), Axis.Y, 0.35f + 0.1f * h, 0.56f * w, 0.5f * w, 8, HullDark, Hull);
        b.Cylinder(V(0, 0.1f * h, 0), Axis.Y, 0.05f * h, 0.47f * w, 0.44f * w, 8, Team);
        for (int k = 0; k < 4; k++)
        {
            var rot = Matrix4x4.CreateRotationY(k * MathF.PI * 0.5f + MathF.PI * 0.25f);
            b.Frustum(V(0.3f * w, 0.1f * h, 0), 0.2f * w, 0.12f * w, 0.06f * w, 0.08f * w, 0.42f * h, Hull, HullLight, rot, 0.2f);
        }
        b.Cylinder(V(0, 0.1f * h, 0), Axis.Y, 0.62f * h, 0.17f * w, 0.11f * w, 8, Hull, HullLight);
        b.Cylinder(V(0, 0.42f * h, 0), Axis.Y, 0.06f * h, 0.165f * w, 0.16f * w, 8, Team);
        b.Cylinder(V(0, 0.7f * h, 0), Axis.Y, 0.05f * h, 0.2f * w, 0.18f * w, 8, Dark);
        b.Sphere(V(0, 0.84f * h, 0), V(0.15f * w, 0.15f * w, 0.15f * w), 6, 8, GlowC);
        var t = new MeshBuilder();
        for (int k = 0; k < 3; k++)
        {
            var rot = Matrix4x4.CreateRotationY(k * MathF.Tau / 3f);
            t.Box(V(0, 0, 0.2f * w), V(0.06f * w, 0.05f * h, 0.24f * w), HullDark, null, rot);
            t.Cylinder(V(0, -0.02f * h, 0.32f * w), Axis.Y, 0.22f * h, 0.035f * w, 0.02f * w, 5, Hull, null, rot);
            t.Sphere(V(0, 0.22f * h, 0.32f * w), V(0.04f * w, 0.04f * w, 0.04f * w), 4, 6, GlowC, rot);
        }
        pivots.Add(V(0, 0.62f * h, 0));
        return t;
    }

    /// <summary>Mobile shield generator: hover skirt + hull with an emitter mast; turret = spinning emitter dish.</summary>
    private static MeshBuilder MobileShield(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        b.Frustum(V(0, 0.02f * h, 0), 1.0f * w, 1.0f * l, 0.92f * w, 0.94f * l, 0.22f * h, Black, Dark, null, 0.3f);
        var prof = MeshBuilder.Chamfered(0.86f * w, 0.32f * h, 0.12f * w);
        b.Loft(prof, [new Station(-0.46f * l, 0.9f, 0.85f, 0, 0.38f * h), new Station(0.2f * l, 1, 1, 0, 0.4f * h), new Station(0.48f * l, 0.7f, 0.6f, 0, 0.34f * h)], Axis.Z, Hull, HullLight);
        b.Box(V(0, 0.565f * h, -0.1f * l), V(0.5f * w, 0.02f * h, 0.42f * l), Team);
        b.Box(V(0, 0.5f * h, 0.36f * l), V(0.3f * w, 0.1f * h, 0.12f * l), Glass);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Cylinder(V(sd * 0.28f * w, 0.36f * h, -0.48f * l), Axis.Z, 0.06f * l, 0.1f * w, 0.12f * w, 6, Dark, GlowO);
        b.Cylinder(V(0, 0.55f * h, -0.05f * l), Axis.Y, 0.3f * h, 0.08f * w, 0.05f * w, 6, HullDark);
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.06f * h, 0.3f * w, 0.24f * w, 8, HullLight, Team);
        t.Sphere(V(0, 0.16f * h, 0), V(0.11f * w, 0.11f * w, 0.11f * w), 5, 8, GlowC);
        for (int k = 0; k < 3; k++)
        {
            var rot = Matrix4x4.CreateRotationY(k * MathF.Tau / 3f);
            t.Cylinder(V(0, 0.03f * h, 0.26f * w), Axis.Y, 0.14f * h, 0.03f * w, 0.02f * w, 5, Hull, GlowC, rot);
        }
        pivots.Add(V(0, 0.85f * h, -0.05f * l));
        return t;
    }

    // ================================================================ missiles

    /// <summary>Missile silo: tactical = armored box with a raised 4-tube launch rack; strategic = big bunker with hazard-striped blast doors.</summary>
    private static void MissileSilo(MeshBuilder b, float w, float h, float l, bool strategic)
    {
        Foundation(b, w, l, 0.08f * h, HullDark);
        if (!strategic)
        {
            b.BevelBox(V(0, 0.32f * h, -0.08f * l), V(0.8f * w, 0.5f * h, 0.7f * l), 0.08f * w, Hull, HullLight);
            b.Box(V(0, 0.575f * h, -0.25f * l), V(0.6f * w, 0.02f * h, 0.22f * l), Team);
            var rack = PitchUp(1.1f) * T(0, 0.55f * h, 0.12f * l);
            b.BevelBox(V(0, 0.2f * h, 0), V(0.62f * w, 0.4f * h, 0.62f * l), 0.06f * w, HullLight, null, rack);
            foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
            foreach (int sy in (ReadOnlySpan<int>)[-1, 1])
                b.Cylinder(V(sx * 0.15f * w, 0.2f * h + sy * 0.1f * h, 0.31f * l), Axis.Z, 0.02f * l, 0.1f * w, 0.1f * w, 6, Black, null, rack);
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
                b.Box(V(sd * 0.32f * w, 0.42f * h, 0.12f * l), V(0.04f * w, 0.4f * h, 0.3f * l), Team, null, rack);
            b.Box(V(0.3f * w, 0.62f * h, -0.36f * l), V(0.05f * w, 0.05f * w, 0.05f * w), GlowR);
            return;
        }
        b.Frustum(V(0, 0.05f * h, 0), 0.92f * w, 0.92f * l, 0.84f * w, 0.84f * l, 0.4f * h, Hull, HullLight, null, 0.12f);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            // Blast doors with hazard stripes along their outer edges.
            b.Box(V(sd * 0.19f * w, 0.47f * h, 0), V(0.36f * w, 0.06f * h, 0.6f * l), Dark);
            for (int k = -2; k <= 2; k++)
                b.Box(V(sd * 0.35f * w, 0.505f * h, k * 0.12f * l), V(0.04f * w, 0.01f * h, 0.06f * l), Hazard);
        }
        b.Box(V(0, 0.505f * h, 0), V(0.02f * w, 0.012f * h, 0.6f * l), GlowY);
        foreach (int sz in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(0, 0.46f * h, sz * 0.38f * l), V(0.7f * w, 0.04f * h, 0.06f * l), Team);
        // Corner vents + a control tower.
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
        foreach (int sz in (ReadOnlySpan<int>)[-1, 1])
            b.Cylinder(V(sx * 0.38f * w, 0.4f * h, sz * 0.38f * l), Axis.Y, 0.18f * h, 0.04f * w, 0.035f * w, 6, HullDark, GlowR);
        b.BevelBox(V(-0.36f * w, 0.75f * h, -0.36f * l), V(0.14f * w, 0.7f * h, 0.14f * l), 0.03f * w, HullLight);
        b.Box(V(-0.36f * w, 0.95f * h, -0.29f * l), V(0.1f * w, 0.08f * h, 0.01f * l), Glass);
    }

    /// <summary>Missile defense. TMD: plinth + yawing twin interceptor pods; anti-nuke: bunker with a domed silo cap and a radar mast.</summary>
    private static MeshBuilder? MissileDefenseModel(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, bool antiNuke)
    {
        if (antiNuke)
        {
            Foundation(b, w, l, 0.08f * h, HullDark);
            b.Frustum(V(0, 0.05f * h, 0), 0.9f * w, 0.9f * l, 0.8f * w, 0.8f * l, 0.3f * h, Hull, HullLight, null, 0.2f);
            b.Cylinder(V(0.06f * w, 0.35f * h, 0.06f * l), Axis.Y, 0.08f * h, 0.3f * w, 0.29f * w, 12, Team);
            b.Sphere(V(0.06f * w, 0.43f * h, 0.06f * l), V(0.27f * w, 0.18f * h, 0.27f * w), 6, 12, HullLight, null, 0.5f);
            b.Cylinder(V(0.06f * w, 0.59f * h, 0.06f * l), Axis.Y, 0.03f * h, 0.06f * w, 0.05f * w, 8, GlowC);
            b.Cylinder(V(-0.34f * w, 0.35f * h, -0.34f * l), Axis.Y, 0.6f * h, 0.04f * w, 0.025f * w, 6, HullDark);
            b.Box(V(-0.34f * w, 0.95f * h, -0.34f * l), V(0.2f * w, 0.12f * h, 0.03f * w), Hull, null, Matrix4x4.CreateRotationY(0.6f));
            foreach (int k in (ReadOnlySpan<int>)[-1, 1])
                b.Box(V(0.4f * w, 0.36f * h, k * 0.25f * l), V(0.03f * w, 0.03f * w, 0.03f * w), GlowC);
            return null;
        }
        DefenseBase(b, w, h, l);
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.12f * h, 0.3f * w, 0.26f * w, 8, Hull, Team);
        t.Cylinder(V(0, 0.12f * h, 0), Axis.Y, 0.22f * h, 0.08f * w, 0.07f * w, 6, HullDark);
        var pitch = PitchUp(0.9f) * T(0, 0.34f * h, 0);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.BevelBox(V(sd * 0.2f * w, 0, 0), V(0.16f * w, 0.18f * h, 0.42f * l), 0.03f * w, HullLight, null, pitch);
            foreach (int sy in (ReadOnlySpan<int>)[-1, 1])
                t.Cylinder(V(sd * 0.2f * w, sy * 0.045f * h, 0.21f * l), Axis.Z, 0.01f * l, 0.035f * w, 0.035f * w, 5, Black, null, pitch);
            t.Box(V(sd * 0.285f * w, 0, 0), V(0.01f * w, 0.1f * h, 0.3f * l), Team, null, pitch);
        }
        // Small tracking radar on top.
        t.Box(V(0, 0.42f * h, -0.04f * l), V(0.22f * w, 0.12f * h, 0.02f * l), Dark);
        t.Box(V(0, 0.42f * h, -0.02f * l), V(0.04f * w, 0.04f * w, 0.01f * l), GlowR);
        pivots.Add(V(0, 0.36f * h, 0));
        return t;
    }

    // ================================================================ artillery

    /// <summary>Artillery installation: heavy stepped plinth + armored gun house with a long barrel at a high angle.</summary>
    private static MeshBuilder ArtilleryStructure(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        Foundation(b, w, l, 0.06f * h, HullDark);
        b.Frustum(V(0, 0.04f * h, 0), 0.86f * w, 0.86f * l, 0.66f * w, 0.66f * l, 0.24f * h, Hull, HullLight, null, 0.22f);
        b.Cylinder(V(0, 0.26f * h, 0), Axis.Y, 0.05f * h, 0.33f * w, 0.32f * w, 12, Team);
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.08f * h, 0.33f * w, 0.3f * w, 12, Dark);
        var house = MeshBuilder.Chamfered(0.5f * w, 0.28f * h, 0.1f * w);
        t.Loft(house, [new Station(-0.3f * l, 0.9f, 0.8f, 0, 0.22f * h), new Station(0.08f * l, 1, 1, 0, 0.22f * h), new Station(0.2f * l, 0.8f, 0.7f, 0, 0.2f * h)], Axis.Z, Hull, HullLight);
        t.Box(V(0, 0.362f * h, -0.08f * l), V(0.3f * w, 0.01f * h, 0.26f * l), Team);
        var barrel = PitchUp(0.55f) * T(0, 0.24f * h, 0.12f * l);
        t.Cylinder(V(0, 0, 0), Axis.Z, 0.18f * l, 0.1f * w, 0.09f * w, 8, HullDark, null, barrel);
        t.Cylinder(V(0, 0, 0.18f * l), Axis.Z, 0.62f * l, 0.06f * w, 0.05f * w, 8, Hull, null, barrel);
        t.Cylinder(V(0, 0, 0.8f * l), Axis.Z, 0.08f * l, 0.075f * w, 0.075f * w, 8, Dark, Black, barrel);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Cylinder(V(sd * 0.11f * w, -0.04f * h, -0.02f * l), Axis.Z, 0.22f * l, 0.03f * w, 0.03f * w, 6, HullDark, null, barrel);
        pivots.Add(V(0, 0.3f * h, 0));
        return t;
    }
}
