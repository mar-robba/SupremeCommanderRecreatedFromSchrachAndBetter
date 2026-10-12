using System.Numerics;
using Raylib_cs;
using Rts.Sim.Map;

namespace Rts.Client.Rendering;

/// <summary>
/// Strategic nuke detonation (sim <c>EffectKind.Nuke</c>, Size = outer damage radius S), SupCom-style, ~10 s of
/// action scaled by S: blinding flash (huge additive glow + full-screen overlay, <see cref="DrawOverlay"/>),
/// terrain-conforming ground light and shockwave rings (bright fast ring to ~2S, slower dust ring), a rising fireball
/// that becomes a mushroom cloud (stem column + rolling cap of fire-lit smoke), dust skirt and base surge, sparks and
/// burning debris, a long-lived scorch decal with a molten core, and lingering smoke drifting away.
/// <para>Owned by <see cref="EffectsRenderer"/>: <see cref="Spawn"/> on the event, <see cref="Update"/> with game dt,
/// <see cref="Build"/> inside the particle pass (adds quads to the shared particle batches), <see cref="DrawOverlay"/>
/// in 2D after EndMode3D. Smoke puffs live in a private pool (sorted back-to-front and shaded every frame by height in
/// the cloud, sun and fire glow); fire/sparks/debris go to the shared <see cref="ParticleSystem"/>. Ground layers are
/// grids draped on the terrain so big rings/decals are not cut by hills.</para>
/// <para>Budget per blast at peak: ~600 private puffs (pool capped at <see cref="MaxPuffs"/>) + ~500 shared particles,
/// a few thousand quads for the draped layers.</para>
/// </summary>
public sealed class NukeFx
{
    public const int MaxPuffs = 4000;
    /// <summary>Seconds a blast stays alive (scorch decal lifetime).</summary>
    public const float Lifetime = 90f;
    /// <summary>Rise time constant of the fireball / cap (s). Puffs use drag 1/Tau so they track the exponential rise.</summary>
    private const float Tau = 2.2f;
    private static readonly Vector3 Up = Vector3.UnitY;
    private static readonly Vector3 Wind = new(1.1f, 0, 0.45f);

    private struct Blast
    {
        public Vector3 Pos;
        public float S, Age, Seed;
        public float CapAcc, StemAcc, FireAcc, DustAcc, BaseAcc;
    }

    private enum PuffKind : byte { Cap, Stem, Dust }

    private struct Puff
    {
        public Vector3 Pos, Vel, Ground;
        public float Age, Life, Size0, Size1, Drag, Rot, RotVel, Seed, Alpha;
        /// <summary>Blast age at emission and blast size (for per-frame shading).</summary>
        public float T0, S;
        public PuffKind Kind;
    }

    private readonly ParticleSystem _ps;
    private readonly Terrain _terrain;
    private readonly List<Blast> _blasts = new();
    private readonly Puff[] _puffs = new Puff[MaxPuffs];
    private readonly float[] _keys = new float[MaxPuffs];
    private readonly int[] _order = new int[MaxPuffs];
    private int _count;
    private float _flash;

    public NukeFx(ParticleSystem ps, Terrain terrain)
    {
        _ps = ps;
        _terrain = terrain;
    }

    public int ActiveBlasts => _blasts.Count;
    public int PuffCount => _count;
    /// <summary>Full-screen flash level 0..1 (decays in real time in <see cref="DrawOverlay"/>).</summary>
    public float FlashLevel => _flash;

    // ------------------------------------------------------------------ shape of the cloud over time

    private static float Rise(float t) => 1f - MathF.Exp(-MathF.Max(0, t) / Tau);
    private static float RiseRate(float t) => MathF.Exp(-MathF.Max(0, t) / Tau) / Tau;
    private static float CapHeight(float t, float s) => s * (0.12f + 1.13f * Rise(t));
    private static float CapRadius(float t, float s) => s * (0.16f + 0.46f * Rise(t));
    private static float CapTube(float t, float s) => s * (0.11f + 0.2f * Rise(t));
    private static float FireLevel(float t) => t < 0.3f ? 1f : MathF.Exp(-(t - 0.3f) / 1.9f);
    private static float DustRadius(float t, float s) => 1.7f * s * EaseOut(t / 3.5f, 2f);

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private static float EaseOut(float x, float p) => 1f - MathF.Pow(1f - Math.Clamp(x, 0f, 1f), p);

    private static Color Col(Vector3 c, float a) =>
        new((byte)Math.Clamp(c.X * 255, 0, 255), (byte)Math.Clamp(c.Y * 255, 0, 255), (byte)Math.Clamp(c.Z * 255, 0, 255), (byte)Math.Clamp(a * 255, 0, 255));

    private float Lod => Math.Clamp(_ps.Lod, 0.5f, 1f);
    private int N(float n) => Math.Max(1, (int)(n * Lod));

    // ------------------------------------------------------------------ spawn / update

    /// <summary>Starts a detonation at a ground point (y = surface height); size = outer damage radius.</summary>
    public void Spawn(Vector3 pos, float size)
    {
        float s = Math.Clamp(size > 0 ? size : 40f, 8f, 200f);
        _blasts.Add(new Blast { Pos = pos, S = s, Seed = _ps.Rand() });
        // Screen flash: full when the blast is in view, dimmer (the sky lights up) when off-screen.
        float vis = FrameView.Current.SphereVisible(pos + new Vector3(0, s * 0.5f, 0), s * 1.5f) ? 1f : 0.45f;
        _flash = MathF.Max(_flash, vis);

        // Initial fireball: hot blobs expanding from the ground and rising with the cap.
        int nf = N(36);
        float h0 = CapHeight(0, s);
        for (int i = 0; i < nf; i++)
        {
            ref var p = ref _ps.Emit();
            var d = _ps.RandDir();
            p.Pos = pos + new Vector3(d.X * s * 0.08f, h0 * 0.6f + MathF.Abs(d.Y) * s * 0.08f, d.Z * s * 0.08f);
            p.Vel = d * s * _ps.Range(0.08f, 0.18f) + Up * (s * 1.13f * RiseRate(0));
            p.Life = _ps.Range(1.0f, 1.8f); p.Size0 = s * _ps.Range(0.25f, 0.4f); p.Size1 = s * _ps.Range(0.5f, 0.7f);
            p.Drag = 1f / Tau; p.RotVel = _ps.Range(-0.8f, 0.8f);
            p.C0 = new Vector4(1f, 0.92f, 0.75f, 1f); p.C1 = new Vector4(0.85f, 0.28f, 0.06f, 0f);
            p.Shape = PShape.Fire; p.Blend = PBlend.Additive;
        }
        // Sparks and burning debris thrown out of the blast.
        _ps.Sparks(pos + Up * 0.8f, N(150), s * 0.95f, new Vector4(1f, 0.85f, 0.5f, 1f), 1.7f, s * 0.012f + 0.1f, 12f, Up * 0.35f);
        _ps.Debris(pos + Up, N(46), s * 0.8f, s * 0.018f + 0.15f);
    }

    /// <summary>Advances blasts and smoke puffs by game time (dt = 0 while paused).</summary>
    public void Update(float dt)
    {
        if (dt <= 0) return;
        for (int i = _blasts.Count - 1; i >= 0; i--)
        {
            var b = _blasts[i];
            b.Age += dt;
            if (b.Age > Lifetime) { _blasts.RemoveAt(i); continue; }
            Emit(ref b, dt);
            _blasts[i] = b;
        }
        for (int i = 0; i < _count;)
        {
            ref var p = ref _puffs[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _puffs[i] = _puffs[--_count];
                continue;
            }
            if (p.Drag > 0) p.Vel *= MathF.Max(0, 1 - p.Drag * dt);
            float wind = p.Kind == PuffKind.Dust ? 0.4f : Smooth(3f, 10f, p.T0 + p.Age);
            p.Pos += (p.Vel + Wind * wind) * dt;
            p.Rot += p.RotVel * dt;
            i++;
        }
    }

    private void Emit(ref Blast b, float dt)
    {
        float t = b.Age;
        if (t > 10.5f) return; // only the decal remains
        float lod = Lod;
        float taper = 1f - Smooth(7f, 10f, t);
        float f = FireLevel(t);
        if (t > 0.15f && taper > 0)
        {
            b.CapAcc += dt * 210f * lod * taper;
            for (; b.CapAcc >= 1; b.CapAcc--) EmitCap(b, t);
        }
        if (t > 0.35f && taper > 0)
        {
            b.StemAcc += dt * 40f * lod * taper;
            for (; b.StemAcc >= 1; b.StemAcc--) EmitStem(b, t);
        }
        if (f > 0.04f)
        {
            b.FireAcc += dt * 55f * lod * f;
            for (; b.FireAcc >= 1; b.FireAcc--) EmitFire(b, t, f);
        }
        if (t < 1.6f)
        {
            b.DustAcc += dt * 60f * lod;
            for (; b.DustAcc >= 1; b.DustAcc--) EmitDust(b, t);
        }
        if (t > 0.6f && t < 8f)
        {
            b.BaseAcc += dt * 10f * lod;
            for (; b.BaseAcc >= 1; b.BaseAcc--) EmitBase(b, t);
        }
    }

    private Vector3 RandFlatDir()
    {
        float phi = _ps.Rand() * MathF.Tau;
        return new Vector3(MathF.Cos(phi), 0, MathF.Sin(phi));
    }

    private bool NewPuff(out int index)
    {
        index = _count;
        if (_count >= MaxPuffs) return false;
        _puffs[_count++] = default;
        return true;
    }

    /// <summary>Cap puff: on a rolling torus (outer bottom, outer side, top) or on the dome closing its top. Velocity
    /// = rise + self-similar expansion + toroidal roll, all decaying with drag 1/Tau like the cap itself.</summary>
    private void EmitCap(in Blast b, float t)
    {
        if (!NewPuff(out int i)) return;
        ref var p = ref _puffs[i];
        float s = b.S, h = CapHeight(t, s), rad = CapRadius(t, s), r = CapTube(t, s), rr = RiseRate(t);
        var dir = RandFlatDir();
        float radial, y;
        Vector3 roll;
        if (_ps.Rand() < 0.62f)
        {
            float th = _ps.Range(-0.55f, 0.9f) * MathF.PI; // 0 = outward, pi/2 = top
            radial = rad + r * MathF.Cos(th);
            y = r * MathF.Sin(th);
            roll = dir * MathF.Sin(th) - Up * MathF.Cos(th); // up the middle, out over the top, down the outside
        }
        else
        {
            float q = MathF.Pow(_ps.Rand(), 0.7f);
            radial = rad * q;
            y = r * (1.05f - 0.2f * q * q);
            roll = dir * q;
        }
        p.Ground = b.Pos;
        p.Pos = b.Pos + dir * radial + Up * (h + y) + _ps.RandDir() * r * 0.15f;
        float linger = Smooth(3.5f, 8f, t);
        p.Vel = Up * (s * 1.13f * rr) + dir * (s * 0.46f * rr * radial / MathF.Max(1e-3f, rad)) + roll * (r * 0.22f)
              + new Vector3(_ps.Range(-1f, 1f), _ps.Range(-0.3f, 0.6f), _ps.Range(-1f, 1f)) * (s * 0.05f * linger); // late puffs break up
        p.Drag = 1f / Tau;
        p.Life = (1.8f + 5.5f * linger) * _ps.Range(0.8f, 1.2f);
        p.Size0 = r * _ps.Range(0.7f, 0.95f);
        p.Size1 = r * _ps.Range(1.15f, 1.45f) * (1f + linger * 0.9f);
        p.Rot = _ps.Rand() * MathF.Tau; p.RotVel = _ps.Range(-0.3f, 0.3f); p.Seed = _ps.Rand();
        p.Alpha = _ps.Range(0.85f, 1f);
        p.T0 = t; p.S = s; p.Kind = PuffKind.Cap;
    }

    /// <summary>Stem puff: column from the ground into the cap, wider at the foot, drifting up (never past the cap).</summary>
    private void EmitStem(in Blast b, float t)
    {
        if (!NewPuff(out int i)) return;
        ref var p = ref _puffs[i];
        float s = b.S, h = CapHeight(t, s) - CapTube(t, s) * 0.6f;
        float y = h * MathF.Pow(_ps.Rand(), 0.85f);
        float foot = 1f - y / MathF.Max(1f, h);
        float rad = s * (0.05f + 0.11f * foot * foot * foot) * MathF.Sqrt(_ps.Rand());
        var dir = RandFlatDir();
        float linger = Smooth(5f, 9f, t);
        p.Life = (1.3f + 4f * linger) * _ps.Range(0.8f, 1.2f);
        p.Ground = b.Pos;
        p.Pos = b.Pos + dir * rad + Up * y;
        p.Vel = Up * ((h - y) / p.Life * _ps.Range(0.4f, 0.8f)) - dir * (rad * 0.15f);
        float w = s * (0.09f + 0.09f * foot);
        p.Size0 = w * _ps.Range(0.8f, 1.1f); p.Size1 = w * _ps.Range(1.4f, 1.8f) * (1f + linger * 0.6f);
        p.Rot = _ps.Rand() * MathF.Tau; p.RotVel = _ps.Range(-0.4f, 0.4f); p.Seed = _ps.Rand();
        p.Alpha = _ps.Range(0.6f, 0.8f);
        p.T0 = t; p.S = s; p.Kind = PuffKind.Stem;
    }

    /// <summary>Fire blob inside the cap (rising with it) or in the stem (shared additive particles).</summary>
    private void EmitFire(in Blast b, float t, float f)
    {
        ref var p = ref _ps.Emit();
        float s = b.S;
        var dir = RandFlatDir();
        if (_ps.Rand() < 0.7f)
        {
            float rad = CapRadius(t, s) * 0.85f, r = CapTube(t, s), q = MathF.Sqrt(_ps.Rand()), rr = RiseRate(t);
            p.Pos = b.Pos + dir * rad * q + Up * (CapHeight(t, s) + _ps.Range(-0.6f, 0.4f) * r);
            p.Vel = Up * (s * 1.13f * rr) + dir * (s * 0.46f * rr * q);
            p.Drag = 1f / Tau;
            p.Size0 = r * _ps.Range(1.0f, 1.5f) * (0.6f + 0.4f * f); p.Size1 = p.Size0 * 1.4f;
        }
        else
        {
            p.Pos = b.Pos + dir * s * 0.06f * MathF.Sqrt(_ps.Rand()) + Up * (CapHeight(t, s) * _ps.Range(0.03f, 0.8f));
            p.Vel = Up * s * 0.22f;
            p.Size0 = s * _ps.Range(0.1f, 0.16f); p.Size1 = p.Size0 * 1.5f;
        }
        p.Life = _ps.Range(0.6f, 1.1f); p.RotVel = _ps.Range(-1f, 1f);
        var hot = Vector3.Lerp(new Vector3(1f, 0.5f, 0.18f), new Vector3(1f, 0.88f, 0.65f), f * f);
        p.C0 = new Vector4(hot, 0.3f + 0.6f * f); p.C1 = new Vector4(0.6f, 0.12f, 0.02f, 0f);
        p.Shape = PShape.Fire; p.Blend = PBlend.Additive;
    }

    /// <summary>Dust kicked up by the shock front: low puffs riding outward behind the dust ring.</summary>
    private void EmitDust(in Blast b, float t)
    {
        if (!NewPuff(out int i)) return;
        ref var p = ref _puffs[i];
        float s = b.S;
        var dir = RandFlatDir();
        var at = b.Pos + dir * DustRadius(t, s) * _ps.Range(0.88f, 1f);
        p.Ground = b.Pos;
        p.Pos = new Vector3(at.X, _ps.GroundAt(at) + s * _ps.Range(0.02f, 0.06f), at.Z);
        p.Vel = dir * s * _ps.Range(0.12f, 0.3f) + Up * s * _ps.Range(0.02f, 0.06f);
        p.Drag = 0.6f;
        p.Life = _ps.Range(2f, 3.2f);
        p.Size0 = s * _ps.Range(0.08f, 0.12f); p.Size1 = s * _ps.Range(0.22f, 0.3f);
        p.Rot = _ps.Rand() * MathF.Tau; p.RotVel = _ps.Range(-0.4f, 0.4f); p.Seed = _ps.Rand();
        p.Alpha = _ps.Range(0.3f, 0.42f);
        p.T0 = t; p.S = s; p.Kind = PuffKind.Dust;
    }

    /// <summary>Base surge: dust rolling out slowly around the foot of the stem.</summary>
    private void EmitBase(in Blast b, float t)
    {
        if (!NewPuff(out int i)) return;
        ref var p = ref _puffs[i];
        float s = b.S;
        var dir = RandFlatDir();
        var at = b.Pos + dir * s * _ps.Range(0.15f, 0.38f);
        p.Ground = b.Pos;
        p.Pos = new Vector3(at.X, _ps.GroundAt(at) + s * _ps.Range(0.02f, 0.1f), at.Z);
        p.Vel = dir * s * _ps.Range(0.03f, 0.08f) + Up * s * _ps.Range(0.02f, 0.06f);
        p.Drag = 0.2f;
        p.Life = _ps.Range(3f, 5f);
        p.Size0 = s * _ps.Range(0.13f, 0.18f); p.Size1 = s * _ps.Range(0.3f, 0.4f);
        p.Rot = _ps.Rand() * MathF.Tau; p.RotVel = _ps.Range(-0.3f, 0.3f); p.Seed = _ps.Rand();
        p.Alpha = _ps.Range(0.3f, 0.42f);
        p.T0 = t; p.S = s; p.Kind = PuffKind.Dust;
    }

    // ------------------------------------------------------------------ rendering

    /// <summary>Adds this frame's quads to the shared particle batches (call after <see cref="ParticleSystem.Build"/>).</summary>
    public void Build(FrameView fv)
    {
        foreach (var b in _blasts) BuildBlast(b, fv);
        BuildPuffs(fv);
    }

    private void BuildBlast(in Blast b, FrameView fv)
    {
        float t = b.Age, s = b.S, f = FireLevel(t);
        var g = b.Pos;

        // Scorch: two noisy dark layers (fade in, hold, fade out).
        float sa = Smooth(0.2f, 1.2f, t) * (1f - Smooth(60f, Lifetime, t));
        if (sa > 0.01f)
        {
            Drape(_ps.Decals, fv, g, s * 1.3f, Col(new Vector3(0.05f, 0.04f, 0.035f), 0.9f * sa), PShape.Scorch, b.Seed, 0.1f);
            Drape(_ps.Decals, fv, g, s * 0.75f, Col(new Vector3(0.03f, 0.025f, 0.02f), 0.8f * sa), PShape.Scorch, b.Seed + 0.37f, 0.12f);
        }
        // Soft shadow of the cap on the ground (offset away from the sun): sells the height when seen from above.
        float shade = Smooth(0.8f, 2.5f, t) * (1f - Smooth(9f, 20f, t));
        if (shade > 0.01f)
        {
            var sun = Atmosphere.SunDir;
            float h = CapHeight(t, s);
            var sc = g + new Vector3(-sun.X, 0, -sun.Z) * (h / MathF.Max(0.2f, sun.Y));
            Drape(_ps.Decals, fv, sc, (CapRadius(t, s) + CapTube(t, s)) * 1.25f, Col(new Vector3(0.02f, 0.025f, 0.03f), 0.42f * shade), PShape.Smoke, b.Seed, 0.2f);
        }
        // Slow dust ring.
        if (t < 4f)
        {
            float a = 0.85f * MathF.Pow(1f - t / 4f, 1.1f) * Smooth(0f, 0.3f, t);
            Drape(_ps.Decals, fv, g, DustRadius(t, s) / 0.82f + 0.5f, Col(new Vector3(0.74f, 0.67f, 0.55f), a), PShape.Ring, b.Seed, 0.3f);
        }

        // Ground lit by the flash, then by the fireball.
        float gl = (t < 2.5f ? (1f - t / 2.5f) * (1f - t / 2.5f) : 0f) + 0.25f * f;
        if (gl > 0.01f) Drape(_ps.Additive, fv, g, s * 2.6f, Col(new Vector3(1f, 0.75f, 0.45f), MathF.Min(1f, gl)), PShape.Glow, 0, 0.35f);
        // Molten core.
        float mc = 0.85f * MathF.Exp(-t / 4f) * Smooth(0f, 0.4f, t);
        if (mc > 0.01f) Drape(_ps.Additive, fv, g, s * 0.6f, Col(new Vector3(1f, 0.38f, 0.1f), mc), PShape.Glow, 0, 0.2f);
        // Bright shock front racing out to ~2S, and a fainter echo behind it.
        if (t < 1.8f)
        {
            float a = 0.95f * MathF.Pow(1f - t / 1.8f, 1.4f);
            Drape(_ps.Additive, fv, g, 2.05f * s * EaseOut(t / 1.8f, 2.5f) / 0.82f + 0.5f, Col(new Vector3(1f, 0.92f, 0.78f), a), PShape.Ring, 0, 0.6f);
        }
        if (t > 0.2f && t < 2.4f)
        {
            float k = (t - 0.2f) / 2.2f;
            Drape(_ps.Additive, fv, g, 1.6f * s * EaseOut(k, 2f) / 0.82f + 0.5f, Col(new Vector3(1f, 0.8f, 0.55f), 0.4f * MathF.Pow(1f - k, 1.5f)), PShape.Ring, 0, 0.5f);
        }

        // Flash core and shock bubble (camera-facing, pulled toward the camera so the terrain does not clip them).
        if (t < 0.8f)
        {
            float k = t / 0.8f;
            Glow(fv, g + Up * s * 0.25f, s * (2.4f + 1.6f * k), Col(new Vector3(1f, 1f, 0.95f), (1f - k) * (1f - k)), PShape.Glow);
        }
        if (t < 0.9f)
        {
            float k = t / 0.9f;
            Glow(fv, g + Up * s * 0.2f, 2f * 1.5f * s * EaseOut(k, 2f) / 0.82f, Col(new Vector3(0.85f, 0.92f, 1f), 0.45f * MathF.Pow(1f - k, 1.5f)), PShape.Ring);
        }
        // Fireball glow riding up with the cap.
        if (f > 0.02f) Glow(fv, g + Up * CapHeight(t, s), s * (1.0f + 0.9f * f), Col(new Vector3(1f, 0.68f, 0.32f), 0.6f * f), PShape.Glow);
    }

    private void BuildPuffs(FrameView fv)
    {
        if (_count == 0) return;
        var cam = fv.CamPos;
        for (int i = 0; i < _count; i++)
        {
            _order[i] = i;
            _keys[i] = -Vector3.DistanceSquared(_puffs[i].Pos, cam);
        }
        Array.Sort(_keys, _order, 0, _count); // far to near
        var sun = Atmosphere.SunDir;
        for (int k = 0; k < _count; k++)
        {
            ref var p = ref _puffs[_order[k]];
            float u = p.Age / p.Life;
            float size = p.Size0 + (p.Size1 - p.Size0) * (1 - (1 - u) * (1 - u));
            if (!fv.SphereVisible(p.Pos, size)) continue;
            float t = p.T0 + p.Age, s = p.S, f = FireLevel(t);
            float a = p.Alpha * Smooth(0f, 0.12f, u) * (1f - Smooth(0.55f, 1f, u)) * (1f - 0.8f * Smooth(7f, 15f, t));
            if (a < 0.01f) continue;
            Vector3 c;
            switch (p.Kind)
            {
                case PuffKind.Cap:
                {
                    var rel = p.Pos - (p.Ground + Up * CapHeight(t, s));
                    float tube = CapTube(t, s);
                    var n = Vector3.Normalize(new Vector3(rel.X, rel.Y * 2.2f + tube * 0.5f, rel.Z) + new Vector3(0, 1e-3f, 0));
                    float lit = 0.62f + 0.5f * MathF.Max(0, Vector3.Dot(n, sun));
                    float hgt = Smooth(-1.3f, 1.1f, rel.Y / tube);
                    var smoke = Vector3.Lerp(new Vector3(0.12f, 0.1f, 0.09f), new Vector3(0.47f, 0.43f, 0.39f), hgt);
                    smoke = Vector3.Lerp(smoke, new Vector3(0.4f, 0.38f, 0.36f), Smooth(6f, 16f, t) * 0.6f) * (lit * (0.92f + 0.16f * p.Seed));
                    c = smoke + new Vector3(1f, 0.42f, 0.12f) * (f * 1.3f * (1f - 0.55f * hgt))
                              + new Vector3(1f, 0.85f, 0.55f) * (0.6f * (1f - Smooth(0.2f, 1.6f, t)));
                    break;
                }
                case PuffKind.Stem:
                {
                    float foot = 1f - Math.Clamp((p.Pos.Y - p.Ground.Y) / MathF.Max(1f, CapHeight(t, s)), 0f, 1f);
                    var n = Vector3.Normalize(new Vector3(p.Pos.X - p.Ground.X, 0.3f, p.Pos.Z - p.Ground.Z) + new Vector3(1e-3f, 0, 0));
                    float lit = 0.72f + 0.38f * MathF.Max(0, Vector3.Dot(n, sun));
                    c = new Vector3(0.34f, 0.3f, 0.27f) * lit + new Vector3(1f, 0.45f, 0.15f) * (f * (0.35f + 0.65f * foot));
                    break;
                }
                default:
                    c = new Vector3(0.46f, 0.4f, 0.32f) * (0.85f + 0.25f * p.Seed) + new Vector3(1f, 0.7f, 0.4f) * (f * 0.3f);
                    break;
            }
            _ps.Alpha.Billboard(p.Pos, size, p.Rot, fv.CamRight, fv.CamUp, Col(c, a), PShape.Smoke, p.Seed);
        }
    }

    /// <summary>Camera-facing quad pulled toward the camera (size compensated) so the ground does not cut it.</summary>
    private void Glow(FrameView fv, Vector3 c, float size, Color col, float shape)
    {
        if (col.A == 0) return;
        var toCam = fv.CamPos - c;
        float d = toCam.Length();
        if (d > 1e-3f)
        {
            float pull = MathF.Min(size * 0.5f, d * 0.6f);
            c += toCam / d * pull;
            size *= (d - pull) / d;
        }
        if (!fv.SphereVisible(c, size)) return;
        _ps.Additive.Billboard(c, size, 0, fv.CamRight, fv.CamUp, col, shape, 0);
    }

    /// <summary>A square of half-size <paramref name="half"/> as an n x n grid draped on the terrain (uv continuous over
    /// the grid, so radial particle shapes map onto the whole square); cells outside the shape circle or the map are skipped.</summary>
    private void Drape(QuadBatch batch, FrameView fv, Vector3 c, float half, Color col, float shape, float seed, float lift)
    {
        if (col.A == 0 || half <= 0.01f || !fv.SphereVisible(c, half * 1.42f)) return;
        int n = Math.Clamp((int)(half / 3.5f), 6, 40);
        float cell = 2f * half / n, inv = 1f / n;
        lift += cell * 0.06f;
        float x0 = c.X - half, z0 = c.Z - half;
        float mapW = _terrain.Width, mapH = _terrain.Height;
        Span<float> row0 = stackalloc float[n + 1];
        Span<float> row1 = stackalloc float[n + 1];
        for (int i = 0; i <= n; i++) row0[i] = GroundMax(x0 + i * cell, z0, cell * 0.5f) + lift;
        float reach = half + cell * 0.71f;
        for (int j = 0; j < n; j++)
        {
            float za = z0 + j * cell, zb = za + cell;
            for (int i = 0; i <= n; i++) row1[i] = GroundMax(x0 + i * cell, zb, cell * 0.5f) + lift;
            float zc = za + cell * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float xa = x0 + i * cell, xb = xa + cell, xc = xa + cell * 0.5f;
                float dx = xc - c.X, dz = zc - c.Z;
                if (dx * dx + dz * dz > reach * reach) continue;
                if (xc < 0 || zc < 0 || xc > mapW || zc > mapH) continue;
                batch.Add(new Vector3(xa, row0[i], za), new Vector3(xb, row0[i + 1], za), new Vector3(xb, row1[i + 1], zb), new Vector3(xa, row1[i], zb),
                    col, shape, seed, new Vector4(i * inv, j * inv, (i + 1) * inv, (j + 1) * inv));
            }
            var tmp = row0; row0 = row1; row1 = tmp;
        }
    }

    /// <summary>Highest ground around a grid corner (keeps draped quads above bumps between the corners).</summary>
    private float GroundMax(float x, float z, float d)
    {
        float h = _ps.GroundAt(new Vector3(x, 0, z));
        h = MathF.Max(h, _ps.GroundAt(new Vector3(x - d, 0, z - d)));
        h = MathF.Max(h, _ps.GroundAt(new Vector3(x + d, 0, z - d)));
        h = MathF.Max(h, _ps.GroundAt(new Vector3(x - d, 0, z + d)));
        return MathF.Max(h, _ps.GroundAt(new Vector3(x + d, 0, z + d)));
    }

    /// <summary>Full-screen flash (2D, call after EndMode3D): white, warming as it fades over ~0.65 s of real time.</summary>
    public void DrawOverlay()
    {
        if (_flash <= 0.002f) return;
        float a = _flash * _flash;
        var col = new Color((byte)255, (byte)(222 + 33 * _flash), (byte)(170 + 85 * _flash), (byte)(a * 235));
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), col);
        _flash = MathF.Max(0, _flash - Raylib.GetFrameTime() / 0.65f);
    }
}
