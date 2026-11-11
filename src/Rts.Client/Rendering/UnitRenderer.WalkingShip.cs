using System.Numerics;
using Raylib_cs;
using Rts.Sim.Snapshot;

namespace Rts.Client.Rendering;

/// <summary>
/// Ships that walk ashore (the Cybran Salem, <see cref="Rts.Sim.Naval.WalkingShips.IsWalkingShip"/>). The sim gives
/// a Hover-layer unit the surface height as elevation (terrain on land, water at sea), so on land the hull would sink
/// into the ground by its draft. Here the hull is lifted to stand <see cref="WalkClearance"/> × height above the
/// ground (<see cref="WalkingShipY"/>, continuous: at sea it is the snapshot Y), and four spider legs reach down from
/// the hull sides to the ground whenever it is within reach (shallows and land), stepping in a diagonal gait while
/// the ship moves. Legs are two instanced boxes per leg (upper, lower), each mapped onto its segment, so the box
/// normals stay exact under the per-axis scale. Pure client visuals; nothing in the sim changes.
/// </summary>
public sealed unsafe partial class UnitRenderer
{
    /// <summary>Gap between the ground and the keel of a walking ship on land, × model height.</summary>
    public const float WalkClearance = 0.35f;
    /// <summary>Hips: hull sides (± fraction of width), height (× h), along the hull (× l), like the folded legs.</summary>
    private const float HipX = 0.44f, HipY = 0.21f;
    private static readonly float[] HipZ = [0.1f, -0.24f];

    private UnitModel? _legModel; // Body = upper segments, Turret = lower segments (lazy: needs the GL context)

    /// <summary>Render height of a walking ship: the snapshot Y, or high enough that the keel (<paramref name="minY"/>,
    /// model space, negative) clears the ground by <see cref="WalkClearance"/> × <paramref name="h"/>.</summary>
    public static float WalkingShipY(float snapY, float groundH, float minY, float h) =>
        MathF.Max(snapY, groundH - minY + WalkClearance * h);

    /// <summary>The ground under a walking ship rendered at <paramref name="renderY"/> is within leg reach.</summary>
    public static bool WalkingShipLegsOut(float renderY, float groundH, float minY, float h) =>
        renderY - groundH <= -minY + (WalkClearance + 0.6f) * h;

    /// <summary>Gait amplitude 0..1: ground speed over the walking speed (MaxSpeed × LandSpeedFactor).</summary>
    public static float WalkAmplitude(float speed, float walkSpeed) =>
        walkSpeed > 1e-3f ? Math.Clamp(speed / walkSpeed, 0f, 1f) : 0f;

    /// <summary>Hook from <see cref="Draw"/>: for a walking ship, returns the lifted position and adds its legs.
    /// Any other unit is returned unchanged.</summary>
    private Vector3 WalkingShip(UnitModel model, in UnitView v, SimSnapshot? prev, Vector3 pos, float heading, Vector4 col, float time)
    {
        if (!model.Naval || _terrain == null) return pos;
        var bp = _db[v.Bp];
        if (!Rts.Sim.Naval.WalkingShips.IsWalkingShip(bp)) return pos;
        var s = bp.Visual.Size;
        float w = MathF.Max(0.1f, s.X), h = MathF.Max(0.1f, s.Y), l = MathF.Max(0.1f, s.Z);
        float ground = _terrain.GetHeight(pos.X, pos.Z);
        float y = WalkingShipY(pos.Y, ground, model.MinY, h);
        if (!WalkingShipLegsOut(y, ground, model.MinY, h)) return pos;
        pos.Y = y;

        float amp = 0;
        if (prev != null && prev.TryGetUnit(v.Index, v.Gen, out var pv))
        {
            float spd = new Vector2(v.Pos.X - pv.Pos.X, v.Pos.Z - pv.Pos.Z).Length() / 0.1f; // snapshots: one tick apart
            amp = WalkAmplitude(spd, bp.Movement.MaxSpeed * bp.Movement.LandSpeedFactor);
        }

        _legModel ??= BuildLegModel();
        Use(_legModel);
        var fwd = new Vector3(MathF.Cos(heading), 0, MathF.Sin(heading));
        var right = new Vector3(MathF.Sin(heading), 0, -MathF.Cos(heading)); // model +X (see AddUnit)
        float stride = 0.1f * l, lift = 0.16f * h, phase = time * 4.2f + v.Index * 0.9f;
        int leg = 0;
        foreach (int sd in (ReadOnlySpan<int>)[-1, 1])
        foreach (float zf in HipZ)
        {
            float ph = phase + ((leg++ is 0 or 3) ? 0 : MathF.PI); // diagonal pairs step together
            var hip = pos + right * (sd * HipX * w) + Vector3.UnitY * (HipY * h) + fwd * (zf * l);
            float splay = (zf > 0 ? 0.07f : -0.07f) * l; // front feet reach forward, rear feet back
            var foot = pos + right * (sd * 1.3f * w) + fwd * (zf * l + splay - amp * stride * MathF.Cos(ph));
            foot.Y = _terrain.GetHeight(foot.X, foot.Z) + amp * lift * MathF.Max(0, MathF.Sin(ph));
            var knee = Vector3.Lerp(hip, foot, 0.42f);
            knee.Y = MathF.Max(hip.Y, foot.Y) + 0.42f * h; // spider knee well above the deck line
            _legModel.Body.Add(Segment(hip, knee, 0.09f * w), col);
            _legModel.Turret!.Add(Segment(knee, foot, 0.06f * w), col);
        }
        return pos;
    }

    /// <summary>Maps the unit box (x, y ∈ ±0.5, z ∈ 0..1) onto the segment a→b with a square cross-section.</summary>
    public static Matrix4x4 Segment(Vector3 a, Vector3 b, float thick)
    {
        var d = b - a;
        float len = d.Length();
        var z = len > 1e-5f ? d / len : Vector3.UnitY;
        var x = Vector3.Cross(Vector3.UnitY, z);
        x = x.LengthSquared() > 1e-6f ? Vector3.Normalize(x) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        x *= thick; y *= thick; z *= len;
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, a.X, a.Y, a.Z, 1);
    }

    private static UnitModel BuildLegModel()
    {
        var hull = Mat.M(76, 72, 82); // Cybran hull / dark hull / team (UnitModels.Cybran.cs)
        var dark = Mat.M(52, 49, 57);
        var upper = new MeshBuilder();
        upper.Box(new Vector3(0, 0, 0.5f), new Vector3(1, 1, 1.04f), hull, Mat.T(205));
        var lower = new MeshBuilder();
        lower.Box(new Vector3(0, 0, 0.5f), new Vector3(1, 1, 1), dark);
        lower.Box(new Vector3(0, 0, 0.97f), new Vector3(1.5f, 1.5f, 0.06f), Mat.G(255, 38, 26)); // glowing foot pad
        return new UnitModel { Body = new InstancedMesh(upper.Upload()), Turret = new InstancedMesh(lower.Upload()) };
    }
}
