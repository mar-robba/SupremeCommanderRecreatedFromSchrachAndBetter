using System.Numerics;

namespace Rts.Client.Rendering;

/// <summary>
/// (9b) Dying units (<see cref="DeathAnimator"/>): each active death sequence adds its model at
/// <see cref="DeathAnim.Pose"/> to the same instanced meshes as the living units (no extra draw call, in the shadow
/// pass too). Instance w = <see cref="DeathAnim.PackShaderW"/>: the lit shader chars the albedo, kills the lights
/// and dithers the model out. Warping-in commanders are drawn as a rising hologram (the Draw loop asks
/// <see cref="DeathAnimator.WarpFrac"/>).
/// </summary>
public sealed unsafe partial class UnitRenderer
{
    private DeathAnimator? _fx;

    /// <summary>Death sequences + ACU warp-in of this game (set by GameSession; null = none).</summary>
    public DeathAnimator? Fx
    {
        get => _fx;
        set
        {
            _fx = value;
            if (value != null) value.Models = this;
        }
    }

    private void AddDying(ReadOnlySpan<Vector4> teamCols, FrameView fv)
    {
        var fx = _fx;
        if (fx == null) return;
        fx.Sync();
        for (int i = 0; i < fx.Count; i++)
        {
            if (!fx.TryGetPose(i, out var pose, out int bp, out int army, out float turretRel)) continue;
            var model = Model(bp);
            float midY = (model.MinY + model.MaxY) * 0.5f;
            if (!fv.SphereVisible(pose.Pos + new Vector3(0, midY, 0), model.Radius * 1.4f)) continue;
            DrawnUnits++;
            var col = (uint)army < (uint)teamCols.Length ? teamCols[army] : new Vector4(0.7f, 0.7f, 0.7f, 0);
            col.W = DeathAnim.PackShaderW(pose.Char, pose.Visible);
            var m = DeathAnim.Matrix(in pose);
            bool lod = LodFor(model, -1, pose.Pos); // (9b-2) far wrecks-to-be use the LOD mesh, same w death fade
            Use(model);
            (lod ? model.LodBody! : model.Body).Add(m, col);
            if (model.Turret == null) continue;
            var yaw = Matrix4x4.CreateRotationY(model.TurretMode == TurretMode.Spin ? 0f : -turretRel);
            var turret = lod ? model.LodTurret! : model.Turret;
            foreach (var pivot in model.TurretPivots)
                turret.Add(yaw * Matrix4x4.CreateTranslation(pivot) * m, col);
        }
    }
}
