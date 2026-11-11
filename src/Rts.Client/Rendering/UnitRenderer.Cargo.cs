using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Carried units (<see cref="SimSnapshot.CargoViews"/> / <see cref="SimSnapshot.CargoUnits"/>): they are off the map
/// (not in <c>snap.Units</c>), so they are drawn from their carrier's interpolated frame (same bob/bank/tilt as the
/// carrier model) in the carrier's army color.
/// <list type="bullet">
/// <item>Land cargo hangs under the transport's belly (top of the unit at the clamps = the transport model's lowest
///   point), in up to three columns along the hull, front first; scaled down to fit the transport.</item>
/// <item>Docked aircraft park on the air staging pads (<see cref="UnitModels.StagingPad"/>) or in rows on the
///   Atlantis flight deck (<see cref="UnitModels.AtlantisSpot"/>); other air carriers: a grid on top.</item>
/// </list>
/// No allocations: the layout uses stackalloc scratch and the regular instance buffers (shadow + lit passes).
/// </summary>
public sealed unsafe partial class UnitRenderer
{
    private const int MaxCargoDrawn = 64;
    /// <summary>Base scale of hanging land cargo / of aircraft parked on a carrier deck / on staging pads.</summary>
    private const float LandCargoScale = 0.8f, DeckAircraftScale = 0.7f, PadAircraftScale = 0.9f;

    /// <summary>Carried units drawn last frame.</summary>
    public int CargoDrawn { get; private set; }

    /// <summary>Adds the instances of every carried unit whose carrier is visible. Called by <see cref="Draw"/>.</summary>
    private void AddCargo(SimSnapshot cur, SimSnapshot? prev, float alpha, ReadOnlySpan<Vector4> teamCols, FrameView fv, float time)
    {
        CargoDrawn = 0;
        for (int c = 0; c < cur.CargoViewCount; c++)
        {
            ref var cv = ref cur.CargoViews[c];
            if (cv.Count <= 0 || !cur.TryGetUnit(cv.Unit, cv.Gen, out var v)) continue;
            if ((v.Flags & UnitFlags.UnderConstruction) != 0) continue;
            var model = Model(v.Bp);
            var pos = v.Pos;
            float heading = v.Heading, turn = 0;
            if (prev != null && prev.TryGetUnit(v.Index, v.Gen, out var pv))
            {
                pos = Vector3.Lerp(pv.Pos, v.Pos, alpha);
                turn = Rts.Sim.Core.DMath.AngleDiff(pv.Heading, v.Heading);
                heading = pv.Heading + turn * alpha;
            }
            if (IsSubmerged(model, pos)) continue;
            if (!fv.SphereVisible(pos + new Vector3(0, (model.MinY + model.MaxY) * 0.5f - 1f, 0), model.Radius + 2f)) continue;
            var col = (uint)v.Army < (uint)teamCols.Length ? teamCols[v.Army] : new Vector4(0.7f, 0.7f, 0.7f, 0);
            var frame = CarrierFrame(model, v.Index, pos, heading, turn, time);
            var cbp = _db[v.Bp];
            int n = Math.Min(cv.Count, MaxCargoDrawn);
            int start = cv.Start;
            if (start < 0 || start + n > cur.CargoUnitCount) continue;
            if (cv.Layer == (byte)CargoLayer.Land) AddHangingCargo(cur.CargoUnits.AsSpan(start, n), model, cbp.Visual.Size, frame, col);
            else AddParkedAircraft(cur.CargoUnits.AsSpan(start, n), model, cbp, frame, col);
        }
    }

    /// <summary>The carrier's model frame (row-vector matrix: right, up, forward, position), same motion as
    /// <see cref="AddUnit"/>: air units bob and bank, ships bob and roll, structures stay level.</summary>
    private Matrix4x4 CarrierFrame(UnitModel model, int index, Vector3 pos, float heading, float turnPerTick, float time)
    {
        var fwd = new Vector3(MathF.Cos(heading), 0, MathF.Sin(heading));
        var up = Vector3.UnitY;
        if (model.Air)
        {
            pos.Y += MathF.Sin(time * 1.7f + index * 0.37f) * 0.08f;
            float bank = Math.Clamp(turnPerTick * 4.5f, -0.75f, 0.75f);
            var side0 = Vector3.Cross(up, fwd);
            up = Vector3.Normalize(up * MathF.Cos(bank) - side0 * MathF.Sin(bank));
        }
        else if (model.Naval)
        {
            float t = time * 1.3f + index * 0.61f;
            pos.Y += MathF.Sin(t) * 0.05f;
            var side0 = Vector3.Cross(up, fwd);
            up = Vector3.Normalize(up + side0 * (MathF.Sin(t * 0.8f) * 0.03f) + fwd * (MathF.Sin(t * 1.1f) * 0.02f));
        }
        else if (!model.Structure && _terrain != null)
        {
            var nrm = _terrain.GetNormal(pos.X, pos.Z);
            up = Vector3.Normalize(Vector3.Lerp(Vector3.UnitY, nrm, 0.85f));
        }
        var z = Vector3.Normalize(fwd - up * Vector3.Dot(fwd, up));
        var x = Vector3.Cross(up, z);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, pos.X, pos.Y, pos.Z, 1);
    }

    /// <summary>Land units clamped under a transport: up to 3 columns (as many as fit the hull width), each unit goes
    /// to the shortest column, columns centred along the hull; everything shrinks to fit the hull length.</summary>
    private void AddHangingCargo(ReadOnlySpan<CargoUnitView> cargo, UnitModel carrier, Vector3 size, in Matrix4x4 frame, Vector4 col)
    {
        const float gap = 0.12f;
        float availW = 0.9f * size.X, availL = 0.92f * size.Z;
        float maxW = 0.1f;
        foreach (ref readonly var cu in cargo) maxW = MathF.Max(maxW, _db[cu.Bp].Visual.Size.X);
        int cols = Math.Clamp((int)(availW / (maxW * LandCargoScale + gap)), 1, 3);
        float colPitch = availW / cols;
        float sw = MathF.Min(LandCargoScale, MathF.Max(0.2f, (colPitch - gap) / maxW));

        Span<byte> colOf = stackalloc byte[MaxCargoDrawn];
        Span<float> colLen = stackalloc float[3];
        for (int k = 0; k < cargo.Length; k++)
        {
            int best = 0;
            for (int c = 1; c < cols; c++) if (colLen[c] < colLen[best] - 1e-4f) best = c;
            colOf[k] = (byte)best;
            colLen[best] += _db[cargo[k].Bp].Visual.Size.Z * sw + gap;
        }
        float longest = 0;
        for (int c = 0; c < cols; c++) longest = MathF.Max(longest, colLen[c] - gap);
        float fit = longest > availL ? availL / longest : 1f;
        float s = sw * fit;

        // Front edge of each column (columns are centred on the hull), then place front to back.
        Span<float> cursor = stackalloc float[3];
        for (int c = 0; c < cols; c++) cursor[c] = (colLen[c] - gap) * fit * 0.5f;
        float belly = carrier.MinY + 0.03f; // the clamps reach a little over the units' tops
        for (int k = 0; k < cargo.Length; k++)
        {
            var bp = _db[cargo[k].Bp];
            var um = Model(cargo[k].Bp);
            int c = colOf[k];
            float len = bp.Visual.Size.Z * s;
            float zc = cursor[c] - len * 0.5f;
            cursor[c] -= len + gap * fit;
            var local = new Vector3((c - (cols - 1) * 0.5f) * colPitch, belly - um.MaxY * s, zc);
            AddCargoInstance(um, frame, local, s, col);
        }
    }

    /// <summary>Docked aircraft: staging pads, Atlantis deck spots, or a grid on top of any other air carrier.</summary>
    private void AddParkedAircraft(ReadOnlySpan<CargoUnitView> cargo, UnitModel carrier, UnitBlueprint cbp, in Matrix4x4 frame, Vector4 col)
    {
        var size = cbp.Visual.Size;
        string shape = cbp.Visual.Shape;
        int gridCols = Math.Max(1, (int)(size.X / 2f));
        for (int k = 0; k < cargo.Length; k++)
        {
            Vector3 spot;
            float s;
            if (UnitModels.BaseShape(shape) == "air_staging") { if (!UnitModels.StagingPad(k, size, out spot)) break; s = PadAircraftScale; }
            else if (shape == "atlantis") { if (!UnitModels.AtlantisSpot(k, size, out spot)) break; s = DeckAircraftScale; }
            else if (UnitModels.BaseShape(shape) == "carrier") { if (!UnitModels.CyCarrierSpot(k, size, out spot)) break; s = DeckAircraftScale; }
            else
            {
                spot = new Vector3((k % gridCols - (gridCols - 1) * 0.5f) * 2f, carrier.MaxY, (k / gridCols) * -2.2f + size.Z * 0.3f);
                s = DeckAircraftScale;
            }
            var um = Model(cargo[k].Bp);
            AddCargoInstance(um, frame, spot + new Vector3(0, 0.02f - um.MinY * s, 0), s, col);
        }
    }

    /// <summary>One carried unit: body (and turrets facing forward) at <paramref name="local"/> in the carrier's frame.</summary>
    private void AddCargoInstance(UnitModel um, in Matrix4x4 frame, Vector3 local, float s, Vector4 col)
    {
        var m = Matrix4x4.CreateScale(s) * Matrix4x4.CreateTranslation(local) * frame;
        Use(um);
        um.Body.Add(m, col);
        CargoDrawn++;
        if (um.Turret == null) return;
        foreach (var p in um.TurretPivots)
            um.Turret.Add(Matrix4x4.CreateScale(s) * Matrix4x4.CreateTranslation(local + p * s) * frame, col);
    }
}
