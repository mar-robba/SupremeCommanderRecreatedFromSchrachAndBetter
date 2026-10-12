using System.Numerics;
using Raylib_cs;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Strategic weapon visuals: tactical/strategic missiles and their interceptors (projectiles), shield
/// hit/down/up bursts, missile launch and mid-air interception, and the nuclear detonation (flash, shockwaves,
/// fireball, mushroom cloud, base surge, long-lived scorch) plus its full-screen flash overlay.
/// </summary>
public sealed partial class EffectsRenderer
{
    private const float NukeFlashDuration = 1.1f;
    private float _nukeFlashT = NukeFlashDuration, _nukeFlashI;

    private static Vector4 Col(float r, float g, float b, float a = 1) => new(r, g, b, a);

    private int Num(float n) => Math.Max(1, (int)MathF.Round(n * _ps.Lod));

    private Vector3 RandHoriz()
    {
        float a = _ps.Rand() * MathF.Tau;
        return new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
    }

    // ------------------------------------------------------------------ projectiles

    private void DrawStrategicProjectile(int id, Vector3 pos, Vector3 dir, string visual, FrameView fv, float zs)
    {
        var cam = fv.CamPos;
        var right = fv.CamRight;
        var up = fv.CamUp;
        float flicker = 0.85f + 0.15f * MathF.Sin(_frame * 1.9f + id * 0.37f);
        float ts = 0.75f + 0.25f * zs; // smoke trails get a bit thicker when zoomed out
        switch (visual)
        {
            case "tactical": // cruise missile
            {
                var noz = pos - dir * (0.35f * zs);
                _proj.Streak(noz, dir, 1.7f * zs * flicker, 0.45f * zs, cam, new Color(255, 160, 70, 210), PShape.Streak, 0);
                _proj.Billboard(noz, 1.2f * zs * flicker, 0, right, up, new Color(255, 232, 175, 255), PShape.Glow, 0);
                _proj.Billboard(noz, 2.6f * zs, 0, right, up, new Color(255, 140, 60, 90), PShape.Glow, 0);
                _proj.Streak(pos + dir * (0.55f * zs), dir, 0.9f * zs, 0.2f * zs, cam, new Color(205, 205, 215, 210), PShape.Streak, 0);
                Trail(id, noz, 0.3f, new Vector4(0.74f, 0.73f, 0.72f, 0.42f), 0.3f * ts, 1.8f);
                break;
            }
            case "nuke": // strategic missile
            {
                var noz = pos - dir * (1.4f * zs);
                _proj.Streak(noz, dir, 5.5f * zs * flicker, 1.4f * zs, cam, new Color(255, 165, 75, 220), PShape.Streak, 0);
                _proj.Streak(noz, dir, 3f * zs * flicker, 0.55f * zs, cam, new Color(255, 245, 220, 255), PShape.Streak, 0);
                _proj.Billboard(noz, 2.8f * zs * flicker, 0, right, up, new Color(255, 245, 225, 255), PShape.Glow, 0);
                _proj.Billboard(noz, 6.5f * zs, 0, right, up, new Color(255, 150, 60, 110), PShape.Glow, 0);
                _proj.Streak(pos + dir * (1.6f * zs), dir, 3f * zs, 0.55f * zs, cam, new Color(225, 225, 235, 230), PShape.Streak, 0);
                Trail(id, noz, 0.7f, new Vector4(0.84f, 0.82f, 0.8f, 0.5f), 0.8f * ts, 7f);
                break;
            }
            case "interceptor": // TMD shot
                _proj.Streak(pos, dir, 2.2f * zs, 0.22f * zs, cam, new Color(120, 220, 255, 255), PShape.Streak, 0);
                _proj.Streak(pos, dir, 1.4f * zs, 0.08f * zs, cam, new Color(255, 255, 255, 240), PShape.Streak, 0);
                _proj.Billboard(pos, 0.55f * zs, 0, right, up, new Color(170, 240, 255, 255), PShape.Glow, 0);
                Trail(id, pos, 0.45f, new Vector4(0.8f, 0.9f, 1f, 0.2f), 0.1f, 0.45f);
                break;
            default: // "antinuke": strategic missile defense
            {
                var noz = pos - dir * (0.8f * zs);
                _proj.Streak(noz, dir, 3f * zs * flicker, 0.7f * zs, cam, new Color(130, 190, 255, 220), PShape.Streak, 0);
                _proj.Billboard(noz, 1.6f * zs * flicker, 0, right, up, new Color(215, 235, 255, 255), PShape.Glow, 0);
                _proj.Billboard(noz, 3.4f * zs, 0, right, up, new Color(80, 140, 255, 120), PShape.Glow, 0);
                _proj.Streak(pos + dir * (1.0f * zs), dir, 1.8f * zs, 0.32f * zs, cam, new Color(225, 230, 245, 225), PShape.Streak, 0);
                Trail(id, noz, 0.8f, new Vector4(0.86f, 0.89f, 0.95f, 0.4f), 0.45f * ts, 2.5f);
                break;
            }
        }
    }

    // ------------------------------------------------------------------ effect events

    /// <summary>Handles the strategic EffectKinds; false for every other kind.</summary>
    private bool SpawnStrategic(in EffectEvent e)
    {
        switch (e.Kind)
        {
            case EffectKind.NukeDetonation:
                NukeDetonation(e.Pos, e.Size); // always (long-lived cloud and scorch, may scroll into view)
                return true;
            case EffectKind.ShieldHit or EffectKind.ShieldDown or EffectKind.ShieldUp
                or EffectKind.MissileLaunch or EffectKind.MissileIntercepted:
                break;
            default:
                return false;
        }
        float size = MathF.Max(0.3f, e.Size);
        if (!FrameView.Current.SphereVisible(e.Pos, size * 3f + 8f)) return true;
        switch (e.Kind)
        {
            case EffectKind.ShieldHit: ShieldHitFx(e.Pos, size); break;
            case EffectKind.ShieldDown: ShieldDownFx(e.Pos, size, e.BlueprintIndex); break;
            case EffectKind.ShieldUp: ShieldUpFx(e.Pos, size); break;
            case EffectKind.MissileLaunch: MissileLaunchFx(e.Pos, size); break;
            case EffectKind.MissileIntercepted: MissileInterceptedFx(e.Pos, size); break;
        }
        return true;
    }

    private void ShieldHitFx(Vector3 pos, float s)
    {
        float k = 0.6f + 0.4f * Math.Clamp(s, 0.3f, 4f);
        _ps.Flash(pos, 1.3f * k, Col(0.55f, 0.8f, 1f, 0.9f), 0.14f);
        _ps.Flash(pos, 0.6f * k, Col(1f, 1f, 1f, 1f), 0.07f);
        _ps.Sparks(pos, Num(3 + 3 * k), 5.5f * k, Col(0.6f, 0.86f, 1f), 0.3f, 0.06f + 0.02f * k, 1.5f);
    }

    /// <summary>Electric spark burst, optionally delayed (seconds).</summary>
    private void ElectricSparks(Vector3 pos, int n, float speed, float delay)
    {
        for (int i = 0; i < n; i++)
        {
            ref var p = ref _ps.Emit();
            p.Pos = pos; p.Vel = _ps.RandDir() * speed * _ps.Range(0.4f, 1.1f); p.Age = -delay;
            p.Life = _ps.Range(0.15f, 0.4f); p.Size0 = 0.12f; p.Size1 = 0.04f;
            p.Gravity = 3f; p.Drag = 2f; p.Stretch = 0.05f;
            p.C0 = Col(0.85f, 0.95f, 1f, 1f); p.C1 = Col(0.3f, 0.55f, 1f, 0);
            p.Shape = PShape.Streak; p.Mode = PMode.Streak; p.Blend = PBlend.Additive;
        }
    }

    private void ShieldDownFx(Vector3 c, float r, int bp)
    {
        r = Math.Clamp(r, 3f, 80f);
        bool hasBp = (uint)bp < (uint)_ctx.Db.All.Count;
        float hs = hasBp && _ctx.Db[bp].Shield is { } sb ? Math.Clamp(sb.HeightScale, 0.2f, 1.5f) : 0.7f;
        float genH = hasBp ? _ctx.Db[bp].Visual.Size.Y : 2f;
        float gy = _ps.GroundAt(c);
        var gen = new Vector3(c.X, gy + genH * 0.7f, c.Z);

        _ps.Flash(c, r * 1.1f, Col(0.5f, 0.75f, 1f, 0.7f), 0.35f);
        _ps.Flash(gen, 3f + genH, Col(0.9f, 0.97f, 1f, 1f), 0.2f);
        // Dome edge bursting outwards (a camera-facing ring reads as the sphere's silhouette).
        ref var ring = ref _ps.Emit();
        ring.Pos = c; ring.Life = 0.55f; ring.Size0 = r * 2.4f; ring.Size1 = r * 3f;
        ring.C0 = Col(0.5f, 0.8f, 1f, 0.6f); ring.C1 = ring.C0 with { W = 0 }; ring.Shape = PShape.Ring; ring.Blend = PBlend.Additive;
        _ps.Ring(new Vector3(c.X, gy, c.Z), r * 0.3f, r * 2.6f, 0.6f, Col(0.45f, 0.75f, 1f, 0.5f));
        // Sparks raining from the whole dome surface.
        int n = Num(Math.Min(70f, 16f + r * 1.3f));
        for (int i = 0; i < n; i++)
        {
            var d = _ps.RandUpDir(0.05f);
            ref var p = ref _ps.Emit();
            p.Pos = c + new Vector3(d.X * r, d.Y * r * hs, d.Z * r);
            p.Vel = d * _ps.Range(1f, 4f); p.Age = -_ps.Range(0f, 0.15f);
            p.Life = _ps.Range(0.5f, 1.1f); p.Size0 = 0.2f; p.Size1 = 0.05f;
            p.Gravity = 6f; p.Drag = 0.8f; p.Stretch = 0.08f;
            p.C0 = Col(0.7f, 0.9f, 1f, 1f); p.C1 = Col(0.3f, 0.5f, 1f, 0);
            p.Shape = PShape.Streak; p.Mode = PMode.Streak; p.Blend = PBlend.Additive;
        }
        // Electric crackle around the generator for ~1.3 s.
        float spread = MathF.Max(0.8f, genH * 0.6f);
        for (int i = 0; i < 7; i++)
        {
            float delay = _ps.Range(0f, 1.3f);
            var at = gen + _ps.RandDir() * spread;
            ref var f = ref _ps.Emit();
            f.Pos = at; f.Age = -delay; f.Life = _ps.Range(0.08f, 0.15f); f.Size0 = _ps.Range(1.4f, 2.6f); f.Size1 = f.Size0 * 0.5f;
            f.C0 = Col(0.75f, 0.9f, 1f, 1f); f.C1 = Col(0.4f, 0.6f, 1f, 0); f.Shape = PShape.Glow; f.Blend = PBlend.Additive;
            if (i % 2 == 0) ElectricSparks(at, Num(6), 5f, delay);
        }
    }

    private void ShieldUpFx(Vector3 c, float r)
    {
        r = Math.Clamp(r, 3f, 80f);
        ref var p = ref _ps.Emit();
        p.Pos = c; p.Life = 0.9f; p.Size0 = r * 0.4f; p.Size1 = r * 1.4f;
        p.C0 = Col(0.35f, 0.65f, 1f, 0.35f); p.C1 = p.C0 with { W = 0 }; p.Shape = PShape.Glow; p.Blend = PBlend.Additive;
        ref var q = ref _ps.Emit();
        q.Pos = c; q.Life = 0.6f; q.Size0 = r * 0.3f; q.Size1 = r * 2.45f;
        q.C0 = Col(0.5f, 0.8f, 1f, 0.35f); q.C1 = q.C0 with { W = 0 }; q.Shape = PShape.Ring; q.Blend = PBlend.Additive;
        float gy = _ps.GroundAt(c);
        for (int i = 0; i < Num(10); i++)
        {
            ref var m = ref _ps.Emit();
            m.Pos = new Vector3(c.X, gy, c.Z) + RandHoriz() * _ps.Range(0.5f, 2.5f) + new Vector3(0, _ps.Range(0.2f, 1f), 0);
            m.Vel = new Vector3(0, _ps.Range(1.5f, 3.5f), 0); m.Life = _ps.Range(0.5f, 1f); m.Size0 = 0.3f; m.Size1 = 0.05f;
            m.C0 = Col(0.55f, 0.85f, 1f, 1f); m.C1 = Col(0.3f, 0.55f, 1f, 0); m.Shape = PShape.Glow; m.Blend = PBlend.Additive;
        }
    }

    private void MissileLaunchFx(Vector3 pos, float s)
    {
        s = Math.Clamp(s, 0.5f, 5f);
        float gy = _ps.GroundAt(pos);
        var g = new Vector3(pos.X, gy, pos.Z);
        _ps.Flash(pos, s * 4f, Col(1f, 0.82f, 0.55f, 1f), 0.3f);
        _ps.Flash(pos, s * 1.6f, Col(1f, 1f, 0.95f, 1f), 0.15f);
        _ps.Sparks(pos, Num(8 + s * 6), 7f * s, Col(1f, 0.7f, 0.3f), 0.5f, 0.08f + 0.03f * s, 4f);
        // Exhaust blast billowing out along the ground.
        int n = Num(10 + s * 9);
        for (int i = 0; i < n; i++)
        {
            var hd = RandHoriz();
            ref var p = ref _ps.Emit();
            p.Pos = g + hd * (s * 0.4f) + new Vector3(0, s * 0.3f, 0);
            p.Vel = hd * (s * _ps.Range(2.5f, 5f)) + new Vector3(0, s * _ps.Range(0.3f, 1.2f), 0);
            p.Drag = 1.3f; p.RotVel = _ps.Range(-0.5f, 0.5f);
            p.Life = _ps.Range(2.5f, 4f) + s * 0.8f; p.Size0 = s * 0.7f; p.Size1 = s * 2.8f;
            p.C0 = Col(0.78f, 0.76f, 0.73f, 0.75f); p.C1 = Col(0.8f, 0.8f, 0.8f, 0);
            p.Shape = PShape.Smoke; p.Blend = PBlend.Alpha;
        }
        // Plume left behind along the (initially vertical) launch path.
        int m = Num(6 + s * 4);
        for (int i = 0; i < m; i++)
        {
            ref var p = ref _ps.Emit();
            p.Pos = pos + new Vector3(_ps.Range(-0.3f, 0.3f) * s, _ps.Range(0f, 2f) * s, _ps.Range(-0.3f, 0.3f) * s);
            p.Vel = new Vector3(_ps.Range(-0.4f, 0.4f), _ps.Range(1.2f, 3.6f) * s, _ps.Range(-0.4f, 0.4f));
            p.Age = -_ps.Range(0f, 0.4f); p.Drag = 1.1f; p.RotVel = _ps.Range(-0.4f, 0.4f);
            p.Life = _ps.Range(2.5f, 4f) + s; p.Size0 = s * 0.6f; p.Size1 = s * 2.2f;
            p.C0 = Col(0.8f, 0.78f, 0.75f, 0.55f); p.C1 = Col(0.82f, 0.82f, 0.82f, 0);
            p.Shape = PShape.Smoke; p.Blend = PBlend.Alpha;
        }
        if (s >= 2f)
        {
            _ps.Ring(g, s * 0.6f, s * 7f, 0.7f, Col(1f, 0.85f, 0.6f, 0.5f));
            for (int i = 0; i < Num(6); i++) _ps.Fireball(pos + _ps.RandDir() * s * 0.4f, s * 0.8f, 0.7f);
        }
    }

    private void MissileInterceptedFx(Vector3 pos, float s)
    {
        s = Math.Clamp(s, 0.5f, 5f);
        _ps.Explosion(pos, s * 0.9f, false); // fireballs + sparks + smoke, no ground ring/scorch
        _ps.Flash(pos, s * 3f, Col(1f, 1f, 0.95f, 1f), 0.12f);
        ref var sh = ref _ps.Emit(); // air shock
        sh.Pos = pos; sh.Life = 0.35f + 0.1f * s; sh.Size0 = s; sh.Size1 = s * 7f;
        sh.C0 = Col(1f, 0.9f, 0.75f, 0.5f); sh.C1 = sh.C0 with { W = 0 }; sh.Shape = PShape.Ring; sh.Blend = PBlend.Additive;
        _ps.Sparks(pos, Num(10 + s * 8), 10f * MathF.Sqrt(s), Col(1f, 0.8f, 0.45f), 0.9f, 0.1f, 9f);
        _ps.Debris(pos, Num(4 + s * 3), 5f + s * 2f, 0.12f + s * 0.06f);
        if (s >= 2f)
            for (int i = 0; i < Num(8); i++)
                _ps.Smoke(pos + _ps.RandDir() * s, s * 1.1f, 4f, Col(0.3f, 0.29f, 0.28f, 0.5f), default, 2.6f);
    }

    // ------------------------------------------------------------------ nuke

    private void NukeDetonation(Vector3 pos, float radius)
    {
        float R = Math.Clamp(radius, 6f, 120f);
        float lod = MathF.Max(0.6f, _ps.Lod);
        int N(float n) => Math.Max(1, (int)MathF.Round(n * lod));
        var upV = Vector3.UnitY;
        float gy = _ps.GroundAt(pos);
        var g = new Vector3(pos.X, gy, pos.Z);
        float H = R * 1.3f;   // mushroom cap altitude above the ground
        float fb = R * 0.22f; // initial fireball height
        const float D = 0.33f; // drag of the rising cloud: settles over ~8-10 s
        var fbPos = g + upV * fb;

        StartNukeFlash(fbPos, R);

        // 1. Blinding flash (alpha > 1 = plateau before the fade) and the glow of the rising fireball.
        {
            ref var p = ref _ps.Emit();
            p.Pos = fbPos; p.Life = 0.75f; p.Size0 = R * 2.5f; p.Size1 = R * 9f;
            p.C0 = Col(1f, 1f, 1f, 3f); p.C1 = Col(1f, 0.85f, 0.6f, 0); p.Shape = PShape.Glow; p.Blend = PBlend.Additive;
            ref var q = ref _ps.Emit();
            q.Pos = fbPos; q.Vel = upV * ((H - fb) * D); q.Drag = D; q.Life = 4.5f; q.Size0 = R * 2.2f; q.Size1 = R * 1.4f;
            q.C0 = Col(1f, 0.7f, 0.35f, 1.3f); q.C1 = Col(0.8f, 0.25f, 0.08f, 0); q.Shape = PShape.Glow; q.Blend = PBlend.Additive;
        }

        // 2. Shockwaves: spherical air shock (camera-facing ring) + fast and slow ground rings (to ~1.3 R).
        {
            ref var a = ref _ps.Emit();
            a.Pos = fbPos; a.Life = 0.7f; a.Size0 = R * 0.6f; a.Size1 = R * 4.2f;
            a.C0 = Col(1f, 0.95f, 0.85f, 0.55f); a.C1 = a.C0 with { W = 0 }; a.Shape = PShape.Ring; a.Blend = PBlend.Additive;
            var ringPos = g + upV * (R * 0.04f);
            ref var r1 = ref _ps.Emit();
            r1.Pos = ringPos; r1.Life = 1.2f; r1.Size0 = R * 0.3f; r1.Size1 = R * 3.2f;
            r1.C0 = Col(1f, 0.92f, 0.75f, 1f); r1.C1 = Col(1f, 0.7f, 0.4f, 0); r1.Shape = PShape.Ring; r1.Mode = PMode.Flat; r1.Blend = PBlend.Additive;
            ref var r2 = ref _ps.Emit();
            r2.Pos = ringPos; r2.Life = 2.6f; r2.Size0 = R * 0.5f; r2.Size1 = R * 3.9f;
            r2.C0 = Col(0.85f, 0.7f, 0.5f, 0.45f); r2.C1 = r2.C0 with { W = 0 }; r2.Shape = PShape.Ring; r2.Mode = PMode.Flat; r2.Blend = PBlend.Additive;
        }

        // 3. Fire: an initial hemispherical burst, then hot blobs rising with the cloud and burning out.
        for (int i = 0; i < N(28); i++)
        {
            ref var p = ref _ps.Emit();
            p.Pos = g + upV * (R * 0.1f) + _ps.RandDir() * (R * 0.15f);
            p.Vel = _ps.RandUpDir(0.05f) * (R * _ps.Range(1.5f, 2.5f)); p.Drag = 2.5f;
            p.Life = _ps.Range(1f, 1.8f); p.Size0 = R * 0.25f; p.Size1 = R * 0.5f; p.RotVel = _ps.Range(-1f, 1f);
            p.C0 = Col(1f, 0.8f, 0.45f, 1.3f); p.C1 = Col(0.6f, 0.15f, 0.03f, 0); p.Shape = PShape.Fire; p.Blend = PBlend.Additive;
        }
        for (int i = 0; i < N(36); i++)
        {
            var start = fbPos + _ps.RandDir() * (R * 0.18f);
            var fin = g + RandHoriz() * (R * 0.5f * MathF.Sqrt(_ps.Rand())) + upV * (H * _ps.Range(0.85f, 1.1f));
            ref var p = ref _ps.Emit();
            p.Pos = start; p.Vel = (fin - start) * D; p.Drag = D;
            p.Life = _ps.Range(2.5f, 4.5f); p.Size0 = R * _ps.Range(0.3f, 0.45f); p.Size1 = R * 0.6f; p.RotVel = _ps.Range(-0.6f, 0.6f);
            p.C0 = Col(1f, 0.72f, 0.38f, 1.4f); p.C1 = Col(0.55f, 0.12f, 0.03f, 0); p.Shape = PShape.Fire; p.Blend = PBlend.Additive;
        }

        // 4. Mushroom cap: dense smoke rising with the fireball and spreading into a torus-weighted head.
        for (int i = 0; i < N(120); i++)
        {
            float rf = R * (0.12f + 0.62f * MathF.Sqrt(_ps.Rand()));
            float hf = H * (1f + _ps.Range(-0.12f, 0.16f)) - rf / R * H * 0.12f; // rim rolls slightly lower
            var start = fbPos + _ps.RandDir() * (R * 0.15f);
            var fin = g + RandHoriz() * rf + upV * hf;
            ref var p = ref _ps.Emit();
            p.Pos = start; p.Vel = (fin - start) * D; p.Drag = D; p.Age = -_ps.Range(0f, 0.35f);
            p.Life = _ps.Range(9f, 12f); p.Size0 = R * _ps.Range(0.22f, 0.32f); p.Size1 = R * _ps.Range(0.5f, 0.65f);
            p.RotVel = _ps.Range(-0.25f, 0.25f);
            float sh = _ps.Range(0.85f, 1.1f);
            p.C0 = Col(0.46f * sh, 0.34f * sh, 0.26f * sh, 2.2f); p.C1 = Col(0.5f, 0.48f, 0.47f, 0);
            p.Shape = PShape.Smoke; p.Blend = PBlend.Alpha;
            // Some cap puffs shed slowly falling embers once the head has formed.
            if (i % 3 == 0)
            {
                float delay = _ps.Range(2f, 5f);
                ref var e = ref _ps.Emit();
                e.Pos = start + (fin - start) * (1f - MathF.Exp(-D * delay)); e.Age = -delay;
                e.Vel = new Vector3(_ps.Range(-1f, 1f), -_ps.Range(0.5f, 2f), _ps.Range(-1f, 1f)); e.Gravity = 0.6f; e.Drag = 0.5f;
                e.Life = _ps.Range(2f, 4f); e.Size0 = _ps.Range(0.5f, 0.9f); e.Size1 = 0.1f;
                e.C0 = Col(1f, 0.6f, 0.25f, 1f); e.C1 = Col(0.8f, 0.25f, 0.05f, 0); e.Shape = PShape.Glow; e.Blend = PBlend.Additive;
            }
        }

        // 5. Stem: puffs leave the ground over ~2.5 s and settle at increasing heights under the cap.
        for (int i = 0; i < N(60); i++)
        {
            var hd = RandHoriz();
            var start = g + upV * (R * 0.05f) + hd * (R * 0.1f * _ps.Rand());
            var fin = g + hd * (R * _ps.Range(0f, 0.09f)) + upV * (H * _ps.Range(0.08f, 0.88f));
            ref var p = ref _ps.Emit();
            p.Pos = start; p.Vel = (fin - start) * D; p.Drag = D; p.Age = -_ps.Range(0.1f, 2.5f);
            p.Life = _ps.Range(8f, 11f); p.Size0 = R * 0.2f; p.Size1 = R * 0.38f; p.RotVel = _ps.Range(-0.3f, 0.3f);
            p.C0 = Col(0.4f, 0.33f, 0.28f, 2f); p.C1 = Col(0.48f, 0.46f, 0.45f, 0);
            p.Shape = PShape.Smoke; p.Blend = PBlend.Alpha;
        }
        for (int i = 0; i < N(14); i++)
        {
            var start = g + upV * (R * 0.05f) + RandHoriz() * (R * 0.08f * _ps.Rand());
            var fin = g + upV * (H * _ps.Range(0.1f, 0.5f));
            ref var p = ref _ps.Emit();
            p.Pos = start; p.Vel = (fin - start) * D; p.Drag = D; p.Age = -_ps.Range(0f, 0.8f);
            p.Life = _ps.Range(1.5f, 3f); p.Size0 = R * 0.18f; p.Size1 = R * 0.3f;
            p.C0 = Col(1f, 0.6f, 0.25f, 1f); p.C1 = Col(0.5f, 0.1f, 0.02f, 0); p.Shape = PShape.Fire; p.Blend = PBlend.Additive;
        }

        // 6. Base surge: a dust ring rolling outwards along the ground, plus lingering dust around the base.
        int ns = N(56);
        for (int i = 0; i < ns; i++)
        {
            float a = (i + _ps.Range(-0.4f, 0.4f)) / ns * MathF.Tau;
            var hd = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            var far = g + hd * (R * _ps.Range(0.95f, 1.3f));
            var start = g + hd * (R * 0.12f) + upV * (R * 0.06f);
            var fin = new Vector3(far.X, MathF.Max(_ps.GroundAt(far), gy - R * 0.3f) + R * 0.08f, far.Z);
            const float Ds = 1.1f;
            ref var p = ref _ps.Emit();
            p.Pos = start; p.Vel = (fin - start) * Ds; p.Drag = Ds;
            p.Life = _ps.Range(5f, 7.5f); p.Size0 = R * 0.14f; p.Size1 = R * _ps.Range(0.4f, 0.55f); p.RotVel = _ps.Range(-0.4f, 0.4f);
            p.C0 = Col(0.52f, 0.44f, 0.34f, 1.5f); p.C1 = Col(0.55f, 0.5f, 0.45f, 0);
            p.Shape = PShape.Smoke; p.Blend = PBlend.Alpha;
        }
        for (int i = 0; i < N(24); i++)
        {
            var at = g + RandHoriz() * (R * _ps.Range(0.1f, 0.6f));
            ref var p = ref _ps.Emit();
            p.Pos = new Vector3(at.X, _ps.GroundAt(at) + R * 0.05f, at.Z);
            p.Vel = new Vector3(_ps.Range(-0.5f, 0.5f), _ps.Range(0.5f, 1.5f), _ps.Range(-0.5f, 0.5f)); p.Drag = 0.3f;
            p.Age = -_ps.Range(0.3f, 1.5f); p.Life = _ps.Range(6f, 9f); p.Size0 = R * 0.2f; p.Size1 = R * 0.45f;
            p.RotVel = _ps.Range(-0.3f, 0.3f);
            p.C0 = Col(0.45f, 0.4f, 0.34f, 1.2f); p.C1 = Col(0.5f, 0.48f, 0.45f, 0);
            p.Shape = PShape.Smoke; p.Blend = PBlend.Alpha;
        }

        // 7. Molten ground glow.
        {
            ref var p = ref _ps.Emit();
            p.Pos = g + upV * (R * 0.03f); p.Life = 7f; p.Size0 = R * 1f; p.Size1 = R * 1.7f;
            p.C0 = Col(1f, 0.45f, 0.12f, 1.2f); p.C1 = Col(0.6f, 0.1f, 0.02f, 0);
            p.Shape = PShape.Glow; p.Mode = PMode.Flat; p.Blend = PBlend.Additive;
        }

        // 8. Debris and embers.
        _ps.Debris(g + upV * (R * 0.05f), N(40), R * 0.75f, 0.35f + R * 0.012f);
        _ps.Sparks(g + upV * (R * 0.15f), N(90), R * 1.1f, Col(1f, 0.6f, 0.25f), 2.4f, 0.35f, 6f);

        // 9. Scorch: tiled decals so it follows the terrain (radius ~0.8 R), dark for about a minute then fading.
        NukeScorch(g, R * 0.8f, 0f, 1, 1.6f);
        for (int i = 0; i < 7; i++) NukeScorch(g, R * 0.55f, R * 0.3f, i / 7f, 1.4f);
        for (int i = 0; i < 14; i++) NukeScorch(g, R * 0.45f, R * 0.6f, i / 14f, 1.2f);
    }

    private void NukeScorch(Vector3 center, float size, float dist, float frac, float alpha)
    {
        float a = (frac + _ps.Range(-0.02f, 0.02f)) * MathF.Tau;
        var at = center + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (dist * _ps.Range(0.9f, 1.1f));
        ref var p = ref _ps.Emit();
        p.Pos = new Vector3(at.X, _ps.GroundAt(at) + 0.07f, at.Z);
        p.Life = _ps.Range(140f, 170f); p.Size0 = size * _ps.Range(0.9f, 1.1f); p.Size1 = p.Size0;
        p.C0 = Col(0.06f, 0.05f, 0.04f, alpha); p.C1 = Col(0.06f, 0.05f, 0.04f, 0);
        p.Shape = PShape.Scorch; p.Mode = PMode.Flat; p.Blend = PBlend.Decal;
    }

    // ------------------------------------------------------------------ screen flash

    private float NukeFlashAlpha()
    {
        float t = _nukeFlashT / NukeFlashDuration;
        if (_nukeFlashI <= 0f || t >= 1f) return 0f;
        float f = t < 0.08f ? 1f : (1f - (t - 0.08f) / 0.92f) * (1f - (t - 0.08f) / 0.92f);
        return _nukeFlashI * 0.92f * f;
    }

    /// <summary>Starts the full-screen flash if the blast is on screen (stronger when close) or near the camera.</summary>
    private void StartNukeFlash(Vector3 fireball, float R)
    {
        var fv = FrameView.Current;
        float d = Vector3.Distance(fv.CamPos, fireball);
        float intensity;
        if (fv.SphereVisible(fireball, R * 0.8f)) intensity = Math.Clamp(1.35f - d / (R * 14f), 0.4f, 1f);
        else if (d < Atmosphere.CameraDistance * 1.5f + R * 3f) intensity = 0.3f;
        else return;
        if (intensity * 0.92f <= NukeFlashAlpha()) return;
        _nukeFlashI = intensity;
        _nukeFlashT = 0f;
    }

    /// <summary>Full-screen white/orange flash fading over ~1 s after an on-screen (or nearby) nuclear
    /// detonation. Call once per frame after EndMode3D (2D pass); uses real frame time.</summary>
    public void DrawOverlay2D()
    {
        if (_nukeFlashI <= 0f) return;
        _nukeFlashT += Raylib.GetFrameTime();
        float a = NukeFlashAlpha();
        if (a <= 0.004f) { _nukeFlashI = 0f; return; }
        float k = Math.Clamp(_nukeFlashT / NukeFlashDuration * 1.6f, 0f, 1f); // white -> orange
        var c = new Color((byte)255, (byte)(255 - 80 * k), (byte)(255 - 170 * k), (byte)(a * 255));
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), c);
    }
}
