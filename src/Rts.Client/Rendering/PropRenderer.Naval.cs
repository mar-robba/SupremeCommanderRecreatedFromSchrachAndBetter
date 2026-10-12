using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;

namespace Rts.Client.Rendering;

/// <summary>
/// Wrecks over deep water (sea floor at least <see cref="SinkMinDepth"/> below the surface) don't lie on the sea
/// floor: they start at the surface (subs at their running depth), sink over several seconds of game time while
/// tilting bow-up, stop glowing once under, churn foam on the surface and dither out. The prop itself stays
/// reclaimable (the strategic view still shows its dot).
/// </summary>
public sealed partial class PropRenderer
{
    /// <summary>Minimum water depth over the wreck position for the sinking animation.</summary>
    public const float SinkMinDepth = 1f;

    /// <returns>True if the wreck is a sinking/sunk sea wreck (baseY, pitch, ember and vis were adjusted).</returns>
    private bool SinkInDeepWater(in PropView p, UnitModel model, UnitBlueprint? bp, float h, double age, bool emit,
                                 ref float baseY, ref float pitch, ref float ember, out float vis)
    {
        vis = 1f;
        float water = _ctx.Terrain.WaterLevel;
        if (water - p.Pos.Y < SinkMinDepth || bp == null) return false;
        float dur = Math.Clamp(5f + h * 1.2f, 6f, 14f);
        float k = (float)Math.Clamp(age / dur, 0.0, 1.0);
        bool sub = bp.Movement.Layer == MotionLayer.Sub;
        float start = sub ? water - 1.5f : water - h * 0.12f;
        float end = MathF.Max(p.Pos.Y - h * 0.2f, start - h - 2.5f);
        float e = k * k * (3f - 2f * k);
        baseY = start + (end - start) * e;
        pitch += (Hash(p.Id, 5) < 0.5f ? -1f : 1f) * (bp.IsStructure ? 0.1f : 0.5f) * e;
        ember *= 1f - Math.Clamp(k / 0.3f, 0f, 1f);
        vis = 1f - Math.Clamp((k - 0.6f) / 0.4f, 0f, 1f);

        // Churned water and foam on the surface while it goes down.
        if (emit && _ps != null && k < 0.85f && Atmosphere.CameraDistance < 220f)
        {
            float r = MathF.Max(0.8f, p.Radius);
            for (int i = 0; i < 2; i++)
            {
                ref var f = ref _ps.Emit();
                float a = _ps.Rand() * MathF.Tau, d = _ps.Rand() * r * 0.8f;
                f.Pos = new Vector3(p.Pos.X + MathF.Cos(a) * d, water + 0.06f, p.Pos.Z + MathF.Sin(a) * d);
                f.Vel = new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 0.4f;
                f.Drag = 0.7f; f.Life = _ps.Range(1.8f, 3f); f.Size0 = r * 0.45f; f.Size1 = r * 1.3f; f.RotVel = _ps.Range(-0.3f, 0.3f);
                f.C0 = new Vector4(0.9f, 0.95f, 1f, 0.5f * (1f - k)); f.C1 = new Vector4(0.9f, 0.95f, 1f, 0);
                f.Shape = PShape.Smoke; f.Mode = PMode.Flat; f.Blend = PBlend.Alpha;
            }
        }
        return true;
    }
}
