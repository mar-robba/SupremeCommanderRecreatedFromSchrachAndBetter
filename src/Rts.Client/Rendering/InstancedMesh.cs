using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>Per-instance vertex data: model matrix (row-vector System.Numerics layout) + a free vec4.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct InstanceData
{
    public Matrix4x4 Model;
    public Vector4 Data;
}

/// <summary>
/// A static mesh plus a persistent, growable per-instance vertex buffer attached to the mesh VAO
/// (locations <see cref="LocTransform"/>..+3 = mat4, <see cref="LocData"/> = vec4, divisor 1).
/// Fill <see cref="Add"/> each frame, then <see cref="Draw"/> with an instancing shader bound.
/// </summary>
public sealed unsafe class InstancedMesh : IDisposable
{
    public const uint LocTransform = 10;
    public const uint LocData = 14;
    private const int GlFloat = 0x1406;

    public Mesh Mesh;
    private uint _vbo;
    private int _capacity;
    private InstanceData[] _items = new InstanceData[16];
    public int Count;

    public InstancedMesh(Mesh mesh) => Mesh = mesh;

    public void Clear() => Count = 0;

    public void Add(in Matrix4x4 model, Vector4 data)
    {
        if (Count == _items.Length) Array.Resize(ref _items, _items.Length * 2);
        ref var it = ref _items[Count++];
        it.Model = model;
        it.Data = data;
    }

    private void EnsureBuffer(int count)
    {
        if (count <= _capacity) return;
        int cap = Math.Max(64, _capacity);
        while (cap < count) cap *= 2;
        if (_vbo != 0) Rlgl.UnloadVertexBuffer(_vbo);
        _capacity = cap;
        int stride = sizeof(InstanceData);
        Rlgl.EnableVertexArray(Mesh.VaoId);
        _vbo = Rlgl.LoadVertexBuffer(null, cap * stride, true);
        Rlgl.EnableVertexBuffer(_vbo);
        for (uint i = 0; i < 4; i++)
        {
            Rlgl.EnableVertexAttribute(LocTransform + i);
            Rlgl.SetVertexAttribute(LocTransform + i, 4, GlFloat, false, stride, (int)(i * 16));
            Rlgl.SetVertexAttributeDivisor(LocTransform + i, 1);
        }
        Rlgl.EnableVertexAttribute(LocData);
        Rlgl.SetVertexAttribute(LocData, 4, GlFloat, false, stride, 64);
        Rlgl.SetVertexAttributeDivisor(LocData, 1);
        Rlgl.DisableVertexBuffer();
        Rlgl.DisableVertexArray();
    }

    /// <summary>Uploads the instances and issues one instanced draw. The caller binds the shader + uniforms.</summary>
    public void Draw()
    {
        if (Count == 0) return;
        EnsureBuffer(Count);
        fixed (InstanceData* p = _items)
            Rlgl.UpdateVertexBuffer(_vbo, p, Count * sizeof(InstanceData), 0);
        Rlgl.EnableVertexArray(Mesh.VaoId);
        Rlgl.DrawVertexArrayElementsInstanced(0, Mesh.TriangleCount * 3, null, Count);
        Rlgl.DisableVertexArray();
    }

    /// <summary>Draws the instances already uploaded by the last <see cref="Draw"/> (e.g. for a second pass).</summary>
    public void DrawAgain()
    {
        if (Count == 0 || _vbo == 0) return;
        Rlgl.EnableVertexArray(Mesh.VaoId);
        Rlgl.DrawVertexArrayElementsInstanced(0, Mesh.TriangleCount * 3, null, Count);
        Rlgl.DisableVertexArray();
    }

    public void Dispose()
    {
        if (_vbo != 0) Rlgl.UnloadVertexBuffer(_vbo);
        _vbo = 0;
        Raylib.UnloadMesh(Mesh);
    }
}
