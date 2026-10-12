using System.Numerics;
using Raylib_cs;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Strategic weapon visuals: tactical/strategic missiles and their interceptors (projectiles), shield
/// hit/down/up bursts, missile launch and mid-air interception. The nuclear detonation (<c>EffectKind.Nuke</c>, real
/// missiles and sandbox instant blasts alike) is drawn by <see cref="NukeFx"/>.
/// </summary>
public sealed partial class EffectsRenderer
{

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
}
