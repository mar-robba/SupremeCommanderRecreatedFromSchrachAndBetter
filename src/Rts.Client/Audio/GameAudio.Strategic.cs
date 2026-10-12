using System.Numerics;
using Raylib_cs;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Audio;

/// <summary>
/// Strategic sounds: shield hits / collapse / restore, tactical and strategic missile launches, intercepts,
/// nuclear detonations (heard across the whole map) and the global "strategic launch" klaxon, which every player
/// hears for every nuke launch (own ones included), as in SupCom.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>Highest <see cref="SimSnapshot.LaunchSerial"/> already announced (int.MinValue = no snapshot seen yet).</summary>
    private int _lastLaunchSerial = int.MinValue;
    private double _lastLaunchAlert = -100;

    /// <summary>Size of MissileLaunch / MissileIntercepted effects: 1.2 = tactical, 3 = strategic.</summary>
    private const float StrategicSize = 2f;

    private int HandleStrategicEffect(in EffectEvent e)
    {
        bool own = e.Army == _ctx.LocalArmy;
        switch (e.Kind)
        {
            case EffectKind.ShieldHit:
            {
                // Size = damage scale (0.3..4): harder hits are louder and a bit deeper.
                float vol = Math.Clamp(0.55f + 0.25f * e.Size, 0.6f, 1.3f);
                float pitch = Math.Clamp(1.12f - 0.07f * e.Size, 0.85f, 1.1f);
                return At(SoundId.ShieldHit, e.Pos, vol, pitch) ? 1 : 0;
            }
            case EffectKind.ShieldDown:
                // Size = dome radius: bigger domes collapse deeper. The owner always hears a collapse faintly.
                At(SoundId.ShieldDown, e.Pos, 1f, Math.Clamp(1.2f - e.Size * 0.012f, 0.85f, 1.1f), minGain: own ? 0.18f : 0f);
                return 1;
            case EffectKind.ShieldUp:
                At(SoundId.ShieldUp, e.Pos, own ? 1f : 0.7f, Math.Clamp(1.2f - e.Size * 0.012f, 0.85f, 1.1f));
                return 0;
            case EffectKind.MissileLaunch:
                if (e.Size >= StrategicSize)
                {
                    At(SoundId.NukeLaunch, e.Pos, 1.2f, 1f, minGain: 0.12f);
                    return 1;
                }
                return At(SoundId.TacticalLaunch, e.Pos, 1f) ? 1 : 0;
            case EffectKind.MissileIntercepted:
            {
                bool nuke = e.Size >= StrategicSize;
                return At(SoundId.Intercept, e.Pos, nuke ? 1.4f : 1f, nuke ? 0.75f : 1f, minGain: nuke ? 0.15f : 0f) ? 1 : 0;
            }
            case EffectKind.Nuke:
                PlayNuke(e.Pos, e.Size);
                return 1;
        }
        return 0;
    }

    /// <summary>
    /// Nuclear blast: full volume within the blast area around the camera focus, then ≈1/distance with a floor,
    /// so it is heard (duller, slightly lower) from anywhere on the map; zoom ducking does not apply.
    /// </summary>
    private void PlayNuke(Vector3 pos, float radius)
    {
        var cam = _ctx.Camera;
        float dx = pos.X - cam.Focus.X, dz = pos.Z - cam.Focus.Z;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        float near = 60f + cam.Distance * 0.8f + MathF.Max(0f, radius);
        float g = dist <= near ? 1f : MathF.Max(0.3f, near / dist);
        Spatial(pos, SoundBank.Info(SoundId.NukeExplosion).Range, out _, out float pan);
        float pitch = SoundBank.NukePitch * (0.97f + 0.06f * (float)_rand.NextDouble()) * (1f - 0.1f * (1f - g));
        _reqs.Add(new Req { Id = SoundId.NukeExplosion, Vol = 1.3f * g, Pan = pan, Pitch = pitch });
    }

    /// <summary>Klaxon for every new strategic launch (each serial once; launches before the first snapshot
    /// seen are ignored). At most one klaxon per 2 s when several nukes fly at once.</summary>
    private void CheckLaunches(SimSnapshot cur)
    {
        if (_lastLaunchSerial == int.MinValue) { _lastLaunchSerial = cur.LaunchSerial; return; }
        if (cur.LaunchSerial <= _lastLaunchSerial) return;
        _lastLaunchSerial = cur.LaunchSerial;
        double now = Raylib.GetTime();
        if (now - _lastLaunchAlert < 2.0) return;
        _lastLaunchAlert = now;
        PlayUi(SoundId.StrategicAlert);
    }
}
