namespace Rts.Client.Audio;

/// <summary>(9b) Death sequence and ACU warp-in recipes (<see cref="Rendering.DeathAnimator"/>): a falling aircraft's
/// dying-engine whine, its ground crash, a ship's groaning and bubbling as it sinks, a structure crumbling into its
/// wreck and the commander warp-in shimmer. Every sound is under 4 s (SynthTests).</summary>
public static partial class SoundBank
{
    private static SoundInfo DeathInfo(SoundId id) => id switch
    {
        SoundId.AircraftFall => new(0.45f, 3, 1.5f, 2),
        SoundId.AircraftCrash => new(0.8f, 3, 2f, 2),
        SoundId.ShipSink => new(0.7f, 2, 2f, 1),
        SoundId.StructureCrumble => new(0.75f, 3, 2f, 2),
        SoundId.WarpIn => new(0.9f, 2, 3f, 1),
        _ => new(0.4f, 2, 1f, 1),
    };

    private static float[] GenerateDeath(SoundId id, ref SynthRng rng) => id switch
    {
        SoundId.AircraftFall => AircraftFall(ref rng),
        SoundId.AircraftCrash => AircraftCrash(ref rng),
        SoundId.ShipSink => ShipSink(ref rng),
        SoundId.StructureCrumble => StructureCrumble(ref rng),
        SoundId.WarpIn => WarpIn(ref rng),
        _ => Synth.Alloc(0.05f),
    };

    /// <summary>Dying engine: a sputtering saw whine falling an octave and a half, with rushing wind rising (≈2.4 s).</summary>
    private static float[] AircraftFall(ref SynthRng r)
    {
        var b = Synth.Alloc(2.6f);
        float f0 = r.Range(820, 980);
        Func<float, float> env = t => MathF.Min(1, t / 0.08f) * Math.Clamp((2.4f - t) / 0.5f, 0f, 1f) * (0.75f + 0.25f * MathF.Sin(t * 37f));
        Synth.Sweep(b, 0, 2.4f, f0, f0 * 0.33f, 0.9f, env, 0.35f, wave: 2);
        Synth.Sweep(b, 0, 2.4f, f0 * 0.5f, f0 * 0.17f, 0.9f, env, 0.25f, wave: 1);
        Synth.BandNoise(b, ref r, 0, 2.5f, 900, 2600, 0.8f, 1.5f, t => MathF.Min(1, t / 1.2f) * Math.Clamp((2.5f - t) / 0.3f, 0f, 1f), 0.4f);
        Synth.Crackle(b, ref r, 0.1f, 2.0f, 18, 0.2f, 0.05f);
        return b;
    }

    /// <summary>Ground crash: a deep thump, a noise blast, metal debris clatter and a rumbling tail (≈2 s).</summary>
    private static float[] AircraftCrash(ref SynthRng r)
    {
        var b = Synth.Alloc(2.2f);
        Synth.Thump(b, 0, r.Range(120, 140), 38, 0.06f, 0.002f, 0.18f, 1f);
        Synth.Noise(b, ref r, 0, 0.001f, 0.12f, 0.9f, 9000, 900, 0.08f, hp: 80);
        Synth.Rumble(b, ref r, 0.02f, 0.01f, 0.5f, 0.8f, 260);
        Synth.Crackle(b, ref r, 0.05f, 1.2f, 60, 0.35f, 0.4f);
        Synth.Saturate(b, 1.6f);
        return b;
    }

    /// <summary>Sinking ship: bursting bubbles, rushing water and a long low metal groan (≈3.6 s).</summary>
    private static float[] ShipSink(ref SynthRng r)
    {
        var b = Synth.Alloc(3.8f);
        Synth.BandNoise(b, ref r, 0, 3.6f, 700, 300, 1.5f, 0.9f, t => MathF.Min(1, t / 0.3f) * Math.Clamp((3.6f - t) / 1.2f, 0f, 1f), 0.5f);
        Func<float, float> groan = t => MathF.Min(1, t / 0.4f) * Math.Clamp((3.2f - t) / 1f, 0f, 1f) * (0.6f + 0.4f * MathF.Sin(t * 5.3f));
        Synth.Sweep(b, 0.3f, 3.2f, r.Range(95, 110), 58, 1.4f, groan, 0.35f, wave: 2);
        Synth.Sweep(b, 0.9f, 2.4f, 190, 140, 1f, t => MathF.Min(1, t / 0.5f) * Math.Clamp((2.4f - t) / 0.8f, 0f, 1f), 0.12f, wave: 1);
        for (int i = 0; i < 14; i++)
        {
            float t0 = r.Range(0.05f, 3.2f);
            Synth.Sweep(b, t0, 0.06f, r.Range(260, 520), r.Range(600, 1100), 0.03f, t => Synth.AD(t, 0.003f, 0.02f), 0.18f);
        }
        Synth.Rumble(b, ref r, 0, 0.2f, 0.9f, 0.4f, 160, length: 3.6f);
        return b;
    }

    /// <summary>Collapsing structure: a heavy cracking thud then a long rumble with falling debris (≈2.5 s).</summary>
    private static float[] StructureCrumble(ref SynthRng r)
    {
        var b = Synth.Alloc(2.7f);
        Synth.Thump(b, 0, r.Range(85, 100), 34, 0.1f, 0.004f, 0.25f, 0.8f);
        Synth.Noise(b, ref r, 0, 0.002f, 0.06f, 0.6f, 5000, 1200, 0.05f, hp: 200);
        Synth.Rumble(b, ref r, 0.03f, 0.15f, 0.7f, 0.9f, 220, length: 2.5f);
        Synth.Crackle(b, ref r, 0.1f, 2.2f, 45, 0.3f, 0.9f);
        Synth.Thump(b, r.Range(0.6f, 0.9f), 70, 30, 0.1f, 0.004f, 0.2f, 0.45f);
        Synth.Saturate(b, 1.4f);
        return b;
    }

    /// <summary>ACU warp-in: a rising electric shimmer, a bright chord as the hologram resolves and a landing thump (≈2.6 s).</summary>
    private static float[] WarpIn(ref SynthRng r)
    {
        var b = Synth.Alloc(2.8f);
        Func<float, float> rise = t => MathF.Min(1, t / 0.6f) * Math.Clamp((1.9f - t) / 0.25f, 0f, 1f);
        Synth.Sweep(b, 0, 1.9f, 140, 1500, 0.9f, rise, 0.3f, wave: 2);
        Synth.Sweep(b, 0, 1.9f, 70, 750, 0.9f, rise, 0.25f, wave: 1);
        Synth.BandNoise(b, ref r, 0, 1.9f, 1500, 7000, 0.8f, 2f, rise, 0.25f);
        Synth.Crackle(b, ref r, 0.2f, 1.6f, 70, 0.2f, 0.2f);
        Synth.Bell(b, 1.75f, Synth.Midi(76), 0.6f, 0.35f, 2f, 1.4f);
        Synth.Bell(b, 1.8f, Synth.Midi(83), 0.6f, 0.3f, 2f, 1.4f);
        Synth.Thump(b, 1.75f, 110, 36, 0.08f, 0.002f, 0.2f, 0.9f);
        Synth.Noise(b, ref r, 1.75f, 0.0005f, 0.05f, 0.5f, 11000, 3000, 0.03f, hp: 900);
        Synth.Saturate(b, 1.5f);
        return b;
    }
}
