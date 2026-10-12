using System.Numerics;
using Rts.Sim.Data;

namespace Rts.Client.Rendering;

/// <summary>
/// T2/T3 shapes (wave 3, tech tiers). Each family reuses the T1 building blocks and adds visibly more mass and
/// detail per tier: extra stacks/modules on factories, pumps on extractors, cooling towers on generators,
/// multi-barrel turrets, heavier hulls. Dispatched from <see cref="Build"/> by Visual.Shape.
/// </summary>
public static partial class UnitModels
{
    /// <summary>Builds the body into b and returns the turret mesh (or null), adding its pivots and turret mode.</summary>
    private static MeshBuilder? TechShape(UnitBlueprint bp, MeshBuilder b, float w, float h, float l, bool hasWeapon,
                                          List<Vector3> pivots, ref TurretMode mode, ref float spin)
    {
        int tier = bp.Has(Rts.Sim.Data.UnitCategory.Tech3) ? 3 : 2;
        MeshBuilder? t = null;
        switch (bp.Visual.Shape)
        {
            case "factory2":
            case "factory3":
                FactoryTier(b, w, h, l, bp.Has(Rts.Sim.Data.UnitCategory.Air), bp.Has(Rts.Sim.Data.UnitCategory.Naval), bp.Visual.Shape == "factory3" ? 3 : 2);
                break;
            case "mex2":
            case "mex3":
                MexTier(b, w, h, l, bp.Visual.Shape == "mex3" ? 3 : 2);
                t = new MeshBuilder(); MexRotor(t, w, h, l); pivots.Add(V(0, 0.80f * h, 0));
                mode = TurretMode.Spin; spin = bp.Visual.Shape == "mex3" ? 3.2f : 2.6f;
                break;
            case "pgen2":
            case "pgen3":
                PowerTier(b, w, h, l, bp.Visual.Shape == "pgen3" ? 3 : 2);
                break;
            case "massfab":
                MassFab(b, w, h, l, tier);
                break;
            case "defense2":
            case "defense3":
            {
                int dt = bp.Visual.Shape == "defense3" ? 3 : 2;
                DefenseTierBase(b, w, h, l, dt);
                t = new MeshBuilder();
                bool aa = bp.Has(Rts.Sim.Data.UnitCategory.AntiAir);
                if (aa && dt == 2) FlakTurret(t, w, h, l);
                else if (aa) SamTurret(t, w, h, l);
                else if (dt == 2) TriadTurret(t, w, h, l);
                else GatlingTurret(t, w, h, l);
                pivots.Add(V(0, (dt == 3 ? 0.42f : 0.38f) * h, 0));
                break;
            }
            case "omni":
                OmniBase(b, w, h, l);
                t = new MeshBuilder(); OmniRing(t, w, h, l); pivots.Add(V(0, 0.6f * h, 0));
                mode = TurretMode.Spin; spin = 0.8f;
                break;
            case "engineer2":
                EngineerTier(b, w, h, l, tier);
                break;
            case "heavy_tank":
                HeavyTank(b, w, h, l);
                if (hasWeapon) { t = new MeshBuilder(); HeavyTankTurret(t, w, h, l); pivots.Add(V(0, 0.60f * h, -0.08f * l)); }
                break;
            case "hover_tank":
                HoverTank(b, w, h, l);
                if (hasWeapon) { t = new MeshBuilder(); HoverTurret(t, w, h, l); pivots.Add(V(0, 0.62f * h, -0.06f * l)); }
                break;
            case "missile_launcher":
                WheeledChassis(b, w, h, l, true);
                b.Box(V(0, 0.47f * h, -0.18f * l), V(0.56f * w, 0.08f * h, 0.34f * l), HullDark);
                if (hasWeapon) { t = new MeshBuilder(); MissilePod(t, w, h, l); pivots.Add(V(0, 0.56f * h, -0.16f * l)); }
                break;
            case "flak":
                Tank(b, w, h, l);
                b.Box(V(0, 0.5f * h, 0.2f * l), V(0.5f * w, 0.05f * h, 0.12f * l), HullDark);
                if (hasWeapon) { t = new MeshBuilder(); MobileFlakTurret(t, w, h, l); pivots.Add(V(0, 0.60f * h, -0.08f * l)); }
                break;
            case "heavy_bot":
                MechLegs(b, w, h, l, 0.48f);
                foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
                    b.BevelBox(V(sd * 0.2f * w, 0.24f * h, 0.12f * l), V(0.2f * w, 0.14f * h, 0.06f * l), 0.02f * w, Team);
                t = new MeshBuilder(); HeavyBotTorso(t, w, h, l); pivots.Add(V(0, 0.48f * h, 0));
                break;
            case "heavy_artillery":
                HeavyArtillery(b, w, h, l);
                if (hasWeapon) { t = new MeshBuilder(); HeavyArtilleryTurret(t, w, h, l); pivots.Add(V(0, 0.55f * h, -0.14f * l)); }
                break;
            case "asf": AirSuperiority(b, w, h, l); break;
            case "strat_bomber": StrategicBomber(b, w, h, l); break;
            case "spy_plane": SpyPlane(b, w, h, l); break;
        }
        return t;
    }

    // ================================================================ structures

    private static void FactoryTier(MeshBuilder b, float w, float h, float l, bool air, bool naval, int tier)
    {
        Factory(b, w, h, l, air, naval);
        float y0 = 0.1f * h;
        float roof = y0 + 0.6f * h;
        // Side buttresses with team bands along the hall.
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Frustum(V(sd * 0.445f * w, y0, -0.12f * l), 0.07f * w, 0.5f * l, 0.04f * w, 0.44f * l, 0.5f * h, HullDark, Hull, null, 0.1f);
            b.Box(V(sd * 0.445f * w, y0 + 0.36f * h, -0.12f * l), V(0.065f * w, 0.05f * h, 0.46f * l), Team);
        }
        // Roof module with stacks.
        b.BevelBox(V(0, roof + 0.07f * h, -0.14f * l), V(0.34f * w, 0.14f * h, 0.26f * l), 0.03f * w, HullLight);
        b.Box(V(0, roof + 0.145f * h, -0.14f * l), V(0.24f * w, 0.01f * h, 0.16f * l), Team);
        int stacks = tier == 3 ? 4 : 2;
        for (int i = 0; i < stacks; i++)
        {
            float x = (tier == 3 ? -0.3f + i * 0.2f : (i == 0 ? -0.24f : 0.24f)) * w;
            b.Cylinder(V(x, roof, -0.34f * l), Axis.Y, 0.5f * h, 0.05f * w, 0.04f * w, 8, Hull, Dark);
            b.Cylinder(V(x, roof + 0.38f * h, -0.34f * l), Axis.Y, 0.05f * h, 0.052f * w, 0.052f * w, 8, Team);
            b.Cylinder(V(x, roof + 0.5f * h, -0.34f * l), Axis.Y, 0.01f, 0.03f * w, 0.03f * w, 8, GlowO);
        }
        // Extra lights on the lintel.
        for (int i = -2; i <= 2; i++)
            b.Box(V(i * 0.1f * w, y0 + 0.535f * h, 0.212f * l), V(0.03f * w, 0.02f * h, 0.01f * l), GlowC);
        if (tier < 3) return;
        // T3: reactor dome on the roof, armored rear block, antenna mast.
        b.Cylinder(V(0, roof + 0.14f * h, -0.14f * l), Axis.Y, 0.05f * h, 0.13f * w, 0.12f * w, 10, HullDark);
        b.Sphere(V(0, roof + 0.19f * h, -0.14f * l), V(0.11f * w, 0.13f * h, 0.11f * w), 4, 10, GlowC, null, 0.5f);
        b.BevelBox(V(0, y0 + 0.34f * h, -0.465f * l), V(0.92f * w, 0.68f * h, 0.07f * l), 0.02f * w, HullDark);
        b.Box(V(0, y0 + 0.5f * h, -0.502f * l), V(0.7f * w, 0.06f * h, 0.01f * l), Team);
        b.Cylinder(V(-0.38f * w, y0 + 0.92f * h, -0.36f * l), Axis.Y, 0.55f * h, 0.012f * w, 0.008f * w, 4, HullLight);
        b.Box(V(-0.38f * w, y0 + 1.48f * h, -0.36f * l), V(0.03f * w, 0.03f * h, 0.03f * w), GlowR);
    }

    private static void MexTier(MeshBuilder b, float w, float h, float l, int tier)
    {
        Mex(b, w, h, l);
        // Pumps on the diagonals and a team band around the core.
        foreach (var (sx, sz) in (ReadOnlySpan<(int, int)>)[(-1, 0), (1, 0), (0, -1), (0, 1)])
        {
            b.Cylinder(V(sx * 0.38f * w, 0.15f * h, sz * 0.38f * l), Axis.Y, 0.32f * h, 0.07f * w, 0.06f * w, 6, HullLight, HullDark);
            b.Box(V(sx * 0.38f * w, 0.48f * h, sz * 0.38f * l), V(0.06f * w, 0.03f * h, 0.06f * l), GlowG);
            b.Box(V(sx * 0.2f * w, 0.3f * h, sz * 0.2f * l), V(sx != 0 ? 0.3f * w : 0.05f * w, 0.05f * h, sz != 0 ? 0.3f * l : 0.05f * l), HullDark);
        }
        b.Cylinder(V(0, 0.6f * h, 0), Axis.Y, 0.06f * h, 0.215f * w, 0.21f * w, 8, Team);
        if (tier < 3) return;
        // T3: armored octagonal cowling and glowing intake tubes.
        var ring = MeshBuilder.NGon(8, 1, MathF.PI / 8);
        b.Loft(ring, [new Station(0.1f * h, 0.5f * w, 0.5f * l), new Station(0.3f * h, 0.46f * w, 0.46f * l)], Axis.Y, HullDark, Hull);
        b.Loft(ring, [new Station(0.3f * h, 0.47f * w, 0.47f * l), new Station(0.34f * h, 0.47f * w, 0.47f * l)], Axis.Y, Team);
        foreach (var (sx, sz) in (ReadOnlySpan<(int, int)>)[(-1, -1), (1, -1), (-1, 1), (1, 1)])
            b.Cylinder(V(sx * 0.16f * w, 0.3f * h, sz * 0.16f * l), Axis.Y, 0.55f * h, 0.03f * w, 0.03f * w, 6, GlowG);
    }

    private static void PowerTier(MeshBuilder b, float w, float h, float l, int tier)
    {
        b.Frustum(V(0, -0.3f, 0), 0.98f * w, 0.98f * l, 0.9f * w, 0.9f * l, 0.3f + 0.08f * h, HullDark, Pad, null, 0.15f);
        var ring = MeshBuilder.NGon(12, 1, MathF.PI / 12);
        float r = tier == 3 ? 0.26f : 0.22f;
        // Central reactor tower (hourglass), team band, glowing crown.
        b.Loft(ring, [new Station(0.08f * h, r * w, r * l), new Station(0.5f * h, r * 0.72f * w, r * 0.72f * l), new Station(0.92f * h, r * 0.86f * w, r * 0.86f * l)], Axis.Y, Hull, HullDark);
        b.Cylinder(V(0, 0.22f * h, 0), Axis.Y, 0.07f * h, r * 0.95f * w, r * 0.92f * w, 12, Team);
        b.Cylinder(V(0, 0.78f * h, 0), Axis.Y, 0.04f * h, r * 0.82f * w, r * 0.84f * w, 12, GlowY);
        b.Cylinder(V(0, 0.92f * h, 0), Axis.Y, 0.005f, r * 0.78f * w, r * 0.78f * w, 12, GlowY);
        // Cooling towers on the corners with pipes to the core.
        float cr = tier == 3 ? 0.1f : 0.11f;
        foreach (var (sx, sz) in (ReadOnlySpan<(int, int)>)[(-1, -1), (1, -1), (-1, 1), (1, 1)])
        {
            float x = sx * 0.33f * w, z = sz * 0.33f * l;
            var small = MeshBuilder.NGon(8, 1, MathF.PI / 8);
            b.Loft(small, [new Station(0.08f * h, cr * w, cr * l, x, z), new Station(0.4f * h, cr * 0.7f * w, cr * 0.7f * l, x, z), new Station(0.6f * h, cr * 0.85f * w, cr * 0.85f * l, x, z)], Axis.Y, HullLight, HullDark);
            b.Cylinder(V(x, 0.6f * h, z), Axis.Y, 0.005f, cr * 0.75f * w, cr * 0.75f * w, 8, GlowY);
            b.Box(V(x * 0.55f, 0.16f * h, z * 0.55f), V(MathF.Abs(x) * 0.6f + 0.04f * w, 0.05f * h, MathF.Abs(z) * 0.6f + 0.04f * l), Dark);
        }
        if (tier < 3) return;
        // T3: side reactors with glowing cores and a top antenna.
        foreach (var (sx, sz) in (ReadOnlySpan<(int, int)>)[(0, -1), (0, 1), (-1, 0), (1, 0)])
        {
            float x = sx * 0.36f * w, z = sz * 0.36f * l;
            b.BevelBox(V(x, 0.16f * h, z), V(0.14f * w, 0.18f * h, 0.14f * l), 0.02f * w, Hull);
            b.Sphere(V(x, 0.26f * h, z), V(0.05f * w, 0.06f * h, 0.05f * l), 3, 8, GlowC);
        }
        b.Cylinder(V(0, 0.92f * h, 0), Axis.Y, 0.25f * h, 0.012f * w, 0.006f * w, 4, HullLight);
        b.Box(V(0, 1.17f * h, 0), V(0.025f * w, 0.025f * h, 0.025f * w), GlowR);
    }

    private static void MassFab(MeshBuilder b, float w, float h, float l, int tier)
    {
        b.Frustum(V(0, -0.3f, 0), 0.96f * w, 0.96f * l, 0.86f * w, 0.86f * l, 0.3f + 0.12f * h, HullDark, Pad, null, 0.2f);
        // Central fabrication chamber: dark frame around a glowing green core.
        b.BevelBox(V(0, 0.42f * h, 0), V(0.44f * w, 0.56f * h, 0.44f * l), 0.05f * w, Hull, HullLight);
        b.Box(V(0, 0.42f * h, 0), V(0.46f * w, 0.3f * h, 0.3f * l), GlowG);
        b.Box(V(0, 0.42f * h, 0), V(0.3f * w, 0.3f * h, 0.46f * l), GlowG);
        b.Box(V(0, 0.71f * h, 0), V(0.32f * w, 0.04f * h, 0.32f * l), Team);
        b.Frustum(V(0, 0.72f * h, 0), 0.3f * w, 0.3f * l, 0.12f * w, 0.12f * l, 0.2f * h, HullDark, Dark);
        // Energy conduits from the corners.
        foreach (var (sx, sz) in (ReadOnlySpan<(int, int)>)[(-1, -1), (1, -1), (-1, 1), (1, 1)])
        {
            float x = sx * 0.35f * w, z = sz * 0.35f * l;
            b.Cylinder(V(x, 0.1f * h, z), Axis.Y, (tier == 3 ? 0.75f : 0.5f) * h, 0.06f * w, 0.05f * w, 6, HullDark, Dark);
            b.Cylinder(V(x, (tier == 3 ? 0.6f : 0.4f) * h, z), Axis.Y, 0.06f * h, 0.065f * w, 0.065f * w, 6, GlowY);
            if (tier == 3) b.Sphere(V(x, 0.88f * h, z), V(0.06f * w, 0.06f * h, 0.06f * l), 3, 8, GlowG);
        }
    }

    private static void DefenseTierBase(MeshBuilder b, float w, float h, float l, int tier)
    {
        float top = tier == 3 ? 0.42f : 0.38f;
        b.Frustum(V(0, -0.3f, 0), 0.98f * w, 0.98f * l, 0.74f * w, 0.74f * l, 0.3f + top * h, HullDark, Hull, null, 0.2f);
        b.Frustum(V(0, top * 0.55f * h, 0), 0.86f * w, 0.86f * l, 0.83f * w, 0.83f * l, 0.07f * h, Team, null, null, 0.2f);
        // Armor buttresses on the corners.
        foreach (var (sx, sz) in (ReadOnlySpan<(int, int)>)[(-1, -1), (1, -1), (-1, 1), (1, 1)])
        {
            b.Frustum(V(sx * 0.38f * w, 0, sz * 0.38f * l), 0.18f * w, 0.18f * l, 0.1f * w, 0.1f * l, top * 0.9f * h, Hull, HullDark, null, 0.2f);
            if (tier == 3) b.Box(V(sx * 0.38f * w, top * 0.9f * h + 0.02f * h, sz * 0.38f * l), V(0.05f * w, 0.04f * h, 0.05f * l), GlowR);
        }
    }

    private static void TriadTurret(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.07f * h, 0.34f * w, 0.32f * w, 10, Dark);
        var prof = MeshBuilder.Chamfered(0.56f * w, 0.32f * h, 0.1f * w);
        t.Loft(prof, [new Station(-0.26f * l, 0.9f, 0.85f, 0, 0.22f * h), new Station(0.1f * l, 1, 1, 0, 0.22f * h), new Station(0.24f * l, 0.72f, 0.62f, 0, 0.2f * h)], Axis.Z, Hull);
        t.Box(V(0, 0.385f * h, -0.06f * l), V(0.3f * w, 0.012f * h, 0.26f * l), Team);
        for (int i = -1; i <= 1; i++)
        {
            t.Cylinder(V(i * 0.12f * w, 0.21f * h, 0.2f * l), Axis.Z, 0.46f * l, 0.045f * w, 0.04f * w, 6, HullDark);
            t.Cylinder(V(i * 0.12f * w, 0.21f * h, 0.62f * l), Axis.Z, 0.06f * l, 0.055f * w, 0.055f * w, 6, Dark);
        }
    }

    private static void GatlingTurret(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.08f * h, 0.36f * w, 0.34f * w, 12, Dark);
        t.BevelBox(V(0, 0.24f * h, -0.04f * l), V(0.56f * w, 0.34f * h, 0.5f * l), 0.08f * w, Hull, HullLight);
        t.Box(V(0, 0.415f * h, -0.08f * l), V(0.34f * w, 0.012f * h, 0.3f * l), Team);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.BevelBox(V(sd * 0.3f * w, 0.24f * h, -0.06f * l), V(0.08f * w, 0.28f * h, 0.38f * l), 0.02f * w, HullDark);
        // Rotary barrel cluster.
        t.Cylinder(V(0, 0.22f * h, 0.2f * l), Axis.Z, 0.08f * l, 0.14f * w, 0.14f * w, 8, HullDark);
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.Tau / 6;
            t.Cylinder(V(MathF.Cos(a) * 0.08f * w, 0.22f * h + MathF.Sin(a) * 0.08f * w, 0.28f * l), Axis.Z, 0.38f * l, 0.025f * w, 0.025f * w, 5, Dark);
        }
        t.Cylinder(V(0, 0.22f * h, 0.6f * l), Axis.Z, 0.04f * l, 0.12f * w, 0.12f * w, 8, HullDark);
    }

    private static void FlakTurret(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.1f * h, 0.33f * w, 0.29f * w, 10, Hull, Team);
        var xf = PitchUp(0.55f) * T(0, 0.26f * h, -0.04f * l);
        t.BevelBox(V(0, 0, 0), V(0.42f * w, 0.24f * h, 0.34f * l), 0.05f * w, HullLight, null, xf);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Cylinder(V(sd * 0.11f * w, 0, 0.14f * l), Axis.Z, 0.5f * l, 0.045f * w, 0.035f * w, 6, Dark, null, xf);
            t.Cylinder(V(sd * 0.11f * w, 0, 0.6f * l), Axis.Z, 0.06f * l, 0.06f * w, 0.06f * w, 6, HullDark, null, xf);
            t.Box(V(sd * 0.212f * w, 0, 0), V(0.01f * w, 0.14f * h, 0.24f * l), Team, null, xf);
        }
    }

    private static void SamTurret(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.1f * h, 0.36f * w, 0.32f * w, 12, Hull, Team);
        t.Box(V(0, 0.16f * h, -0.02f * l), V(0.16f * w, 0.14f * h, 0.16f * l), HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            var xf = PitchUp(0.65f) * T(sd * 0.2f * w, 0.28f * h, -0.02f * l);
            t.BevelBox(V(0, 0, 0.04f * l), V(0.22f * w, 0.26f * h, 0.5f * l), 0.03f * w, HullLight, null, xf);
            t.Box(V(sd * 0.112f * w, 0, 0.04f * l), V(0.01f * w, 0.18f * h, 0.4f * l), Team, null, xf);
            for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                t.Box(V((i - 0.5f) * 0.1f * w, (j - 0.5f) * 0.12f * h, 0.295f * l), V(0.07f * w, 0.08f * h, 0.012f * l), j == 1 ? GlowO : Dark, null, xf);
        }
    }

    private static void OmniBase(MeshBuilder b, float w, float h, float l)
    {
        b.Frustum(V(0, -0.3f, 0), 0.96f * w, 0.96f * l, 0.76f * w, 0.76f * l, 0.3f + 0.06f * h, HullDark, Pad, null, 0.2f);
        var oct = MeshBuilder.NGon(8, 1, MathF.PI / 8);
        b.Loft(oct, [new Station(0.06f * h, 0.3f * w, 0.3f * l), new Station(0.5f * h, 0.14f * w, 0.14f * l), new Station(0.95f * h, 0.04f * w, 0.04f * l)], Axis.Y, Hull, HullLight);
        b.Cylinder(V(0, 0.16f * h, 0), Axis.Y, 0.05f * h, 0.27f * w, 0.26f * w, 8, Team);
        b.Sphere(V(0, 0.8f * h, 0), V(0.09f * w, 0.05f * h, 0.09f * w), 3, 8, GlowC);
        b.Box(V(0, 0.97f * h, 0), V(0.04f * w, 0.03f * h, 0.04f * w), GlowR);
        foreach (var (sx, sz) in (ReadOnlySpan<(int, int)>)[(-1, -1), (1, -1), (-1, 1), (1, 1)])
        {
            var leg = MeshBuilder.Rect(0.06f * w, 0.06f * l);
            b.Loft(leg, [new Station(0.06f * h, 1, 1, sx * 0.36f * w, sz * 0.36f * l), new Station(0.45f * h, 1, 1, sx * 0.1f * w, sz * 0.1f * l)], Axis.Y, HullDark);
        }
    }

    private static void OmniRing(MeshBuilder t, float w, float h, float l)
    {
        // A segmented sensor ring on two arms, spinning around the spire.
        for (int i = 0; i < 12; i++)
        {
            var xf = Matrix4x4.CreateRotationY(i * MathF.Tau / 12);
            t.Box(V(0, 0, 0.42f * w), V(0.2f * w, 0.05f * h, 0.05f * w), i % 3 == 0 ? Team : HullLight, null, xf);
        }
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Box(V(sd * 0.21f * w, 0, 0), V(0.4f * w, 0.02f * h, 0.03f * w), HullDark);
            t.Box(V(sd * 0.43f * w, 0, 0), V(0.05f * w, 0.07f * h, 0.07f * w), GlowC);
        }
    }

    // ================================================================ land units

    private static void EngineerTier(MeshBuilder b, float w, float h, float l, int tier)
    {
        Engineer(b, w, h, l);
        // Armored skirts with team stripes, second (left) crane arm, sensor mast.
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Box(V(sd * 0.49f * w, 0.3f * h, 0), V(0.03f * w, 0.24f * h, 0.8f * l), HullDark);
            b.Box(V(sd * 0.505f * w, 0.34f * h, 0.05f * l), V(0.01f * w, 0.06f * h, 0.5f * l), Team);
        }
        b.Cylinder(V(-0.16f * w, 0.66f * h, -0.24f * l), Axis.Y, 0.1f * h, 0.08f * w, 0.07f * w, 6, HullDark);
        var boom = Matrix4x4.CreateRotationX(-0.75f) * T(-0.16f * w, 0.74f * h, -0.24f * l);
        b.Box(V(0, 0, 0.18f * l), V(0.06f * w, 0.06f * h, 0.38f * l), HullLight, null, boom);
        b.Box(V(0, 0, 0.38f * l), V(0.09f * w, 0.09f * h, 0.05f * l), tier == 3 ? GlowC : GlowY, null, boom);
        b.Cylinder(V(0.24f * w, 0.66f * h, -0.36f * l), Axis.Y, 0.5f * h, 0.012f * w, 0.008f * w, 4, HullLight);
        b.Box(V(0.24f * w, 1.16f * h, -0.36f * l), V(0.04f * w, 0.04f * h, 0.04f * w), GlowC);
        if (tier == 3)
        {
            b.BevelBox(V(0, 0.72f * h, -0.32f * l), V(0.5f * w, 0.14f * h, 0.24f * l), 0.03f * w, Hull);
            b.Box(V(0, 0.795f * h, -0.32f * l), V(0.36f * w, 0.01f * h, 0.16f * l), Team);
        }
    }

    private static void HeavyTank(MeshBuilder b, float w, float h, float l)
    {
        Tracks(b, w, h, l, 0.25f * w, 0.42f * h);
        var hull = MeshBuilder.Chamfered(0.84f * w, 0.28f * h, 0.07f * w);
        b.Loft(hull, [new Station(-0.5f * l, 0.96f, 1, 0, 0.48f * h), new Station(0.26f * l, 1, 1, 0, 0.48f * h), new Station(0.5f * l, 0.9f, 0.4f, 0, 0.42f * h)], Axis.Z, Hull);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            // Side armor skirts over the tracks.
            b.Box(V(sd * 0.47f * w, 0.36f * h, 0.02f * l), V(0.06f * w, 0.18f * h, 0.84f * l), HullDark);
            b.Box(V(sd * 0.502f * w, 0.4f * h, 0.02f * l), V(0.01f * w, 0.06f * h, 0.6f * l), Team);
            b.Box(V(sd * 0.3f * w, 0.44f * h, 0.495f * l), V(0.08f * w, 0.05f * h, 0.015f * l), GlowY);
        }
        b.Box(V(0, 0.63f * h, -0.4f * l), V(0.56f * w, 0.04f * h, 0.16f * l), Dark);
    }

    private static void HeavyTankTurret(MeshBuilder t, float w, float h, float l)
    {
        var prof = MeshBuilder.Chamfered(0.62f * w, 0.28f * h, 0.08f * w);
        t.Loft(prof, [new Station(-0.28f * l, 0.9f, 0.9f, 0, 0.14f * h), new Station(0.08f * l, 1, 1, 0, 0.14f * h), new Station(0.2f * l, 0.8f, 0.72f, 0, 0.13f * h)], Axis.Z, Hull);
        t.Box(V(0, 0.285f * h, -0.06f * l), V(0.34f * w, 0.012f * h, 0.24f * l), Team);
        t.Cylinder(V(0.14f * w, 0.28f * h, -0.16f * l), Axis.Y, 0.05f * h, 0.07f * w, 0.06f * w, 6, HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Cylinder(V(sd * 0.08f * w, 0.14f * h, 0.2f * l), Axis.Z, 0.48f * l, 0.04f * w, 0.035f * w, 6, HullDark);
            t.Cylinder(V(sd * 0.08f * w, 0.14f * h, 0.64f * l), Axis.Z, 0.07f * l, 0.05f * w, 0.05f * w, 6, Dark);
        }
    }

    private static void HoverTank(MeshBuilder b, float w, float h, float l)
    {
        // Hover skirt with a glowing underside, sleek wedge hull.
        b.Frustum(V(0, 0.04f * h, 0), 0.96f * w, 0.96f * l, 0.88f * w, 0.9f * l, 0.2f * h, Black, HullDark, null, 0.25f);
        b.Box(V(0, 0.03f * h, 0), V(0.8f * w, 0.02f * h, 0.8f * l), GlowC);
        var hull = MeshBuilder.Chamfered(0.8f * w, 0.26f * h, 0.12f * w);
        b.Loft(hull, [new Station(-0.46f * l, 0.92f, 0.9f, 0, 0.36f * h), new Station(0.2f * l, 1, 1, 0, 0.38f * h), new Station(0.48f * l, 0.7f, 0.3f, 0, 0.3f * h)], Axis.Z, Hull);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Box(V(sd * 0.402f * w, 0.4f * h, -0.08f * l), V(0.01f * w, 0.06f * h, 0.56f * l), Team);
            b.Cylinder(V(sd * 0.3f * w, 0.36f * h, -0.5f * l), Axis.Z, 0.12f * l, 0.07f * w, 0.08f * w, 6, HullDark);
            b.Cylinder(V(sd * 0.3f * w, 0.36f * h, -0.505f * l), Axis.Z, 0.005f, 0.06f * w, 0.06f * w, 6, GlowC);
        }
    }

    private static void HoverTurret(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.12f * h, 0.24f * w, 0.2f * w, 8, Hull, Team);
        t.Box(V(0, 0.08f * h, 0.12f * l), V(0.22f * w, 0.1f * h, 0.14f * l), HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Cylinder(V(sd * 0.05f * w, 0.08f * h, 0.18f * l), Axis.Z, 0.3f * l, 0.025f * w, 0.025f * w, 5, Dark);
    }

    private static void MissilePod(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.08f * h, 0.22f * w, 0.2f * w, 8, HullDark);
        var xf = PitchUp(0.5f) * T(0, 0.24f * h, 0);
        t.BevelBox(V(0, 0, 0.04f * l), V(0.62f * w, 0.32f * h, 0.56f * l), 0.05f * w, Hull, null, xf);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Box(V(sd * 0.312f * w, 0, 0.04f * l), V(0.01f * w, 0.2f * h, 0.44f * l), Team, null, xf);
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 2; j++)
            t.Box(V((i - 1) * 0.17f * w, (j - 0.5f) * 0.14f * h, 0.325f * l), V(0.12f * w, 0.1f * h, 0.012f * l), j == 1 ? GlowO : Dark, null, xf);
    }

    private static void MobileFlakTurret(MeshBuilder t, float w, float h, float l)
    {
        var prof = MeshBuilder.Chamfered(0.5f * w, 0.24f * h, 0.1f * w);
        t.Loft(prof, [new Station(-0.22f * l, 0.9f, 0.9f, 0, 0.12f * h), new Station(0.1f * l, 1, 1, 0, 0.12f * h)], Axis.Z, Hull);
        t.Box(V(0, 0.245f * h, -0.06f * l), V(0.28f * w, 0.01f * h, 0.2f * l), Team);
        var xf = PitchUp(0.6f) * T(0, 0.14f * h, 0.06f * l);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Cylinder(V(sd * 0.1f * w, 0, 0), Axis.Z, 0.5f * l, 0.04f * w, 0.032f * w, 6, Dark, null, xf);
            t.Cylinder(V(sd * 0.1f * w, 0, 0.46f * l), Axis.Z, 0.06f * l, 0.05f * w, 0.05f * w, 6, HullDark, null, xf);
        }
    }

    private static void HeavyBotTorso(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, -0.01f * h, 0), Axis.Y, 0.05f * h, 0.14f * w, 0.14f * w, 8, Dark);
        t.Loft(MeshBuilder.Chamfered(1, 1, 0.2f), [new Station(0.03f * h, 0.46f * w, 0.36f * l), new Station(0.24f * h, 0.62f * w, 0.48f * l), new Station(0.36f * h, 0.5f * w, 0.38f * l)], Axis.Y, Hull, HullLight);
        t.Box(V(0, 0.22f * h, 0.245f * l), V(0.26f * w, 0.05f * h, 0.012f * l), GlowC);
        t.BevelBox(V(0, 0.4f * h, 0.04f * l), V(0.18f * w, 0.07f * h, 0.2f * l), 0.02f * w, HullLight);
        t.Box(V(0, 0.4f * h, 0.145f * l), V(0.14f * w, 0.02f * h, 0.01f * l), GlowC);
        // Shoulder armor (team), big cannon on the right arm, missile box on the left.
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.BevelBox(V(sd * 0.38f * w, 0.3f * h, 0), V(0.18f * w, 0.1f * h, 0.36f * l), 0.03f * w, Team);
        t.BevelBox(V(0.4f * w, 0.16f * h, 0.12f * l), V(0.16f * w, 0.16f * h, 0.46f * l), 0.03f * w, HullDark);
        t.Cylinder(V(0.4f * w, 0.16f * h, 0.34f * l), Axis.Z, 0.36f * l, 0.06f * w, 0.05f * w, 8, Dark);
        t.Cylinder(V(0.4f * w, 0.16f * h, 0.7f * l), Axis.Z, 0.06f * l, 0.075f * w, 0.075f * w, 8, HullDark);
        t.BevelBox(V(-0.4f * w, 0.18f * h, 0.04f * l), V(0.16f * w, 0.16f * h, 0.3f * l), 0.03f * w, Hull);
        t.Box(V(-0.4f * w, 0.18f * h, 0.195f * l), V(0.1f * w, 0.1f * h, 0.01f * l), GlowO);
        t.BevelBox(V(0, 0.22f * h, -0.27f * l), V(0.44f * w, 0.2f * h, 0.14f * l), 0.03f * w, HullDark);
    }

    private static void HeavyArtillery(MeshBuilder b, float w, float h, float l)
    {
        Artillery(b, w, h, l);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Box(V(sd * 0.47f * w, 0.34f * h, 0), V(0.05f * w, 0.16f * h, 0.8f * l), HullDark);
            b.Box(V(sd * 0.36f * w, 0.18f * h, -0.56f * l), V(0.16f * w, 0.3f * h, 0.06f * l), Hull);
        }
        b.Box(V(0, 0.5f * h, 0.38f * l), V(0.5f * w, 0.06f * h, 0.12f * l), Glass);
    }

    private static void HeavyArtilleryTurret(MeshBuilder t, float w, float h, float l)
    {
        var prof = MeshBuilder.Chamfered(0.7f * w, 0.36f * h, 0.1f * w);
        t.Loft(prof, [new Station(-0.3f * l, 1, 1, 0, 0.18f * h), new Station(0.1f * l, 1, 1, 0, 0.18f * h), new Station(0.18f * l, 0.8f, 0.8f, 0, 0.18f * h)], Axis.Z, Hull);
        t.Box(V(0, 0.362f * h, -0.08f * l), V(0.4f * w, 0.012f * h, 0.3f * l), Team);
        var xf = PitchUp(0.55f) * T(0, 0.24f * h, 0.06f * l);
        t.Box(V(0, 0, 0.02f * l), V(0.24f * w, 0.22f * h, 0.2f * l), HullDark, null, xf);
        t.Cylinder(V(0, 0, 0.1f * l), Axis.Z, 0.78f * l, 0.08f * w, 0.06f * w, 8, HullDark, null, xf);
        t.Cylinder(V(0, 0, 0.36f * l), Axis.Z, 0.12f * l, 0.095f * w, 0.095f * w, 8, Hull, null, xf);
        t.Cylinder(V(0, 0, 0.86f * l), Axis.Z, 0.1f * l, 0.1f * w, 0.1f * w, 8, Dark, null, xf);
    }

    // ================================================================ air

    private static void AirSuperiority(MeshBuilder b, float w, float h, float l)
    {
        var hex = MeshBuilder.NGon(6, 1, 0);
        b.Loft(hex, [
            new Station(-0.5f * l, 0.14f * w, 0.16f * h, 0, 0.46f * h), new Station(-0.15f * l, 0.15f * w, 0.26f * h, 0, 0.5f * h),
            new Station(0.2f * l, 0.1f * w, 0.2f * h, 0, 0.5f * h), new Station(0.5f * l, 0.01f * w, 0.02f * h, 0, 0.47f * h)], Axis.Z, Hull, Dark);
        b.Sphere(V(0, 0.6f * h, 0.16f * l), V(0.05f * w, 0.12f * h, 0.14f * l), 4, 6, Glass, null, 0.5f);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            // Large delta wing with team tips, canards, twin canted fins, twin engines.
            Vector2[] wing = [V2(sd * 0.1f * w, 0.2f * l), V2(sd * 0.5f * w, -0.34f * l), V2(sd * 0.5f * w, -0.42f * l), V2(sd * 0.1f * w, -0.44f * l)];
            b.Slab(wing, 0.42f * h, 0.47f * h, Hull);
            Vector2[] tip = [V2(sd * 0.4f * w, -0.2f * l), V2(sd * 0.5f * w, -0.34f * l), V2(sd * 0.5f * w, -0.42f * l), V2(sd * 0.4f * w, -0.42f * l)];
            b.Slab(tip, 0.471f * h, 0.475f * h, Team);
            Vector2[] canard = [V2(sd * 0.08f * w, 0.32f * l), V2(sd * 0.22f * w, 0.24f * l), V2(sd * 0.22f * w, 0.2f * l), V2(sd * 0.08f * w, 0.22f * l)];
            b.Slab(canard, 0.48f * h, 0.5f * h, HullLight);
            Vector2[] fin = [V2(-0.5f * l, 0.5f * h), V2(-0.26f * l, 0.5f * h), V2(-0.42f * l, 1.0f * h), V2(-0.5f * l, 1.0f * h)];
            var cant = Matrix4x4.CreateTranslation(-sd * 0.12f * w, -0.5f * h, 0) * Matrix4x4.CreateRotationZ(-sd * 0.35f) * Matrix4x4.CreateTranslation(sd * 0.12f * w, 0.5f * h, 0);
            b.SideSlab(fin, sd * 0.11f * w, sd * 0.13f * w, Team, null, cant);
            b.Cylinder(V(sd * 0.07f * w, 0.42f * h, -0.5f * l), Axis.Z, 0.2f * l, 0.06f * w, 0.07f * w, 8, HullDark);
            b.Cylinder(V(sd * 0.07f * w, 0.42f * h, -0.505f * l), Axis.Z, 0.005f, 0.055f * w, 0.055f * w, 8, GlowO);
        }
    }

    private static void StrategicBomber(MeshBuilder b, float w, float h, float l)
    {
        // Flying wing: diamond planform, central hull, four engines, team leading-edge stripes.
        Vector2[] wing = [V2(0, 0.5f * l), V2(0.5f * w, -0.18f * l), V2(0.46f * w, -0.3f * l), V2(0, -0.12f * l), V2(-0.46f * w, -0.3f * l), V2(-0.5f * w, -0.18f * l)];
        b.Slab([wing[0], wing[1], wing[2], wing[3]], 0.38f * h, 0.5f * h, Hull);
        b.Slab([wing[0], wing[3], wing[4], wing[5]], 0.38f * h, 0.5f * h, Hull);
        var oct = MeshBuilder.NGon(8, 1, MathF.PI / 8);
        b.Loft(oct, [
            new Station(-0.42f * l, 0.12f * w, 0.18f * h, 0, 0.5f * h), new Station(-0.05f * l, 0.16f * w, 0.36f * h, 0, 0.5f * h),
            new Station(0.3f * l, 0.1f * w, 0.26f * h, 0, 0.48f * h), new Station(0.5f * l, 0.02f * w, 0.06f * h, 0, 0.46f * h)], Axis.Z, HullLight, Dark);
        b.Box(V(0, 0.62f * h, 0.3f * l), V(0.12f * w, 0.08f * h, 0.1f * l), Glass);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            Vector2[] stripe = [V2(sd * 0.02f * w, 0.47f * l), V2(sd * 0.44f * w, -0.12f * l), V2(sd * 0.42f * w, -0.17f * l), V2(sd * 0.02f * w, 0.4f * l)];
            b.Slab(stripe, 0.501f * h, 0.505f * h, Team);
            foreach (float x in (ReadOnlySpan<float>)[0.14f, 0.26f])
            {
                b.Cylinder(V(sd * x * w, 0.42f * h, -0.24f * l), Axis.Z, 0.3f * l, 0.05f * w, 0.05f * w, 8, HullDark);
                b.Cylinder(V(sd * x * w, 0.42f * h, -0.245f * l), Axis.Z, 0.005f, 0.04f * w, 0.04f * w, 8, GlowO);
            }
        }
    }

    private static void SpyPlane(MeshBuilder b, float w, float h, float l)
    {
        var hex = MeshBuilder.NGon(6, 1, 0);
        b.Loft(hex, [
            new Station(-0.5f * l, 0.05f * w, 0.14f * h, 0, 0.5f * h), new Station(-0.1f * l, 0.08f * w, 0.3f * h, 0, 0.5f * h),
            new Station(0.3f * l, 0.06f * w, 0.22f * h, 0, 0.5f * h), new Station(0.5f * l, 0.005f * w, 0.02f * h, 0, 0.5f * h)], Axis.Z, Dark, Black);
        b.Sphere(V(0, 0.62f * h, 0.24f * l), V(0.035f * w, 0.12f * h, 0.12f * l), 4, 6, Glass, null, 0.5f);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            Vector2[] wing = [V2(sd * 0.05f * w, 0.1f * l), V2(sd * 0.5f * w, -0.3f * l), V2(sd * 0.5f * w, -0.38f * l), V2(sd * 0.05f * w, -0.4f * l)];
            b.Slab(wing, 0.44f * h, 0.48f * h, HullDark);
            b.Cylinder(V(sd * 0.22f * w, 0.46f * h, -0.4f * l), Axis.Z, 0.55f * l, 0.05f * w, 0.035f * w, 8, Dark);
            b.Cylinder(V(sd * 0.22f * w, 0.46f * h, -0.405f * l), Axis.Z, 0.005f, 0.045f * w, 0.045f * w, 8, GlowC);
            Vector2[] fin = [V2(-0.36f * l, 0.5f * h), V2(-0.2f * l, 0.5f * h), V2(-0.3f * l, 1.0f * h), V2(-0.36f * l, 1.0f * h)];
            b.SideSlab(fin, sd * 0.21f * w, sd * 0.23f * w, Team);
        }
    }
}
