using System.Numerics;
using Rts.Sim.Data;

namespace Rts.Client.Rendering;

/// <summary>
/// Cybran ships and water structures (shapes "cy_frigate" … "cy_torpedo_launcher", dispatched by <c>CybranShape</c> in
/// UnitModels.Cybran.cs): knife-keeled faceted hulls with a raked blade prow, asymmetric wedge bridges with red visor
/// slits and raked antenna spikes, team stripes on the topsides, red stern glows. Same conventions as the UEF ships:
/// y = 0 is the waterline, hull bottom at -0.3 h, deck at 0.25 h; one shared turret mesh per model (origin = pivot,
/// barrels along +Z); water structures float with their pontoons reaching below the waterline.
/// </summary>
public static partial class UnitModels
{
    /// <summary>
    /// Test hook (nothing is uploaded to the GPU): vertex counts of the body and turret meshes, the turret pivot count and
    /// the body's lowest point for a "cy_*" blueprint, built by the same shape code as <see cref="Build"/>.
    /// </summary>
    public static (int Body, int Turret, int Pivots, float MinY) CybranMeshStats(UnitBlueprint bp)
    {
        var s = bp.Visual.Size;
        float w = MathF.Max(0.1f, s.X), h = MathF.Max(0.1f, s.Y), l = MathF.Max(0.1f, s.Z);
        var b = new MeshBuilder();
        var pivots = new List<Vector3>();
        var mode = TurretMode.None;
        float spin = 0;
        var t = CybranShape(bp, b, w, h, l, bp.Weapons.Length > 0, pivots, ref mode, ref spin);
        return (b.VertexCount, t?.VertexCount ?? 0, pivots.Count, b.Bounds().minY);
    }

    // ================================================================ shared ship parts

    /// <summary>
    /// Faceted Cybran hull lofted along Z: knife keel, one hard chine facet per side, vertical topsides; a pinched raked
    /// stern and a long blade bow whose keel rises to a raked cutwater, its tip <paramref name="bowRise"/> h above the
    /// deck. Bottom at -0.3 h; returns the deck height (<paramref name="deckF"/> h).
    /// </summary>
    private static float CyShipHull(MeshBuilder b, float w, float h, float l, float deckF = 0.25f, float bowRise = 0.06f)
    {
        Vector2[] prof = [V2(-0.5f, 1f), V2(-0.5f, 0.6f), V2(-0.32f, 0.18f), V2(0, 0), V2(0.32f, 0.18f), V2(0.5f, 0.6f), V2(0.5f, 1f)];
        float bottom = -0.3f * h, hh = (deckF + 0.3f) * h, deck = bottom + hh, rise = bowRise * h;
        b.Loft(prof, [
            new Station(-0.5f * l, 0.72f * w, hh * 0.8f, 0, bottom + hh * 0.2f),
            new Station(-0.4f * l, 0.92f * w, hh * 0.94f, 0, bottom + hh * 0.06f),
            new Station(-0.12f * l, w, hh, 0, bottom),
            new Station(0.16f * l, 0.96f * w, hh, 0, bottom),
            new Station(0.36f * l, 0.62f * w, hh + rise * 0.5f, 0, bottom),
            new Station(0.5f * l, 0.05f * w, hh * 0.55f + rise, 0, bottom + hh * 0.45f)], Axis.Z, CyHull, CyHullDark);
        return deck;
    }

    /// <summary>Dark deck plate from z0 to z1 (fractions of l), inset from the hull edges.</summary>
    private static void CyDeckPlate(MeshBuilder b, float w, float l, float deck, float z0, float z1)
    {
        Vector2[] p = [V2(-0.33f * w, z0 * l), V2(0.33f * w, z0 * l), V2(0.44f * w, (z0 + 0.08f) * l), V2(0.44f * w, z1 * l),
                       V2(-0.44f * w, z1 * l), V2(-0.44f * w, (z0 + 0.08f) * l)];
        b.Slab(p, deck + 0.001f, deck + 0.004f, CyPad);
    }

    /// <summary>CyShipHull station positions (fractions of l) and widths (fractions of w): the topsides are linear between them.</summary>
    private static readonly float[] CyHullZ = [-0.5f, -0.4f, -0.12f, 0.16f, 0.36f, 0.5f], CyHullW = [0.72f, 0.92f, 1f, 0.96f, 0.62f, 0.05f];

    /// <summary>Team stripe and a red seam on both topsides between z0 and z1 (fractions of l), lofted along the
    /// CyShipHull stations so they stay on the hull side where it narrows.</summary>
    private static void CySideMarks(MeshBuilder b, float w, float h, float l, float deck, float z0, float z1)
    {
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            Strip(z0, z1, 0.012f * w, 0.05f * h, deck - 0.05f * h, CyTeam);
            float s0 = z0 + 0.25f * (z1 - z0);
            Strip(s0, s0 + 0.7f * (z1 - z0), 0.01f * w, 0.012f * h, deck - 0.12f * h, CyRedDim);

            void Strip(float a, float c, float thick, float ht, float y, Raylib_cs.Color col)
            {
                var st = new List<Station>();
                void Add(float zf)
                {
                    int k = 1;
                    while (k < CyHullZ.Length - 1 && zf > CyHullZ[k]) k++;
                    float u = (zf - CyHullZ[k - 1]) / (CyHullZ[k] - CyHullZ[k - 1]);
                    float half = 0.5f * w * (CyHullW[k - 1] + (CyHullW[k] - CyHullW[k - 1]) * u);
                    st.Add(new Station(zf * l, 1, 1, sd * (half + 0.004f * w - thick * 0.5f), y));
                }
                Add(a);
                foreach (float k in CyHullZ) if (k > a && k < c) Add(k);
                Add(c);
                b.Loft(MeshBuilder.Rect(thick, ht), st.ToArray(), Axis.Z, col);
            }
        }
    }

    /// <summary><paramref name="n"/> red engine glows on the transom.</summary>
    private static void CySternGlow(MeshBuilder b, float w, float h, float l, int n)
    {
        for (int i = 0; i < n; i++)
        {
            float x = (i - (n - 1) * 0.5f) * 0.2f * w;
            b.Box(V(x, 0.06f * h, -0.5f * l - 0.002f * l), V(0.11f * w, 0.07f * h, 0.006f * l), CyRed);
        }
    }

    /// <summary>
    /// Asymmetric Cybran bridge between z0 and z1: a faceted wedge block offset by <paramref name="off"/> w with team
    /// stripes on its flanks, a narrower upper block with a red visor slit, a raked mast spike (cross spar, red tip
    /// light) and a smaller second spike. Returns the upper block top.
    /// </summary>
    private static float CyBridge(MeshBuilder b, float w, float h, float l, float deck, float z0, float z1, float bh, float off, float mastH)
    {
        float cx = off * w, len = z1 - z0, y0 = deck - 0.01f * h;
        CyWedge(b, cx, y0, 0.62f * w, bh, z0, z1, CyHull, CyHullDark, 0.05f, 0.62f);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(cx + sd * 0.312f * w, y0 + 0.24f * bh, z0 + 0.5f * len), V(0.008f * w, 0.1f * bh, 0.4f * len), CyTeam);
        float ux = cx - 0.03f * w, uy = y0 + 0.7f * bh, uz0 = z0 + len * 0.2f, uz1 = z1 - len * 0.15f, uh = 0.6f * bh;
        CyWedge(b, ux, uy, 0.36f * w, uh, uz0, uz1, CyHullLight, CyHull, -0.08f, 0.6f);
        float nh = uh * (0.35f + 0.4f * 0.6f);
        b.Box(V(ux, uy + nh * 0.55f, uz1 + 0.003f * l), V(0.15f * w, 0.035f * h, 0.006f * l), CyRed);

        float mz = uz0 + (uz1 - uz0) * 0.4f, top = uy + uh;
        var mb = V(ux - 0.02f * w, top - 0.02f * h, mz);
        var mt = V(ux - 0.04f * w, top + mastH, mz - 0.05f * l);
        Spike(b, mb, mt, 0.035f * w, CyHullDark);
        b.Box(Vector3.Lerp(mb, mt, 0.5f), V(0.3f * w, 0.018f * h, 0.025f * l), CyHullDark);
        b.Box(Vector3.Lerp(mb, mt, 0.86f), V(0.03f * w, 0.03f * w, 0.03f * w), CyRed);
        Spike(b, V(ux + 0.02f * w, top - 0.02f * h, mz + 0.03f * l), V(ux + 0.08f * w, top + 0.4f * mastH, mz - 0.02f * l), 0.02f * w, CyHullLight);
        return top;
    }

    /// <summary>
    /// Cybran proton cannon turret (origin = pivot on the deck, barrels along +Z): hex race ring, faceted housing that
    /// narrows to a wedge front, team roof plate, <paramref name="barrels"/> barrels with red-capped muzzle collars, a red
    /// slit under the barrels, a red rear vent and an antenna spike. <paramref name="r"/> = housing half-width,
    /// <paramref name="bl"/> = barrel length (world units).
    /// </summary>
    private static MeshBuilder CyShipGun(float r, float bl, int barrels)
    {
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.1f * r, 0), Axis.Y, 0.25f * r, 0.85f * r, 0.78f * r, 6, CyBlack);
        Vector2[] prof = [V2(-0.5f, 0), V2(0.5f, 0), V2(0.5f, 0.55f), V2(0.22f, 1f), V2(-0.3f, 1f), V2(-0.5f, 0.55f)];
        t.Loft(prof, [new Station(-0.9f * r, 1.5f * r, 0.75f * r, 0, 0.1f * r), new Station(0.3f * r, 1.9f * r, 0.75f * r, 0, 0.1f * r),
                      new Station(0.95f * r, 1.1f * r, 0.45f * r, 0, 0.12f * r)], Axis.Z, CyHull, CyHullDark);
        t.Box(V(-0.05f * r, 0.853f * r, -0.3f * r), V(0.5f * r, 0.006f * r, 0.9f * r), CyTeam);
        float sp = barrels >= 3 ? 0.42f * r : 0.5f * r;
        for (int i = 0; i < barrels; i++)
        {
            float x = (i - (barrels - 1) * 0.5f) * sp;
            t.Cylinder(V(x, 0.4f * r, 0.6f * r), Axis.Z, bl, 0.12f * r, 0.1f * r, 5, CyBlack);
            t.Cylinder(V(x, 0.4f * r, 0.6f * r + bl * 0.86f), Axis.Z, bl * 0.14f, 0.15f * r, 0.15f * r, 5, CyHullDark, CyRed);
        }
        t.Box(V(0, 0.18f * r, 0.953f * r), V(0.8f * r, 0.05f * r, 0.01f * r), CyRed);
        t.Box(V(0, 0.45f * r, -0.905f * r), V(0.7f * r, 0.08f * r, 0.01f * r), CyRedDim);
        Spike(t, V(-0.5f * r, 0.84f * r, -0.6f * r), V(-0.56f * r, 1.6f * r, -1.0f * r), 0.05f * r, CyHullDark);
        return t;
    }

    /// <summary>Static nanodart AA launcher at p (size s, facing yaw): hex post, faceted box raked up with a 2x2 cluster of
    /// red-tipped tubes.</summary>
    private static void CyAaPod(MeshBuilder b, Vector3 p, float s, float yaw = 0)
    {
        var f = RotY(yaw) * T(p.X, p.Y, p.Z);
        b.Cylinder(V(0, 0, 0), Axis.Y, 0.3f * s, 0.3f * s, 0.26f * s, 6, CyHullDark, null, f);
        var xf = PitchUp(0.75f) * T(0, 0.45f * s, 0) * f;
        b.BevelBox(V(0, 0, 0), V(0.8f * s, 0.5f * s, 0.9f * s), 0.12f * s, CyHull, CyTeamDim, xf);
        for (int i = 0; i < 4; i++)
        {
            float x = (i & 1) == 0 ? -0.18f * s : 0.18f * s, y = i < 2 ? -0.1f * s : 0.12f * s;
            b.Cylinder(V(x, y, 0.4f * s), Axis.Z, 0.2f * s, 0.09f * s, 0.09f * s, 5, CyBlack, CyRed, xf);
        }
    }

    /// <summary>Static nanite torpedo tubes: a launcher block on each side at z with two tubes pointing outboard.</summary>
    private static void CyTorpedoTubes(MeshBuilder b, float w, float h, float deck, float z)
    {
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.BevelBox(V(sd * 0.3f * w, deck + 0.04f * h, z), V(0.14f * w, 0.08f * h, 0.12f * w), 0.02f * w, CyHullDark, CyHull);
            float len = 0.17f * w, x0 = sd > 0 ? 0.3f * w : -0.3f * w - len;
            foreach (float dz in (ReadOnlySpan<float>)[-0.03f, 0.03f])
                b.Cylinder(V(x0, deck + 0.05f * h, z + dz * w), Axis.X, len, 0.028f * w, 0.028f * w, 6, CyBlack, CyRedDim);
        }
    }

    /// <summary>Anti-torpedo zapper (static): hex post with a faceted dome and a red emitter on top.</summary>
    private static void CyTorpedoZapper(MeshBuilder b, Vector3 p, float s)
    {
        b.Cylinder(p, Axis.Y, 0.35f * s, 0.42f * s, 0.36f * s, 6, CyHullDark);
        b.Sphere(p + V(0, 0.35f * s, 0), V(0.32f * s, 0.26f * s, 0.32f * s), 3, 6, CyHull, null, 0.5f);
        b.Box(p + V(0, 0.62f * s, 0), V(0.1f * s, 0.1f * s, 0.1f * s), CyRed);
    }

    // ================================================================ ships

    /// <summary>Trident frigate: one twin proton cannon forward, an offset wedge bridge with a raked mast, a nanodart AA
    /// launcher on the stern, bow spikes.</summary>
    private static MeshBuilder? CyFrigate(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, bool hasWeapon)
    {
        float deck = CyShipHull(b, w, h, l);
        CyDeckPlate(b, w, l, deck, -0.47f, 0.14f);
        CySideMarks(b, w, h, l, deck, -0.2f, 0.15f);
        CySternGlow(b, w, h, l, 2);
        CyBridge(b, w, h, l, deck, -0.24f * l, 0.06f * l, 0.3f * h, 0.08f, 0.55f * h);
        CyAaPod(b, V(0, deck, -0.38f * l), 0.2f * w, MathF.PI);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            Spike(b, V(sd * 0.16f * w, deck + 0.02f * h, 0.36f * l), V(sd * 0.1f * w, deck + 0.1f * h, 0.47f * l), 0.03f * w, CyHullDark);
        if (!hasWeapon) return null;
        pivots.Add(V(0, deck + 0.01f * h, 0.26f * l));
        return CyShipGun(0.17f * w, 0.14f * l, 2);
    }

    /// <summary>Salem destroyer: two proton cannons forward (B superfiring on a hex barbette), wedge bridge, torpedo tubes
    /// and an anti-torpedo zapper aft, and four spider legs folded against the topsides (it can walk ashore in SupCom).</summary>
    private static MeshBuilder? CyDestroyer(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, bool hasWeapon)
    {
        float deck = CyShipHull(b, w, h, l);
        CyDeckPlate(b, w, l, deck, -0.47f, 0.14f);
        CySideMarks(b, w, h, l, deck, -0.2f, 0.15f);
        CySternGlow(b, w, h, l, 2);
        CyBridge(b, w, h, l, deck, -0.2f * l, 0.06f * l, 0.3f * h, 0.07f, 0.6f * h);
        b.Cylinder(V(0, deck - 0.02f * h, 0.18f * l), Axis.Y, 0.14f * h, 0.19f * w, 0.17f * w, 6, CyHullDark, CyBlack);
        b.Cylinder(V(0, deck + 0.06f * h, 0.18f * l), Axis.Y, 0.03f * h, 0.181f * w, 0.177f * w, 6, CyTeam);
        CyTorpedoTubes(b, w, h, deck, -0.3f * l);
        CyTorpedoZapper(b, V(0, deck, -0.42f * l), 0.16f * w);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        foreach (float zf in (ReadOnlySpan<float>)[0.1f, -0.24f])
        {
            float z = zf * l;
            CyLeg(b, V(sd * 0.44f * w, deck - 0.04f * h, z), V(sd * 0.54f * w, deck + 0.1f * h, z - 0.03f * l),
                  V(sd * 0.5f * w, 0.02f * h, z - 0.08f * l), 0.035f * w, CyHull, CyHullDark, CyTeam);
        }
        if (!hasWeapon) return null;
        pivots.Add(V(0, deck + 0.01f * h, 0.31f * l));
        pivots.Add(V(0, deck + 0.12f * h, 0.18f * l));
        return CyShipGun(0.15f * w, 0.11f * l, 2);
    }

    /// <summary>Siren cruiser: proton cannons fore and aft, a block of Loa cruise missile cells (red-seamed hatches)
    /// forward of a port-offset bridge, nanodart AA pods aft of the bridge, an aft sensor spike.</summary>
    private static MeshBuilder? CyCruiser(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, bool hasWeapon)
    {
        float deck = CyShipHull(b, w, h, l, 0.25f, 0.05f);
        CyDeckPlate(b, w, l, deck, -0.47f, 0.14f);
        CySideMarks(b, w, h, l, deck, -0.22f, 0.15f);
        CySternGlow(b, w, h, l, 3);
        CyBridge(b, w, h, l, deck, -0.16f * l, 0.1f * l, 0.34f * h, -0.06f, 0.75f * h);
        b.BevelBox(V(0, deck + 0.04f * h, 0.19f * l), V(0.5f * w, 0.08f * h, 0.13f * l), 0.03f * w, CyHullDark, CyHull);
        for (int i = 0; i < 6; i++)
        {
            float x = (i % 2 == 0 ? -0.11f : 0.11f) * w, z = (0.145f + (i / 2) * 0.045f) * l;
            b.Box(V(x, deck + 0.083f * h, z), V(0.17f * w, 0.006f * h, 0.035f * l), CyBlack);
            b.Box(V(x, deck + 0.087f * h, z), V(0.15f * w, 0.004f * h, 0.006f * l), CyRedDim);
        }
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            CyAaPod(b, V(sd * 0.3f * w, deck, -0.22f * l), 0.16f * w, MathF.PI);
        b.Frustum(V(0.3f * w, deck, -0.27f * l), 0.1f * w, 0.04f * l, 0.05f * w, 0.02f * l, 0.12f * h, CyHullDark, null, null, 0.25f);
        Spike(b, V(0.3f * w, deck + 0.1f * h, -0.27f * l), V(0.33f * w, deck + 0.62f * h, -0.3f * l), 0.025f * w, CyHull);
        b.Box(V(0.32f * w, deck + 0.5f * h, -0.29f * l), V(0.025f * w, 0.025f * w, 0.025f * w), CyRed);
        if (!hasWeapon) return null;
        pivots.Add(V(0, deck + 0.01f * h, 0.32f * l));
        pivots.Add(V(0, deck + 0.01f * h, -0.35f * l));
        return CyShipGun(0.15f * w, 0.1f * l, 2);
    }

    /// <summary>Galaxy battleship: three triple proton cannon batteries (two forward, B superfiring, one aft), a big
    /// stepped bridge with twin raked masts, armor belts on the topsides, nanodart AA pods, torpedo tubes, two
    /// anti-torpedo zappers, a stern sensor fin and three red engine glows.</summary>
    private static MeshBuilder? CyBattleship(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, bool hasWeapon)
    {
        float deck = CyShipHull(b, w, h, l, 0.25f, 0.05f);
        CyDeckPlate(b, w, l, deck, -0.47f, 0.14f);
        CySideMarks(b, w, h, l, deck, -0.22f, 0.15f);
        CySternGlow(b, w, h, l, 3);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.BevelBox(V(sd * 0.5f * w, 0.05f * h, -0.04f * l), V(0.06f * w, 0.1f * h, 0.4f * l), 0.02f * w, CyHullDark);
            b.Box(V(sd * 0.531f * w, 0.05f * h, -0.04f * l), V(0.004f * w, 0.012f * h, 0.34f * l), CyRedDim);
        }
        b.Cylinder(V(0, deck - 0.02f * h, 0.19f * l), Axis.Y, 0.17f * h, 0.2f * w, 0.18f * w, 6, CyHullDark, CyBlack);
        b.Cylinder(V(0, deck + 0.08f * h, 0.19f * l), Axis.Y, 0.03f * h, 0.19f * w, 0.187f * w, 6, CyTeam);
        CyWedge(b, 0, deck - 0.01f * h, 0.5f * w, 0.08f * h, -0.195f * l, -0.1f * l, CyHullDark, CyHull, 0, 0.8f);
        b.Cylinder(V(0, deck - 0.02f * h, -0.35f * l), Axis.Y, 0.06f * h, 0.2f * w, 0.18f * w, 6, CyHullDark, CyBlack); // aft barbette
        CyBridge(b, w, h, l, deck, -0.13f * l, 0.12f * l, 0.3f * h, 0.05f, 0.8f * h);
        Spike(b, V(-0.12f * w, deck + 0.4f * h, -0.08f * l), V(-0.2f * w, deck + 0.95f * h, -0.14f * l), 0.03f * w, CyHullDark);
        b.Box(V(-0.185f * w, deck + 0.85f * h, -0.13f * l), V(0.02f * w, 0.02f * w, 0.02f * w), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            foreach (float z in (ReadOnlySpan<float>)[-0.17f, 0.0f])
                CyAaPod(b, V(sd * 0.4f * w, deck, z * l), 0.11f * w, sd * 0.5f);
            CyTorpedoZapper(b, V(sd * 0.3f * w, deck, -0.43f * l), 0.1f * w);
        }
        CyTorpedoTubes(b, w, h, deck, -0.25f * l);
        Fin(b, 0, 0.03f * w, deck, -0.48f * l, -0.4f * l, -0.5f * l, deck + 0.35f * h, CyHullDark);
        Fin(b, 0, 0.034f * w, deck + 0.24f * h, -0.483f * l, -0.47f * l, -0.497f * l, deck + 0.33f * h, CyTeam);
        if (!hasWeapon) return null;
        pivots.Add(V(0, deck + 0.01f * h, 0.31f * l));
        pivots.Add(V(0, deck + 0.15f * h, 0.19f * l));
        pivots.Add(V(0, deck + 0.03f * h, -0.35f * l)); // raised: the barrels clear the deckhouse and the torpedo tubes
        return CyShipGun(0.17f * w, 0.11f * l, 3);
    }

    /// <summary>Sliver submarine: flattened hexagonal hull, raked faceted sail (team cap, red visor, dive planes, sensor
    /// spike), cruciform tail with team tips, red bow slits and a red propulsor glow.</summary>
    private static void CySub(MeshBuilder b, float w, float h, float l)
    {
        float cy = 0.02f * h;
        b.Loft(MeshBuilder.NGon(6, 1, 0), [
            new Station(-0.5f * l, 0.05f * w, 0.05f * h, 0, cy), new Station(-0.38f * l, 0.3f * w, 0.24f * h, 0, cy),
            new Station(-0.1f * l, 0.5f * w, 0.38f * h, 0, cy), new Station(0.28f * l, 0.48f * w, 0.36f * h, 0, cy + 0.01f * h),
            new Station(0.43f * l, 0.3f * w, 0.24f * h, 0, cy), new Station(0.5f * l, 0.1f * w, 0.08f * h, 0, cy - 0.02f * h)], Axis.Z, CyHull, CyHullDark);
        Vector2[] sail = [V2(0, 0.5f), V2(0.5f, 0.15f), V2(0.5f, -0.3f), V2(0.2f, -0.5f), V2(-0.2f, -0.5f), V2(-0.5f, -0.3f), V2(-0.5f, 0.15f)];
        b.Loft(sail, [new Station(0.3f * h, 0.2f * w, 0.22f * l, 0, 0.1f * l), new Station(0.62f * h, 0.17f * w, 0.18f * l, 0, 0.08f * l),
                      new Station(0.8f * h, 0.12f * w, 0.12f * l, 0, 0.055f * l)], Axis.Y, CyHull, CyTeam);
        b.Box(V(0, 0.7f * h, 0.142f * l), V(0.06f * w, 0.03f * h, 0.02f * l), CyRed);
        Spike(b, V(0.03f * w, 0.79f * h, 0.06f * l), V(0.05f * w, 1.05f * h, -0.04f * l), 0.02f * w, CyHullDark);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            Vector2[] plane = [V2(sd * 0.07f * w, 0.13f * l), V2(sd * 0.28f * w, 0.11f * l), V2(sd * 0.28f * w, 0.08f * l), V2(sd * 0.07f * w, 0.05f * l)];
            b.Slab(plane, 0.6f * h, 0.625f * h, CyHullDark);
            Vector2[] tail = [V2(sd * 0.04f * w, -0.36f * l), V2(sd * 0.42f * w, -0.45f * l), V2(sd * 0.42f * w, -0.5f * l), V2(sd * 0.04f * w, -0.48f * l)];
            b.Slab(tail, cy - 0.012f * h, cy + 0.012f * h, CyHullDark);
            Vector2[] tip = [V2(sd * 0.32f * w, -0.43f * l), V2(sd * 0.42f * w, -0.45f * l), V2(sd * 0.42f * w, -0.5f * l), V2(sd * 0.32f * w, -0.49f * l)];
            b.Slab(tip, cy + 0.012f * h, cy + 0.018f * h, CyTeam);
            b.Box(V(sd * 0.35f * w, cy + 0.05f * h, 0.36f * l), V(0.03f * w, 0.03f * h, 0.06f * l), CyRed);
        }
        Fin(b, 0, 0.03f * w, cy + 0.08f * h, -0.48f * l, -0.36f * l, -0.5f * l, 0.5f * h, CyHullDark);
        Fin(b, 0, 0.03f * w, cy - 0.08f * h, -0.48f * l, -0.38f * l, -0.5f * l, -0.36f * h, CyHullDark);
        b.Cylinder(V(0, cy, -0.515f * l), Axis.Z, 0.015f * l, 0.08f * w, 0.06f * w, 6, CyRed);
    }

    // ================================================================ water structures

    /// <summary>
    /// Cybran naval factory (10x10 floating dock, rollout toward +Z): two faceted pontoon piers ending in claw tips frame
    /// the slipway, a faceted hall closes the back (dark door with a red strip, team roof plate), pairs of nanolathe
    /// build arms arch over the bay with red emitters, spires with red lights on the back corners, a sensor tower on the
    /// right pier. Tier 2 adds red-glowing vents, tier 3 a third arm pair and a central spire.
    /// </summary>
    private static void CyNavalFactory(MeshBuilder b, float w, float h, float l, int tier)
    {
        float top = 0.14f * h, bot = -0.3f * h, ph = top - bot, pw = 0.24f * w, pc = (top + bot) * 0.5f;
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            float px = sd * 0.37f * w;
            Vector2[] prof = [V2(-0.5f * sd, 0), V2(0.3f * sd, 0), V2(0.5f * sd, 0.5f), V2(0.3f * sd, 1), V2(-0.5f * sd, 1)];
            b.Loft(prof, [
                new Station(-0.5f * l, pw, ph, px, bot), new Station(0.34f * l, pw, ph, px, bot),
                new Station(0.5f * l, 0.3f * pw, 0.7f * ph, px + sd * 0.05f * w, bot + 0.2f * ph)], Axis.Z, CyHull, CyHullDark);
            b.Box(V(px - sd * 0.024f * w, top + 0.002f, -0.08f * l), V(0.17f * w, 0.004f, 0.8f * l), CyPad);
            b.Box(V(px - sd * 0.124f * w, top - 0.06f * h, 0.02f * l), V(0.008f * w, 0.05f * h, 0.6f * l), CyTeam);
            for (int k = 0; k < 6; k++)
                b.Box(V(px - sd * 0.105f * w, top + 0.01f * h, (-0.14f + k * 0.09f) * l), V(0.012f * w, 0.02f * h, 0.012f * w), CyRed);
            // Back corner spires with red lights.
            float sx = sd * 0.4f * w, sz = -0.43f * l, st = top + (1.15f + 0.12f * tier) * h;
            b.Frustum(V(sx, top, sz), 0.12f * w, 0.1f * l, 0.06f * w, 0.05f * l, 0.3f * h, CyHullDark, null, null, 0.25f);
            Spike(b, V(sx, top + 0.28f * h, sz), V(sx + sd * 0.03f * w, st, sz - 0.03f * l), 0.03f * w, CyHull);
            b.Box(V(sx + sd * 0.02f * w, top + 0.3f * h + 0.68f * (st - top - 0.3f * h), sz - 0.02f * l), V(0.025f * w, 0.025f * w, 0.025f * w), CyRed);
        }
        // Back block closing the U and the faceted hall on it (profile in z, y; lofted along X).
        b.Box(V(0, pc, -0.37f * l), V(0.52f * w, ph, 0.26f * l), CyHull, CyPad);
        Vector2[] hall = [V2(-0.5f, 0), V2(0.5f, 0), V2(0.5f, 0.42f), V2(0.12f, 1f), V2(-0.28f, 1f), V2(-0.5f, 0.72f)];
        float hz = -0.36f * l, hd = 0.3f * l, hH = 0.72f * h;
        b.Loft(hall, [new Station(-0.37f * w, 0.8f * hd, 0.8f * hH, hz, top), new Station(-0.28f * w, hd, hH, hz, top),
                      new Station(0.28f * w, hd, hH, hz, top), new Station(0.37f * w, 0.8f * hd, 0.8f * hH, hz, top)], Axis.X, CyHull, CyHullDark);
        float front = hz + 0.5f * hd;
        b.Box(V(0, top + 0.13f * h, front + 0.003f * l), V(0.36f * w, 0.26f * h, 0.006f * l), CyBlack);
        b.Box(V(0, top + 0.28f * h, front + 0.003f * l), V(0.44f * w, 0.02f * h, 0.006f * l), CyRed);
        b.Box(V(0, top + hH + 0.003f * h, hz - 0.08f * hd), V(0.5f * w, 0.006f * h, 0.3f * hd), CyTeam);
        // Submerged slipway with rails and cradle blocks.
        b.Box(V(0, -0.25f * h, 0.13f * l), V(0.5f * w, 0.03f * h, 0.7f * l), CyBlack);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            b.Box(V(sd * 0.16f * w, -0.22f * h, 0.13f * l), V(0.02f * w, 0.04f * h, 0.7f * l), CyHullDark);
        foreach (float z in (ReadOnlySpan<float>)[-0.1f, 0.1f, 0.3f])
            b.Box(V(0, -0.2f * h, z * l), V(0.2f * w, 0.07f * h, 0.03f * l), CyHullDark);
        // Nanolathe build arms over the slipway.
        float elbowY = top + (0.95f + 0.12f * (tier - 1)) * h;
        CyDockArms(b, w, h, l, 0.04f * l, top, elbowY);
        CyDockArms(b, w, h, l, 0.24f * l, top, elbowY - 0.05f * h);
        if (tier >= 3) CyDockArms(b, w, h, l, 0.41f * l, top, elbowY - 0.1f * h);
        // Sensor tower on the right pier, nanite tanks on the left one.
        float tx = 0.39f * w, tz = 0.3f * l;
        b.Frustum(V(tx, top, tz), 0.1f * w, 0.08f * l, 0.05f * w, 0.04f * l, 0.45f * h, CyHull, CyHullLight, null, 0.25f);
        b.Box(V(tx, top + 0.36f * h, tz + 0.024f * l), V(0.05f * w, 0.03f * h, 0.006f * l), CyRed);
        Spike(b, V(tx, top + 0.44f * h, tz), V(tx + 0.01f * w, top + 0.95f * h, tz - 0.03f * l), 0.015f * w, CyHullDark);
        foreach (float z in (ReadOnlySpan<float>)[0.12f, 0.24f])
        {
            b.Cylinder(V(-0.39f * w, top, z * l), Axis.Y, 0.16f * h, 0.045f * w, 0.04f * w, 6, CyHullDark, CyTeamDim);
            b.Box(V(-0.39f * w, top + 0.08f * h, z * l + 0.04f * w), V(0.02f * w, 0.06f * h, 0.01f * w), CyRedDim);
        }
        if (tier >= 2)
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
                b.Frustum(V(sd * 0.18f * w, top + hH - 0.02f * h, -0.39f * l), 0.08f * w, 0.06f * l, 0.05f * w, 0.035f * l, 0.35f * h, CyHullDark, CyRed, null, 0.25f);
        if (tier >= 3)
        {
            Spike(b, V(0, top + hH - 0.02f * h, -0.4f * l), V(0, top + hH + 0.95f * h, -0.42f * l), 0.05f * w, CyHull, 6);
            b.Box(V(0, top + hH + 0.6f * h, -0.413f * l), V(0.035f * w, 0.035f * w, 0.035f * w), CyRed);
            foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
                Spike(b, V(sd * 0.42f * w, top - 0.02f * h, 0.45f * l), V(sd * 0.45f * w, top + 0.5f * h, 0.5f * l), 0.02f * w, CyHullDark);
        }
    }

    /// <summary>A pair of nanolathe build arms at z: shoulder block on each pier, an arm arching up and in, two claw
    /// prongs and a red emitter at the wrist above the slipway.</summary>
    private static void CyDockArms(MeshBuilder b, float w, float h, float l, float z, float top, float elbowY)
    {
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            var sh = V(sd * 0.34f * w, top + 0.08f * h, z);
            var el = V(sd * 0.27f * w, elbowY, z - 0.02f * l);
            var wr = V(sd * 0.11f * w, elbowY - 0.22f * h, z);
            b.BevelBox(V(sd * 0.34f * w, top + 0.05f * h, z), V(0.07f * w, 0.1f * h, 0.07f * l), 0.015f * w, CyHullDark, CyTeam);
            Limb(b, sh, el, 0.018f * w, CyHull);
            Limb(b, el, wr, 0.014f * w, CyHullDark);
            b.Box(el, V(0.04f * w, 0.04f * w, 0.04f * w), CyTeam);
            foreach (int k in (ReadOnlySpan<int>)[-1, 1])
                Spike(b, wr, wr + V(-sd * 0.02f * w, -0.2f * h, k * 0.02f * l), 0.012f * w, CyBlack);
            b.Box(wr + V(0, -0.02f * h, 0), V(0.025f * w, 0.025f * w, 0.025f * w), CyRed);
        }
    }

    /// <summary>
    /// Cybran sonar: hex float with a team band and three raked stabilizer claws with red tips, a faceted tapering mast
    /// with red slits; T2 adds a collar and three raked spikes. Turret = the spinning head (hub, 2 or 3 blade arms with
    /// red emitters and claw tips, a top spike) at the mast top.
    /// </summary>
    private static MeshBuilder CySonar(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, int tier)
    {
        float top = 0.06f * h, mastTop = 0.8f * h;
        b.Cylinder(V(0, -0.16f * h, 0), Axis.Y, 0.22f * h, 0.47f * w, 0.42f * w, 6, CyHull, CyPad);
        b.Cylinder(V(0, -0.02f * h, 0), Axis.Y, 0.035f * h, 0.444f * w, 0.438f * w, 6, CyTeam);
        for (int i = 0; i < 3; i++)
        {
            var r = RotY(i * MathF.Tau / 3 + MathF.PI / 6);
            Fin(b, 0, 0.04f * w, top, 0.12f * w, 0.4f * w, 0.46f * w, top + 0.22f * h, CyHullDark, r);
            b.Box(Vector3.Transform(V(0, top + 0.2f * h, 0.45f * w), r), V(0.04f * w, 0.04f * w, 0.04f * w), CyRed);
        }
        b.Frustum(V(0, top, 0), 0.3f * w, 0.3f * w, 0.12f * w, 0.12f * w, mastTop - top, CyHull, CyHullDark, null, 0.3f);
        b.Box(V(0, top + 0.08f * h, 0), V(0.29f * w, 0.04f * h, 0.29f * w), CyTeam);
        for (int k = 0; k < 3; k++)
        {
            float y = (0.2f + 0.18f * k) * h, f = y / (mastTop - top);
            b.Box(V(0, top + y, (0.15f - 0.09f * f) * w + 0.004f * w), V(0.08f * w, 0.02f * h, 0.008f * w), CyRed);
        }
        if (tier >= 2)
        {
            b.Cylinder(V(0, 0.36f * h, 0), Axis.Y, 0.06f * h, 0.2f * w, 0.17f * w, 6, CyHullDark, CyTeam);
            for (int i = 0; i < 3; i++)
            {
                float a = i * MathF.Tau / 3;
                Spike(b, V(MathF.Cos(a) * 0.12f * w, 0.4f * h, MathF.Sin(a) * 0.12f * w), V(MathF.Cos(a) * 0.32f * w, 0.66f * h, MathF.Sin(a) * 0.32f * w), 0.03f * w, CyHullDark);
            }
        }
        var t = new MeshBuilder();
        t.Cylinder(V(0, -0.02f * h, 0), Axis.Y, 0.06f * h, 0.1f * w, 0.08f * w, 6, CyHullDark);
        int arms = tier >= 2 ? 3 : 2;
        for (int i = 0; i < arms; i++)
        {
            var r = RotY(i * MathF.Tau / arms);
            t.Box(V(0, 0.02f * h, 0.2f * w), V(0.05f * w, 0.025f * h, 0.36f * w), CyHull, null, r);
            Spike(t, Vector3.Transform(V(0, 0.02f * h, 0.36f * w), r), Vector3.Transform(V(0, 0.08f * h, 0.46f * w), r), 0.025f * w, CyHullDark);
            t.Box(V(0, 0.02f * h, 0.38f * w), V(0.06f * w, 0.04f * h, 0.04f * w), CyRed, null, r);
        }
        t.Cylinder(V(0, 0.035f * h, 0), Axis.Y, 0.02f * h, 0.075f * w, 0.07f * w, 6, CyTeam);
        Spike(t, V(0, 0.04f * h, 0), V(0, 0.2f * h, 0), 0.04f * w, CyHullDark);
        pivots.Add(V(0, mastTop, 0));
        return t;
    }

    /// <summary>Cybran torpedo launcher: hex float with a team band, a deck ring, three raked corner spikes and red lights;
    /// turret = angular housing with twin red-tipped nanite torpedo tubes.</summary>
    private static MeshBuilder? CyTorpedoLauncher(MeshBuilder b, List<Vector3> pivots, float w, float h, float l, bool hasWeapon)
    {
        float top = 0.2f * h;
        b.Cylinder(V(0, -0.3f * h, 0), Axis.Y, 0.5f * h, 0.5f * w, 0.44f * w, 6, CyHull, CyPad);
        b.Cylinder(V(0, 0.02f * h, 0), Axis.Y, 0.06f * h, 0.466f * w, 0.459f * w, 6, CyTeam);
        b.Cylinder(V(0, top, 0), Axis.Y, 0.02f * h, 0.34f * w, 0.33f * w, 6, CyHullDark);
        for (int i = 0; i < 3; i++)
        {
            float a = MathF.PI / 6 + i * MathF.Tau / 3, c = a + MathF.PI / 3;
            Spike(b, V(MathF.Cos(a) * 0.38f * w, top, MathF.Sin(a) * 0.38f * w), V(MathF.Cos(a) * 0.56f * w, top + 0.4f * h, MathF.Sin(a) * 0.56f * w), 0.04f * w, CyHullDark);
            b.Box(V(MathF.Cos(c) * 0.4f * w, top + 0.01f * h, MathF.Sin(c) * 0.4f * w), V(0.04f * w, 0.025f * h, 0.04f * w), CyRed);
        }
        if (!hasWeapon) return null;
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.06f * h, 0.3f * w, 0.28f * w, 6, CyBlack);
        Vector2[] prof = [V2(-0.5f, 0), V2(0.5f, 0), V2(0.5f, 0.5f), V2(0.2f, 1f), V2(-0.3f, 1f), V2(-0.5f, 0.5f)];
        t.Loft(prof, [new Station(-0.22f * l, 0.42f * w, 0.24f * h, 0, 0.06f * h), new Station(0.08f * l, 0.5f * w, 0.24f * h, 0, 0.06f * h),
                      new Station(0.18f * l, 0.34f * w, 0.16f * h, 0, 0.07f * h)], Axis.Z, CyHull, CyHullDark);
        t.Box(V(-0.03f * w, 0.302f * h, -0.06f * l), V(0.18f * w, 0.006f * h, 0.2f * l), CyTeam);
        t.Box(V(0.1f * w, 0.305f * h, 0.05f * l), V(0.05f * w, 0.012f * h, 0.05f * w), CyRed);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            t.Cylinder(V(sd * 0.12f * w, 0.16f * h, 0), Axis.Z, 0.36f * l, 0.07f * w, 0.07f * w, 6, CyHullDark);
            t.Cylinder(V(sd * 0.12f * w, 0.16f * h, 0.32f * l), Axis.Z, 0.05f * l, 0.085f * w, 0.085f * w, 6, CyBlack, CyRed);
        }
        pivots.Add(V(0, top + 0.015f * h, 0));
        return t;
    }
}
