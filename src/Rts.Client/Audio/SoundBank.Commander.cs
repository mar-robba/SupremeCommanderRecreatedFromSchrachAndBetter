namespace Rts.Client.Audio;

/// <summary>
/// Commander sound recipes: the ACU Overcharge shot (energy discharge on top of the regular gun) and its impact
/// (electric blast on top of the regular explosion), and the commander death blast (a quantum core bursting: a
/// sizzling electric shriek over a nuke-like boom, shorter than <see cref="SoundId.NukeExplosion"/>), and the Personal
/// Teleporter: a rising hum when the charge starts, an imploding zap at the origin, a zap-and-thump at the arrival.
/// </summary>
public static partial class SoundBank
{
    private static SoundInfo CommanderInfo(SoundId id) => id switch
    {
        SoundId.OverchargeFire => new(0.7f, 3, 1.6f, 2),
        SoundId.OverchargeHit => new(0.75f, 3, 1.7f, 2),
        SoundId.CommanderBlast => new(1f, 2, 4f, 1),
        SoundId.TeleportCharge => new(0.55f, 2, 1.5f, 1),
        SoundId.TeleportOut => new(0.7f, 2, 1.8f, 1),
        SoundId.TeleportIn => new(0.8f, 2, 1.8f, 1),
        _ => AlertInfo(id), // SoundBank.Alerts.cs
    };

    private static float[] GenerateCommander(SoundId id, ref SynthRng rng) => id switch
    {
        SoundId.OverchargeFire => OverchargeFire(ref rng),
        SoundId.OverchargeHit => OverchargeHit(ref rng),
        SoundId.CommanderBlast => CommanderBlast(ref rng),
        SoundId.TeleportCharge => TeleportCharge(ref rng),
        SoundId.TeleportOut => TeleportOut(ref rng),
        SoundId.TeleportIn => TeleportIn(ref rng),
        _ => GenerateAlert(id, ref rng), // SoundBank.Alerts.cs
    };

    /// <summary>Overcharge shot: a deep electric "vwomm" (falling square + saw), a sub thump and a hissing zap tail.</summary>
    private static float[] OverchargeFire(ref SynthRng r)
    {
        var b = Synth.Alloc(1.0f);
        float f0 = r.Range(520, 600);
        Synth.Sweep(b, 0, 0.75f, f0, 70, 0.18f, t => Synth.AD(t, 0.004f, 0.22f), 0.55f, wave: 1);
        Synth.Sweep(b, 0, 0.6f, f0 * 2.02f, 160, 0.12f, t => Synth.AD(t, 0.003f, 0.14f), 0.3f, wave: 2);
        Synth.Sweep(b, 0, 0.35f, 3200, 900, 0.06f, t => Synth.AD(t, 0.001f, 0.05f), 0.2f);
        Synth.Thump(b, 0, 95, 38, 0.06f, 0.003f, 0.16f, 0.8f);
        Synth.Noise(b, ref r, 0, 0.0008f, 0.03f, 0.7f, 9000, 2500, 0.03f, hp: 900);
        Synth.BandNoise(b, ref r, 0.02f, 0.8f, 4200, 1600, 0.25f, 3f, t => Synth.AD(t, 0.01f, 0.18f), 0.35f);
        Synth.Crackle(b, ref r, 0.02f, 0.5f, 70, 0.22f, 0.2f);
        Synth.Saturate(b, 1.8f);
        return b;
    }

    /// <summary>Overcharge impact: a bright electric crack, a punchy low boom and a fading sizzle of arcs.</summary>
    private static float[] OverchargeHit(ref SynthRng r)
    {
        var b = Synth.Alloc(1.4f);
        Synth.Noise(b, ref r, 0, 0.0004f, 0.02f, 1f, 12000, 4000, 0.02f, hp: 1500);
        Synth.Thump(b, 0, 120, 34, 0.08f, 0.002f, 0.22f, 1.1f);
        Synth.Noise(b, ref r, 0.003f, 0.006f, 0.25f, 0.6f, 3000, 300, 0.2f);
        Synth.Sweep(b, 0, 0.5f, 1900, 240, 0.1f, t => Synth.AD(t, 0.002f, 0.1f), 0.3f, wave: 1);
        Synth.BandNoise(b, ref r, 0.03f, 1.2f, 5200, 2200, 0.5f, 3.5f, t => Synth.AD(t, 0.02f, 0.3f), 0.4f);
        Synth.Crackle(b, ref r, 0.03f, 1f, 110, 0.35f, 0.35f);
        Synth.Saturate(b, 2f);
        return b;
    }

    /// <summary>Commander death blast: flash crack, electric shriek falling into a very low boom, roar and rumble.</summary>
    private static float[] CommanderBlast(ref SynthRng r)
    {
        var b = Synth.Alloc(3.9f);
        Synth.Noise(b, ref r, 0, 0.001f, 0.03f, 1f, 11000, 2500, 0.03f, hp: 600);
        Synth.Sweep(b, 0, 1.2f, 2600, 120, 0.3f, t => Synth.AD(t, 0.003f, 0.35f), 0.4f, wave: 1);
        Synth.Sweep(b, 0, 0.9f, 1300, 90, 0.25f, t => Synth.AD(t, 0.003f, 0.3f), 0.3f, wave: 2);
        Synth.Thump(b, 0, 66, 18, 0.22f, 0.004f, 0.75f, 1.4f);
        Synth.Thump(b, 0.015f, 110, 34, 0.12f, 0.003f, 0.3f, 0.5f);
        Synth.Noise(b, ref r, 0.005f, 0.01f, 0.9f, 1.1f, 2600, 160, 0.6f);
        Synth.Rumble(b, ref r, 0, 0.08f, 1.2f, 1.4f, 105, length: 3.8f);
        Synth.BandNoise(b, ref r, 0.05f, 2.2f, 4800, 1800, 0.8f, 3f, t => Synth.AD(t, 0.02f, 0.6f), 0.35f);
        Synth.Crackle(b, ref r, 0.1f, 2.6f, 90, 0.3f, 0.9f);
        for (int i = 0; i < 4; i++)
        {
            float t0 = r.Range(0.5f, 2.6f);
            Synth.Rumble(b, ref r, t0, r.Range(0.08f, 0.2f), r.Range(0.3f, 0.5f), r.Range(0.3f, 0.5f), r.Range(200, 380));
        }
        Synth.Saturate(b, 2.2f);
        return b;
    }

    /// <summary>Teleporter charge: a low saw hum and its octave rising and swelling, a whistling band of noise and sparse arcs.</summary>
    private static float[] TeleportCharge(ref SynthRng r)
    {
        const float len = 2.3f;
        var b = Synth.Alloc(len);
        float f0 = r.Range(62, 70);
        Func<float, float> swell = t => MathF.Min(1f, t / 1.5f) * MathF.Min(1f, MathF.Max(0f, len - t) / 0.45f);
        Synth.Sweep(b, 0, len, f0, f0 * 3.2f, 1.1f, swell, 0.4f, wave: 2);
        Synth.Sweep(b, 0, len, f0 * 2.01f, f0 * 6.5f, 1.0f, swell, 0.22f, wave: 1);
        Synth.Sweep(b, 0.2f, len - 0.2f, 900, 2600, 0.9f, t => swell(t + 0.2f) * 0.6f, 0.08f);
        Synth.BandNoise(b, ref r, 0, len, 700, 3600, 1.0f, 5f, swell, 0.18f);
        Synth.Crackle(b, ref r, 0.4f, len - 0.5f, 24, 0.12f, 0.5f);
        Synth.Saturate(b, 1.4f);
        return b;
    }

    /// <summary>Teleporter departure: a quick upward zap sucked into a bright crack, a soft whoosh and a short sizzle.</summary>
    private static float[] TeleportOut(ref SynthRng r)
    {
        var b = Synth.Alloc(1.0f);
        Synth.Sweep(b, 0, 0.32f, r.Range(240, 280), 2600, 0.12f, t => MathF.Min(1f, t / 0.22f) * Synth.AD(t, 0f, 0.6f), 0.35f, wave: 1);
        Synth.BandNoise(b, ref r, 0, 0.4f, 600, 5200, 0.15f, 3f, t => MathF.Min(1f, t / 0.25f) * (t < 0.3f ? 1f : MathF.Exp(-(t - 0.3f) / 0.03f)), 0.35f);
        Synth.Noise(b, ref r, 0.28f, 0.0005f, 0.03f, 0.9f, 11000, 3000, 0.03f, hp: 1200);
        Synth.Thump(b, 0.28f, 140, 60, 0.05f, 0.002f, 0.12f, 0.5f);
        Synth.BandNoise(b, ref r, 0.3f, 0.6f, 4200, 1500, 0.3f, 3f, t => Synth.AD(t, 0.01f, 0.15f), 0.2f);
        Synth.Crackle(b, ref r, 0.3f, 0.5f, 60, 0.15f, 0.2f);
        Synth.Saturate(b, 1.6f);
        return b;
    }

    /// <summary>Teleporter arrival: a bright crack falling into a deep zap, a low thump and crackling arcs.</summary>
    private static float[] TeleportIn(ref SynthRng r)
    {
        var b = Synth.Alloc(1.3f);
        Synth.Noise(b, ref r, 0, 0.0004f, 0.025f, 1f, 12000, 3500, 0.02f, hp: 1200);
        Synth.Sweep(b, 0, 0.7f, r.Range(2300, 2600), 160, 0.12f, t => Synth.AD(t, 0.002f, 0.16f), 0.35f, wave: 1);
        Synth.Sweep(b, 0, 0.6f, 1200, 110, 0.15f, t => Synth.AD(t, 0.003f, 0.2f), 0.25f, wave: 2);
        Synth.Thump(b, 0, 100, 36, 0.07f, 0.002f, 0.2f, 0.9f);
        Synth.BandNoise(b, ref r, 0.01f, 0.5f, 5000, 1200, 0.2f, 2.5f, t => Synth.AD(t, 0.005f, 0.12f), 0.35f);
        Synth.Crackle(b, ref r, 0.03f, 0.9f, 80, 0.25f, 0.3f);
        Synth.Saturate(b, 1.8f);
        return b;
    }
}
