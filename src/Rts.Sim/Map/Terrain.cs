using System.Numerics;
using Rts.Sim.Data;

namespace Rts.Sim.Map;

public enum DepositType : byte { Mass, Hydrocarbon }

public readonly record struct Deposit(Vector2 Pos, DepositType Type);

/// <summary>Per-cell static traversability bits (derived from slope and water).</summary>
[Flags]
public enum PassFlags : byte
{
    None = 0,
    /// <summary>Dry land with acceptable slope.</summary>
    Land = 1,
    /// <summary>Water deep enough for ships.</summary>
    DeepWater = 2,
    /// <summary>Any water surface (hover units can cross).</summary>
    WaterSurface = 4,
    /// <summary>Gentle slope ignoring water (amphibious units walk on the seabed).</summary>
    Seabed = 8,
}

/// <summary>
/// Heightmap terrain. World is the (x, z) plane with y up; the map spans [0, Width] x [0, Height].
/// One cell = 1x1 world units; heights are stored at cell corners ((Width+1) x (Height+1) vertices).
/// IMMUTABLE after construction: safe to read from any thread (sim and render).
/// Dynamic blocking by structures lives in <see cref="World.OccupancyGrid"/>, not here.
/// </summary>
public sealed class Terrain
{
    public readonly int Width;
    public readonly int Height;
    public readonly float[] Heights;
    public readonly float WaterLevel;
    public readonly PassFlags[] Pass;
    public readonly Deposit[] Deposits;
    public readonly Vector2[] StartPositions;
    public readonly int Seed;

    /// <summary>Max rise/run per cell for land passability.</summary>
    public const float DefaultMaxSlope = 0.75f;
    /// <summary>Minimum water depth for ships.</summary>
    public const float MinShipDepth = 1.0f;

    public Terrain(int width, int height, float[] heights, float waterLevel, Deposit[] deposits, Vector2[] startPositions, int seed)
    {
        if (heights.Length != (width + 1) * (height + 1)) throw new ArgumentException("heights size mismatch");
        Width = width;
        Height = height;
        Heights = heights;
        WaterLevel = waterLevel;
        Deposits = deposits;
        StartPositions = startPositions;
        Seed = seed;
        Pass = new PassFlags[width * height];
        ComputePassability();
    }

    public int VertexStride => Width + 1;

    public float VertexHeight(int vx, int vz)
    {
        vx = Math.Clamp(vx, 0, Width);
        vz = Math.Clamp(vz, 0, Height);
        return Heights[vz * (Width + 1) + vx];
    }

    /// <summary>Bilinear ground height at a world position (clamped to the map).</summary>
    public float GetHeight(float x, float z)
    {
        x = Math.Clamp(x, 0, Width);
        z = Math.Clamp(z, 0, Height);
        int x0 = Math.Min((int)x, Width - 1), z0 = Math.Min((int)z, Height - 1);
        float fx = x - x0, fz = z - z0;
        float h00 = VertexHeight(x0, z0), h10 = VertexHeight(x0 + 1, z0);
        float h01 = VertexHeight(x0, z0 + 1), h11 = VertexHeight(x0 + 1, z0 + 1);
        return (h00 * (1 - fx) + h10 * fx) * (1 - fz) + (h01 * (1 - fx) + h11 * fx) * fz;
    }

    public float GetHeight(Vector2 p) => GetHeight(p.X, p.Y);

    /// <summary>Height of the surface units stand on: ground or water surface, whichever is higher.</summary>
    public float GetSurfaceHeight(Vector2 p) => MathF.Max(GetHeight(p.X, p.Y), WaterLevel);

    public Vector3 GetNormal(float x, float z)
    {
        const float e = 0.5f;
        float hl = GetHeight(x - e, z), hr = GetHeight(x + e, z);
        float hd = GetHeight(x, z - e), hu = GetHeight(x, z + e);
        return Vector3.Normalize(new Vector3(hl - hr, 2 * e, hd - hu));
    }

    public bool IsUnderwater(Vector2 p) => GetHeight(p.X, p.Y) < WaterLevel;

    public bool InBounds(Vector2 p) => p.X >= 0 && p.Y >= 0 && p.X <= Width && p.Y <= Height;

    public Vector2 Clamp(Vector2 p, float margin = 0.5f) =>
        new(Math.Clamp(p.X, margin, Width - margin), Math.Clamp(p.Y, margin, Height - margin));

    public int CellIndex(int cx, int cz) => cz * Width + cx;

    public bool CellInBounds(int cx, int cz) => cx >= 0 && cz >= 0 && cx < Width && cz < Height;

    public PassFlags PassAt(int cx, int cz) => CellInBounds(cx, cz) ? Pass[cz * Width + cx] : PassFlags.None;

    /// <summary>Static terrain traversability of a cell for a motion layer (ignores structures).</summary>
    public bool CanTraverse(MotionLayer layer, int cx, int cz)
    {
        if (!CellInBounds(cx, cz)) return false;
        var p = Pass[cz * Width + cx];
        return layer switch
        {
            MotionLayer.Air => true,
            MotionLayer.Land => (p & PassFlags.Land) != 0,
            MotionLayer.Hover => (p & (PassFlags.Land | PassFlags.WaterSurface)) != 0,
            MotionLayer.Amphibious => (p & (PassFlags.Land | PassFlags.Seabed)) != 0,
            MotionLayer.Water or MotionLayer.Sub => (p & PassFlags.DeepWater) != 0,
            _ => false,
        };
    }

    private void ComputePassability()
    {
        for (int cz = 0; cz < Height; cz++)
        for (int cx = 0; cx < Width; cx++)
        {
            float h00 = VertexHeight(cx, cz), h10 = VertexHeight(cx + 1, cz);
            float h01 = VertexHeight(cx, cz + 1), h11 = VertexHeight(cx + 1, cz + 1);
            float min = MathF.Min(MathF.Min(h00, h10), MathF.Min(h01, h11));
            float max = MathF.Max(MathF.Max(h00, h10), MathF.Max(h01, h11));
            float slope = max - min;
            var f = PassFlags.None;
            bool gentle = slope <= DefaultMaxSlope;
            if (gentle) f |= PassFlags.Seabed;
            if (gentle && min >= WaterLevel) f |= PassFlags.Land;
            if (min < WaterLevel) f |= PassFlags.WaterSurface;
            if (max < WaterLevel - MinShipDepth) f |= PassFlags.DeepWater;
            Pass[cz * Width + cx] = f;
        }
    }
}
