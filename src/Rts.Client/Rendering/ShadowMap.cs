using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>
/// Directional sun shadow map (depth-only FBO) fitted to the ground area visible by the camera.
/// Units render into it (instanced depth pass); units and terrain sample it with PCF.
/// The terrain is drawn before the units each frame, so it uses the previous frame's map (invisible lag).
/// </summary>
public sealed class ShadowMap : IDisposable
{
    public const int Size = 2048;
    public const int TextureSlot = 4;
    private readonly uint _fbo;
    private readonly uint _depth;
    public readonly bool Supported;
    public Matrix4x4 LightVP;
    public Matrix4x4 LightVPGl;
    public bool Valid;
    /// <summary>Frame counter (advanced by the terrain pass at the start of each frame).</summary>
    public static int Frame;
    private int _renderedFrame = -10;

    public static ShadowMap? Current { get; private set; }

    public ShadowMap()
    {
        _fbo = Rlgl.LoadFramebuffer();
        _depth = Rlgl.LoadTextureDepth(Size, Size, false);
        Rlgl.FramebufferAttach(_fbo, _depth, FramebufferAttachType.Depth, FramebufferAttachTextureType.Texture2D, 0);
        Supported = Rlgl.FramebufferComplete(_fbo);
        if (!Supported) Console.Error.WriteLine("WARNING: shadow framebuffer incomplete, shadows disabled");
        Current = this;
    }

    /// <summary>Fits the light frustum to the camera's visible ground area around the focus point.</summary>
    public void Fit(FrameView fv, Vector3 focus, float camDistance)
    {
        float maxReach = camDistance * 2.2f + 20f;
        var pts = new Vector3[5];
        pts[4] = focus;
        int k = 0;
        foreach (var (nx, ny) in (ReadOnlySpan<(float, float)>)[(-1, -1), (1, -1), (1, 1), (-1, 1)])
        {
            var a = Vector4.Transform(new Vector4(nx, ny, -1, 1), fv.InvViewProj);
            var b = Vector4.Transform(new Vector4(nx, ny, 1, 1), fv.InvViewProj);
            var p0 = new Vector3(a.X, a.Y, a.Z) / a.W;
            var p1 = new Vector3(b.X, b.Y, b.Z) / b.W;
            var d = Vector3.Normalize(p1 - p0);
            float t = d.Y < -1e-3f ? (focus.Y - p0.Y) / d.Y : float.MaxValue;
            var hit = p0 + d * MathF.Min(t, maxReach * 2);
            var off = hit - focus;
            off.Y = 0;
            if (off.Length() > maxReach) off = Vector3.Normalize(off) * maxReach;
            pts[k++] = focus + off;
        }
        var center = Vector3.Zero;
        foreach (var p in pts) center += p;
        center /= pts.Length;
        float r = 0;
        foreach (var p in pts) r = MathF.Max(r, Vector3.Distance(p, center));
        r = MathF.Min(r + 6f, 260f);
        // Quantize the radius to limit shimmering while zooming.
        r = MathF.Ceiling(r / 8f) * 8f;

        var sun = Atmosphere.SunDir;
        var view = Matrix4x4.CreateLookAt(center + sun * 300f, center, Vector3.UnitY);
        // Snap the center to shadow texels so the map does not shimmer while panning.
        float texel = 2 * r / Size;
        var lc = Vector3.Transform(center, view);
        float sx = MathF.Round(lc.X / texel) * texel - lc.X;
        float sy = MathF.Round(lc.Y / texel) * texel - lc.Y;
        var proj = Matrix4x4.CreateOrthographicOffCenter(-r + sx, r + sx, -r + sy, r + sy, 1f, 650f);
        LightVP = view * proj;
        LightVPGl = Matrix4x4.Transpose(LightVP);
    }

    public void Begin()
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableFramebuffer(_fbo);
        Rlgl.Viewport(0, 0, Size, Size);
        Rlgl.ClearScreenBuffers();
    }

    public void End()
    {
        Rlgl.DisableFramebuffer();
        Rlgl.Viewport(0, 0, Raylib.GetRenderWidth(), Raylib.GetRenderHeight());
        Valid = Supported;
        _renderedFrame = Frame;
    }

    /// <summary>Binds the depth texture and sets shadow uniforms on a program (shadowOn = 0 if unavailable).</summary>
    public static void Bind(ShaderProgram p)
    {
        var sm = Current;
        // Only use a map rendered this frame or the previous one (units are not drawn in the strategic view).
        bool on = sm != null && sm.Valid && Frame - sm._renderedFrame <= 1;
        if (on)
        {
            Rlgl.ActiveTextureSlot(TextureSlot);
            Rlgl.EnableTexture(sm!._depth);
            Rlgl.ActiveTextureSlot(0);
            p.Set("shadowMap", TextureSlot);
            p.SetMatrix("shadowVP", sm.LightVPGl);
            p.Set("shadowTexel", 1f / Size);
        }
        p.Set("shadowOn", on ? 1f : 0f);
    }

    public void Dispose()
    {
        Rlgl.UnloadFramebuffer(_fbo);
        if (Current == this) Current = null;
    }
}
