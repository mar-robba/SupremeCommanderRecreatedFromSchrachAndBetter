using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;

namespace Rts.Client.Rendering;

/// <summary>
/// Cybran faction models (every "cy_*" Visual.Shape): angular, faceted, asymmetric; dark gunmetal hulls with team-color
/// accents and red glowing seams/lenses; spidery legs on bots and walkers. This file: palette, shared helpers, the
/// shape dispatcher and the land units (commanders, engineers, bots, tanks, artillery). Aircraft:
/// <c>UnitModels.CybranAir.cs</c>; ships: <c>UnitModels.CybranNaval.cs</c>; structures:
/// <c>UnitModels.CybranStructures.cs</c>; experimentals: <c>UnitModels.CybranExperimental.cs</c>.
/// </summary>
public static partial class UnitModels
{
    private static readonly Color CyHull = Mat.M(76, 72, 82);
    private static readonly Color CyHullLight = Mat.M(110, 104, 116);
    private static readonly Color CyHullDark = Mat.M(52, 49, 57);
    private static readonly Color CyBlack = Mat.M(27, 25, 30);
    private static readonly Color CyTrim = Mat.M(146, 132, 132);
    private static readonly Color CyPad = Mat.M(66, 63, 70);
    private static readonly Color CyTeam = Mat.T(205);
    private static readonly Color CyTeamDim = Mat.T(160);
    private static readonly Color CyRed = Mat.G(255, 38, 26);
    private static readonly Color CyRedDim = Mat.G(175, 26, 22);
    private static readonly Color CyViolet = Mat.G(196, 80, 255);
    private static readonly Color CyGreen = Mat.G(120, 255, 110);
    private static readonly Color CyAmber = Mat.G(255, 140, 40);

    /// <summary>True for Cybran model shapes (all start with "cy_").</summary>
    public static bool IsCybranShape(string shape) => shape.StartsWith("cy_", StringComparison.Ordinal);

    /// <summary>
    /// The UEF shape a Cybran shape plays the role of ("cy_frigate" → "frigate", "cy_air_staging" → "air_staging"), for
    /// client code that keys on shape names (cargo pads, naval labels). Other shapes are returned unchanged.
    /// </summary>
    public static string BaseShape(string shape) => shape switch
    {
        "cy_air_staging" => "air_staging",
        "cy_frigate" => "frigate",
        "cy_destroyer" => "destroyer",
        "cy_cruiser" => "cruiser",
        "cy_battleship" => "battleship",
        "cy_sub" => "sub",
        "cy_transport" => "transport",
        "cy_transport2" => "transport2",
        _ when IsCybranShape(shape) => shape[3..],
        _ => shape,
    };

    private static int CyTier(UnitBlueprint bp) => bp.Has(UnitCategory.Experimental) ? 4 : bp.Has(UnitCategory.Tech3) ? 3 : bp.Has(UnitCategory.Tech2) ? 2 : 1;

    // ================================================================ dispatcher

    /// <summary>Builds the body into <paramref name="b"/> and returns the turret mesh (or null) for a "cy_*" shape.</summary>
    private static MeshBuilder? CybranShape(UnitBlueprint bp, MeshBuilder b, float w, float h, float l, bool hasWeapon,
                                            List<Vector3> pivots, ref TurretMode mode, ref float spin)
    {
        int tier = CyTier(bp);
        bool air = bp.Has(UnitCategory.Air);
        MeshBuilder? t = null;
        switch (bp.Visual.Shape)
        {
            // ---- commanders & engineers
            case "cy_commander": t = CyCommander(b, pivots, w, h, l, false); break;
            case "cy_scu": t = CyCommander(b, pivots, w, h, l, true); break;
            case "cy_engineer": CyEngineer(b, w, h, l, 1); break;
            case "cy_engineer2": CyEngineer(b, w, h, l, 2); break;
            case "cy_engineer3": CyEngineer(b, w, h, l, 3); break;
            // ---- land
            case "cy_scout": t = CyScout(b, pivots, w, h, l, hasWeapon); break;
            case "cy_light_bot": t = CyLightBot(b, pivots, w, h, l); break;
            case "cy_bot": t = CyMantis(b, pivots, w, h, l); break;
            case "cy_artillery": t = CyMedusa(b, pivots, w, h, l); break;
            case "cy_aa": t = CySkySlammer(b, pivots, w, h, l); break;
            case "cy_heavy_tank": t = CyRhino(b, pivots, w, h, l); break;
            case "cy_amphibious": t = CyWagner(b, pivots, w, h, l); break;
            case "cy_missile_launcher": t = CyViper(b, pivots, w, h, l); break;
            case "cy_flak": t = CyBanger(b, pivots, w, h, l); break;
            case "cy_stealth": t = CyDeceiver(b, pivots, w, h, l); mode = TurretMode.Spin; spin = 1.3f; break;
            case "cy_assault_bot": t = CyLoyalist(b, pivots, w, h, l); break;
            case "cy_heavy_artillery": t = CyTrebuchet(b, pivots, w, h, l); break;
            // ---- air (UnitModels.CybranAir.cs)
            case "cy_air_scout": CyAirScout(b, w, h, l); break;
            case "cy_interceptor": CyProwler(b, w, h, l); break;
            case "cy_bomber": CyZeus(b, w, h, l); break;
            case "cy_gunship": t = CyRenegade(b, pivots, w, h, l, hasWeapon); break;
            case "cy_torpedo_bomber": CyCormorant(b, w, h, l); break;
            case "cy_asf": CyGemini(b, w, h, l); break;
            case "cy_strat_bomber": CyRevenant(b, w, h, l); break;
            case "cy_spy_plane": CySpook(b, w, h, l); break;
            case "cy_transport": CySkyhook(b, w, h, l); break;
            case "cy_transport2": t = CyDragonFly(b, pivots, w, h, l, hasWeapon); break;
            case "cy_air_staging": CyAirStaging(b, w, h, l); break;
            // ---- naval (UnitModels.CybranNaval.cs)
            case "cy_frigate": t = CyFrigate(b, pivots, w, h, l, hasWeapon); break;
            case "cy_destroyer": t = CyDestroyer(b, pivots, w, h, l, hasWeapon); break;
            case "cy_cruiser": t = CyCruiser(b, pivots, w, h, l, hasWeapon); break;
            case "cy_battleship": t = CyBattleship(b, pivots, w, h, l, hasWeapon); break;
            case "cy_sub": CySub(b, w, h, l); break;
            case "cy_naval_factory": CyNavalFactory(b, w, h, l, tier); break;
            case "cy_sonar": t = CySonar(b, pivots, w, h, l, 1); mode = TurretMode.Spin; spin = 1.5f; break;
            case "cy_sonar2": t = CySonar(b, pivots, w, h, l, 2); mode = TurretMode.Spin; spin = 2.0f; break;
            case "cy_torpedo_launcher": t = CyTorpedoLauncher(b, pivots, w, h, l, hasWeapon); break;
#if CYBRAN_MODELS_FULL // TODO (5l, sub-agent C2): remove this guard once the experimental/structure partials exist
            // ---- experimentals (UnitModels.CybranExperimental.cs)
            case "cy_monkeylord": t = CyMonkeylord(b, pivots, w, h, l); break;
            case "cy_soul_ripper": t = CySoulRipper(b, pivots, w, h, l); break;
            case "cy_scathis": t = CyScathis(b, pivots, w, h, l); break;
            // ---- structures (UnitModels.CybranStructures.cs)
            case "cy_factory": CyFactory(b, w, h, l, air, 1); break;
            case "cy_factory2": CyFactory(b, w, h, l, air, 2); break;
            case "cy_factory3": CyFactory(b, w, h, l, air, 3); break;
            case "cy_gateway": t = CyGateway(b, pivots, w, h, l); mode = TurretMode.Spin; spin = 0.6f; break;
            case "cy_mex": t = CyMex(b, pivots, w, h, l, 1); mode = TurretMode.Spin; spin = 2.0f; break;
            case "cy_mex2": t = CyMex(b, pivots, w, h, l, 2); mode = TurretMode.Spin; spin = 2.6f; break;
            case "cy_mex3": t = CyMex(b, pivots, w, h, l, 3); mode = TurretMode.Spin; spin = 3.2f; break;
            case "cy_pgen": CyPowerGen(b, w, h, l, 1); break;
            case "cy_pgen2": CyPowerGen(b, w, h, l, 2); break;
            case "cy_pgen3": t = CyPowerGen3(b, pivots, w, h, l); mode = TurretMode.Spin; spin = 0.8f; break;
            case "cy_hydro": CyHydro(b, w, h, l); break;
            case "cy_massfab": CyMassFab(b, w, h, l, 2); break;
            case "cy_massfab3": CyMassFab(b, w, h, l, 3); break;
            case "cy_storage": CyStorage(b, w, h, l, bp.Has(UnitCategory.EnergyStorage)); break;
            case "cy_pd": t = CyPointDefense(b, pivots, w, h, l, 1); break;
            case "cy_pd2": t = CyPointDefense(b, pivots, w, h, l, 2); break;
            case "cy_aa_turret": t = CyAaTurret(b, pivots, w, h, l, 1); break;
            case "cy_aa_turret2": t = CyAaTurret(b, pivots, w, h, l, 2); break;
            case "cy_aa_turret3": t = CyAaTurret(b, pivots, w, h, l, 3); break;
            case "cy_shield": t = CyShield(b, pivots, w, h, l, CyShieldLevel(bp)); mode = TurretMode.Spin; spin = 0.9f; break;
            case "cy_missile_silo": CyMissileSilo(b, w, h, l, false); break;
            case "cy_nuke_silo": CyMissileSilo(b, w, h, l, true); break;
            case "cy_tmd": t = CyZapper(b, pivots, w, h, l); if (!hasWeapon) { mode = TurretMode.Spin; spin = 0.7f; } break;
            case "cy_antinuke": t = CyAntiNuke(b, pivots, w, h, l); break;
            case "cy_artillery_structure": t = CyArtilleryStructure(b, pivots, w, h, l, tier); break;
            case "cy_radar": t = CyRadar(b, pivots, w, h, l, 1); mode = TurretMode.Spin; spin = 1.2f; break;
            case "cy_radar2": t = CyRadar(b, pivots, w, h, l, 2); mode = TurretMode.Spin; spin = 1.6f; break;
            case "cy_omni": t = CyOmni(b, pivots, w, h, l); mode = TurretMode.Spin; spin = 0.9f; break;
            case "cy_stealth_field": t = CyStealthField(b, pivots, w, h, l); mode = TurretMode.Spin; spin = 0.7f; break;
#endif
            default: CyGeneric(b, w, h, l, bp.IsStructure); break;
        }
        return t;
    }

    /// <summary>Unknown "cy_*" shape: an angular block (structure) or a small legged hull (mobile).</summary>
    private static void CyGeneric(MeshBuilder b, float w, float h, float l, bool structure)
    {
        if (structure)
        {
            Foundation(b, w, l, 0.08f * h, CyHullDark);
            b.Frustum(V(0, 0.08f * h, 0), 0.8f * w, 0.8f * l, 0.5f * w, 0.5f * l, 0.8f * h, CyHull, CyHullLight, null, 0.2f);
            b.Box(V(0, 0.5f * h, 0.33f * l), V(0.3f * w, 0.04f * h, 0.02f * l), CyRed);
            return;
        }
        CyLegSet(b, 2, w, h, l, 0.42f, 0.66f, 0.7f, 0.3f, 0.5f, 0.62f, 0.05f, CyHull, CyHullDark, CyTeam);
        CyWedge(b, 0, 0.32f * h, 0.6f * w, 0.36f * h, -0.4f * l, 0.42f * l, CyHull, CyHullDark);
        b.Box(V(0, 0.45f * h, 0.38f * l), V(0.2f * w, 0.05f * h, 0.03f * l), CyRed);
    }

    /// <summary>Cybran culling radius for models whose parts reach far outside Visual.Size (long barrels, wide legs).</summary>
    private static float CybranCullRadius(string shape, float w, float h, float l, float radius) => shape switch
    {
        "cy_scathis" => MathF.Max(radius, 1.5f * MathF.Max(w, l)),
        "cy_monkeylord" => MathF.Max(radius, 0.85f * MathF.Max(w, l)),
        "cy_artillery_structure" or "cy_heavy_artillery" => MathF.Max(radius, 1.1f * l),
        _ => radius,
    };

    // ================================================================ helpers

    /// <summary>Row-vector frame whose +Z points along <paramref name="dir"/> (unit), origin at <paramref name="a"/>.</summary>
    private static Matrix4x4 AxisFrame(Vector3 a, Vector3 dir)
    {
        var up = MathF.Abs(dir.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
        var x = Vector3.Normalize(Vector3.Cross(up, dir));
        var y = Vector3.Cross(dir, x);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, dir.X, dir.Y, dir.Z, 0, a.X, a.Y, a.Z, 1);
    }

    /// <summary>Faceted spike (cone with <paramref name="sides"/> faces) from base center a to the tip.</summary>
    private static void Spike(MeshBuilder b, Vector3 a, Vector3 tip, float r, Color col, int sides = 4)
    {
        var d = tip - a;
        float len = d.Length();
        if (len < 1e-5f) return;
        b.Loft(MeshBuilder.NGon(sides, r, MathF.PI / sides), [new Station(0, 1, 1), new Station(len, 0, 0)], Axis.Z, col, null, AxisFrame(a, d / len));
    }

    /// <summary>Two-segment leg hip → knee → foot with a joint block and a claw tip on the ground.</summary>
    private static void CyLeg(MeshBuilder b, Vector3 hip, Vector3 knee, Vector3 foot, float r, Color upper, Color lower, Color joint)
    {
        Limb(b, hip, knee, r, upper);
        var mid = knee + (foot - knee) * 0.82f;
        Limb(b, knee, mid, r * 0.78f, lower);
        Spike(b, mid, foot, r * 0.62f, CyBlack);
        b.Box(knee, new Vector3(r * 2.3f, r * 2.1f, r * 2.3f), joint);
    }

    /// <summary>
    /// Spider legs: <paramref name="perSide"/> legs per side fanned over ±<paramref name="spread"/> rad around the
    /// side axis; hip/knee/foot radii are fractions of the half-extent (x by w, z by l), heights fractions of h.
    /// </summary>
    private static void CyLegSet(MeshBuilder b, int perSide, float w, float h, float l, float hipY, float kneeY, float spread,
                                 float hipR, float kneeR, float footR, float r, Color upper, Color lower, Color joint, float zOff = 0)
    {
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            for (int i = 0; i < perSide; i++)
            {
                float phi = perSide == 1 ? 0 : spread * (1 - 2f * i / (perSide - 1));
                float dx = sd * MathF.Cos(phi), dz = MathF.Sin(phi);
                var hip = V(dx * hipR * w, hipY * h, dz * hipR * l + zOff);
                var knee = V(dx * kneeR * w, kneeY * h, dz * kneeR * l + zOff);
                var foot = V(dx * footR * w, 0, dz * footR * l + zOff);
                CyLeg(b, hip, knee, foot, r * w, upper, lower, joint);
            }
    }

    /// <summary>Angular Cybran hull lofted along Z: flat belly at y0, sloped flanks, a narrow (optionally offset) ridge;
    /// the nose (z1) is lower and narrower than the body, the tail slightly pinched.</summary>
    private static void CyWedge(MeshBuilder b, float cx, float y0, float hw, float hh, float z0, float z1, Color side, Color? cap = null,
                                float skew = 0, float nose = 0.45f)
    {
        Vector2[] prof = [V2(-0.5f, 0), V2(0.5f, 0), V2(0.5f, 0.42f), V2(0.17f + skew, 1f), V2(-0.17f + skew, 1f), V2(-0.5f, 0.42f)];
        float len = z1 - z0;
        b.Loft(prof, [
            new Station(z0, hw * 0.82f, hh * 0.78f, cx, y0), new Station(z0 + len * 0.3f, hw, hh, cx, y0),
            new Station(z0 + len * 0.7f, hw, hh, cx, y0), new Station(z1, hw * nose, hh * (0.35f + 0.4f * nose), cx, y0)], Axis.Z, side, cap);
    }

    /// <summary>Thin triangular fin in the ZY plane at x (thickness th): base from z0 to z1 at y0, apex at (zt, yt).</summary>
    private static void Fin(MeshBuilder b, float x, float th, float y0, float z0, float z1, float zt, float yt, Color col, Matrix4x4? xf = null)
    {
        Vector2[] tri = [V2(z0, y0), V2(z1, y0), V2(zt, yt)];
        b.SideSlab(tri, x - th * 0.5f, x + th * 0.5f, col, null, xf);
    }

    /// <summary>Angular tracks: black faceted track slabs with a dark armored skirt and red running light.</summary>
    private static void CyTracks(MeshBuilder b, float w, float h, float l, float tw, float th)
    {
        Vector2[] prof = [V2(-0.5f * l, th * 0.5f), V2(-0.4f * l, 0), V2(0.4f * l, 0), V2(0.5f * l, th * 0.55f), V2(0.44f * l, th), V2(-0.46f * l, th)];
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.SideSlab(prof, sd * (w / 2 - tw), sd * w / 2, CyBlack);
            Vector2[] skirt = [V2(-0.47f * l, th * 0.55f), V2(0.47f * l, th * 0.55f), V2(0.42f * l, th * 1.12f), V2(-0.44f * l, th * 1.12f)];
            b.SideSlab(skirt, sd * (w / 2 + 0.01f * w), sd * (w / 2 - tw * 0.4f), CyHullDark);
            b.Box(V(sd * (w / 2 + 0.012f * w), th * 0.85f, 0.36f * l), V(0.01f * w, 0.05f * h, 0.06f * l), CyRedDim);
        }
    }

    // ================================================================ commanders

    /// <summary>
    /// Cybran ACU / SCU: digitigrade legs with forward knees and heel claws (body), turret = hunched keel-chested torso,
    /// forward-thrust head with a red visor slit, swept shoulder spikes, a back spine of fins, gun right arm (+X) and a
    /// claw build arm (-X). The SCU is bulkier: wider shoulders, a big back module, a squat head.
    /// </summary>
    private static MeshBuilder CyCommander(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, bool scu)
    {
        float hip = scu ? 0.46f : 0.47f;
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            var hipP = V(sd * 0.16f * w, hip * h, -0.02f * l);
            var knee = V(sd * 0.21f * w, 0.29f * h, 0.15f * l);
            var ankle = V(sd * 0.2f * w, 0.1f * h, -0.1f * l);
            var toe = V(sd * 0.2f * w, 0.02f * h, 0.17f * l);
            Limb(b, hipP, knee, (scu ? 0.08f : 0.07f) * w, CyHull);
            Limb(b, knee, ankle, 0.055f * w, CyHullDark);
            Limb(b, ankle, toe, 0.05f * w, CyHull);
            b.BevelBox(knee + V(0, 0.01f * h, 0.02f * l), V(0.12f * w, 0.1f * h, 0.1f * l), 0.03f * w, CyTeam);
            Spike(b, knee + V(0, 0.03f * h, 0.03f * l), knee + V(0, 0.09f * h, 0.15f * l), 0.03f * w, CyHullLight);
            Spike(b, ankle, V(ankle.X, 0, -0.21f * l), 0.03f * w, CyBlack);
            foreach (int k in (ReadOnlySpan<int>)[-1, 1])
                Spike(b, toe, toe + V(k * 0.05f * w, -0.02f * h, 0.08f * l), 0.022f * w, CyBlack);
            b.Box(ankle + V(sd * 0.03f * w, 0, 0), V(0.02f * w, 0.03f * h, 0.03f * l), CyRed);
        }
        b.BevelBox(V(0, hip * h - 0.02f * h, -0.02f * l), V(0.42f * w, 0.1f * h, 0.24f * l), 0.03f * w, CyHullDark);

        var t = new MeshBuilder();
        float sw = scu ? 1.12f : 1f; // shoulder/torso width factor
        t.Cylinder(V(0, -0.01f * h, 0), Axis.Y, 0.05f * h, 0.11f * w, 0.11f * w, 6, CyBlack);
        // Keel chest: pentagon pointing forward (xz profile), widening then narrowing upward.
        Vector2[] chest = [V2(-0.5f, -0.4f), V2(0.5f, -0.4f), V2(0.5f, 0.12f), V2(0f, 0.5f), V2(-0.5f, 0.12f)];
        t.Loft(chest, [new Station(0.03f * h, 0.36f * w * sw, 0.3f * l), new Station(0.2f * h, 0.56f * w * sw, 0.42f * l, 0, 0.03f * l),
                       new Station(0.32f * h, 0.46f * w * sw, 0.34f * l, 0, 0.05f * l)], Axis.Y, CyHull, CyHullLight);
        t.Box(V(0, 0.17f * h, 0.215f * l), V(0.09f * w, 0.06f * h, 0.05f * l), CyRed); // reactor core in the keel
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Box(V(sd * 0.14f * w, 0.23f * h, 0.15f * l), V(0.1f * w, 0.012f * h, 0.12f * l), CyTeamDim, null, Matrix4x4.CreateRotationY(-sd * 0.6f) * T(0, 0, 0));
        // Head: forward-thrust wedge with a red visor slit (SCU: squat and wider).
        float hy = scu ? 0.33f : 0.36f, hw = scu ? 0.2f : 0.15f;
        t.Loft(MeshBuilder.Chamfered(hw * w, 0.08f * h, 0.025f * w), [
            new Station(-0.04f * l, 0.9f, 0.9f, 0, hy * h), new Station(0.12f * l, 1, 1, 0, hy * h), new Station(0.2f * l, 0.6f, 0.55f, 0, hy * h - 0.01f * h)], Axis.Z, CyHullLight, CyHull);
        t.Box(V(0, hy * h + 0.004f * h, 0.19f * l), V(hw * 0.62f * w, 0.016f * h, 0.02f * l), CyRed);
        if (!scu) Spike(t, V(-0.04f * w, hy * h + 0.04f * h, 0.0f), V(-0.06f * w, hy * h + 0.16f * h, -0.08f * l), 0.012f * w, CyHullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            // Shoulder plates (team color) with swept spikes; dark upper arms.
            t.BevelBox(V(sd * 0.33f * w * sw, 0.27f * h, -0.01f * l), V(0.2f * w * sw, 0.09f * h, 0.28f * l), 0.04f * w, CyTeam);
            Spike(t, V(sd * 0.36f * w * sw, 0.3f * h, -0.06f * l), V(sd * 0.47f * w * sw, 0.43f * h, -0.24f * l), 0.04f * w, CyHullLight);
            if (scu) Spike(t, V(sd * 0.3f * w * sw, 0.31f * h, 0.04f * l), V(sd * 0.36f * w * sw, 0.4f * h, -0.06f * l), 0.03f * w, CyHullDark);
            t.Box(V(sd * 0.35f * w * sw, 0.17f * h, 0), V(0.1f * w, 0.14f * h, 0.12f * l), CyBlack);
        }
        float ax = 0.36f * w * sw;
        // Right arm (+X): molecular ripper cannon: angular housing, long barrel, red muzzle ring.
        t.BevelBox(V(ax, 0.07f * h, 0.1f * l), V(0.14f * w, 0.12f * h, 0.36f * l), 0.035f * w, CyHull);
        t.Box(V(ax, 0.134f * h, 0.06f * l), V(0.08f * w, 0.01f * h, 0.2f * l), CyTeamDim);
        t.Cylinder(V(ax, 0.07f * h, 0.28f * l), Axis.Z, 0.22f * l, 0.034f * w, 0.03f * w, 5, CyBlack);
        t.Cylinder(V(ax, 0.07f * h, 0.47f * l), Axis.Z, 0.04f * l, 0.042f * w, 0.042f * w, 5, CyHullDark, CyRed);
        t.Box(V(ax + 0.072f * w, 0.07f * h, 0.12f * l), V(0.008f * w, 0.025f * h, 0.18f * l), CyRedDim);
        // Left arm (-X): claw build arm with a red nanolathe emitter between two prongs.
        t.BevelBox(V(-ax, 0.07f * h, 0.08f * l), V(0.13f * w, 0.11f * h, 0.3f * l), 0.03f * w, CyHull);
        t.Box(V(-ax, 0.07f * h, 0.245f * l), V(0.06f * w, 0.06f * h, 0.03f * l), CyRed);
        foreach (int k in (ReadOnlySpan<int>)[-1, 1])
            Spike(t, V(-ax + k * 0.04f * w, 0.08f * h, 0.22f * l), V(-ax + k * 0.06f * w, 0.05f * h, 0.37f * l), 0.02f * w, CyHullLight);
        // Back: armored pack with red vents and a spine of fins (SCU: bigger module with twin spikes).
        float bz = scu ? -0.24f : -0.22f;
        t.BevelBox(V(0, 0.2f * h, bz * l), V((scu ? 0.42f : 0.3f) * w, (scu ? 0.22f : 0.18f) * h, (scu ? 0.16f : 0.12f) * l), 0.03f * w, CyHullDark, CyHull);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Box(V(sd * 0.08f * w, 0.2f * h, (bz - (scu ? 0.081f : 0.061f)) * l), V(0.05f * w, 0.1f * h, 0.01f * l), CyRed);
            if (scu) Spike(t, V(sd * 0.14f * w, 0.3f * h, bz * l), V(sd * 0.18f * w, 0.5f * h, (bz - 0.12f) * l), 0.035f * w, CyHull);
        }
        for (int i = 0; i < 3; i++)
        {
            float z0 = (bz + 0.04f - i * 0.06f) * l;
            Fin(t, 0, 0.016f * w, 0.29f * h - i * 0.03f * h, z0 - 0.06f * l, z0, z0 - 0.09f * l, 0.4f * h - i * 0.04f * h, CyHullLight);
        }
        pivots.Add(V(0, hip * h, 0));
        return t;
    }

    /// <summary>Attachment anchors on the Cybran ACU/SCU torso (see <see cref="CyCommander"/>).</summary>
    private static MechAnchor CybranAnchors(string shape, float w, float h, float l) => shape == "cy_scu"
        ? new MechAnchor(0.4f * w, 0.07f * h, 0.28f * l, 0.32f * h, -0.24f * l, 0.31f * h)
        : new MechAnchor(0.36f * w, 0.07f * h, 0.28f * l, 0.31f * h, -0.22f * l, 0.29f * h);

    /// <summary>Cybran enhancement attachments (Visual hints not handled by the UEF switch). Returns false if unknown.</summary>
    private static bool CybranEnhancement(string visual, MeshBuilder m, MechAnchor a, float s, float w, float h, float l)
    {
        float x = s * a.ArmX, y = a.ArmY;
        switch (visual)
        {
            case "laser": case "microwave_laser": // microwave laser: long emitter housing with red coils and a glowing lens
                m.BevelBox(V(x, y + 0.02f * h, 0.14f * l), V(0.18f * w, 0.16f * h, 0.4f * l), 0.04f * w, CyHullDark, CyHull);
                for (int i = 0; i < 3; i++)
                    m.Cylinder(V(x, y + 0.02f * h, (0.36f + i * 0.07f) * l), Axis.Z, 0.03f * l, 0.07f * w, 0.07f * w, 6, (i & 1) == 0 ? CyRed : CyBlack);
                m.Cylinder(V(x, y + 0.02f * h, 0.56f * l), Axis.Z, 0.12f * l, 0.05f * w, 0.035f * w, 6, CyHull, CyRed);
                return true;
            case "emp": // EMP array: claw of three prongs around a violet-white capacitor ball
                m.BevelBox(V(x, y, 0.12f * l), V(0.17f * w, 0.14f * h, 0.3f * l), 0.03f * w, CyHull);
                m.Sphere(V(x, y, 0.33f * l), V(0.06f * w, 0.06f * w, 0.06f * w), 4, 6, CyViolet);
                for (int k = 0; k < 3; k++)
                {
                    float ang = MathF.PI / 2 + k * MathF.Tau / 3f;
                    var p0 = V(x + MathF.Cos(ang) * 0.07f * w, y + MathF.Sin(ang) * 0.07f * w, 0.26f * l);
                    Spike(m, p0, p0 + V(MathF.Cos(ang) * 0.02f * w, MathF.Sin(ang) * 0.02f * w, 0.18f * l), 0.02f * w, CyHullLight);
                }
                return true;
            case "torpedo": // nanite torpedo launcher on the forearm
                m.BevelBox(V(x + s * 0.06f * w, y - 0.04f * h, 0.12f * l), V(0.1f * w, 0.09f * h, 0.3f * l), 0.02f * w, CyHullDark);
                foreach (int k in (ReadOnlySpan<int>)[-1, 1])
                    m.Cylinder(V(x + s * 0.06f * w + k * 0.025f * w, y - 0.04f * h, 0.26f * l), Axis.Z, 0.06f * l, 0.02f * w, 0.02f * w, 6, CyBlack, CyRedDim);
                return true;
            case "coolant": case "cooling": // heat sink augmentation: finned radiator block along the gun arm
                for (int i = 0; i < 4; i++)
                    m.Box(V(x, y + 0.08f * h, (0.02f + i * 0.06f) * l), V(0.18f * w, 0.03f * h, 0.02f * l), i == 3 ? CyRed : CyHullLight);
                return true;
            case "stealth": // personal stealth generator: angular back pack with violet emitter vanes
                m.BevelBox(V(0, a.BackY, a.BackZ - 0.02f * l), V(0.3f * w, 0.14f * h, 0.12f * l), 0.03f * w, CyHull, CyHullDark);
                foreach (int k in (ReadOnlySpan<int>)[-1, 1])
                    Fin(m, k * 0.1f * w, 0.012f * w, a.BackY + 0.07f * h, a.BackZ - 0.04f * l, a.BackZ + 0.04f * l, a.BackZ - 0.1f * l, a.BackY + 0.22f * h, CyViolet);
                return true;
            case "cloak": // personal cloaking generator: taller emitter mast with a violet crystal and ring
                m.BevelBox(V(0, a.BackY, a.BackZ - 0.02f * l), V(0.32f * w, 0.14f * h, 0.13f * l), 0.03f * w, CyHull, CyHullDark);
                m.Cylinder(V(0, a.BackY + 0.07f * h, a.BackZ - 0.03f * l), Axis.Y, 0.18f * h, 0.03f * w, 0.02f * w, 5, CyHullDark);
                m.Sphere(V(0, a.BackY + 0.3f * h, a.BackZ - 0.03f * l), V(0.05f * w, 0.08f * h, 0.05f * w), 3, 4, CyViolet);
                Ring(m, V(0, a.BackY + 0.2f * h, a.BackZ - 0.03f * l), 0.1f * w, 0.012f * w, 0.02f * h, 8, CyHullLight, CyViolet);
                return true;
            case "nano_repair": case "repair": // nano-repair system: green-lit canisters on the back
                foreach (int k in (ReadOnlySpan<int>)[-1, 1])
                {
                    m.Cylinder(V(k * 0.1f * w, a.BackY - 0.05f * h, a.BackZ - 0.06f * l), Axis.Y, 0.18f * h, 0.045f * w, 0.04f * w, 6, CyHullLight, CyGreen);
                    m.Box(V(k * 0.1f * w, a.BackY + 0.02f * h, a.BackZ - 0.105f * l), V(0.03f * w, 0.08f * h, 0.01f * l), CyGreen);
                }
                return true;
        }
        return false;
    }

    // ================================================================ engineers

    /// <summary>Crab-like engineer: faceted carapace on 4 (T1) or 6 legs, red eye, nanolathe arm(s); T3 adds ridge spikes and an antenna.</summary>
    private static void CyEngineer(MeshBuilder b, float w, float h, float l, int tier)
    {
        int legs = tier == 1 ? 2 : 3;
        CyLegSet(b, legs, w, h, l, 0.38f, 0.64f, tier == 1 ? 0.75f : 0.95f, 0.28f, 0.5f, 0.62f, 0.05f, CyHull, CyHullDark, tier >= 2 ? CyTeam : CyHullDark);
        var hex = MeshBuilder.NGon(6, 1, 0);
        b.Loft(hex, [new Station(0.26f * h, 0.3f * w, 0.32f * l), new Station(0.44f * h, 0.36f * w, 0.4f * l, 0, -0.02f * l),
                     new Station(0.6f * h, 0.22f * w, 0.26f * l, 0, -0.04f * l)], Axis.Y, CyHull, CyHullLight);
        b.Box(V(0, 0.605f * h, -0.06f * l), V(0.16f * w, 0.01f * h, 0.22f * l), CyTeam);
        b.Box(V(0, 0.4f * h, 0.37f * l), V(0.16f * w, 0.05f * h, 0.03f * l), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            if (sd < 0 && tier == 1) continue;
            float x = sd * 0.1f * w;
            var a0 = V(x, 0.58f * h, -0.04f * l);
            var a1 = V(x, 0.84f * h, 0.18f * l);
            var a2 = V(x * 1.2f, 0.66f * h, 0.42f * l);
            Limb(b, a0, a1, 0.035f * w, CyHullLight);
            Limb(b, a1, a2, 0.028f * w, CyHull);
            b.Box(a2, V(0.06f * w, 0.06f * h, 0.05f * l), CyRed);
        }
        if (tier >= 2)
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
                Spike(b, V(sd * 0.22f * w, 0.48f * h, -0.24f * l), V(sd * 0.3f * w, 0.62f * h, -0.48f * l), 0.035f * w, CyHullDark);
        if (tier >= 3)
        {
            for (int i = 0; i < 3; i++)
                Spike(b, V(0, 0.58f * h, (-0.16f + i * 0.08f) * l), V(0, 0.76f * h - i * 0.04f * h, (-0.24f + i * 0.08f) * l), 0.03f * w, CyHullLight);
            b.Cylinder(V(-0.14f * w, 0.56f * h, -0.12f * l), Axis.Y, 0.4f * h, 0.012f * w, 0.006f * w, 4, CyHullDark);
            b.Box(V(-0.14f * w, 0.97f * h, -0.12f * l), V(0.025f * w, 0.03f * h, 0.025f * w), CyRed);
        }
    }

    // ================================================================ T1 land

    /// <summary>Mole: low wedge on four short legs, tall offset antenna (asymmetric), red sensor eye.</summary>
    private static MeshBuilder? CyScout(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, bool gun)
    {
        CyLegSet(b, 2, w, h, l, 0.3f, 0.5f, 0.6f, 0.26f, 0.46f, 0.58f, 0.05f, CyHull, CyHullDark, CyHullDark);
        CyWedge(b, 0, 0.22f * h, 0.56f * w, 0.32f * h, -0.44f * l, 0.48f * l, CyHull, CyHullLight, 0.08f, 0.35f);
        b.Box(V(0.03f * w, 0.536f * h, -0.08f * l), V(0.14f * w, 0.012f * h, 0.3f * l), CyTeam);
        b.Box(V(0, 0.36f * h, 0.42f * l), V(0.14f * w, 0.05f * h, 0.03f * l), CyRed);
        b.Cylinder(V(-0.16f * w, 0.48f * h, -0.24f * l), Axis.Y, 0.62f * h, 0.018f * w, 0.008f * w, 4, CyHullDark);
        Spike(b, V(-0.16f * w, 0.9f * h, -0.24f * l), V(-0.16f * w, 0.9f * h, -0.08f * l), 0.03f * w, CyHullLight);
        b.Box(V(-0.16f * w, 1.1f * h, -0.24f * l), V(0.04f * w, 0.04f * h, 0.04f * w), CyRed);
        if (!gun) return null;
        var t = new MeshBuilder();
        t.Box(V(0, 0.04f * h, 0), V(0.16f * w, 0.08f * h, 0.18f * l), CyHullDark);
        t.Cylinder(V(0, 0.05f * h, 0.08f * l), Axis.Z, 0.22f * l, 0.025f * w, 0.02f * w, 5, CyBlack, CyRedDim);
        pivots.Add(V(0.1f * w, 0.54f * h, 0.12f * l));
        return t;
    }

    /// <summary>Hunter: small reverse-jointed walker, pod body; turret = pulse-laser pod with a red lens.</summary>
    private static MeshBuilder CyLightBot(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            var hip = V(sd * 0.18f * w, 0.5f * h, -0.04f * l);
            var knee = V(sd * 0.26f * w, 0.3f * h, 0.2f * l);
            var ankle = V(sd * 0.24f * w, 0.1f * h, -0.12f * l);
            Limb(b, hip, knee, 0.08f * w, CyHull);
            Limb(b, knee, ankle, 0.06f * w, CyHullDark);
            Spike(b, ankle, V(ankle.X, 0, 0.24f * l), 0.05f * w, CyBlack);
            Spike(b, ankle, V(ankle.X, 0, -0.24f * l), 0.04f * w, CyBlack);
            b.Box(knee, V(0.13f * w, 0.1f * h, 0.12f * l), CyTeam);
        }
        CyWedge(b, 0, 0.42f * h, 0.52f * w, 0.24f * h, -0.34f * l, 0.38f * l, CyHull, CyHullDark, -0.06f, 0.4f);
        b.Box(V(0, 0.52f * h, 0.34f * l), V(0.16f * w, 0.04f * h, 0.03f * l), CyRed);
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.01f * h, 0), Axis.Y, 0.04f * h, 0.12f * w, 0.12f * w, 6, CyBlack);
        t.Loft(MeshBuilder.Chamfered(0.3f * w, 0.14f * h, 0.05f * w), [new Station(-0.18f * l, 0.8f, 0.8f, 0.02f * w, 0.09f * h),
            new Station(0.08f * l, 1, 1, 0.02f * w, 0.09f * h), new Station(0.2f * l, 0.6f, 0.6f, 0.02f * w, 0.08f * h)], Axis.Z, CyHullLight, CyHull);
        t.Box(V(0.02f * w, 0.162f * h, -0.04f * l), V(0.16f * w, 0.01f * h, 0.16f * l), CyTeam);
        t.Cylinder(V(0.08f * w, 0.08f * h, 0.16f * l), Axis.Z, 0.22f * l, 0.03f * w, 0.026f * w, 5, CyBlack, CyRed);
        pivots.Add(V(0, 0.64f * h, 0));
        return t;
    }

    /// <summary>Mantis: four-legged assault walker, abdomen behind; turret = thorax with two forward claw-guns.</summary>
    private static MeshBuilder CyMantis(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        CyLegSet(b, 2, w, h, l, 0.42f, 0.66f, 0.62f, 0.26f, 0.5f, 0.64f, 0.06f, CyHull, CyHullDark, CyTeam);
        // Abdomen: tapering faceted pod behind the hips.
        b.Loft(MeshBuilder.NGon(6, 1, 0), [new Station(-0.06f * l, 0.24f * w, 0.18f * h, 0, 0.44f * h),
            new Station(-0.26f * l, 0.3f * w, 0.24f * h, 0, 0.48f * h), new Station(-0.46f * l, 0.1f * w, 0.1f * h, 0, 0.5f * h)], Axis.Z, CyHullDark, CyBlack);
        b.Box(V(0, 0.6f * h, -0.26f * l), V(0.12f * w, 0.01f * h, 0.16f * l), CyRedDim);
        b.BevelBox(V(0, 0.42f * h, 0), V(0.34f * w, 0.1f * h, 0.22f * l), 0.03f * w, CyHullDark);
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.01f * h, 0), Axis.Y, 0.04f * h, 0.12f * w, 0.12f * w, 6, CyBlack);
        Vector2[] thorax = [V2(-0.5f, -0.45f), V2(0.5f, -0.45f), V2(0.45f, 0.2f), V2(0f, 0.5f), V2(-0.45f, 0.2f)];
        t.Loft(thorax, [new Station(0.02f * h, 0.36f * w, 0.32f * l), new Station(0.18f * h, 0.48f * w, 0.4f * l, 0, 0.02f * l),
                        new Station(0.3f * h, 0.3f * w, 0.28f * l, 0, 0.04f * l)], Axis.Y, CyHull, CyTeam);
        t.Box(V(0, 0.2f * h, 0.215f * l), V(0.12f * w, 0.035f * h, 0.03f * l), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            var s0 = V(sd * 0.24f * w, 0.16f * h, 0.02f * l);
            var e = V(sd * 0.3f * w, 0.1f * h, 0.24f * l);
            Limb(t, s0, e, 0.06f * w, CyHullLight);
            t.Cylinder(e, Axis.Z, 0.2f * l, 0.035f * w, 0.028f * w, 5, CyBlack, CyRedDim);
            Spike(t, e + V(0, -0.03f * h, 0), e + V(sd * 0.02f * w, -0.1f * h, 0.16f * l), 0.03f * w, CyHullDark);
        }
        pivots.Add(V(0, 0.46f * h, 0.04f * l));
        return t;
    }

    /// <summary>Medusa: squat four-legged artillery walker; turret = thick mortar tube pitched up in an armored cradle.</summary>
    private static MeshBuilder CyMedusa(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        CyLegSet(b, 2, w, h, l, 0.36f, 0.6f, 0.7f, 0.26f, 0.5f, 0.62f, 0.06f, CyHull, CyHullDark, CyHullDark);
        b.Loft(MeshBuilder.NGon(8, 1, MathF.PI / 8), [new Station(0.24f * h, 0.3f * w, 0.34f * l), new Station(0.4f * h, 0.38f * w, 0.42f * l),
            new Station(0.5f * h, 0.3f * w, 0.32f * l)], Axis.Y, CyHull, CyHullDark);
        b.Box(V(0, 0.36f * h, 0.4f * l), V(0.18f * w, 0.04f * h, 0.03f * l), CyRed);
        Ring(b, V(0, 0.44f * h, 0), 0.36f * w, 0.02f * w, 0.04f * h, 8, CyTeam, CyHullDark);
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.06f * h, 0.22f * w, 0.2f * w, 6, CyHullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            t.Box(V(sd * 0.14f * w, 0.14f * h, -0.02f * l), V(0.05f * w, 0.18f * h, 0.24f * l), CyHull);
        var pitch = PitchUp(0.75f) * T(0, 0.15f * h, -0.04f * l);
        t.Cylinder(V(0, 0, -0.12f * l), Axis.Z, 0.46f * l, 0.09f * w, 0.075f * w, 6, CyHullLight, null, pitch);
        t.Cylinder(V(0, 0, 0.3f * l), Axis.Z, 0.05f * l, 0.095f * w, 0.095f * w, 6, CyBlack, CyRedDim, pitch);
        t.Box(V(0, 0.09f * w, 0.0f), V(0.06f * w, 0.02f * w, 0.2f * l), CyTeam, null, pitch);
        pivots.Add(V(0, 0.5f * h, 0));
        return t;
    }

    /// <summary>Sky Slammer: four-legged walker; turret = twin angled missile pods with red seeker tips.</summary>
    private static MeshBuilder CySkySlammer(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        CyLegSet(b, 2, w, h, l, 0.4f, 0.64f, 0.6f, 0.26f, 0.5f, 0.62f, 0.055f, CyHull, CyHullDark, CyTeam);
        CyWedge(b, 0, 0.32f * h, 0.56f * w, 0.24f * h, -0.38f * l, 0.38f * l, CyHull, CyHullDark, 0.05f);
        b.Box(V(0, 0.44f * h, 0.34f * l), V(0.16f * w, 0.035f * h, 0.03f * l), CyRed);
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.05f * h, 0.16f * w, 0.14f * w, 6, CyBlack);
        t.Box(V(0, 0.08f * h, 0), V(0.14f * w, 0.1f * h, 0.16f * l), CyHullDark);
        var pitch = PitchUp(0.55f) * T(0, 0.12f * h, 0);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.BevelBox(V(sd * 0.16f * w, 0, 0.02f * l), V(0.13f * w, 0.13f * w, 0.4f * l), 0.03f * w, CyHull, CyTeam, pitch);
            foreach (int k in (ReadOnlySpan<int>)[-1, 1])
                t.Cylinder(V(sd * 0.16f * w + k * 0.03f * w, 0, 0.22f * l), Axis.Z, 0.04f * l, 0.022f * w, 0.006f * w, 5, CyRed, null, pitch);
        }
        pivots.Add(V(0, 0.56f * h, -0.02f * l));
        return t;
    }

    // ================================================================ T2 land

    /// <summary>Rhino: tracked heavy tank, spiked wedge prow and red seams; turret = angular wedge with a thick bolter and a side pod.</summary>
    private static MeshBuilder CyRhino(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        CyTracks(b, w, h, l, 0.22f * w, 0.36f * h);
        CyWedge(b, 0, 0.26f * h, 0.66f * w, 0.32f * h, -0.47f * l, 0.5f * l, CyHull, CyHullDark, 0, 0.3f);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            Spike(b, V(sd * 0.18f * w, 0.36f * h, 0.42f * l), V(sd * 0.2f * w, 0.32f * h, 0.62f * l), 0.04f * w, CyHullLight);
            b.Box(V(sd * 0.2f * w, 0.47f * h, -0.08f * l), V(0.012f * w, 0.025f * h, 0.5f * l), CyRedDim);
        }
        b.Box(V(0, 0.58f * h, -0.36f * l), V(0.3f * w, 0.012f * h, 0.12f * l), CyTeam);
        var t = new MeshBuilder();
        Vector2[] prof = [V2(-0.5f, 0), V2(0.5f, 0), V2(0.42f, 0.7f), V2(0.05f, 1), V2(-0.3f, 1), V2(-0.46f, 0.6f)];
        t.Loft(prof, [new Station(-0.24f * l, 0.42f * w, 0.18f * h), new Station(0.06f * l, 0.48f * w, 0.2f * h), new Station(0.2f * l, 0.3f * w, 0.14f * h)], Axis.Z, CyHull, CyHullLight);
        t.Box(V(-0.05f * w, 0.2f * h, -0.08f * l), V(0.16f * w, 0.012f * h, 0.18f * l), CyTeam);
        t.Cylinder(V(0.04f * w, 0.1f * h, 0.16f * l), Axis.Z, 0.36f * l, 0.05f * w, 0.042f * w, 6, CyBlack);
        t.Cylinder(V(0.04f * w, 0.1f * h, 0.5f * l), Axis.Z, 0.04f * l, 0.056f * w, 0.056f * w, 6, CyHullDark, CyRed);
        t.BevelBox(V(-0.2f * w, 0.1f * h, 0.04f * l), V(0.1f * w, 0.1f * h, 0.2f * l), 0.02f * w, CyHullDark);
        t.Box(V(-0.2f * w, 0.1f * h, 0.145f * l), V(0.05f * w, 0.04f * h, 0.01f * l), CyRed);
        pivots.Add(V(0, 0.56f * h, -0.04f * l));
        return t;
    }

    /// <summary>Wagner: amphibious crab, wide flat carapace on six legs with two front claws; turret = rocket pod + bolter.</summary>
    private static MeshBuilder CyWagner(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        CyLegSet(b, 3, w, h, l, 0.36f, 0.6f, 0.75f, 0.3f, 0.48f, 0.56f, 0.045f, CyHull, CyHullDark, CyTeam, -0.04f * l);
        b.Loft(MeshBuilder.NGon(6, 1, MathF.PI / 6), [new Station(0.26f * h, 0.36f * w, 0.3f * l), new Station(0.4f * h, 0.42f * w, 0.36f * l),
            new Station(0.52f * h, 0.3f * w, 0.24f * l)], Axis.Y, CyHull, CyHullLight);
        b.Box(V(0, 0.36f * h, 0.33f * l), V(0.24f * w, 0.04f * h, 0.03f * l), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            var c0 = V(sd * 0.22f * w, 0.36f * h, 0.24f * l);
            var c1 = V(sd * 0.3f * w, 0.42f * h, 0.42f * l);
            var c2 = V(sd * 0.16f * w, 0.24f * h, 0.56f * l);
            Limb(b, c0, c1, 0.06f * w, CyHullLight);
            Limb(b, c1, c2, 0.05f * w, CyHull);
            Spike(b, c2, c2 + V(-sd * 0.08f * w, -0.04f * h, 0.08f * l), 0.035f * w, CyBlack);
            b.Box(c1, V(0.08f * w, 0.07f * h, 0.07f * l), CyTeam);
        }
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.05f * h, 0.16f * w, 0.15f * w, 6, CyBlack);
        t.BevelBox(V(-0.08f * w, 0.12f * h, -0.02f * l), V(0.18f * w, 0.14f * h, 0.24f * l), 0.03f * w, CyHull, CyTeam);
        for (int i = 0; i < 2; i++)
        foreach (int k in (ReadOnlySpan<int>)[-1, 1])
            t.Cylinder(V(-0.08f * w + k * 0.04f * w, 0.1f * h + i * 0.06f * h, 0.1f * l), Axis.Z, 0.02f * l, 0.022f * w, 0.022f * w, 6, CyRedDim);
        t.Box(V(0.09f * w, 0.09f * h, 0.02f * l), V(0.1f * w, 0.1f * h, 0.2f * l), CyHullDark);
        t.Cylinder(V(0.09f * w, 0.09f * h, 0.1f * l), Axis.Z, 0.26f * l, 0.03f * w, 0.026f * w, 5, CyBlack, CyRed);
        pivots.Add(V(0, 0.52f * h, -0.04f * l));
        return t;
    }

    /// <summary>Viper: four-legged missile walker; turret = tall angled launcher box with 2 x 2 red warheads.</summary>
    private static MeshBuilder CyViper(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        CyLegSet(b, 2, w, h, l, 0.38f, 0.62f, 0.65f, 0.26f, 0.5f, 0.62f, 0.055f, CyHull, CyHullDark, CyTeam);
        CyWedge(b, 0, 0.3f * h, 0.6f * w, 0.26f * h, -0.44f * l, 0.42f * l, CyHull, CyHullDark, -0.05f);
        b.Box(V(0, 0.42f * h, 0.38f * l), V(0.16f * w, 0.035f * h, 0.03f * l), CyRed);
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.05f * h, 0.16f * w, 0.15f * w, 6, CyBlack);
        var pitch = PitchUp(0.5f) * T(0, 0.12f * h, -0.06f * l);
        t.BevelBox(V(0, 0, 0), V(0.34f * w, 0.24f * h, 0.5f * l), 0.05f * w, CyHull, CyHullLight, pitch);
        t.Box(V(0.171f * w, 0, -0.02f * l), V(0.01f * w, 0.1f * h, 0.36f * l), CyTeam, null, pitch);
        for (int i = -1; i <= 1; i += 2)
        foreach (int k in (ReadOnlySpan<int>)[-1, 1])
            t.Cylinder(V(i * 0.08f * w, k * 0.05f * h, 0.25f * l), Axis.Z, 0.05f * l, 0.04f * w, 0.008f * w, 5, CyRed, null, pitch);
        Fin(t, -0.17f * w, 0.012f * w, 0.1f * h, -0.18f * l, 0.04f * l, -0.2f * l, 0.3f * h, CyHullDark);
        pivots.Add(V(0, 0.54f * h, 0));
        return t;
    }

    /// <summary>Banger: tracked flak carrier; turret = boxy flak block with two short thick barrels pitched up.</summary>
    private static MeshBuilder CyBanger(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        CyTracks(b, w, h, l, 0.22f * w, 0.34f * h);
        CyWedge(b, 0, 0.26f * h, 0.64f * w, 0.28f * h, -0.46f * l, 0.48f * l, CyHull, CyHullDark, 0.06f, 0.4f);
        b.Box(V(0, 0.52f * h, -0.34f * l), V(0.24f * w, 0.012f * h, 0.12f * l), CyTeam);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            Spike(b, V(sd * 0.22f * w, 0.42f * h, -0.4f * l), V(sd * 0.26f * w, 0.62f * h, -0.56f * l), 0.03f * w, CyHullLight);
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.05f * h, 0.2f * w, 0.18f * w, 6, CyBlack);
        Vector2[] prof = [V2(-0.5f, 0), V2(0.5f, 0), V2(0.45f, 0.8f), V2(0.2f, 1), V2(-0.45f, 1), V2(-0.5f, 0.7f)];
        t.Loft(prof, [new Station(-0.2f * l, 0.44f * w, 0.24f * h), new Station(0.1f * l, 0.48f * w, 0.26f * h), new Station(0.18f * l, 0.36f * w, 0.18f * h)], Axis.Z, CyHull, CyHullLight);
        t.Box(V(0, 0.262f * h, -0.06f * l), V(0.2f * w, 0.01f * h, 0.16f * l), CyTeam);
        var pitch = PitchUp(0.6f) * T(0, 0.14f * h, 0.1f * l);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Cylinder(V(sd * 0.1f * w, 0, 0), Axis.Z, 0.26f * l, 0.05f * w, 0.045f * w, 6, CyBlack, null, pitch);
            t.Cylinder(V(sd * 0.1f * w, 0, 0.22f * l), Axis.Z, 0.05f * l, 0.058f * w, 0.058f * w, 6, CyHullDark, CyRedDim, pitch);
        }
        pivots.Add(V(0, 0.54f * h, -0.06f * l));
        return t;
    }

    /// <summary>Deceiver: low four-legged hull; spinning turret = stealth emitter mast with three violet vanes.</summary>
    private static MeshBuilder CyDeceiver(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        CyLegSet(b, 2, w, h, l, 0.32f, 0.54f, 0.65f, 0.28f, 0.5f, 0.62f, 0.05f, CyHull, CyHullDark, CyTeam);
        CyWedge(b, 0, 0.24f * h, 0.62f * w, 0.24f * h, -0.44f * l, 0.44f * l, CyHull, CyHullDark, 0, 0.5f);
        b.Box(V(0, 0.34f * h, 0.41f * l), V(0.18f * w, 0.035f * h, 0.03f * l), CyViolet);
        b.Cylinder(V(0, 0.46f * h, -0.04f * l), Axis.Y, 0.18f * h, 0.12f * w, 0.08f * w, 6, CyHullDark, CyTeam);
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.46f * h, 0.035f * w, 0.02f * w, 5, CyHullLight);
        t.Sphere(V(0, 0.48f * h, 0), V(0.05f * w, 0.07f * h, 0.05f * w), 3, 4, CyViolet);
        for (int k = 0; k < 3; k++)
        {
            var rot = RotY(k * MathF.Tau / 3f);
            Fin(t, 0, 0.01f * w, 0.06f * h, 0.03f * w, 0.18f * w, 0.26f * w, 0.4f * h, CyHull, RotY(MathF.PI / 2) * rot);
            t.Box(V(0.2f * w, 0.24f * h, 0), V(0.02f * w, 0.12f * h, 0.012f * w), CyViolet, null, rot);
        }
        pivots.Add(V(0, 0.64f * h, -0.04f * l));
        return t;
    }

    // ================================================================ T3 land

    /// <summary>Loyalist: heavy four-legged assault walker; turret = armored torso with a heavy arm gun, a shield plate and back spikes.</summary>
    private static MeshBuilder CyLoyalist(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        CyLegSet(b, 2, w, h, l, 0.44f, 0.7f, 0.6f, 0.24f, 0.5f, 0.64f, 0.07f, CyHull, CyHullDark, CyTeam);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        foreach (int fz in (ReadOnlySpan<int>)[-1, 1])
        {
            var knee = V(sd * MathF.Cos(0.6f) * 0.5f * w, 0.7f * h, fz * MathF.Sin(0.6f) * 0.5f * l);
            Spike(b, knee, knee + V(sd * 0.05f * w, 0.14f * h, fz * 0.04f * l), 0.04f * w, CyHullLight);
        }
        b.BevelBox(V(0, 0.44f * h, 0), V(0.4f * w, 0.12f * h, 0.3f * l), 0.04f * w, CyHullDark);
        b.Box(V(0, 0.44f * h, 0.152f * l), V(0.2f * w, 0.04f * h, 0.01f * l), CyRed);
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.01f * h, 0), Axis.Y, 0.05f * h, 0.14f * w, 0.14f * w, 6, CyBlack);
        Vector2[] chest = [V2(-0.5f, -0.45f), V2(0.5f, -0.45f), V2(0.5f, 0.15f), V2(0.1f, 0.5f), V2(-0.3f, 0.5f), V2(-0.5f, 0.2f)];
        t.Loft(chest, [new Station(0.03f * h, 0.4f * w, 0.34f * l), new Station(0.2f * h, 0.56f * w, 0.42f * l, 0, 0.02f * l),
                       new Station(0.34f * h, 0.42f * w, 0.32f * l, 0, 0.03f * l)], Axis.Y, CyHull, CyHullLight);
        t.Box(V(0, 0.24f * h, 0.22f * l), V(0.14f * w, 0.03f * h, 0.02f * l), CyRed);
        t.BevelBox(V(0, 0.36f * h, 0.06f * l), V(0.14f * w, 0.06f * h, 0.14f * l), 0.02f * w, CyHullLight);
        t.Box(V(0, 0.362f * h, 0.13f * l), V(0.1f * w, 0.014f * h, 0.012f * l), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.BevelBox(V(sd * 0.32f * w, 0.28f * h, 0), V(0.18f * w, 0.1f * h, 0.26f * l), 0.04f * w, CyTeam);
            Spike(t, V(sd * 0.12f * w, 0.32f * h, -0.12f * l), V(sd * 0.2f * w, 0.56f * h, -0.3f * l), 0.035f * w, CyHullDark);
        }
        // Right: heavy bolter arm; left: angled shield plate over a light gun.
        t.BevelBox(V(0.34f * w, 0.12f * h, 0.12f * l), V(0.16f * w, 0.16f * h, 0.36f * l), 0.04f * w, CyHull);
        t.Cylinder(V(0.34f * w, 0.12f * h, 0.3f * l), Axis.Z, 0.24f * l, 0.05f * w, 0.044f * w, 6, CyBlack);
        t.Cylinder(V(0.34f * w, 0.12f * h, 0.52f * l), Axis.Z, 0.04f * l, 0.056f * w, 0.056f * w, 6, CyHullDark, CyRed);
        t.Box(V(-0.36f * w, 0.14f * h, 0.12f * l), V(0.04f * w, 0.2f * h, 0.34f * l), CyHullLight, null, Matrix4x4.CreateRotationY(-0.12f));
        t.Box(V(-0.38f * w, 0.24f * h, 0.12f * l), V(0.045f * w, 0.02f * h, 0.3f * l), CyTeamDim, null, Matrix4x4.CreateRotationY(-0.12f));
        t.Cylinder(V(-0.3f * w, 0.08f * h, 0.2f * l), Axis.Z, 0.2f * l, 0.03f * w, 0.026f * w, 5, CyBlack, CyRedDim);
        pivots.Add(V(0, 0.48f * h, 0));
        return t;
    }

    /// <summary>Trebuchet: long six-legged artillery walker; turret = heavy mount with a long ribbed barrel pitched up.</summary>
    private static MeshBuilder CyTrebuchet(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        CyLegSet(b, 3, w, h, l, 0.38f, 0.64f, 0.85f, 0.28f, 0.5f, 0.62f, 0.05f, CyHull, CyHullDark, CyTeam);
        CyWedge(b, 0, 0.3f * h, 0.6f * w, 0.28f * h, -0.46f * l, 0.4f * l, CyHull, CyHullDark, 0.04f, 0.5f);
        b.Box(V(0, 0.44f * h, 0.37f * l), V(0.2f * w, 0.04f * h, 0.02f * l), CyRed);
        b.Box(V(0, 0.582f * h, -0.32f * l), V(0.18f * w, 0.01f * h, 0.16f * l), CyTeam);
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.08f * h, 0.24f * w, 0.22f * w, 8, CyBlack);
        t.BevelBox(V(0, 0.16f * h, -0.06f * l), V(0.36f * w, 0.2f * h, 0.3f * l), 0.05f * w, CyHull, CyHullLight);
        t.Box(V(0, 0.262f * h, -0.12f * l), V(0.2f * w, 0.01f * h, 0.14f * l), CyTeam);
        var pitch = PitchUp(0.35f) * T(0, 0.18f * h, 0.02f * l);
        t.Cylinder(V(0, 0, 0), Axis.Z, 0.66f * l, 0.07f * w, 0.05f * w, 6, CyHullLight, null, pitch);
        for (int i = 0; i < 3; i++)
            t.Cylinder(V(0, 0, (0.1f + i * 0.12f) * l), Axis.Z, 0.04f * l, 0.085f * w, 0.085f * w, 6, CyBlack, null, pitch);
        t.Cylinder(V(0, 0, 0.62f * l), Axis.Z, 0.06f * l, 0.07f * w, 0.07f * w, 6, CyHullDark, CyRed, pitch);
        pivots.Add(V(0, 0.58f * h, -0.04f * l));
        return t;
    }
}
