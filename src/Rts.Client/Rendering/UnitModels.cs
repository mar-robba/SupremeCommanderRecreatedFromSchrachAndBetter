using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;

namespace Rts.Client.Rendering;

public enum TurretMode { None, Weapon, Spin }

/// <summary>GPU meshes of one blueprint: body + optional turret (drawn once per pivot, rotated by turret yaw).</summary>
public sealed class UnitModel
{
    public required InstancedMesh Body;
    public InstancedMesh? Turret;
    public Vector3[] TurretPivots = [];
    public TurretMode TurretMode;
    public float SpinSpeed;
    public float MinY, MaxY;
    /// <summary>Bounding sphere radius around (0, (MinY+MaxY)/2, 0).</summary>
    public float Radius;
    public bool Air, Naval, Structure, Hover;
}

/// <summary>
/// Procedural low-poly models for every VisualBlueprint.Shape, built at the blueprint's Visual.Size
/// (so round parts stay round). UEF-like palette: grey-blue metal, dark trim, team-color panels, glowing lights.
/// Model space: forward +Z, up +Y, origin at ground level (y = 0) in the footprint center.
/// </summary>
public static partial class UnitModels
{
    private static readonly Color Hull = Mat.M(116, 122, 131);
    private static readonly Color HullLight = Mat.M(146, 152, 160);
    private static readonly Color HullDark = Mat.M(78, 83, 92);
    private static readonly Color Dark = Mat.M(46, 49, 54);
    private static readonly Color Black = Mat.M(32, 33, 36);
    private static readonly Color Glass = Mat.M(40, 62, 84);
    private static readonly Color Pad = Mat.M(92, 96, 104);
    private static readonly Color Team = Mat.T(215);
    private static readonly Color TeamDim = Mat.T(175);
    private static readonly Color GlowY = Mat.G(255, 205, 90);
    private static readonly Color GlowC = Mat.G(120, 220, 255);
    private static readonly Color GlowG = Mat.G(110, 255, 130);
    private static readonly Color GlowR = Mat.G(255, 70, 50);
    private static readonly Color GlowO = Mat.G(255, 150, 60);

    private static Vector3 V(float x, float y, float z) => new(x, y, z);
    private static Matrix4x4 T(float x, float y, float z) => Matrix4x4.CreateTranslation(x, y, z);
    private static Matrix4x4 PitchUp(float rad) => Matrix4x4.CreateRotationX(-rad);

    public static UnitModel Build(UnitBlueprint bp)
    {
        var s = bp.Visual.Size;
        float w = MathF.Max(0.1f, s.X), h = MathF.Max(0.1f, s.Y), l = MathF.Max(0.1f, s.Z);
        var b = new MeshBuilder();
        MeshBuilder? t = null;
        var pivots = new List<Vector3>();
        var mode = TurretMode.None;
        float spin = 0;
        bool hasWeapon = bp.Weapons.Length > 0;
        bool air = bp.Movement.Layer == MotionLayer.Air || (bp.Has(UnitCategory.Air) && bp.IsMobile);

        switch (bp.Visual.Shape)
        {
            case "tank":
                Tank(b, w, h, l);
                if (hasWeapon) { t = new MeshBuilder(); TankTurret(t, w, h, l); pivots.Add(V(0, 0.60f * h, -0.06f * l)); }
                break;
            case "artillery":
                Artillery(b, w, h, l);
                if (hasWeapon) { t = new MeshBuilder(); ArtilleryTurret(t, w, h, l); pivots.Add(V(0, 0.56f * h, -0.12f * l)); }
                break;
            case "aa":
                WheeledChassis(b, w, h, l, true);
                if (hasWeapon) { t = new MeshBuilder(); AaLauncher(t, w, h, l); pivots.Add(V(0, 0.60f * h, -0.12f * l)); }
                break;
            case "scout":
                Scout(b, w, h, l);
                if (hasWeapon) { t = new MeshBuilder(); ScoutGun(t, w, h, l); pivots.Add(V(0, 0.62f * h, 0.05f * l)); }
                break;
            case "engineer":
                Engineer(b, w, h, l);
                break;
            case "commander":
                MechLegs(b, w, h, l, 0.50f);
                t = new MeshBuilder(); CommanderTorso(t, w, h, l); pivots.Add(V(0, 0.50f * h, 0));
                break;
            case "bot":
                MechLegs(b, w, h, l, 0.52f);
                t = new MeshBuilder(); BotTorso(t, w, h, l, hasWeapon); pivots.Add(V(0, 0.52f * h, 0));
                break;
            case "factory":
                Factory(b, w, h, l, bp.Has(UnitCategory.Air), bp.Has(UnitCategory.Naval));
                break;
            case "mex":
                Mex(b, w, h, l);
                t = new MeshBuilder(); MexRotor(t, w, h, l); pivots.Add(V(0, 0.80f * h, 0));
                mode = TurretMode.Spin; spin = 2.2f;
                break;
            case "pgen":
                if (bp.Has(UnitCategory.HydroCarbon) || w > 2.6f) Hydro(b, w, h, l); else PowerGen(b, w, h, l);
                break;
            case "storage":
                if (bp.Has(UnitCategory.EnergyStorage)) EnergyStorage(b, w, h, l); else MassStorage(b, w, h, l);
                break;
            case "defense":
                DefenseBase(b, w, h, l);
                t = new MeshBuilder();
                if (bp.Has(UnitCategory.AntiAir)) AaTurret(t, w, h, l); else PdTurret(t, w, h, l);
                pivots.Add(V(0, 0.36f * h, 0));
                break;
            case "radar":
                Radar(b, w, h, l);
                t = new MeshBuilder(); RadarDish(t, w, h, l); pivots.Add(V(0, 0.80f * h, 0));
                mode = TurretMode.Spin; spin = 1.1f;
                break;
            case "wall":
                b.BevelBox(V(0, h * 0.5f - 0.15f, 0), V(w, h + 0.3f, l), MathF.Min(w, h) * 0.18f, Hull, HullLight);
                b.Box(V(0, h * 0.72f, 0), V(w * 1.01f, h * 0.1f, l * 0.9f), Team);
                break;
            case "fighter": Fighter(b, w, h, l); break;
            case "bomber": Bomber(b, w, h, l); break;
            case "gunship": Gunship(b, w, h, l); break;
            case "transport": Transport(b, w, h, l); break;
            case "frigate":
            case "destroyer":
            case "cruiser":
            {
                float deck = Ship(b, w, h, l, bp.Visual.Shape);
                t = new MeshBuilder(); NavalTurret(t, w, h, l);
                pivots.Add(V(0, deck, 0.27f * l));
                if (bp.Visual.Shape != "frigate") pivots.Add(V(0, deck, -0.32f * l));
                if (bp.Visual.Shape == "cruiser") pivots.Add(V(0, deck + 0.12f * h, 0.15f * l));
                break;
            }
            case "sub": Sub(b, w, h, l); break;
            case "shield_gen": t = ShieldGenerator(b, pivots, w, h, l); mode = TurretMode.Spin; spin = 0.9f; break;
            case "mobile_shield": t = MobileShield(b, pivots, w, h, l); mode = TurretMode.Spin; spin = 1.6f; break;
            case "missile_silo": MissileSilo(b, w, h, l, bp.Silo?.Kind == SiloKind.Strategic); break;
            case "missile_defense": t = MissileDefenseModel(b, pivots, w, h, l, bp.Silo != null); break;
            case "artillery_structure": t = ArtilleryStructure(b, pivots, w, h, l); break;
            case "naval_factory": case "battleship": case "sonar": case "torpedo_launcher": case "torpedo_bomber":
                t = NavalShape(bp, b, w, h, l, hasWeapon, pivots, ref mode, ref spin); // naval shapes: UnitModels.Naval.cs
                break;
            case "experimental":
                Spider(b, w, h, l);
                t = new MeshBuilder(); SpiderTurret(t, w, h, l); pivots.Add(V(0, 0.62f * h, 0));
                break;
            case "factory2": case "factory3": case "mex2": case "mex3": case "pgen2": case "pgen3": case "massfab": case "defense2": case "defense3": case "omni": case "engineer2": case "heavy_tank": case "hover_tank": case "missile_launcher": case "flak": case "heavy_bot": case "heavy_artillery": case "asf": case "strat_bomber": case "spy_plane":
                t = TechShape(bp, b, w, h, l, hasWeapon, pivots, ref mode, ref spin); // T2/T3 shapes: UnitModels.Tech.cs
                break;
            default:
                b.BevelBox(V(0, h * 0.5f, 0), V(w, h, l), MathF.Min(w, h) * 0.15f, Hull, HullLight);
                b.Box(V(0, h * 0.7f, 0), V(w * 1.01f, h * 0.08f, l * 0.6f), Team);
                break;
        }
        if (t != null && mode == TurretMode.None) mode = TurretMode.Weapon;

        var (minY, maxY, _) = b.Bounds();
        float radius = 0.5f * MathF.Sqrt(w * w + l * l + h * h) * 1.1f;
        var model = new UnitModel
        {
            Body = new InstancedMesh(b.Upload()),
            Turret = t != null ? new InstancedMesh(t.Upload()) : null,
            TurretPivots = pivots.ToArray(),
            TurretMode = t != null ? mode : TurretMode.None,
            SpinSpeed = spin,
            MinY = minY,
            MaxY = MathF.Max(maxY, h),
            Radius = radius,
            Air = air,
            Naval = bp.Movement.Layer is MotionLayer.Water or MotionLayer.Sub || bp.Visual.Shape is "frigate" or "destroyer" or "cruiser" or "sub",
            Structure = bp.IsStructure,
            Hover = bp.Movement.Layer == MotionLayer.Hover,
        };
        return model;
    }

    // ================================================================ land units

    private static void Tracks(MeshBuilder b, float w, float h, float l, float tw, float th)
    {
        Vector2[] prof = [V2(-0.5f * l, th * 0.45f), V2(-0.43f * l, 0), V2(0.43f * l, 0), V2(0.5f * l, th * 0.45f), V2(0.47f * l, th), V2(-0.47f * l, th)];
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            float x0 = sd * (w / 2 - tw), x1 = sd * w / 2;
            b.SideSlab(prof, x0, x1, Black);
            // Fender on top of the track.
            b.Box(V(sd * (w / 2 - tw / 2), th + 0.012f * h, 0), V(tw * 1.04f, 0.03f * h, l * 0.9f), HullDark);
        }
    }

    private static Vector2 V2(float x, float y) => new(x, y);

    private static void Tank(MeshBuilder b, float w, float h, float l)
    {
        Tracks(b, w, h, l, 0.26f * w, 0.40f * h);
        var hull = MeshBuilder.Chamfered(0.80f * w, 0.26f * h, 0.06f * w);
        b.Loft(hull, [new Station(-0.48f * l, 1, 1, 0, 0.47f * h), new Station(0.28f * l, 1, 1, 0, 0.47f * h), new Station(0.5f * l, 0.92f, 0.45f, 0, 0.41f * h)], Axis.Z, Hull);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.402f * w, 0.50f * h, -0.06f * l), V(0.012f * w, 0.07f * h, 0.62f * l), Team);
        b.Box(V(0, 0.61f * h, -0.38f * l), V(0.52f * w, 0.025f * h, 0.16f * l), Dark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.28f * w, 0.42f * h, 0.495f * l), V(0.08f * w, 0.05f * h, 0.015f * l), GlowY);
    }

    private static void TankTurret(MeshBuilder t, float w, float h, float l)
    {
        var prof = MeshBuilder.Chamfered(0.54f * w, 0.24f * h, 0.07f * w);
        t.Loft(prof, [new Station(-0.24f * l, 0.9f, 0.9f, 0, 0.12f * h), new Station(0.08f * l, 1, 1, 0, 0.12f * h), new Station(0.19f * l, 0.78f, 0.7f, 0, 0.11f * h)], Axis.Z, Hull);
        t.Box(V(0, 0.245f * h, -0.04f * l), V(0.30f * w, 0.01f * h, 0.22f * l), Team);
        t.Cylinder(V(-0.12f * w, 0.24f * h, -0.12f * l), Axis.Y, 0.035f * h, 0.07f * w, 0.06f * w, 6, HullDark);
        t.Box(V(0, 0.12f * h, 0.20f * l), V(0.18f * w, 0.13f * h, 0.06f * l), HullDark);
        t.Cylinder(V(0, 0.12f * h, 0.21f * l), Axis.Z, 0.46f * l, 0.045f * w, 0.038f * w, 6, HullDark);
        t.Cylinder(V(0, 0.12f * h, 0.62f * l), Axis.Z, 0.08f * l, 0.06f * w, 0.06f * w, 6, Dark);
    }

    private static void Artillery(MeshBuilder b, float w, float h, float l)
    {
        Tracks(b, w, h, l, 0.24f * w, 0.36f * h);
        var hull = MeshBuilder.Chamfered(0.84f * w, 0.24f * h, 0.05f * w);
        b.Loft(hull, [new Station(-0.5f * l, 1, 1, 0, 0.44f * h), new Station(0.30f * l, 1, 1, 0, 0.44f * h), new Station(0.46f * l, 0.9f, 0.5f, 0, 0.38f * h)], Axis.Z, Hull);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Box(V(sd * 0.422f * w, 0.47f * h, 0.05f * l), V(0.012f * w, 0.06f * h, 0.5f * l), Team);
            // Stabilizer spades at the back.
            b.Box(V(sd * 0.3f * w, 0.2f * h, -0.52f * l), V(0.12f * w, 0.25f * h, 0.05f * l), HullDark);
        }
    }

    private static void ArtilleryTurret(MeshBuilder t, float w, float h, float l)
    {
        var prof = MeshBuilder.Chamfered(0.62f * w, 0.30f * h, 0.08f * w);
        t.Loft(prof, [new Station(-0.26f * l, 1, 1, 0, 0.15f * h), new Station(0.14f * l, 1, 1, 0, 0.15f * h), new Station(0.2f * l, 0.8f, 0.8f, 0, 0.15f * h)], Axis.Z, Hull);
        t.Box(V(0, 0.302f * h, -0.06f * l), V(0.36f * w, 0.01f * h, 0.26f * l), Team);
        var xf = PitchUp(0.45f) * T(0, 0.2f * h, 0.08f * l);
        t.Box(V(0, 0, 0.04f * l), V(0.2f * w, 0.18f * h, 0.16f * l), HullDark, null, xf);
        t.Cylinder(V(0, 0, 0.1f * l), Axis.Z, 0.62f * l, 0.075f * w, 0.06f * w, 6, HullDark, null, xf);
        t.Cylinder(V(0, 0, 0.66f * l), Axis.Z, 0.09f * l, 0.085f * w, 0.085f * w, 6, Dark, null, xf);
    }

    private static void Wheel(MeshBuilder b, float x, float y, float z, float r, float width, int sd)
    {
        b.Cylinder(V(x - width / 2, y, z), Axis.X, width, r, r, 8, Black);
        b.Cylinder(V(x + sd * width / 2, y, z), Axis.X, sd * 0.01f, r * 0.5f, r * 0.5f, 6, HullDark);
    }

    private static void WheeledChassis(MeshBuilder b, float w, float h, float l, bool cab)
    {
        float r = 0.2f * h;
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        foreach (float z in (ReadOnlySpan<float>)[-0.3f * l, 0.02f * l, 0.32f * l])
            Wheel(b, sd * (0.5f * w - 0.09f * w), r, z, r, 0.17f * w, sd);
        var hull = MeshBuilder.Chamfered(0.70f * w, 0.30f * h, 0.06f * w);
        b.Loft(hull, [new Station(-0.48f * l, 1, 1, 0, 0.42f * h), new Station(0.30f * l, 1, 1, 0, 0.42f * h), new Station(0.48f * l, 0.9f, 0.55f, 0, 0.37f * h)], Axis.Z, Hull);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.352f * w, 0.45f * h, -0.05f * l), V(0.01f * w, 0.07f * h, 0.6f * l), Team);
        if (cab)
        {
            b.Box(V(0, 0.6f * h, 0.36f * l), V(0.5f * w, 0.08f * h, 0.12f * l), HullDark);
            b.Box(V(0, 0.62f * h, 0.42f * l), V(0.42f * w, 0.05f * h, 0.02f * l), Glass);
        }
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.24f * w, 0.36f * h, 0.485f * l), V(0.07f * w, 0.05f * h, 0.015f * l), GlowY);
    }

    private static void AaLauncher(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.1f * h, 0.22f * w, 0.19f * w, 8, HullDark);
        var xf = PitchUp(0.6f) * T(0, 0.24f * h, -0.02f * l);
        t.BevelBox(V(0, 0, 0.06f * l), V(0.5f * w, 0.28f * h, 0.44f * l), 0.05f * w, Hull, null, xf);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Box(V(sd * 0.252f * w, 0, 0.06f * l), V(0.01f * w, 0.18f * h, 0.34f * l), Team, null, xf);
            foreach (int sy in (ReadOnlySpan<int>)[-1, 1])
                t.Box(V(sd * 0.11f * w, sy * 0.065f * h, 0.285f * l), V(0.08f * w, 0.08f * h, 0.012f * l), sy > 0 ? GlowO : Dark, null, xf);
        }
    }

    private static void Scout(MeshBuilder b, float w, float h, float l)
    {
        float r = 0.24f * h;
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        foreach (float z in (ReadOnlySpan<float>)[-0.3f * l, 0.3f * l])
            Wheel(b, sd * (0.5f * w - 0.1f * w), r, z, r, 0.2f * w, sd);
        var hull = MeshBuilder.Chamfered(0.62f * w, 0.28f * h, 0.07f * w);
        b.Loft(hull, [new Station(-0.46f * l, 0.9f, 0.9f, 0, 0.44f * h), new Station(0.1f * l, 1, 1, 0, 0.46f * h), new Station(0.5f * l, 0.7f, 0.4f, 0, 0.4f * h)], Axis.Z, Hull);
        b.Box(V(0, 0.61f * h, -0.12f * l), V(0.4f * w, 0.02f * h, 0.3f * l), Team);
        b.Cylinder(V(-0.18f * w, 0.58f * h, -0.3f * l), Axis.Y, 0.55f * h, 0.02f * w, 0.015f * w, 4, HullDark);
        b.Box(V(-0.18f * w, 1.13f * h, -0.3f * l), V(0.05f * w, 0.05f * h, 0.05f * w), GlowC);
        b.Box(V(0, 0.5f * h, 0.39f * l), V(0.4f * w, 0.06f * h, 0.03f * l), Glass);
    }

    private static void ScoutGun(MeshBuilder t, float w, float h, float l)
    {
        t.Box(V(0, 0.06f * h, 0), V(0.18f * w, 0.12f * h, 0.2f * l), HullDark);
        t.Cylinder(V(0, 0.07f * h, 0.08f * l), Axis.Z, 0.25f * l, 0.03f * w, 0.03f * w, 5, Dark);
    }

    private static void Engineer(MeshBuilder b, float w, float h, float l)
    {
        Tracks(b, w, h, l, 0.24f * w, 0.36f * h);
        var hull = MeshBuilder.Chamfered(0.72f * w, 0.32f * h, 0.07f * w);
        b.Loft(hull, [new Station(-0.46f * l, 1, 1, 0, 0.50f * h), new Station(0.18f * l, 1, 1, 0, 0.50f * h), new Station(0.46f * l, 0.85f, 0.5f, 0, 0.42f * h)], Axis.Z, Hull);
        b.Box(V(0, 0.665f * h, -0.12f * l), V(0.5f * w, 0.012f * h, 0.42f * l), Team);
        b.Box(V(0, 0.55f * h, 0.36f * l), V(0.5f * w, 0.07f * h, 0.02f * l), Glass);
        // Crane arm: base, boom, tool head.
        b.Cylinder(V(0.14f * w, 0.66f * h, -0.18f * l), Axis.Y, 0.1f * h, 0.09f * w, 0.08f * w, 6, HullDark);
        var boom = Matrix4x4.CreateRotationX(-0.55f) * T(0.14f * w, 0.74f * h, -0.18f * l);
        b.Box(V(0, 0, 0.22f * l), V(0.07f * w, 0.07f * h, 0.46f * l), HullLight, null, boom);
        var fore = Matrix4x4.CreateRotationX(0.5f) * T(0.14f * w, 0.74f * h + 0.39f * l * 0.52f, -0.18f * l + 0.39f * l * 0.85f);
        b.Box(V(0, 0, 0.1f * l), V(0.06f * w, 0.06f * h, 0.22f * l), HullLight, null, fore);
        b.Box(V(0, 0, 0.23f * l), V(0.1f * w, 0.1f * h, 0.05f * l), GlowY, null, fore);
        b.Box(V(-0.22f * w, 0.69f * h, 0.12f * l), V(0.06f * w, 0.04f * h, 0.06f * w), GlowY);
    }

    // ================================================================ mechs

    private static void MechLegs(MeshBuilder b, float w, float h, float l, float hip)
    {
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            float x = sd * 0.2f * w;
            b.BevelBox(V(x, 0.04f * h, 0.05f * l), V(0.22f * w, 0.08f * h, 0.46f * l), 0.03f * w, HullDark);
            b.Box(V(x, 0.082f * h, 0.22f * l), V(0.16f * w, 0.012f * h, 0.1f * l), Team);
            b.Frustum(V(x, 0.08f * h, -0.02f * l), 0.13f * w, 0.17f * l, 0.17f * w, 0.22f * l, 0.2f * h, Hull, null, null, 0.2f);
            b.BevelBox(V(x, 0.31f * h, 0.03f * l), V(0.18f * w, 0.08f * h, 0.22f * l), 0.03f * w, HullLight);
            b.Frustum(V(x, 0.33f * h, -0.03f * l), 0.15f * w, 0.18f * l, 0.17f * w, 0.2f * l, (hip - 0.36f) * h, Hull, null, null, 0.2f);
        }
        b.BevelBox(V(0, hip * h - 0.03f * h, 0), V(0.46f * w, 0.09f * h, 0.27f * l), 0.03f * w, HullDark);
    }

    private static void CommanderTorso(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, -0.01f * h, 0), Axis.Y, 0.06f * h, 0.12f * w, 0.12f * w, 8, Dark);
        var chest = MeshBuilder.Chamfered(1, 1, 0.2f);
        t.Loft(chest, [new Station(0.04f * h, 0.38f * w, 0.28f * l), new Station(0.21f * h, 0.58f * w, 0.42f * l, 0, 0.01f * l), new Station(0.31f * h, 0.5f * w, 0.36f * l)], Axis.Y, Hull, HullLight);
        t.Box(V(0, 0.17f * h, 0.215f * l), V(0.24f * w, 0.05f * h, 0.02f * l), Team);
        t.Box(V(0, 0.11f * h, 0.19f * l), V(0.14f * w, 0.04f * h, 0.02f * l), GlowC);
        // Head with visor.
        t.BevelBox(V(0, 0.345f * h, 0.03f * l), V(0.17f * w, 0.075f * h, 0.19f * l), 0.025f * w, HullLight);
        t.Box(V(0, 0.35f * h, 0.125f * l), V(0.13f * w, 0.022f * h, 0.012f * l), GlowC);
        // Backpack with exhausts.
        t.BevelBox(V(0, 0.2f * h, -0.25f * l), V(0.4f * w, 0.2f * h, 0.14f * l), 0.03f * w, HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Cylinder(V(sd * 0.11f * w, 0.26f * h, -0.27f * l), Axis.Y, 0.1f * h, 0.035f * w, 0.03f * w, 6, Dark);
            t.Box(V(sd * 0.11f * w, 0.16f * h, -0.322f * l), V(0.08f * w, 0.04f * h, 0.01f * l), GlowO);
            // Shoulder pauldrons (team color) and upper arms.
            t.BevelBox(V(sd * 0.37f * w, 0.26f * h, 0), V(0.2f * w, 0.1f * h, 0.3f * l), 0.04f * w, Team);
            t.Box(V(sd * 0.37f * w, 0.16f * h, 0), V(0.12f * w, 0.14f * h, 0.14f * l), Dark);
        }
        // Gun arm (+X) and build arm (-X).
        t.BevelBox(V(0.37f * w, 0.07f * h, 0.11f * l), V(0.15f * w, 0.12f * h, 0.38f * l), 0.03f * w, Hull);
        t.Cylinder(V(0.37f * w, 0.07f * h, 0.3f * l), Axis.Z, 0.22f * l, 0.04f * w, 0.035f * w, 6, Dark);
        t.Box(V(0.37f * w, 0.135f * h, 0.08f * l), V(0.1f * w, 0.012f * h, 0.2f * l), TeamDim);
        t.BevelBox(V(-0.37f * w, 0.07f * h, 0.09f * l), V(0.15f * w, 0.12f * h, 0.32f * l), 0.03f * w, Hull);
        t.Box(V(-0.37f * w, 0.07f * h, 0.27f * l), V(0.07f * w, 0.07f * h, 0.05f * l), GlowY);
        t.Box(V(-0.32f * w, 0.07f * h, 0.3f * l), V(0.025f * w, 0.05f * h, 0.08f * l), HullLight);
        t.Box(V(-0.42f * w, 0.07f * h, 0.3f * l), V(0.025f * w, 0.05f * h, 0.08f * l), HullLight);
    }

    private static void BotTorso(MeshBuilder t, float w, float h, float l, bool gun)
    {
        t.Cylinder(V(0, -0.01f * h, 0), Axis.Y, 0.05f * h, 0.12f * w, 0.12f * w, 6, Dark);
        t.Loft(MeshBuilder.Chamfered(1, 1, 0.22f), [new Station(0.03f * h, 0.42f * w, 0.32f * l), new Station(0.26f * h, 0.56f * w, 0.42f * l), new Station(0.36f * h, 0.42f * w, 0.32f * l)], Axis.Y, Hull, Team);
        t.Box(V(0, 0.24f * h, 0.215f * l), V(0.22f * w, 0.04f * h, 0.012f * l), GlowC);
        if (gun)
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            {
                t.Box(V(sd * 0.34f * w, 0.18f * h, 0.08f * l), V(0.12f * w, 0.12f * h, 0.36f * l), HullDark);
                t.Cylinder(V(sd * 0.34f * w, 0.18f * h, 0.26f * l), Axis.Z, 0.18f * l, 0.035f * w, 0.035f * w, 5, Dark);
            }
    }

    // ================================================================ structures

    private static void Foundation(MeshBuilder b, float w, float l, float top, Color col, float inset = 0.04f)
    {
        b.Frustum(V(0, -0.35f, 0), w, l, w * (1 - inset), l * (1 - inset), 0.35f + top, col, Pad, null, 0.06f);
    }

    private static void Factory(MeshBuilder b, float w, float h, float l, bool air, bool naval)
    {
        Foundation(b, w, l, 0.1f * h, HullDark, 0.02f);
        float y0 = 0.1f * h;
        // Main hall with a sloped roof.
        b.Box(V(0, y0 + 0.3f * h, -0.12f * l), V(0.8f * w, 0.6f * h, 0.6f * l), Hull);
        b.Frustum(V(0, y0 + 0.6f * h, -0.12f * l), 0.8f * w, 0.6f * l, 0.62f * w, 0.42f * l, 0.12f * h, HullLight, HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Box(V(sd * 0.401f * w, y0 + 0.45f * h, -0.12f * l), V(0.01f * w, 0.08f * h, 0.56f * l), Team);
            // Corner towers with lights.
            b.Frustum(V(sd * 0.38f * w, y0, -0.36f * l), 0.16f * w, 0.16f * l, 0.12f * w, 0.12f * l, 0.92f * h, Hull, HullDark, null, 0.15f);
            b.Box(V(sd * 0.38f * w, y0 + 0.7f * h, -0.36f * l), V(0.14f * w, 0.09f * h, 0.14f * l), Team);
            b.Box(V(sd * 0.38f * w, y0 + 0.95f * h, -0.36f * l), V(0.035f * w, 0.05f * h, 0.035f * l), GlowR);
            // Door posts.
            b.Box(V(sd * 0.27f * w, y0 + 0.24f * h, 0.185f * l), V(0.04f * w, 0.48f * h, 0.04f * l), HullLight);
        }
        // Gantry crane across the roof.
        b.Box(V(0, y0 + 0.86f * h, -0.02f * l), V(0.84f * w, 0.05f * h, 0.05f * l), HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.41f * w, y0 + 0.6f * h, -0.02f * l), V(0.03f * w, 0.5f * h, 0.04f * l), HullDark);
        // Roof vents.
        for (int i = -1; i <= 1; i++)
            b.Cylinder(V(i * 0.16f * w, y0 + 0.72f * h, -0.24f * l), Axis.Y, 0.1f * h, 0.035f * w, 0.03f * w, 6, Dark);
        // Door: dark opening + team lintel.
        b.Box(V(0, y0 + 0.24f * h, 0.181f * l), V(0.5f * w, 0.46f * h, 0.01f * l), Black);
        b.Box(V(0, y0 + 0.5f * h, 0.19f * l), V(0.58f * w, 0.06f * h, 0.04f * l), Team);
        if (air)
        {
            // Landing pad + control tower.
            b.Cylinder(V(0, 0, 0.34f * l), Axis.Y, y0 + 0.02f * h, 0.16f * w, 0.16f * w, 12, HullDark, Pad);
            for (int i = 0; i < 8; i++)
            {
                float a = i * MathF.Tau / 8;
                b.Box(V(MathF.Cos(a) * 0.14f * w, y0 + 0.03f * h, 0.34f * l + MathF.Sin(a) * 0.14f * w), V(0.02f * w, 0.02f * h, 0.02f * w), GlowC);
            }
            b.Cylinder(V(0.36f * w, y0, 0.3f * l), Axis.Y, 0.55f * h, 0.04f * w, 0.035f * w, 6, Hull);
            b.BevelBox(V(0.36f * w, y0 + 0.6f * h, 0.3f * l), V(0.12f * w, 0.1f * h, 0.12f * l), 0.02f * w, HullLight);
            b.Box(V(0.36f * w, y0 + 0.6f * h, 0.3f * l), V(0.125f * w, 0.04f * h, 0.125f * l), Glass);
        }
        else
        {
            // Rollout ramp with lights.
            Vector2[] ramp = [V2(0.18f * l, 0), V2(0.48f * l, 0), V2(0.48f * l, 0.02f * h), V2(0.18f * l, y0 + 0.01f * h)];
            b.SideSlab(ramp, -0.25f * w, 0.25f * w, naval ? HullDark : Pad);
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            {
                b.Box(V(sd * 0.24f * w, 0.05f * h, 0.33f * l), V(0.02f * w, 0.03f * h, 0.28f * l), Team);
                b.Box(V(sd * 0.24f * w, 0.05f * h, 0.47f * l), V(0.03f * w, 0.04f * h, 0.02f * l), GlowY);
            }
        }
    }

    private static void Mex(MeshBuilder b, float w, float h, float l)
    {
        b.Frustum(V(0, -0.3f, 0), 0.96f * w, 0.96f * l, 0.8f * w, 0.8f * l, 0.3f + 0.22f * h, HullDark, Hull, null, 0.25f);
        foreach (var (sx, sz) in (ReadOnlySpan<(int, int)>)[(-1, -1), (1, -1), (-1, 1), (1, 1)])
        {
            b.Cylinder(V(sx * 0.27f * w, 0.2f * h, sz * 0.27f * l), Axis.Y, 0.42f * h, 0.07f * w, 0.05f * w, 6, Hull);
            b.Box(V(sx * 0.3f * w, 0.23f * h, sz * 0.3f * l), V(0.12f * w, 0.03f * h, 0.12f * l), Team);
        }
        b.Cylinder(V(0, 0.2f * h, 0), Axis.Y, 0.6f * h, 0.25f * w, 0.18f * w, 8, Hull, HullDark);
        b.Cylinder(V(0, 0.42f * h, 0), Axis.Y, 0.1f * h, 0.238f * w, 0.226f * w, 8, GlowG);
    }

    private static void MexRotor(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.14f * h, 0.13f * w, 0.1f * w, 6, HullLight);
        for (int i = 0; i < 3; i++)
        {
            var xf = Matrix4x4.CreateRotationY(i * MathF.Tau / 3);
            t.Box(V(0, 0.06f * h, 0.17f * w), V(0.05f * w, 0.06f * h, 0.2f * w), HullDark, null, xf);
        }
        t.Box(V(0, 0.15f * h, 0), V(0.05f * w, 0.04f * h, 0.05f * w), GlowG);
    }

    private static void PowerGen(MeshBuilder b, float w, float h, float l)
    {
        b.Frustum(V(0, -0.3f, 0), 0.96f * w, 0.96f * l, 0.86f * w, 0.86f * l, 0.3f + 0.12f * h, HullDark, Pad, null, 0.25f);
        var ring = MeshBuilder.NGon(10, 1, MathF.PI / 10);
        b.Loft(ring, [new Station(0.12f * h, 0.40f * w, 0.40f * l), new Station(0.55f * h, 0.29f * w, 0.29f * l), new Station(0.95f * h, 0.34f * w, 0.34f * l)], Axis.Y, Hull, HullDark);
        b.Cylinder(V(0, 0.95f * h, 0), Axis.Y, 0.005f, 0.28f * w, 0.28f * w, 10, GlowY);
        b.Cylinder(V(0, 0.24f * h, 0), Axis.Y, 0.08f * h, 0.385f * w, 0.375f * w, 10, Team);
        b.Cylinder(V(0, 0.82f * h, 0), Axis.Y, 0.04f * h, 0.33f * w, 0.335f * w, 10, GlowY);
        foreach (var (sx, sz) in (ReadOnlySpan<(int, int)>)[(-1, -1), (1, -1), (-1, 1), (1, 1)])
            b.Box(V(sx * 0.33f * w, 0.18f * h, sz * 0.33f * l), V(0.08f * w, 0.14f * h, 0.08f * l), HullLight);
    }

    private static void Hydro(MeshBuilder b, float w, float h, float l)
    {
        b.Frustum(V(0, -0.3f, 0), 0.96f * w, 0.96f * l, 0.84f * w, 0.84f * l, 0.3f + 0.1f * h, HullDark, Pad, null, 0.25f);
        b.Sphere(V(0, 0.1f * h, 0), V(0.26f * w, 0.42f * h, 0.26f * l), 6, 10, Hull, null, 0.5f);
        b.Cylinder(V(0, 0.1f * h, 0), Axis.Y, 0.08f * h, 0.27f * w, 0.26f * w, 10, Team);
        b.Cylinder(V(0, 0.5f * h, 0), Axis.Y, 0.04f * h, 0.03f * w, 0.03f * w, 6, GlowY);
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF.Tau / 3 + 0.5f;
            float x = MathF.Cos(a) * 0.32f * w, z = MathF.Sin(a) * 0.32f * l;
            var ring = MeshBuilder.NGon(8, 1, MathF.PI / 8);
            b.Loft(ring, [new Station(0.1f * h, 0.12f * w, 0.12f * l, x, z), new Station(0.5f * h, 0.08f * w, 0.08f * l, x, z), new Station(0.8f * h, 0.1f * w, 0.1f * l, x, z)], Axis.Y, Hull, HullDark);
            b.Cylinder(V(x, 0.8f * h, z), Axis.Y, 0.005f, 0.08f * w, 0.08f * w, 8, GlowY);
            b.Cylinder(V(x, 0.3f * h, z), Axis.Y, 0.05f * h, 0.105f * w, 0.1f * w, 8, Team);
        }
    }

    private static void MassStorage(MeshBuilder b, float w, float h, float l)
    {
        b.Frustum(V(0, -0.3f, 0), 0.96f * w, 0.96f * l, 0.9f * w, 0.9f * l, 0.3f + 0.15f * h, HullDark, Pad, null, 0.2f);
        b.Frustum(V(0, 0.15f * h, 0), 0.82f * w, 0.82f * l, 0.72f * w, 0.72f * l, 0.6f * h, Hull, HullDark, null, 0.2f);
        b.Frustum(V(0, 0.3f * h, 0), 0.8f * w, 0.8f * l, 0.79f * w, 0.79f * l, 0.12f * h, Team, null, null, 0.2f);
        for (int i = -1; i <= 1; i++)
        for (int j = -1; j <= 1; j++)
            b.Box(V(i * 0.2f * w, 0.76f * h, j * 0.2f * l), V(0.15f * w, 0.03f * h, 0.15f * l), GlowG);
    }

    private static void EnergyStorage(MeshBuilder b, float w, float h, float l)
    {
        b.Frustum(V(0, -0.3f, 0), 0.96f * w, 0.96f * l, 0.88f * w, 0.88f * l, 0.3f + 0.12f * h, HullDark, Pad, null, 0.2f);
        b.Cylinder(V(0, 0.12f * h, 0), Axis.Y, 0.62f * h, 0.36f * w, 0.36f * w, 10, Hull);
        b.Sphere(V(0, 0.74f * h, 0), V(0.36f * w, 0.22f * h, 0.36f * l), 6, 10, HullLight, null, 0.5f);
        b.Cylinder(V(0, 0.3f * h, 0), Axis.Y, 0.06f * h, 0.37f * w, 0.37f * w, 10, GlowY);
        b.Cylinder(V(0, 0.55f * h, 0), Axis.Y, 0.06f * h, 0.37f * w, 0.37f * w, 10, GlowY);
        b.Cylinder(V(0, 0.16f * h, 0), Axis.Y, 0.08f * h, 0.375f * w, 0.375f * w, 10, Team);
        foreach (var (sx, sz) in (ReadOnlySpan<(int, int)>)[(-1, -1), (1, -1), (-1, 1), (1, 1)])
            b.Box(V(sx * 0.32f * w, 0.4f * h, sz * 0.32f * l), V(0.07f * w, 0.6f * h, 0.07f * l), HullDark);
    }

    private static void DefenseBase(MeshBuilder b, float w, float h, float l)
    {
        b.Frustum(V(0, -0.3f, 0), 0.96f * w, 0.96f * l, 0.7f * w, 0.7f * l, 0.3f + 0.36f * h, HullDark, Hull, null, 0.25f);
        b.Frustum(V(0, 0.2f * h, 0), 0.79f * w, 0.79f * l, 0.76f * w, 0.76f * l, 0.07f * h, Team, null, null, 0.25f);
    }

    private static void PdTurret(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.06f * h, 0.3f * w, 0.28f * w, 8, Dark);
        var prof = MeshBuilder.Chamfered(0.5f * w, 0.3f * h, 0.1f * w);
        t.Loft(prof, [new Station(-0.24f * l, 0.9f, 0.85f, 0, 0.2f * h), new Station(0.1f * l, 1, 1, 0, 0.2f * h), new Station(0.22f * l, 0.7f, 0.6f, 0, 0.18f * h)], Axis.Z, Hull);
        t.Box(V(0, 0.352f * h, -0.06f * l), V(0.26f * w, 0.01f * h, 0.24f * l), Team);
        t.Cylinder(V(0, 0.19f * h, 0.2f * l), Axis.Z, 0.42f * l, 0.06f * w, 0.05f * w, 6, HullDark);
        t.Cylinder(V(0, 0.19f * h, 0.58f * l), Axis.Z, 0.07f * l, 0.075f * w, 0.075f * w, 6, Dark);
    }

    private static void AaTurret(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.1f * h, 0.3f * w, 0.26f * w, 8, Hull, Team);
        var xf = PitchUp(0.75f) * T(0, 0.22f * h, -0.02f * l);
        t.BevelBox(V(0, 0, 0), V(0.34f * w, 0.2f * h, 0.3f * l), 0.04f * w, HullLight, null, xf);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        foreach (int sy in (ReadOnlySpan<int>)[-1, 1])
            t.Cylinder(V(sd * 0.08f * w, sy * 0.05f * h, 0.1f * l), Axis.Z, 0.34f * l, 0.03f * w, 0.025f * w, 5, Dark, null, xf);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Box(V(sd * 0.172f * w, 0, 0), V(0.01f * w, 0.12f * h, 0.22f * l), Team, null, xf);
    }

    private static void Radar(MeshBuilder b, float w, float h, float l)
    {
        b.Frustum(V(0, -0.3f, 0), 0.92f * w, 0.92f * l, 0.72f * w, 0.72f * l, 0.3f + 0.06f * h, HullDark, Pad, null, 0.2f);
        var leg = MeshBuilder.Rect(0.07f * w, 0.07f * l);
        foreach (var (sx, sz) in (ReadOnlySpan<(int, int)>)[(-1, -1), (1, -1), (-1, 1), (1, 1)])
            b.Loft(leg, [new Station(0.06f * h, 1, 1, sx * 0.3f * w, sz * 0.3f * l), new Station(0.77f * h, 1, 1, sx * 0.08f * w, sz * 0.08f * l)], Axis.Y, Hull);
        foreach (float y in (ReadOnlySpan<float>)[0.25f, 0.5f])
        {
            float r = 0.3f - (0.22f * (y - 0.06f) / 0.71f);
            b.Box(V(0, y * h, r * l), V(2 * r * w, 0.025f * h, 0.03f * l), HullDark);
            b.Box(V(0, y * h, -r * l), V(2 * r * w, 0.025f * h, 0.03f * l), HullDark);
            b.Box(V(r * w, y * h, 0), V(0.03f * w, 0.025f * h, 2 * r * l), HullDark);
            b.Box(V(-r * w, y * h, 0), V(0.03f * w, 0.025f * h, 2 * r * l), HullDark);
        }
        b.Box(V(0, 0.38f * h, 0), V(0.3f * w, 0.05f * h, 0.3f * l), Team);
        b.Box(V(0, 0.775f * h, 0), V(0.3f * w, 0.03f * h, 0.3f * l), HullDark);
    }

    private static void RadarDish(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.06f * h, 0.04f * w, 0.04f * w, 6, Hull);
        var xf = PitchUp(0.35f) * T(0, 0.08f * h, 0);
        t.Cylinder(V(0, 0, -0.06f * w), Axis.Z, 0.12f * w, 0.06f * w, 0.48f * w, 10, HullLight, Pad, xf);
        t.Cylinder(V(0, 0, 0.06f * w), Axis.Z, 0.3f * w, 0.015f * w, 0.015f * w, 4, Dark, null, xf);
        t.Box(V(0, 0, 0.37f * w), V(0.05f * w, 0.05f * w, 0.05f * w), GlowR, null, xf);
        t.Box(V(0, 0, -0.07f * w), V(0.2f * w, 0.2f * w, 0.02f * w), Team, null, xf);
    }

    // ================================================================ air

    private static void Fighter(MeshBuilder b, float w, float h, float l)
    {
        var hex = MeshBuilder.NGon(6, 1, 0);
        b.Loft(hex, [
            new Station(-0.5f * l, 0.1f * w, 0.14f * h, 0, 0.45f * h), new Station(-0.2f * l, 0.13f * w, 0.24f * h, 0, 0.48f * h),
            new Station(0.15f * l, 0.12f * w, 0.22f * h, 0, 0.5f * h), new Station(0.38f * l, 0.07f * w, 0.12f * h, 0, 0.48f * h),
            new Station(0.5f * l, 0.01f * w, 0.02f * h, 0, 0.46f * h)], Axis.Z, Hull, Dark);
        b.Sphere(V(0, 0.58f * h, 0.12f * l), V(0.06f * w, 0.14f * h, 0.15f * l), 4, 6, Glass, null, 0.5f);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            Vector2[] wing = [V2(sd * 0.08f * w, 0.14f * l), V2(sd * 0.5f * w, -0.3f * l), V2(sd * 0.5f * w, -0.4f * l), V2(sd * 0.08f * w, -0.42f * l)];
            b.Slab(wing, 0.40f * h, 0.46f * h, Hull);
            Vector2[] tip = [V2(sd * 0.36f * w, -0.17f * l), V2(sd * 0.5f * w, -0.3f * l), V2(sd * 0.5f * w, -0.4f * l), V2(sd * 0.36f * w, -0.4f * l)];
            b.Slab(tip, 0.461f * h, 0.465f * h, Team);
            Vector2[] fin = [V2(-0.5f * l, 0.5f * h), V2(-0.28f * l, 0.5f * h), V2(-0.42f * l, 0.98f * h), V2(-0.5f * l, 0.98f * h)];
            var cant = Matrix4x4.CreateTranslation(-sd * 0.09f * w, -0.5f * h, 0) * Matrix4x4.CreateRotationZ(-sd * 0.3f) * Matrix4x4.CreateTranslation(sd * 0.09f * w, 0.5f * h, 0);
            b.SideSlab(fin, sd * 0.08f * w, sd * 0.1f * w, Team, null, cant);
        }
        b.Box(V(0, 0.45f * h, -0.502f * l), V(0.13f * w, 0.18f * h, 0.01f * l), GlowO);
    }

    private static void Bomber(MeshBuilder b, float w, float h, float l)
    {
        var oct = MeshBuilder.NGon(8, 1, MathF.PI / 8);
        b.Loft(oct, [
            new Station(-0.5f * l, 0.07f * w, 0.12f * h, 0, 0.55f * h), new Station(-0.25f * l, 0.12f * w, 0.26f * h, 0, 0.5f * h),
            new Station(0.25f * l, 0.12f * w, 0.28f * h, 0, 0.5f * h), new Station(0.42f * l, 0.08f * w, 0.18f * h, 0, 0.5f * h),
            new Station(0.5f * l, 0.02f * w, 0.05f * h, 0, 0.48f * h)], Axis.Z, Hull, Dark);
        b.Sphere(V(0, 0.68f * h, 0.3f * l), V(0.06f * w, 0.1f * h, 0.1f * l), 4, 6, Glass, null, 0.5f);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            Vector2[] wing = [V2(sd * 0.1f * w, 0.12f * l), V2(sd * 0.5f * w, -0.08f * l), V2(sd * 0.5f * w, -0.18f * l), V2(sd * 0.1f * w, -0.12f * l)];
            b.Slab(wing, 0.46f * h, 0.52f * h, Hull);
            Vector2[] stripe = [V2(sd * 0.38f * w, 0.0f * l), V2(sd * 0.44f * w, -0.03f * l), V2(sd * 0.44f * w, -0.15f * l), V2(sd * 0.38f * w, -0.14f * l)];
            b.Slab(stripe, 0.521f * h, 0.525f * h, Team);
            b.Cylinder(V(sd * 0.27f * w, 0.4f * h, -0.18f * l), Axis.Z, 0.36f * l, 0.06f * w, 0.055f * w, 8, HullDark);
            b.Cylinder(V(sd * 0.27f * w, 0.4f * h, -0.185f * l), Axis.Z, 0.006f, 0.045f * w, 0.045f * w, 8, GlowO);
            Vector2[] stab = [V2(0, -0.38f * l), V2(sd * 0.2f * w, -0.46f * l), V2(sd * 0.2f * w, -0.52f * l), V2(0, -0.5f * l)];
            b.Slab(stab, 0.55f * h, 0.58f * h, Hull);
        }
        Vector2[] fin = [V2(-0.5f * l, 0.6f * h), V2(-0.3f * l, 0.6f * h), V2(-0.44f * l, 1.0f * h), V2(-0.52f * l, 1.0f * h)];
        b.SideSlab(fin, -0.012f * w, 0.012f * w, Team);
    }

    private static void Gunship(MeshBuilder b, float w, float h, float l)
    {
        var oct = MeshBuilder.NGon(8, 1, MathF.PI / 8);
        b.Loft(oct, [
            new Station(-0.48f * l, 0.08f * w, 0.12f * h, 0, 0.55f * h), new Station(-0.1f * l, 0.2f * w, 0.36f * h, 0, 0.5f * h),
            new Station(0.3f * l, 0.18f * w, 0.3f * h, 0, 0.46f * h), new Station(0.5f * l, 0.05f * w, 0.1f * h, 0, 0.42f * h)], Axis.Z, Hull, Dark);
        b.Box(V(0, 0.5f * h, 0.36f * l), V(0.2f * w, 0.12f * h, 0.1f * l), Glass);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Box(V(sd * 0.25f * w, 0.55f * h, 0), V(0.3f * w, 0.05f * h, 0.12f * l), HullDark);
            b.Cylinder(V(sd * 0.36f * w, 0.42f * h, 0), Axis.Y, 0.2f * h, 0.15f * w, 0.15f * w, 10, Hull, Black);
            b.Cylinder(V(sd * 0.36f * w, 0.48f * h, 0), Axis.Y, 0.08f * h, 0.155f * w, 0.155f * w, 10, Team);
            b.Cylinder(V(sd * 0.18f * w, 0.36f * h, 0.12f * l), Axis.Z, 0.25f * l, 0.04f * w, 0.04f * w, 6, Dark);
        }
        b.Box(V(0, 0.55f * h, -0.47f * l), V(0.2f * w, 0.12f * h, 0.03f * l), Team);
    }

    private static void Transport(MeshBuilder b, float w, float h, float l)
    {
        b.Loft(MeshBuilder.Chamfered(0.36f * w, 0.5f * h, 0.08f * w), [
            new Station(-0.5f * l, 0.7f, 0.6f, 0, 0.55f * h), new Station(-0.3f * l, 1, 1, 0, 0.5f * h),
            new Station(0.32f * l, 1, 1, 0, 0.5f * h), new Station(0.5f * l, 0.6f, 0.5f, 0, 0.45f * h)], Axis.Z, Hull, Dark);
        b.Box(V(0, 0.62f * h, 0.42f * l), V(0.2f * w, 0.1f * h, 0.1f * l), Glass);
        b.Box(V(0, 0.76f * h, 0), V(0.2f * w, 0.02f * h, 0.5f * l), Team);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        foreach (float z in (ReadOnlySpan<float>)[-0.28f, 0.22f])
        {
            b.Box(V(sd * 0.28f * w, 0.55f * h, z * l), V(0.22f * w, 0.05f * h, 0.06f * l), HullDark);
            b.Cylinder(V(sd * 0.38f * w, 0.4f * h, z * l), Axis.Y, 0.25f * h, 0.12f * w, 0.12f * w, 10, Hull, Black);
            b.Cylinder(V(sd * 0.38f * w, 0.5f * h, z * l), Axis.Y, 0.08f * h, 0.125f * w, 0.125f * w, 10, Team);
        }
    }

    // ================================================================ naval

    /// <summary>Returns the deck height.</summary>
    private static float Ship(MeshBuilder b, float w, float h, float l, string kind)
    {
        Vector2[] prof = [V2(-0.5f, 1.0f), V2(-0.44f, 0.3f), V2(0, 0), V2(0.44f, 0.3f), V2(0.5f, 1.0f)];
        float bottom = -0.3f * h, hh = 0.55f * h, deck = bottom + hh;
        b.Loft(prof, [
            new Station(-0.5f * l, 0.84f * w, hh * 0.9f, 0, bottom + hh * 0.1f), new Station(-0.25f * l, w, hh, 0, bottom),
            new Station(0.22f * l, 0.96f * w, hh, 0, bottom), new Station(0.4f * l, 0.62f * w, hh * 1.03f, 0, bottom),
            new Station(0.5f * l, 0.04f * w, hh * 1.08f, 0, bottom)], Axis.Z, Hull, HullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.49f * w, deck - 0.06f * h, -0.05f * l), V(0.012f * w, 0.06f * h, 0.6f * l), Team);
        b.Box(V(0, deck + 0.001f, 0), V(0.8f * w, 0.004f, 0.7f * l), Pad);
        // Superstructure.
        float sl = kind == "cruiser" ? 0.3f : 0.22f;
        b.BevelBox(V(0, deck + 0.18f * h, -0.04f * l), V(0.56f * w, 0.36f * h, sl * l), 0.05f * w, HullLight);
        b.BevelBox(V(0, deck + 0.42f * h, -0.02f * l), V(0.4f * w, 0.14f * h, sl * 0.6f * l), 0.04f * w, Hull);
        b.Box(V(0, deck + 0.43f * h, -0.02f * l + sl * 0.3f * l + 0.001f), V(0.34f * w, 0.05f * h, 0.01f * l), Glass);
        b.Cylinder(V(0, deck + 0.49f * h, -0.06f * l), Axis.Y, 0.4f * h, 0.03f * w, 0.02f * w, 5, HullDark);
        b.Box(V(0, deck + 0.75f * h, -0.06f * l), V(0.28f * w, 0.03f * h, 0.04f * l), HullDark);
        b.Box(V(0, deck + 0.9f * h, -0.06f * l), V(0.04f * w, 0.04f * h, 0.04f * w), GlowR);
        b.Box(V(0, deck + 0.37f * h, -0.04f * l), V(0.57f * w, 0.05f * h, sl * 1.01f * l), Team);
        if (kind == "cruiser")
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
                b.Box(V(sd * 0.28f * w, deck + 0.05f * h, -0.32f * l), V(0.16f * w, 0.1f * h, 0.12f * l), HullDark, Dark);
        return deck;
    }

    private static void NavalTurret(MeshBuilder t, float w, float h, float l)
    {
        var prof = MeshBuilder.Chamfered(0.3f * w, 0.16f * h, 0.05f * w);
        t.Loft(prof, [new Station(-0.06f * l, 0.9f, 0.9f, 0, 0.08f * h), new Station(0.04f * l, 1, 1, 0, 0.08f * h), new Station(0.07f * l, 0.7f, 0.7f, 0, 0.08f * h)], Axis.Z, Hull);
        t.Box(V(0, 0.162f * h, -0.01f * l), V(0.18f * w, 0.006f * h, 0.07f * l), Team);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Cylinder(V(sd * 0.05f * w, 0.08f * h, 0.06f * l), Axis.Z, 0.12f * l, 0.022f * w, 0.02f * w, 5, HullDark);
    }

    private static void Sub(MeshBuilder b, float w, float h, float l)
    {
        var oct = MeshBuilder.NGon(8, 1, MathF.PI / 8);
        b.Loft(oct, [
            new Station(-0.5f * l, 0.08f * w, 0.08f * h, 0, 0.1f * h), new Station(-0.35f * l, 0.38f * w, 0.32f * h, 0, 0.1f * h),
            new Station(0.3f * l, 0.5f * w, 0.4f * h, 0, 0.1f * h), new Station(0.45f * l, 0.35f * w, 0.3f * h, 0, 0.1f * h),
            new Station(0.5f * l, 0.1f * w, 0.1f * h, 0, 0.1f * h)], Axis.Z, HullDark, Dark);
        b.BevelBox(V(0, 0.6f * h, 0.1f * l), V(0.18f * w, 0.4f * h, 0.16f * l), 0.04f * w, Hull, Team);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.25f * w, 0.1f * h, -0.44f * l), V(0.2f * w, 0.02f * h, 0.06f * l), Team);
    }

    // ================================================================ experimental

    private static void Spider(MeshBuilder b, float w, float h, float l)
    {
        b.Loft(MeshBuilder.NGon(6, 1, 0), [
            new Station(0.42f * h, 0.24f * w, 0.32f * l), new Station(0.52f * h, 0.3f * w, 0.4f * l), new Station(0.62f * h, 0.26f * w, 0.34f * l)], Axis.Y, Hull, HullDark);
        b.Cylinder(V(0, 0.5f * h, 0), Axis.Y, 0.05f * h, 0.305f * w, 0.3f * w, 6, Team, null, null, 0);
        for (int i = 0; i < 6; i++)
        {
            float a = (i < 3 ? 0.55f + i * 1.0f : MathF.PI + 0.55f + (i - 3) * 1.0f);
            var dir = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            var hip = new Vector3(dir.X * 0.26f * w, 0.5f * h, dir.Z * 0.32f * l);
            var knee = new Vector3(dir.X * 0.52f * w, 0.85f * h, dir.Z * 0.6f * l);
            var foot = new Vector3(dir.X * 0.62f * w, 0, dir.Z * 0.72f * l);
            Limb(b, hip, knee, 0.07f * w, Hull);
            Limb(b, knee, foot, 0.05f * w, HullDark);
            b.Box(knee, new Vector3(0.1f * w, 0.08f * h, 0.1f * w), Team);
        }
        b.Box(V(0, 0.5f * h, 0.3f * l), V(0.2f * w, 0.05f * h, 0.03f * l), GlowR);
    }

    private static void Limb(MeshBuilder b, Vector3 a, Vector3 c, float r, Color col)
    {
        var d = c - a;
        float len = d.Length();
        var dir = d / len;
        // Build a box along Z then rotate Z -> dir.
        var up = MathF.Abs(dir.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
        var x = Vector3.Normalize(Vector3.Cross(up, dir));
        var y = Vector3.Cross(dir, x);
        var m = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, dir.X, dir.Y, dir.Z, 0, a.X, a.Y, a.Z, 1);
        b.Loft(MeshBuilder.Chamfered(2 * r, 2 * r, r * 0.4f), [new Station(0, 1, 1), new Station(len, 0.8f, 0.8f)], Axis.Z, col, null, m);
    }

    private static void SpiderTurret(MeshBuilder t, float w, float h, float l)
    {
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.05f * h, 0.2f * w, 0.2f * w, 8, Dark);
        t.Loft(MeshBuilder.Chamfered(0.36f * w, 0.18f * h, 0.06f * w), [new Station(-0.2f * l, 0.9f, 0.9f, 0, 0.13f * h), new Station(0.12f * l, 1, 1, 0, 0.13f * h), new Station(0.2f * l, 0.7f, 0.7f, 0, 0.13f * h)], Axis.Z, Hull);
        t.Box(V(0, 0.222f * h, -0.04f * l), V(0.2f * w, 0.01f * h, 0.2f * l), Team);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Cylinder(V(sd * 0.08f * w, 0.13f * h, 0.15f * l), Axis.Z, 0.4f * l, 0.035f * w, 0.03f * w, 6, HullDark);
            t.Box(V(sd * 0.08f * w, 0.13f * h, 0.56f * l), V(0.04f * w, 0.04f * w, 0.02f * l), GlowO);
        }
    }
}
