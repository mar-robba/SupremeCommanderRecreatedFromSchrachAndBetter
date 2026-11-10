using System.Numerics;
using Raylib_cs;
using Rts.Sim.Map;

namespace Rts.Client.Rendering;

/// <summary>Particle shapes understood by the particle fragment shader.</summary>
public static class PShape
{
    public const float Glow = 0, Smoke = 1, Ring = 2, Streak = 3, Disk = 4, Scorch = 5, Fire = 6, Chunk = 7;
}

public enum PMode : byte { Billboard, Flat, Streak }

public enum PBlend : byte { Additive, Alpha, Decal }

public struct Particle
{
    public Vector3 Pos, Vel;
    public float Age, Life;
    public float Size0, Size1;
    public float Drag, Gravity;
    public float Rot, RotVel;
    public float Seed;
    /// <summary>Streak length in seconds of velocity (PMode.Streak).</summary>
    public float Stretch;
    public Vector4 C0, C1;
    public float Shape;
    public PMode Mode;
    public PBlend Blend;
    /// <summary>Stops on the ground (debris).</summary>
    public bool Collide;
}

/// <summary>
/// Pooled CPU particle system (swap-remove arrays, no allocations per frame) rendered as quads in three
/// batches: ground decals (alpha), smoke/debris (alpha), fire/sparks/flashes (additive).
/// Spawner helpers build the effect recipes used for sim EffectEvents.
/// </summary>
public sealed class ParticleSystem : IDisposable
{
    public const int Capacity = 60000;
    private readonly Particle[] _p = new Particle[Capacity];
    private int _count;
    private readonly Terrain _terrain;
    private uint _rng = 0x9E3779B9u;
    public readonly QuadBatch Additive, Alpha, Decals;

    public int Count => _count;
    /// <summary>0..1 multiplier on optional particles (reduced when zoomed out or near capacity).</summary>
    public float Lod = 1f;
    /// <summary>User setting (Settings → Graphics → Particles): multiplier folded into <see cref="Lod"/> each frame.</summary>
    public static float Quality = 1f;

    public ParticleSystem(Terrain terrain)
    {
        _terrain = terrain;
        Additive = new QuadBatch(Shaders.Particle.Shader);
        Alpha = new QuadBatch(Shaders.Particle.Shader);
        Decals = new QuadBatch(Shaders.Particle.Shader);
    }

    // ------------------------------------------------------------------ random helpers (client only, not deterministic)

    public float Rand()
    {
        _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
        return (_rng & 0xFFFFFF) / 16777216f;
    }

    public float Range(float a, float b) => a + (b - a) * Rand();

    public Vector3 RandDir()
    {
        while (true)
        {
            var v = new Vector3(Rand() * 2 - 1, Rand() * 2 - 1, Rand() * 2 - 1);
            float l = v.LengthSquared();
            if (l > 0.01f && l <= 1f) return v / MathF.Sqrt(l);
        }
    }

    public Vector3 RandUpDir(float minY = 0.2f)
    {
        var d = RandDir();
        d.Y = MathF.Abs(d.Y) * (1 - minY) + minY;
        return Vector3.Normalize(d);
    }

    private int Num(float n) => Math.Max(1, (int)MathF.Round(n * Lod));

    public float GroundAt(Vector3 p) => MathF.Max(_terrain.GetHeight(p.X, p.Z), _terrain.WaterLevel);

    // ------------------------------------------------------------------ emission

    public ref Particle Emit()
    {
        if (_count >= Capacity) return ref _p[Capacity - 1];
        ref var p = ref _p[_count++];
        p = default;
        p.Seed = Rand();
        p.Rot = Rand() * MathF.Tau;
        p.C1 = new Vector4(-1);
        return ref p;
    }

    private static Vector4 C(float r, float g, float b, float a = 1) => new(r, g, b, a);

    public void Flash(Vector3 pos, float size, Vector4 color, float life = 0.12f)
    {
        ref var p = ref Emit();
        p.Pos = pos; p.Life = life; p.Size0 = size; p.Size1 = size * 0.4f;
        p.C0 = color; p.C1 = color with { W = 0 }; p.Shape = PShape.Glow; p.Blend = PBlend.Additive;
    }

    public void Sparks(Vector3 pos, int n, float speed, Vector4 color, float life = 0.45f, float size = 0.12f, float gravity = 9f, Vector3? bias = null)
    {
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Emit();
            var d = RandUpDir(0.1f);
            if (bias is Vector3 b) d = Vector3.Normalize(d + b);
            p.Pos = pos; p.Vel = d * speed * Range(0.4f, 1.1f);
            p.Life = life * Range(0.5f, 1.2f); p.Size0 = size; p.Size1 = size * 0.5f;
            p.Gravity = gravity; p.Drag = 1.2f; p.Stretch = 0.06f;
            p.C0 = color; p.C1 = color with { W = 0 }; p.Shape = PShape.Streak; p.Mode = PMode.Streak; p.Blend = PBlend.Additive;
        }
    }

    public void Smoke(Vector3 pos, float size, float life, Vector4 color, Vector3 vel = default, float growth = 2.2f)
    {
        ref var p = ref Emit();
        p.Pos = pos; p.Vel = vel + new Vector3(Range(-0.3f, 0.3f), Range(0.3f, 0.8f), Range(-0.3f, 0.3f)) * size;
        p.Life = life * Range(0.7f, 1.3f); p.Size0 = size; p.Size1 = size * growth;
        p.Drag = 1.5f; p.RotVel = Range(-0.6f, 0.6f);
        p.C0 = color; p.C1 = color with { W = 0 }; p.Shape = PShape.Smoke; p.Blend = PBlend.Alpha;
    }

    public void Fireball(Vector3 pos, float size, float life)
    {
        ref var p = ref Emit();
        p.Pos = pos; p.Vel = RandDir() * size * 1.2f + new Vector3(0, size * 0.8f, 0);
        p.Life = life * Range(0.7f, 1.2f); p.Size0 = size * Range(0.6f, 1.0f); p.Size1 = size * 1.6f;
        p.Drag = 3f; p.RotVel = Range(-1, 1);
        p.C0 = C(1.0f, 0.62f, 0.22f, 1); p.C1 = C(0.55f, 0.12f, 0.03f, 0);
        p.Shape = PShape.Fire; p.Blend = PBlend.Additive;
    }

    public void Ring(Vector3 pos, float size0, float size1, float life, Vector4 color)
    {
        ref var p = ref Emit();
        p.Pos = pos + new Vector3(0, 0.12f, 0); p.Life = life; p.Size0 = size0; p.Size1 = size1;
        p.C0 = color; p.C1 = color with { W = 0 }; p.Shape = PShape.Ring; p.Mode = PMode.Flat; p.Blend = PBlend.Additive;
    }

    public void Scorch(Vector3 pos, float size)
    {
        ref var p = ref Emit();
        p.Pos = new Vector3(pos.X, GroundAt(pos) + 0.07f, pos.Z); p.Life = 35f; p.Size0 = size; p.Size1 = size;
        p.C0 = C(0.05f, 0.04f, 0.035f, 0.85f); p.C1 = C(0.05f, 0.04f, 0.035f, 0);
        p.Shape = PShape.Scorch; p.Mode = PMode.Flat; p.Blend = PBlend.Decal;
    }

    public void Debris(Vector3 pos, int n, float speed, float size)
    {
        for (int i = 0; i < n; i++)
        {
            ref var p = ref Emit();
            p.Pos = pos; p.Vel = RandUpDir(0.45f) * speed * Range(0.5f, 1.1f);
            p.Life = Range(1.2f, 2.2f); p.Size0 = size * Range(0.5f, 1.1f); p.Size1 = p.Size0;
            p.Gravity = 14f; p.Drag = 0.3f; p.RotVel = Range(-8, 8); p.Collide = true;
            float g = Range(0.12f, 0.25f);
            p.C0 = C(g, g, g * 1.05f, 1); p.C1 = C(g, g, g, 0);
            p.Shape = PShape.Chunk; p.Blend = PBlend.Alpha;
            // Each chunk gets a little burning glow.
            if (Rand() < 0.5f)
            {
                ref var f = ref Emit();
                f = p; f.Shape = PShape.Glow; f.Blend = PBlend.Additive; f.Size0 *= 1.6f; f.Size1 = 0;
                f.C0 = C(1f, 0.55f, 0.2f, 0.9f); f.C1 = C(1f, 0.3f, 0.1f, 0); f.Life *= 0.6f;
            }
        }
    }

    // ------------------------------------------------------------------ recipes

    public void MuzzleFlash(Vector3 pos, float size, Vector4 tint)
    {
        Flash(pos, 0.9f * size, tint, 0.09f);
        Flash(pos, 0.45f * size, C(1, 1, 0.9f, 1), 0.06f);
        Sparks(pos, Num(3), 6f * size, tint, 0.15f, 0.06f, 0);
    }

    public void Impact(Vector3 pos, float size, Vector4 tint)
    {
        Flash(pos, 1.3f * size, tint, 0.12f);
        Sparks(pos, Num(7), 7f * MathF.Sqrt(size), C(1, 0.75f, 0.35f), 0.4f, 0.08f * size);
        Smoke(pos, 0.45f * size, 0.9f, C(0.32f, 0.3f, 0.28f, 0.55f));
        if (pos.Y - GroundAt(pos) < 0.6f && Rand() < Lod)
            Smoke(pos, 0.6f * size, 1.1f, C(0.45f, 0.39f, 0.3f, 0.45f), new Vector3(0, 0.5f, 0));
    }

    public void Explosion(Vector3 pos, float radius, bool ground)
    {
        radius = MathF.Max(0.4f, radius);
        Flash(pos + new Vector3(0, 0.3f, 0), radius * 4.5f, C(1, 0.8f, 0.5f, 0.9f), 0.18f);
        int nf = Num(4 + radius * 3);
        for (int i = 0; i < nf; i++) Fireball(pos + RandDir() * radius * 0.4f, radius * 1.1f, 0.55f + radius * 0.06f);
        Sparks(pos, Num(10 + radius * 6), 9f * MathF.Sqrt(radius), C(1, 0.7f, 0.3f), 0.7f, 0.1f);
        int ns = Num(3 + radius * 2);
        for (int i = 0; i < ns; i++)
            Smoke(pos + RandDir() * radius * 0.5f + new Vector3(0, radius * 0.4f, 0), radius * 1.0f, 2.2f + radius * 0.2f, C(0.27f, 0.26f, 0.25f, 0.45f), new Vector3(0, 1.2f, 0), 2.2f);
        if (ground)
        {
            Ring(new Vector3(pos.X, GroundAt(pos), pos.Z), radius * 0.5f, radius * 5f, 0.4f, C(1f, 0.8f, 0.55f, 0.35f));
            Scorch(pos, radius * Range(2.4f, 3.2f));
        }
    }

    public void UnitDeath(Vector3 pos, float size, bool air)
    {
        bool ground = !air && pos.Y - GroundAt(pos) < 1.5f;
        Explosion(pos + new Vector3(0, size * 0.3f, 0), size * 0.75f, ground);
        Debris(pos + new Vector3(0, size * 0.3f, 0), Num(4 + size * 3), 6f + size * 2f, 0.12f + size * 0.07f);
        int col = Num(3 + size * 2);
        for (int i = 0; i < col; i++)
        {
            ref var p = ref Emit();
            p.Pos = pos + new Vector3(Range(-0.3f, 0.3f) * size, 0.2f, Range(-0.3f, 0.3f) * size);
            p.Vel = new Vector3(Range(-0.2f, 0.2f), Range(1.0f, 2.2f), Range(-0.2f, 0.2f));
            p.Age = -Range(0f, 1.2f); // delayed start: lingering smoke column
            p.Life = Range(3f, 5f); p.Size0 = size * 0.6f; p.Size1 = size * 2.6f; p.Drag = 0.4f; p.RotVel = Range(-0.4f, 0.4f);
            p.C0 = C(0.24f, 0.23f, 0.22f, 0.36f); p.C1 = C(0.4f, 0.4f, 0.4f, 0);
            p.Shape = PShape.Smoke; p.Blend = PBlend.Alpha;
        }
    }

    public void BuildStarted(Vector3 pos, float size)
    {
        Ring(new Vector3(pos.X, GroundAt(pos), pos.Z), size * 0.3f, size * 2.2f, 0.7f, C(0.4f, 0.85f, 1f, 0.8f));
        for (int i = 0; i < Num(10); i++)
        {
            ref var p = ref Emit();
            p.Pos = pos + new Vector3(Range(-0.5f, 0.5f) * size, Range(0, 0.4f), Range(-0.5f, 0.5f) * size);
            p.Vel = new Vector3(0, Range(1f, 2.5f), 0); p.Life = Range(0.5f, 1f); p.Size0 = 0.25f; p.Size1 = 0.05f;
            p.C0 = C(0.5f, 0.9f, 1f, 1f); p.C1 = C(0.3f, 0.6f, 1f, 0); p.Shape = PShape.Glow; p.Blend = PBlend.Additive;
        }
    }

    public void BuildComplete(Vector3 pos, float size)
    {
        Flash(pos + new Vector3(0, size * 0.4f, 0), size * 2.5f, C(0.6f, 0.9f, 1f, 0.9f), 0.35f);
        Ring(new Vector3(pos.X, GroundAt(pos), pos.Z), size * 0.5f, size * 3.5f, 0.6f, C(0.6f, 0.95f, 1f, 0.9f));
        for (int i = 0; i < Num(16); i++)
        {
            ref var p = ref Emit();
            p.Pos = pos + new Vector3(Range(-0.5f, 0.5f) * size, Range(0, 0.8f) * size, Range(-0.5f, 0.5f) * size);
            p.Vel = new Vector3(Range(-0.3f, 0.3f), Range(2f, 4f), Range(-0.3f, 0.3f)); p.Drag = 1f;
            p.Life = Range(0.6f, 1.2f); p.Size0 = 0.3f; p.Size1 = 0.05f;
            p.C0 = C(0.8f, 0.97f, 1f, 1f); p.C1 = C(0.3f, 0.7f, 1f, 0); p.Shape = PShape.Glow; p.Blend = PBlend.Additive;
        }
    }

    public void Reclaim(Vector3 pos, float size)
    {
        for (int i = 0; i < Num(8); i++)
        {
            ref var p = ref Emit();
            p.Pos = pos + new Vector3(Range(-0.5f, 0.5f) * size, Range(0, 0.3f), Range(-0.5f, 0.5f) * size);
            p.Vel = new Vector3(Range(-0.2f, 0.2f), Range(0.8f, 1.8f), Range(-0.2f, 0.2f));
            p.Life = Range(0.7f, 1.4f); p.Size0 = 0.3f; p.Size1 = 0.08f; p.RotVel = 2;
            p.C0 = C(0.35f, 1f, 0.45f, 0.9f); p.C1 = C(0.2f, 0.8f, 0.3f, 0); p.Shape = PShape.Glow; p.Blend = PBlend.Additive;
        }
    }

    public void FlakBurst(Vector3 pos)
    {
        Flash(pos, 1.6f, C(1f, 0.6f, 0.25f, 0.9f), 0.1f);
        Smoke(pos, 0.7f, 1.4f, C(0.12f, 0.12f, 0.12f, 0.75f), default, 2.0f);
        Sparks(pos, Num(5), 5f, C(1f, 0.7f, 0.3f), 0.3f, 0.06f, 2f);
    }

    // ------------------------------------------------------------------ simulation + rendering

    public void Update(float dt)
    {
        if (dt <= 0) return;
        for (int i = 0; i < _count;)
        {
            ref var p = ref _p[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _p[i] = _p[--_count];
                continue;
            }
            if (p.Age >= 0)
            {
                if (p.Drag > 0) p.Vel *= MathF.Max(0, 1 - p.Drag * dt);
                p.Vel.Y -= p.Gravity * dt;
                p.Pos += p.Vel * dt;
                p.Rot += p.RotVel * dt;
                if (p.Collide)
                {
                    float g = GroundAt(p.Pos);
                    if (p.Pos.Y < g) { p.Pos.Y = g; p.Vel *= 0.3f; p.Vel.Y = MathF.Abs(p.Vel.Y) * 0.3f; p.RotVel *= 0.5f; }
                }
            }
            i++;
        }
    }

    /// <summary>Instanced quads of the pool particles, one buffer per blend mode (filled by <see cref="Build"/>).
    /// The <see cref="QuadBatch"/>es above stay for free-form quads (nuke decals draped on the terrain).</summary>
    public readonly ParticleInstances InstAdditive = new(), InstAlpha = new(), InstDecals = new();

    /// <summary>Writes one instance per live, visible particle (the GPU expands it into its quad).</summary>
    public void Build(FrameView fv)
    {
        InstAdditive.Clear();
        InstAlpha.Clear();
        InstDecals.Clear();
        var planes = fv;
        for (int i = 0; i < _count; i++)
        {
            ref var p = ref _p[i];
            if (p.Age < 0) continue;
            float t = p.Age / p.Life;
            float size = p.Size0 + (p.Size1 - p.Size0) * (1 - (1 - t) * (1 - t));
            float sp = 0;
            if (p.Mode == PMode.Streak) sp = p.Vel.Length();
            if (!planes.SphereVisible(p.Pos, size + sp * p.Stretch)) continue;
            var c4 = p.C1.X < 0 ? p.C0 : Vector4.Lerp(p.C0, p.C1, t);
            c4 = Vector4.Clamp(c4 * 255f, Vector4.Zero, new Vector4(255f));
            var list = p.Blend switch { PBlend.Additive => InstAdditive, PBlend.Alpha => InstAlpha, _ => InstDecals };
            ref var q = ref list.Add();
            q.Col = new Color((byte)c4.X, (byte)c4.Y, (byte)c4.Z, (byte)c4.W);
            if (p.Mode == PMode.Streak && sp >= 1e-3f)
            {
                var dir = p.Vel / sp;
                q.PosSize = new Vector4(p.Pos, size);
                q.Dir = new Vector4(dir, size * 2 + sp * p.Stretch);
                q.Params = new Vector4(p.Shape, p.Seed, (float)PMode.Streak, 0);
            }
            else
            {
                q.PosSize = new Vector4(p.Pos, size);
                q.Dir = default;
                // A streak that stopped is drawn as a small glow (as QuadBatch did).
                bool stopped = p.Mode == PMode.Streak;
                q.Params = new Vector4(stopped ? PShape.Glow : p.Shape, p.Seed, stopped ? 0f : (float)p.Mode, stopped ? 0f : p.Rot);
            }
        }
    }

    public void Dispose()
    {
        InstAdditive.Dispose();
        InstAlpha.Dispose();
        InstDecals.Dispose();
        Additive.Dispose();
        Alpha.Dispose();
        Decals.Dispose();
    }
}
