using System.Numerics;
using Rts.Client.Audio;

namespace Rts.Client.Rendering;

/// <summary>
/// (9b) Particles and sound cues of the death sequences: burning smoke trail and ground/water crash of aircraft;
/// deck fires, secondary blasts and churning foam of sinking ships; dust, debris and rumble of collapsing
/// structures; cook-off blasts and the landing dust of experimentals; the overloading core of a dying commander.
/// Emission is rate-limited per sequence (game time) and thinned by distance and the particle LOD.
/// </summary>
public sealed partial class DeathAnimator
{
    private static Vector4 C(float r, float g, float b, float a = 1) => new(r, g, b, a);

    /// <summary>Close enough for the continuous emitters (one-shot bursts always play).</summary>
    private bool Near(Vector3 p) =>
        Vector3.DistanceSquared(FrameView.Current.CamPos, p) < 420f * 420f && _ps.Rand() < MathF.Max(0.35f, _ps.Lod);

    private void OnStart(ref Anim a)
    {
        ref readonly var s = ref a.Plan.S;
        switch (s.Kind)
        {
            case DeathKind.AirCrash:
                UnitFxSounds.Play(SoundId.AircraftFall, s.Pos, s.Huge ? 1.2f : 0.8f, s.Huge ? 0.7f : Math.Clamp(1.25f - s.Radius * 0.12f, 0.8f, 1.2f));
                break;
            case DeathKind.Collapse:
            {
                var g = new Vector3(s.End.X, s.Floor, s.End.Z);
                _ps.Ring(g, s.Radius * 0.6f, s.Radius * 2.6f, 0.9f, C(0.55f, 0.48f, 0.38f, 0.35f));
                break;
            }
            case DeathKind.CommanderFall:
                _ps.Flash(s.Pos + new Vector3(0, s.Height * 0.55f, 0), s.Radius * 3f, C(0.5f, 0.85f, 1f, 0.9f), 0.25f);
                _ps.Sparks(s.Pos + new Vector3(0, s.Height * 0.55f, 0), 24, 9f, C(0.6f, 0.9f, 1f), 0.6f, 0.1f);
                break;
        }
        a.NextBurst = 0.15f;
    }

    private void Emit(ref Anim a, float t0, float dt)
    {
        ref readonly var s = ref a.Plan.S;
        var pose = DeathAnim.Pose(in a.Plan, a.T);
        float u = a.Plan.Duration > 0 ? Math.Clamp(a.T / a.Plan.Duration, 0f, 1f) : 1f;
        a.EmitAcc += dt;
        switch (s.Kind)
        {
            case DeathKind.AirCrash:
                if (a.T >= a.Plan.ImpactTime) { if (!a.ImpactDone) { a.ImpactDone = true; Crash(in a.Plan); } break; }
                if (a.EmitAcc >= 0.035f)
                {
                    a.EmitAcc = 0;
                    if (!Near(pose.Pos)) break;
                    float sz = MathF.Max(0.35f, s.Radius * 0.45f);
                    var c = pose.Pos + new Vector3(0, s.Height * 0.4f, 0);
                    _ps.Smoke(c, sz, 1.8f + u, C(0.1f, 0.095f, 0.09f, 0.6f), Vector3.Zero, 3.2f);
                    ref var f = ref _ps.Emit();
                    f.Pos = c; f.Vel = new Vector3(0, 0.6f, 0); f.Life = _ps.Range(0.18f, 0.3f);
                    f.Size0 = sz * 1.3f; f.Size1 = sz * 0.4f; f.Drag = 2f; f.RotVel = _ps.Range(-2f, 2f);
                    f.C0 = C(1f, 0.6f, 0.2f, 1f); f.C1 = C(0.6f, 0.15f, 0.03f, 0f); f.Shape = PShape.Fire; f.Blend = PBlend.Additive;
                    if (s.Huge && a.T >= a.NextBurst) { a.NextBurst = a.T + _ps.Range(0.25f, 0.5f); _ps.Explosion(c + _ps.RandDir() * s.Radius * 0.4f, s.Radius * 0.2f, false); }
                }
                break;

            case DeathKind.ShipSink:
            {
                if (!a.ImpactDone && a.T >= a.Plan.ImpactTime)
                {
                    a.ImpactDone = true;
                    UnitFxSounds.Play(SoundId.ShipSink, s.End, s.Huge ? 1.3f : 1f, s.Huge ? 0.75f : Math.Clamp(1.2f - s.Height * 0.06f, 0.8f, 1.15f));
                }
                if (u < 0.5f && a.T >= a.NextBurst)
                {
                    a.NextBurst = a.T + _ps.Range(0.5f, 1.3f);
                    var b = Deck(in pose, in s);
                    _ps.Explosion(b, MathF.Max(0.4f, s.Radius * 0.22f), false);
                    UnitFxSounds.Play(SoundId.ExplosionSmall, b, 0.55f, _ps.Range(0.85f, 1.05f));
                }
                if (a.EmitAcc < 0.07f) break;
                a.EmitAcc = 0;
                if (!Near(pose.Pos)) break;
                if (u < 0.7f && pose.Pos.Y > s.Water - s.Height * 0.5f)
                {
                    var d = Deck(in pose, in s);
                    _ps.Fireball(d, MathF.Max(0.3f, s.Radius * 0.12f), 0.5f);
                    _ps.Smoke(d + new Vector3(0, 0.4f, 0), MathF.Max(0.5f, s.Radius * 0.25f), 3f, C(0.09f, 0.085f, 0.08f, 0.5f), new Vector3(0.2f, 1.4f, 0.1f), 2.8f);
                }
                if (u > 0.1f && u < 0.92f) Foam(new Vector3(pose.Pos.X, s.Water + 0.06f, pose.Pos.Z), MathF.Max(0.8f, s.Radius * 0.7f), 1f - u);
                break;
            }

            case DeathKind.Collapse:
            {
                var g = new Vector3(s.End.X, s.Floor, s.End.Z);
                if (!a.ImpactDone && a.T >= a.Plan.ImpactTime)
                {
                    a.ImpactDone = true;
                    _ps.Debris(g + new Vector3(0, s.Height * 0.5f, 0), 6 + (int)(s.Radius * 3f), 5f + s.Radius, 0.15f + s.Radius * 0.05f);
                    UnitFxSounds.Play(SoundId.StructureCrumble, g, s.Huge ? 1.3f : 1f, s.Huge ? 0.7f : Math.Clamp(1.15f - s.Radius * 0.06f, 0.75f, 1.1f));
                }
                if (u > 0.85f || a.EmitAcc < 0.06f) break;
                a.EmitAcc = 0;
                if (!Near(g)) break;
                float ang = _ps.Rand() * MathF.Tau, r = s.Radius * _ps.Range(0.5f, 1.1f);
                var dir = new Vector3(MathF.Cos(ang), 0, MathF.Sin(ang));
                var at = g + dir * r;
                at.Y = _ps.GroundAt(at) + 0.2f;
                _ps.Smoke(at, MathF.Max(0.5f, s.Radius * 0.4f), 2.6f, C(0.45f, 0.4f, 0.33f, 0.42f), dir * 1.2f + new Vector3(0, 0.5f, 0), 2.6f);
                if (_ps.Rand() < 0.4f) _ps.Fireball(pose.Pos + new Vector3(_ps.Range(-0.4f, 0.4f) * s.Radius, s.Height * pose.Squash * 0.6f, _ps.Range(-0.4f, 0.4f) * s.Radius), MathF.Max(0.3f, s.Radius * 0.15f), 0.6f);
                break;
            }

            case DeathKind.BigFall:
            {
                if (a.T < a.Plan.ImpactTime && a.T >= a.NextBurst)
                {
                    a.NextBurst = a.T + _ps.Range(0.14f, 0.3f);
                    var b = pose.Pos + new Vector3(_ps.Range(-0.5f, 0.5f) * s.Radius, s.Height * _ps.Range(0.3f, 0.9f), _ps.Range(-0.5f, 0.5f) * s.Radius);
                    _ps.Explosion(b, MathF.Max(0.6f, s.Radius * 0.15f), false);
                    _ps.Sparks(b, 10, 8f, C(1f, 0.75f, 0.4f), 0.6f, 0.12f);
                    if (_ps.Rand() < 0.5f) UnitFxSounds.Play(SoundId.ExplosionSmall, b, 0.7f, _ps.Range(0.75f, 0.95f));
                }
                if (!a.LandDone && u >= 0.78f)
                {
                    a.LandDone = true;
                    var g = new Vector3(pose.Pos.X, s.Floor, pose.Pos.Z);
                    _ps.Ring(g, s.Radius * 0.5f, s.Radius * 3.2f, 1.1f, C(0.55f, 0.48f, 0.38f, 0.4f));
                    for (int i = 0; i < 10; i++)
                    {
                        float ang = i * MathF.Tau / 10f;
                        var dir = new Vector3(MathF.Cos(ang), 0, MathF.Sin(ang));
                        _ps.Smoke(g + dir * s.Radius * 0.8f + new Vector3(0, 0.4f, 0), s.Radius * 0.35f, 3f, C(0.45f, 0.4f, 0.33f, 0.45f), dir * 2.5f, 2.6f);
                    }
                    UnitFxSounds.Play(SoundId.StructureCrumble, g, 1.3f, 0.65f, minGain: 0.05f);
                }
                if (a.EmitAcc < 0.08f) break;
                a.EmitAcc = 0;
                if (!Near(pose.Pos)) break;
                var top = pose.Pos + new Vector3(_ps.Range(-0.4f, 0.4f) * s.Radius, s.Height * pose.Squash * 0.7f, _ps.Range(-0.4f, 0.4f) * s.Radius);
                _ps.Fireball(top, MathF.Max(0.4f, s.Radius * 0.12f), 0.7f);
                _ps.Smoke(top + new Vector3(0, 0.5f, 0), MathF.Max(0.8f, s.Radius * 0.3f), 3.5f, C(0.1f, 0.095f, 0.09f, 0.5f), new Vector3(0.2f, 1.6f, 0.1f), 2.8f);
                break;
            }

            case DeathKind.CommanderFall:
            {
                if (a.T >= a.Plan.ImpactTime || a.EmitAcc < 0.06f) break;
                a.EmitAcc = 0;
                float k = a.T / a.Plan.ImpactTime;
                var core = pose.Pos + new Vector3(0, s.Height * 0.5f, 0);
                _ps.Flash(core, s.Radius * (0.8f + 2.2f * k * k), C(0.45f, 0.8f, 1f, 0.55f + 0.4f * k), 0.12f);
                _ps.Sparks(core + _ps.RandDir() * s.Radius * 0.5f, 3 + (int)(6 * k), 7f, C(0.65f, 0.9f, 1f), 0.35f, 0.08f, 3f);
                if (_ps.Rand() < 0.35f) _ps.Explosion(core + _ps.RandDir() * s.Radius * 0.5f, 0.35f, false);
                break;
            }
        }
    }

    /// <summary>A random point on a ship's deck.</summary>
    private Vector3 Deck(in DeathPose pose, in DeathStart s)
    {
        var fwd = new Vector3(MathF.Cos(pose.Yaw), 0, MathF.Sin(pose.Yaw));
        var side = new Vector3(-fwd.Z, 0, fwd.X);
        return pose.Pos + fwd * (_ps.Range(-0.8f, 0.8f) * s.Radius) + side * (_ps.Range(-0.2f, 0.2f) * s.Radius) + new Vector3(0, s.Height * 0.55f, 0);
    }

    private void Foam(Vector3 at, float r, float strength)
    {
        for (int i = 0; i < 2; i++)
        {
            ref var f = ref _ps.Emit();
            float ang = _ps.Rand() * MathF.Tau, d = _ps.Rand() * r;
            f.Pos = at + new Vector3(MathF.Cos(ang) * d, 0, MathF.Sin(ang) * d);
            f.Vel = new Vector3(MathF.Cos(ang), 0, MathF.Sin(ang)) * 0.5f;
            f.Drag = 0.7f; f.Life = _ps.Range(1.8f, 3f); f.Size0 = r * 0.4f; f.Size1 = r * 1.2f; f.RotVel = _ps.Range(-0.3f, 0.3f);
            f.C0 = C(0.9f, 0.95f, 1f, 0.55f * strength + 0.1f); f.C1 = C(0.9f, 0.95f, 1f, 0);
            f.Shape = PShape.Smoke; f.Mode = PMode.Flat; f.Blend = PBlend.Alpha;
        }
    }

    /// <summary>Aircraft hits the ground (explosion, debris, scorch, dust) or the water (splash column, foam ring).</summary>
    private void Crash(in DeathPlan p)
    {
        ref readonly var s = ref p.S;
        var at = new Vector3(s.End.X, p.CrashY, s.End.Z);
        float r = MathF.Max(0.6f, s.Radius * (s.Huge ? 0.7f : 0.9f));
        bool water = s.Water > s.Floor + 0.3f;
        _ps.Explosion(at + new Vector3(0, 0.3f, 0), r, !water);
        if (water)
        {
            for (int i = 0; i < 14 + (int)(r * 6); i++)
            {
                ref var d = ref _ps.Emit();
                d.Pos = at + new Vector3(_ps.Range(-0.4f, 0.4f) * r, 0.1f, _ps.Range(-0.4f, 0.4f) * r);
                d.Vel = new Vector3(_ps.Range(-1.5f, 1.5f), _ps.Range(5f, 10f) * MathF.Sqrt(r), _ps.Range(-1.5f, 1.5f));
                d.Gravity = 14f; d.Drag = 0.6f; d.Life = _ps.Range(0.8f, 1.4f); d.Size0 = 0.35f * r; d.Size1 = 0.9f * r;
                d.C0 = C(0.92f, 0.96f, 1f, 0.7f); d.C1 = C(0.9f, 0.95f, 1f, 0); d.Shape = PShape.Smoke; d.Blend = PBlend.Alpha;
            }
            _ps.Ring(at, r * 0.5f, r * 4f, 1.2f, C(0.9f, 0.95f, 1f, 0.5f));
            Foam(at + new Vector3(0, 0.06f, 0), r * 1.5f, 1f);
        }
        else
        {
            _ps.Debris(at + new Vector3(0, 0.4f, 0), 8 + (int)(r * 4), 6f + r * 2f, 0.14f + r * 0.06f);
            for (int i = 0; i < 4; i++)
                _ps.Smoke(at + _ps.RandUpDir() * r * 0.5f, r * 0.9f, 3.5f, C(0.4f, 0.36f, 0.3f, 0.45f), new Vector3(0, 0.8f, 0), 2.6f);
        }
        UnitFxSounds.Play(SoundId.AircraftCrash, at, s.Huge ? 1.4f : 1f, s.Huge ? 0.7f : Math.Clamp(1.2f - s.Radius * 0.1f, 0.8f, 1.15f), minGain: s.Huge ? 0.08f : 0f);
    }
}
