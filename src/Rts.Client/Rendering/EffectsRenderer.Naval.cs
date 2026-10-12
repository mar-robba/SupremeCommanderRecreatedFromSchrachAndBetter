using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Naval effects: torpedoes (dark body under the water + rising bubble trail that pops into foam flecks on the
/// surface, splash when an air-dropped torpedo enters the water), torpedo hits (underwater flash, water column,
/// foam), torpedo-defense interceptions (small underwater burst + foam), ship wakes (foam V at the bow and a
/// turbulent stern trail on the water, scaled by speed and hull size; none for submerged subs) and ships/subs
/// dying on water.
/// <para>Underwater visuals live in a small pooled particle array. They are drawn after the water: every point
/// is moved along its view ray to just above the water plane (<see cref="PullAboveWater"/>: same screen position,
/// passes the depth test against the water surface, still hidden by nearer geometry) and tinted/faded by its
/// depth, so they read as "seen through the water".</para>
/// </summary>
public sealed partial class EffectsRenderer
{
    private struct UwParticle
    {
        public Vector3 Pos, Vel;
        public float Age, Life, Size0, Size1, Seed, Shape;
        public Vector4 C0, C1;
        public bool Additive, Bubble;
    }

    private const int UwCapacity = 8192;
    private readonly UwParticle[] _uw = new UwParticle[UwCapacity];
    private int _uwCount;
    private QuadBatch? _uwAlpha, _uwAdd;
    private float _wakeAcc;
    private Vector3 _uwCam;
    private static readonly Vector4 Foam = new(0.9f, 0.95f, 1f, 1f);
    private static readonly Vector3 DeepTint = new(0.06f, 0.2f, 0.3f);

    private float Water => _ctx.Terrain.WaterLevel;
    public int UnderwaterParticleCount => _uwCount;

    // ------------------------------------------------------------------ frame hooks (called from Draw)

    /// <summary>Begins the underwater batches (torpedo bodies are added while projectiles are drawn) and advances
    /// the underwater particles.</summary>
    private void BeginNaval(float dt)
    {
        _uwAlpha ??= new QuadBatch(Shaders.Particle.Shader);
        _uwAdd ??= new QuadBatch(Shaders.Particle.Shader);
        _uwAlpha.Begin();
        _uwAdd.Begin();
        _uwCam = FrameView.Current.CamPos;
        if (dt <= 0) return;
        float water = Water;
        for (int i = 0; i < _uwCount;)
        {
            ref var p = ref _uw[i];
            p.Age += dt;
            if (p.Bubble)
            {
                // Bubbles wobble up and pop into a foam fleck at the surface.
                p.Vel.X += MathF.Sin(p.Age * 9f + p.Seed * 30f) * 1.4f * dt;
                p.Vel.Z += MathF.Cos(p.Age * 8f + p.Seed * 20f) * 1.4f * dt;
            }
            p.Vel *= MathF.Max(0, 1 - 0.8f * dt);
            if (p.Bubble) p.Vel.Y = MathF.Max(p.Vel.Y, 0.5f);
            p.Pos += p.Vel * dt;
            bool popped = p.Bubble && p.Pos.Y >= water - 0.04f;
            if (popped && _ps.Rand() < 0.5f * _ps.Lod) FoamFleck(p.Pos, 0.25f + p.Size1 * 1.6f, 0.3f, 1.4f);
            if (p.Age >= p.Life || popped)
            {
                _uw[i] = _uw[--_uwCount];
                continue;
            }
            i++;
        }
    }

    /// <summary>Emits wakes and builds the underwater particle quads (after the main particle build).</summary>
    private void NavalFrame(float dt, SimSnapshot cur, SimSnapshot? prev, float alpha, FrameView fv)
    {
        EmitWakes(dt, cur, prev, alpha, fv);
        if (_uwCount == 0) return;
        var right = fv.CamRight;
        var up = fv.CamUp;
        for (int i = 0; i < _uwCount; i++)
        {
            ref var p = ref _uw[i];
            if (p.Age < 0) continue;
            float t = p.Age / p.Life;
            float size = p.Size0 + (p.Size1 - p.Size0) * (1 - (1 - t) * (1 - t));
            if (!fv.SphereVisible(p.Pos, size + 1f)) continue;
            var c = Vector4.Lerp(p.C0, p.C1, t);
            var pos = PullAboveWater(p.Pos, out float scale, out float vis);
            if (!p.Additive)
            {
                var rgb = Vector3.Lerp(new Vector3(c.X, c.Y, c.Z), DeepTint + new Vector3(0.35f, 0.45f, 0.5f), 1 - vis);
                c = new Vector4(rgb, c.W * vis);
            }
            else c *= vis;
            (p.Additive ? _uwAdd! : _uwAlpha!).Billboard(pos, size * scale, p.Seed * 6f, right, up, ToColor(c), p.Shape, p.Seed);
        }
    }

    private static Color ToColor(Vector4 c) => new(
        (byte)Math.Clamp(c.X * 255, 0, 255), (byte)Math.Clamp(c.Y * 255, 0, 255),
        (byte)Math.Clamp(c.Z * 255, 0, 255), (byte)Math.Clamp(c.W * 255, 0, 255));

    /// <summary>
    /// Moves an underwater point along its view ray to just above the water plane (same screen position, passes
    /// the depth test against the water surface). scale = size factor keeping the projected size; vis = 1 at the
    /// surface fading with depth.
    /// </summary>
    private Vector3 PullAboveWater(Vector3 p, out float scale, out float vis)
    {
        float water = Water;
        scale = 1f;
        vis = 1f;
        if (p.Y >= water) return p;
        vis = MathF.Exp(-(water - p.Y) * 0.55f);
        var d = _uwCam - p;
        if (d.Y <= 1e-3f) return p;
        float t = MathF.Min((water + 0.05f - p.Y) / d.Y, 0.95f);
        scale = 1f - t;
        return p + d * t;
    }

    private ref UwParticle EmitUw()
    {
        if (_uwCount >= UwCapacity) return ref _uw[UwCapacity - 1];
        ref var p = ref _uw[_uwCount++];
        p = default;
        p.Seed = _ps.Rand();
        return ref p;
    }

    private void Bubbles(Vector3 pos, int n, float spread, float size, float life = 1.6f)
    {
        float water = Water;
        for (int i = 0; i < n; i++)
        {
            ref var p = ref EmitUw();
            p.Pos = pos + new Vector3(_ps.Range(-spread, spread), _ps.Range(-spread, spread) * 0.5f, _ps.Range(-spread, spread));
            if (p.Pos.Y > water - 0.08f) p.Pos.Y = water - 0.08f;
            p.Vel = new Vector3(_ps.Range(-0.3f, 0.3f), _ps.Range(0.7f, 1.5f), _ps.Range(-0.3f, 0.3f));
            p.Life = life * _ps.Range(0.7f, 1.2f);
            p.Size0 = size * _ps.Range(0.6f, 1.1f); p.Size1 = p.Size0 * 1.5f;
            p.C0 = new Vector4(0.9f, 0.97f, 1f, 0.95f); p.C1 = p.C0 with { W = 0.5f };
            p.Shape = PShape.Ring; p.Bubble = true;
        }
    }

    private void UnderwaterFlash(Vector3 pos, float size, Vector4 color, float life = 0.25f)
    {
        ref var p = ref EmitUw();
        p.Pos = pos; p.Life = life; p.Size0 = size; p.Size1 = size * 0.5f;
        p.C0 = color; p.C1 = color with { W = 0 }; p.Shape = PShape.Glow; p.Additive = true;
    }

    // ------------------------------------------------------------------ surface recipes (main particle pool)

    /// <summary>Flat foam patch on the water surface.</summary>
    private void FoamFleck(Vector3 pos, float size, float alpha, float life, Vector3 vel = default, float growth = 2.4f)
    {
        ref var p = ref _ps.Emit();
        p.Pos = new Vector3(pos.X, Water + 0.06f, pos.Z); p.Vel = vel; p.Drag = 0.7f;
        p.Life = life * _ps.Range(0.8f, 1.2f); p.Size0 = size; p.Size1 = size * growth; p.RotVel = _ps.Range(-0.3f, 0.3f);
        p.C0 = Foam with { W = alpha }; p.C1 = Foam with { W = 0 };
        p.Shape = PShape.Smoke; p.Mode = PMode.Flat; p.Blend = PBlend.Alpha;
    }

    /// <summary>White water: spray thrown up, a foam ring and a lingering foam patch (size ≈ radius).</summary>
    private void Splash(Vector3 pos, float size, float height = 1f)
    {
        var s = new Vector3(pos.X, Water + 0.06f, pos.Z);
        _ps.Ring(s, size * 0.3f, size * 3.2f, 0.55f, new Vector4(0.8f, 0.9f, 1f, 0.45f));
        int n = Math.Max(3, (int)((6 + size * 8) * _ps.Lod));
        for (int i = 0; i < n; i++)
        {
            ref var p = ref _ps.Emit();
            p.Pos = s + new Vector3(_ps.Range(-0.3f, 0.3f) * size, 0, _ps.Range(-0.3f, 0.3f) * size);
            p.Vel = new Vector3(_ps.Range(-1.2f, 1.2f) * size, _ps.Range(3.5f, 7f) * MathF.Sqrt(size) * height, _ps.Range(-1.2f, 1.2f) * size);
            p.Gravity = 9.8f; p.Drag = 0.6f;
            p.Life = _ps.Range(0.7f, 1.2f) * MathF.Sqrt(height); p.Size0 = size * 0.35f; p.Size1 = size * 1.1f; p.RotVel = _ps.Range(-1, 1);
            p.C0 = new Vector4(0.92f, 0.96f, 1f, 0.8f); p.C1 = new Vector4(0.85f, 0.9f, 0.95f, 0);
            p.Shape = PShape.Smoke; p.Blend = PBlend.Alpha; p.Collide = true;
        }
        FoamFleck(pos, size * 1.2f, 0.55f, 3.5f, default, 2.6f);
    }

    // ------------------------------------------------------------------ torpedoes

    private void DrawTorpedo(int id, Vector3 pos, Vector3 dir, FrameView fv)
    {
        float water = Water;
        var flat = new Vector3(dir.X, 0, dir.Z);
        if (pos.Y > water + 0.05f)
        {
            // Falling from a bomber / deck launcher: dark body in the air, no trail.
            _uwAlpha!.Streak(pos + dir * 0.45f, dir, 0.9f, 0.2f, fv.CamPos, new Color(40, 44, 48, 235), PShape.Disk, 0);
            _trails[id] = (pos, _frame);
            return;
        }
        if (flat.LengthSquared() > 1e-6f) dir = Vector3.Normalize(flat);
        float zs = Math.Clamp(Atmosphere.CameraDistance / 70f, 1f, 3f);
        // Surface: a thin white streak of churned water trailing the torpedo (what you actually see from above).
        var top = new Vector3(pos.X, water + 0.07f, pos.Z);
        _uwAlpha!.Streak(top - dir * 0.3f, dir, 3.2f * zs, 0.38f * zs, fv.CamPos, new Color((byte)225, (byte)240, (byte)250, (byte)120), PShape.Streak, 0);
        // Body: a dark cigar under the water, seen through it.
        var head = PullAboveWater(pos + dir * 0.6f * zs, out float sh, out float vis);
        var tail = PullAboveWater(pos - dir * 0.6f * zs, out _, out _);
        var axis = head - tail;
        float len = axis.Length();
        if (len > 1e-4f)
        {
            byte a = (byte)(245 * MathF.Max(0.6f, vis));
            _uwAlpha.Streak(head, axis / len, len, 0.3f * zs * sh, fv.CamPos, new Color((byte)12, (byte)22, (byte)30, a), PShape.Disk, 0);
        }
        // Bubble trail along the path travelled since last frame; splash when it just entered the water.
        if (_trails.TryGetValue(id, out var last))
        {
            if (last.pos.Y > water + 0.02f) Splash(pos, 0.55f, 0.7f);
            var d = pos - last.pos;
            float dl = d.Length();
            if (dl > 30f) { _trails[id] = (pos, _frame); return; }
            const float spacing = 0.28f;
            int n = Math.Min(16, (int)(dl / spacing));
            if (n == 0) { _trails[id] = (last.pos, _frame); return; }
            for (int k = 1; k <= n; k++)
            {
                var p = last.pos + d * (k / (float)n);
                if (_ps.Rand() < 0.75f * _ps.Lod) Bubbles(p - dir * 0.5f, 1, 0.12f, 0.13f * zs, 1.3f);
                if (k % 3 == 0) FoamFleck(p - dir * 0.6f, 0.3f, 0.22f, 1.6f, default, 2.2f);
            }
            _trails[id] = (pos, _frame);
        }
        else _trails[id] = (pos, _frame);
    }

    // ------------------------------------------------------------------ sim events

    /// <summary>Naval recipes for sim effect events; true if the event was fully handled.</summary>
    private bool SpawnNaval(in EffectEvent e, float size)
    {
        if (e.Kind == EffectKind.UnitDeath) return NavalDeath(e, size);
        if (e.Kind is not (EffectKind.MuzzleFlash or EffectKind.Impact)) return false;
        if ((uint)e.BlueprintIndex >= (uint)_ctx.Db.All.Count) return false;
        var ws = _ctx.Db[e.BlueprintIndex].Weapons;
        if ((uint)e.WeaponIndex >= (uint)ws.Length) return false;
        string vis = ws[e.WeaponIndex].ProjectileVisual;
        float water = Water;
        switch (vis)
        {
            case "torpedo" when e.Kind == EffectKind.MuzzleFlash:
                // Launch: a burst of bubbles under water (subs), a small splash from a deck, nothing from the air.
                if (e.Pos.Y < water - 0.2f) Bubbles(e.Pos, 6, 0.3f, 0.18f);
                else if (e.Pos.Y < water + 3f) Splash(e.Pos, 0.4f, 0.5f);
                return true;
            case "torpedo":
            {
                // Hit (or shore): underwater flash, bubbles, a tall water column and foam.
                var under = new Vector3(e.Pos.X, MathF.Min(e.Pos.Y, water - 0.5f), e.Pos.Z);
                UnderwaterFlash(under, 3.2f, new Vector4(0.75f, 0.92f, 1f, 1f), 0.3f);
                UnderwaterFlash(under, 1.6f, new Vector4(1f, 0.85f, 0.6f, 1f), 0.15f);
                Bubbles(under, 14, 0.7f, 0.22f, 1.4f);
                Splash(e.Pos, 1.3f, 1.6f);
                Splash(e.Pos + new Vector3(_ps.Range(-0.6f, 0.6f), 0, _ps.Range(-0.6f, 0.6f)), 0.8f, 2.2f);
                return true;
            }
            case "antitorpedo" when e.Kind == EffectKind.MuzzleFlash:
                // Flares / depth charge fired from the defender.
                _ps.MuzzleFlash(e.Pos, 0.6f, new Vector4(0.7f, 0.9f, 1f, 1f));
                _ps.Sparks(e.Pos, Math.Max(2, (int)(4 * _ps.Lod)), 5f, new Vector4(0.8f, 0.95f, 1f, 1f), 0.4f, 0.08f, 6f);
                return true;
            case "antitorpedo":
            {
                // Torpedo destroyed: small underwater burst + foam.
                UnderwaterFlash(e.Pos, 1.8f, new Vector4(0.6f, 0.9f, 1f, 1f), 0.22f);
                Bubbles(e.Pos, 8, 0.4f, 0.18f, 1.2f);
                Splash(e.Pos, 0.6f, 0.9f);
                return true;
            }
        }
        return false;
    }

    /// <summary>Ship deaths add a big splash + foam; submerged deaths are an underwater burst instead of a fireball.</summary>
    private bool NavalDeath(in EffectEvent e, float size)
    {
        if ((uint)e.BlueprintIndex >= (uint)_ctx.Db.All.Count) return false;
        var bp = _ctx.Db[e.BlueprintIndex];
        float water = Water;
        if (_ctx.Terrain.GetHeight(e.Pos.X, e.Pos.Z) > water - 0.3f) return false; // not over water
        if (!FrameView.Current.SphereVisible(e.Pos, size * 6 + 4)) return false;
        if (e.Pos.Y < water - 0.5f)
        {
            UnderwaterFlash(e.Pos, size * 2.5f, new Vector4(1f, 0.8f, 0.5f, 1f), 0.4f);
            UnderwaterFlash(e.Pos, size * 4f, new Vector4(0.6f, 0.85f, 1f, 0.8f), 0.6f);
            Bubbles(e.Pos, (int)(18 + size * 8), size * 0.6f, 0.25f, 2.2f);
            Splash(e.Pos, size * 0.8f, 1.8f);
            return true;
        }
        if (bp.Movement.Layer == MotionLayer.Air) Splash(e.Pos, size * 0.6f, 1.2f);
        else
        {
            Splash(e.Pos, size * 0.7f, 1.5f);
            Bubbles(new Vector3(e.Pos.X, water - 0.4f, e.Pos.Z), (int)(8 + size * 4), size * 0.5f, 0.24f, 2f);
        }
        return false; // the normal explosion plays too
    }

    // ------------------------------------------------------------------ wakes

    /// <summary>Foam wakes behind moving surface ships (game time, ~12 emissions per second, pooled particles).</summary>
    private void EmitWakes(float dt, SimSnapshot cur, SimSnapshot? prev, float alpha, FrameView fv)
    {
        if (prev == null || dt <= 0) return;
        _wakeAcc += dt;
        if (_wakeAcc < 0.08f) return;
        float step = MathF.Min(_wakeAcc, 0.3f);
        _wakeAcc = 0;
        if (Atmosphere.CameraDistance > 300f) return;
        float tickDt = TickSeconds * Math.Max(1, cur.Tick - prev.Tick);
        float water = Water;
        float k = step / 0.08f * _ps.Lod;
        for (int i = 0; i < cur.UnitCount; i++)
        {
            ref var v = ref cur.Units[i];
            if ((uint)v.Bp >= (uint)_ctx.Db.All.Count) continue;
            var bp = _ctx.Db[v.Bp];
            if (bp.Movement.Layer != MotionLayer.Water || (v.Flags & UnitFlags.UnderConstruction) != 0) continue;
            if (!prev.TryGetUnit(v.Index, v.Gen, out var pv)) continue;
            var vel = new Vector2(v.Pos.X - pv.Pos.X, v.Pos.Z - pv.Pos.Z) / tickDt;
            float speed = vel.Length();
            if (speed < 0.25f) continue;
            var pos = Vector3.Lerp(pv.Pos, v.Pos, alpha);
            float len = MathF.Max(1.5f, bp.Visual.Size.Z), wid = MathF.Max(0.8f, bp.Visual.Size.X);
            if (!fv.SphereVisible(pos, len * 2f + 6f)) continue;
            float h = pv.Heading + Rts.Sim.Core.DMath.AngleDiff(pv.Heading, v.Heading) * alpha;
            var fwd = new Vector3(MathF.Cos(h), 0, MathF.Sin(h));
            var right = new Vector3(-fwd.Z, 0, fwd.X);
            float sf = Math.Clamp(speed / MathF.Max(1f, bp.Movement.MaxSpeed), 0.25f, 1f);
            float big = MathF.Sqrt(len / 6f);
            pos.Y = water;

            // Bow wave: foam leaves both sides of the bow and drifts outwards → a V spreading behind the ship.
            var bow = pos + fwd * (len * 0.42f);
            for (int s = -1; s <= 1; s += 2)
            {
                if (_ps.Rand() > k) continue;
                FoamFleck(bow + right * (s * wid * 0.32f), wid * 0.25f, 0.42f * sf, 2.4f * big, right * (s * (0.45f + speed * 0.22f)), 3.0f);
            }
            // Hull sides: thinner foam lines along the waterline.
            if (_ps.Rand() < 0.6f * k)
            {
                int s = _ps.Rand() < 0.5f ? -1 : 1;
                FoamFleck(pos + fwd * _ps.Range(-0.3f, 0.3f) * len + right * (s * wid * 0.5f), wid * 0.25f, 0.3f * sf, 1.6f * big, right * (s * 0.4f), 2.6f);
            }
            // Stern: churned white water.
            var stern = pos - fwd * (len * 0.46f);
            int n = Math.Max(1, (int)MathF.Round(k * (1 + big)));
            for (int j = 0; j < n; j++)
                FoamFleck(stern + right * _ps.Range(-0.25f, 0.25f) * wid, wid * 0.4f, 0.34f * sf, 3f * big, -fwd * (speed * 0.12f) + right * _ps.Range(-0.3f, 0.3f), 2.4f);
        }
    }
}
