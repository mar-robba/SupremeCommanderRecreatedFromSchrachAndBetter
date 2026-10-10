using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// Camera matrices of the current frame (captured inside BeginMode3D from rlgl, so they always match raylib),
/// frustum planes for culling and world-to-screen projection for icons/labels.
/// Matrices are in System.Numerics row-vector convention (v * M); use <see cref="ViewProjGl"/> for shader uniforms.
/// </summary>
public sealed class FrameView
{
    public Matrix4x4 View, Proj, ViewProj, InvViewProj;
    /// <summary>Column-vector matrix ready for Rlgl.SetUniformMatrix / SetShaderValueMatrix.</summary>
    public Matrix4x4 ViewProjGl, InvViewProjGl;
    public Vector3 CamPos, CamRight, CamUp, CamForward;
    public float ScreenW, ScreenH;
    private readonly Vector4[] _planes = new Vector4[6];

    /// <summary>Last captured view (valid after the 3D pass; used by 2D passes such as icons).</summary>
    public static readonly FrameView Current = new();

    /// <summary>Call inside BeginMode3D.</summary>
    public static FrameView Capture()
    {
        var f = Current;
        // rlgl matrices are column-vector math matrices; transpose to row-vector.
        f.View = Matrix4x4.Transpose(Rlgl.GetMatrixModelview());
        f.Proj = Matrix4x4.Transpose(Rlgl.GetMatrixProjection());
        f.ViewProj = f.View * f.Proj;
        Matrix4x4.Invert(f.ViewProj, out f.InvViewProj);
        f.ViewProjGl = Matrix4x4.Transpose(f.ViewProj);
        f.InvViewProjGl = Matrix4x4.Transpose(f.InvViewProj);
        Matrix4x4.Invert(f.View, out var inv);
        f.CamRight = Vector3.Normalize(new Vector3(inv.M11, inv.M12, inv.M13));
        f.CamUp = Vector3.Normalize(new Vector3(inv.M21, inv.M22, inv.M23));
        f.CamForward = -Vector3.Normalize(new Vector3(inv.M31, inv.M32, inv.M33));
        f.CamPos = new Vector3(inv.M41, inv.M42, inv.M43);
        f.ScreenW = Raylib.GetScreenWidth();
        f.ScreenH = Raylib.GetScreenHeight();

        var m = f.ViewProj;
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        f._planes[0] = Norm(c4 + c1);
        f._planes[1] = Norm(c4 - c1);
        f._planes[2] = Norm(c4 + c2);
        f._planes[3] = Norm(c4 - c2);
        f._planes[4] = Norm(c4 + c3);
        f._planes[5] = Norm(c4 - c3);
        return f;
    }

    private static Vector4 Norm(Vector4 p)
    {
        float l = new Vector3(p.X, p.Y, p.Z).Length();
        return l > 0 ? p / l : p;
    }

    public bool SphereVisible(Vector3 c, float r)
    {
        for (int i = 0; i < 6; i++)
        {
            var p = _planes[i];
            if (p.X * c.X + p.Y * c.Y + p.Z * c.Z + p.W < -r) return false;
        }
        return true;
    }

    /// <summary>Projects a world point to screen pixels. False if behind the camera.</summary>
    public bool WorldToScreen(Vector3 p, out Vector2 s)
    {
        var c = Vector4.Transform(new Vector4(p, 1f), ViewProj);
        if (c.W <= 1e-4f) { s = default; return false; }
        float ix = c.X / c.W, iy = c.Y / c.W;
        s = new Vector2((ix * 0.5f + 0.5f) * ScreenW, (0.5f - iy * 0.5f) * ScreenH);
        return true;
    }
}
