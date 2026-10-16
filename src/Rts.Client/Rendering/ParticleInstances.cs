using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>One particle quad for <see cref="ParticleInstances"/>: the vertex shader expands it into a billboard,
/// a ground-aligned quad or a velocity streak (same corners and uvs as <see cref="QuadBatch"/>).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleInstance
{
    /// <summary>xyz = center (streak: head), w = size (streak: width).</summary>
    public Vector4 PosSize;
    /// <summary>Streak: xyz = unit direction, w = length. Other modes: unused.</summary>
    public Vector4 Dir;
    /// <summary>x = shape (<see cref="PShape"/>), y = seed, z = mode (<see cref="PMode"/>), w = rotation.</summary>
    public Vector4 Params;
    public Color Col;
}

/// <summary>
/// GPU-instanced particle quads: one 52-byte instance per particle instead of four 32-byte vertices built on the
/// CPU (<see cref="QuadBatch"/>). A static unit quad (corners + 6 indices) lives in the VAO; the per-instance
/// buffer grows on demand and is re-uploaded every frame. Draw with <see cref="Shaders.ParticleInst"/>
/// (uniforms viewProj, camRight, camUp, camPos, time); the caller sets the blend/depth state.
/// </summary>
public sealed unsafe class ParticleInstances : IDisposable
{
    public const uint LocCorner = 0, LocPosSize = 10, LocDir = 11, LocParams = 12, LocColor = 13;
    private const int GlFloat = 0x1406, GlUnsignedByte = 0x1401;

    private uint _vao, _cornerVbo, _ebo, _vbo;
    private int _capacity;
    private ParticleInstance[] _items = new ParticleInstance[1024];
    public int Count;

    public void Clear() => Count = 0;

    /// <summary>Appends an instance and returns it for filling (all fields must be written).</summary>
    public ref ParticleInstance Add()
    {
        if (Count == _items.Length) Array.Resize(ref _items, _items.Length * 2);
        return ref _items[Count++];
    }

    private void EnsureVao()
    {
        if (_vao != 0) return;
        _vao = Rlgl.LoadVertexArray();
        Rlgl.EnableVertexArray(_vao);
        Vector2* corners = stackalloc Vector2[4] { new(-1, -1), new(1, -1), new(1, 1), new(-1, 1) };
        _cornerVbo = Rlgl.LoadVertexBuffer(corners, 4 * sizeof(Vector2), false);
        Rlgl.SetVertexAttribute(LocCorner, 2, GlFloat, false, sizeof(Vector2), 0);
        Rlgl.EnableVertexAttribute(LocCorner);
        ushort* ix = stackalloc ushort[6] { 0, 1, 2, 0, 2, 3 };
        _ebo = Rlgl.LoadVertexBufferElement(ix, 6 * sizeof(ushort), false);
        Rlgl.DisableVertexArray();
    }

    private void EnsureBuffer(int count)
    {
        EnsureVao();
        if (count <= _capacity) return;
        int cap = Math.Max(1024, _capacity);
        while (cap < count) cap *= 2;
        if (_vbo != 0) Rlgl.UnloadVertexBuffer(_vbo);
        _capacity = cap;
        int stride = sizeof(ParticleInstance);
        Rlgl.EnableVertexArray(_vao);
        _vbo = Rlgl.LoadVertexBuffer(null, cap * stride, true);
        Rlgl.EnableVertexBuffer(_vbo);
        Attr(LocPosSize, 4, GlFloat, false, stride, 0);
        Attr(LocDir, 4, GlFloat, false, stride, 16);
        Attr(LocParams, 4, GlFloat, false, stride, 32);
        Attr(LocColor, 4, GlUnsignedByte, true, stride, 48);
        Rlgl.DisableVertexBuffer();
        Rlgl.DisableVertexArray();
    }

    private static void Attr(uint loc, int n, int type, bool norm, int stride, int offset)
    {
        Rlgl.EnableVertexAttribute(loc);
        Rlgl.SetVertexAttribute(loc, n, type, norm, stride, offset);
        Rlgl.SetVertexAttributeDivisor(loc, 1);
    }

    /// <summary>Uploads the instances and draws them (shader must be bound by the caller).</summary>
    public void Draw()
    {
        if (Count == 0) return;
        EnsureBuffer(Count);
        fixed (ParticleInstance* p = _items)
            Rlgl.UpdateVertexBuffer(_vbo, p, Count * sizeof(ParticleInstance), 0);
        Rlgl.EnableVertexArray(_vao);
        Rlgl.DrawVertexArrayElementsInstanced(0, 6, null, Count);
        Rlgl.DisableVertexArray();
    }

    /// <summary>Binds the instanced particle shader and its per-frame uniforms.</summary>
    public static void BindShader(FrameView fv)
    {
        var sp = Shaders.ParticleInst;
        sp.Use();
        sp.SetMatrix("viewProj", fv.ViewProjGl);
        sp.Set("camRight", fv.CamRight);
        sp.Set("camUp", fv.CamUp);
        sp.Set("camPos", fv.CamPos);
        sp.Set("time", Atmosphere.Time);
        sp.Use();
    }

    public void Dispose()
    {
        if (_vbo != 0) Rlgl.UnloadVertexBuffer(_vbo);
        if (_cornerVbo != 0) Rlgl.UnloadVertexBuffer(_cornerVbo);
        if (_ebo != 0) Rlgl.UnloadVertexBuffer(_ebo);
        if (_vao != 0) Rlgl.UnloadVertexArray(_vao);
        _vbo = _cornerVbo = _ebo = _vao = 0;
    }
}
