using System.Numerics;

namespace Rts.Client.Rendering;

/// <summary>
/// (8a) Cybran shapes: "cy_missile_sub" (T3 strategic missile sub Plan B), "cy_carrier" (T3 aircraft carrier) and
/// "cy_wall" (T1 wall section, tiles edge to edge in a line). Palette and helpers of <c>UnitModels.Cybran.cs</c> /
/// <c>UnitModels.CybranNaval.cs</c>; dispatched from <see cref="CybranShape"/>. Parked aircraft on the carrier deck:
/// <see cref="CyCarrierSpot"/> (UnitRenderer.Cargo.cs).
/// </summary>
public static partial class UnitModels
{
    /// <summary>Plan B: the Sliver hull stretched to a boomer, with a raised faceted missile spine aft of the sail
    /// (two red-seamed strategic hatches, team rims) and a Loa cell block forward.</summary>
    private static void CyMissileSub(MeshBuilder b, float w, float h, float l)
    {
        CySub(b, w, h, l);
        float y = 0.02f * h + 0.3f * h;
        CyWedge(b, 0, y - 0.04f * h, 0.42f * w, 0.14f * h, -0.3f * l, 0.02f * l, CyHullDark, CyHull, 0, 0.6f);
        foreach (float z in (ReadOnlySpan<float>)[-0.22f, -0.1f])
        {
            b.Cylinder(V(0, y + 0.07f * h, z * l), Axis.Y, 0.025f * h, 0.12f * w, 0.12f * w, 6, CyTeam);
            b.Cylinder(V(0, y + 0.09f * h, z * l), Axis.Y, 0.012f * h, 0.09f * w, 0.09f * w, 6, CyBlack);
            b.Box(V(0, y + 0.104f * h, z * l), V(0.16f * w, 0.004f * h, 0.008f * l), CyRed);
        }
        b.Box(V(0, y + 0.03f * h, 0.24f * l), V(0.26f * w, 0.06f * h, 0.08f * l), CyHullDark, CyRedDim);
    }

    private const float CyCarrierDeckTop = 0.5f;
    private static readonly float[] CyCarrierCols = [-0.32f, -0.08f];
    private const int CyCarrierRows = 8;

    /// <summary>Local position (top of the flight deck) of docked aircraft k on the Cybran carrier: two columns of
    /// spots from the stern forward on the port side of the island (16 = the carrier's capacity).</summary>
    public static bool CyCarrierSpot(int k, Vector3 size, out Vector3 pos)
    {
        pos = default;
        if ((uint)k >= (uint)(CyCarrierCols.Length * CyCarrierRows)) return false;
        int row = k / CyCarrierCols.Length, col = k % CyCarrierCols.Length;
        pos = new Vector3(CyCarrierCols[col] * size.X, CyCarrierDeckTop * size.Y + 0.004f, (-0.4f + row * 0.1f) * size.Z);
        return true;
    }

    /// <summary>Cybran T3 aircraft carrier: faceted hull under a full-length angular flight deck (red lane seams, team
    /// edge stripes), a tall raked island on starboard with red visor and sensor spikes, nanodart SAM pods (turrets)
    /// on deck-edge sponsons, a stern hangar slot with red glow.</summary>
    private static MeshBuilder CyCarrier(MeshBuilder b, List<Vector3> pivots, float w, float h, float l)
    {
        float deck = CyShipHull(b, w * 0.86f, h, l, 0.42f, 0.03f), top = CyCarrierDeckTop * h;
        Vector2[] fd = [V2(-0.5f * w, -0.48f * l), V2(0.5f * w, -0.48f * l), V2(0.5f * w, 0.28f * l), V2(0.22f * w, 0.47f * l),
                        V2(-0.22f * w, 0.47f * l), V2(-0.5f * w, 0.28f * l)];
        b.Slab(fd, deck, top, CyHullDark, CyPad);
        for (int k = 0; k < 8; k++) b.Box(V(-0.2f * w, top + 0.002f, (-0.4f + k * 0.1f) * l), V(0.01f * w, 0.004f, 0.05f * l), CyRedDim);
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        {
            b.Box(V(sd * 0.5f * w, (deck + top) * 0.5f, -0.1f * l), V(0.006f * w, (top - deck) * 0.6f, 0.7f * l), CyTeam);
            b.Box(V(sd * 0.44f * w, top + 0.003f, 0.05f * l), V(0.01f * w, 0.004f, 0.76f * l), CyTrim);
        }
        b.Box(V(0, top + 0.003f, -0.47f * l), V(0.5f * w, 0.004f, 0.01f * l), CyRed);
        b.Box(V(0, (deck - 0.3f * h) * 0.5f + 0.05f * h, -0.48f * l), V(0.4f * w, 0.2f * h, 0.03f * l), CyBlack);
        b.Box(V(0, 0.02f * h, -0.497f * l), V(0.3f * w, 0.03f * h, 0.006f * l), CyRed);
        // Island: raked faceted tower, visor, spikes.
        float ix = 0.33f * w, iz = -0.02f * l;
        CyWedge(b, ix, top, 0.22f * w, 0.32f * h, iz - 0.12f * l, iz + 0.1f * l, CyHull, CyTeam, 0.05f, 0.7f);
        b.Box(V(ix, top + 0.24f * h, iz + 0.095f * l), V(0.14f * w, 0.04f * h, 0.006f * l), CyRed);
        Spike(b, V(ix, top + 0.3f * h, iz - 0.04f * l), V(ix + 0.04f * w, top + 0.75f * h, iz - 0.09f * l), 0.025f * w, CyHullDark);
        Spike(b, V(ix - 0.05f * w, top + 0.28f * h, iz - 0.08f * l), V(ix - 0.08f * w, top + 0.55f * h, iz - 0.12f * l), 0.018f * w, CyHull);
        b.Box(V(ix + 0.035f * w, top + 0.68f * h, iz - 0.085f * l), V(0.025f * w, 0.025f * w, 0.025f * w), CyRed);
        // SAM sponsons: two per side.
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
            foreach (float z in (ReadOnlySpan<float>)[0.2f, -0.34f])
            {
                b.Frustum(V(sd * 0.5f * w, deck - 0.1f * h, z * l), 0.04f * w, 0.03f * l, 0.12f * w, 0.06f * l, 0.1f * h, CyHullDark, CyPad);
                pivots.Add(V(sd * 0.5f * w, top - 0.01f * h, z * l));
            }
        var t = new MeshBuilder();
        t.Cylinder(V(0, 0, 0), Axis.Y, 0.03f * h, 0.05f * w, 0.045f * w, 6, CyHullDark, CyHull);
        var xf = PitchUp(0.4f) * T(0, 0.06f * h, 0);
        t.BevelBox(V(0, 0, 0), V(0.08f * w, 0.05f * h, 0.035f * l), 0.01f * w, CyHull, CyTeam, xf);
        for (int k = 0; k < 4; k++)
            t.Box(V((-0.025f + (k & 1) * 0.05f) * w, ((k >> 1) - 0.5f) * 0.022f * h, 0.0177f * l), V(0.018f * w, 0.012f * h, 0.002f * l), CyRed, null, xf);
        return t;
    }

    /// <summary>Cybran wall section: a faceted block filling the whole 1x1 footprint at the base (neighbours join edge
    /// to edge in a line, either direction), a chamfered crest with a red seam on every side and team corner caps.</summary>
    private static void CyWall(MeshBuilder b, float w, float h, float l)
    {
        b.Box(V(0, h * 0.25f - 0.15f, 0), V(w, h * 0.5f + 0.3f, l), CyHullDark);
        b.Frustum(V(0, 0.5f * h, 0), w, l, 0.82f * w, 0.82f * l, 0.38f * h, CyHull, CyHullLight, null, 0.18f);
        b.Box(V(0, 0.58f * h, 0), V(w * 1.005f, 0.035f * h, 0.3f * l), CyRedDim);
        b.Box(V(0, 0.58f * h, 0), V(0.3f * w, 0.035f * h, l * 1.005f), CyRedDim);
        b.Frustum(V(0, 0.88f * h, 0), 0.5f * w, 0.5f * l, 0.3f * w, 0.3f * l, 0.1f * h, CyTeam, CyHullDark, null, 0.2f);
    }
}
