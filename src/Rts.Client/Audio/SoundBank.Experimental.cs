namespace Rts.Client.Audio;

/// <summary>
/// Experimental-scale sound recipes: the "Experimental detected" klaxon, the super-heavy cannon boom (Mavor,
/// T3 heavy artillery) and the huge blast of experimental death explosions.
/// </summary>
public static partial class SoundBank
{
    private static SoundInfo ExperimentalInfo(SoundId id) => id switch
    {
        SoundId.ExperimentalAlert => new(0.55f, 1, 1f, 1, Ui: true),
        SoundId.SuperHeavyFire => new(0.95f, 2, 3f, 2),
        SoundId.ExplosionHuge => new(0.95f, 3, 2.5f, 2),
        _ => new(0.4f, 2, 1f, 1),
    };

    private static float[] GenerateExperimental(SoundId id, ref SynthRng rng) => id switch
    {
        SoundId.ExperimentalAlert => ExperimentalAlert(),
        SoundId.SuperHeavyFire => SuperHeavyFire(ref rng),
        SoundId.ExplosionHuge => ExplosionHuge(ref rng),
        _ => Synth.Alloc(0.05f),
    };

    /// <summary>"Experimental detected": a buzzing two-tone horn (hi-lo), three times (≈1.9 s).</summary>
    private static float[] ExperimentalAlert()
    {
        var b = Synth.Alloc(2f);
        const float pulse = 0.29f, period = 0.64f;
        Func<float, float> env = t => MathF.Min(1, t / 0.015f) * Math.Clamp((pulse - t) / 0.04f, 0f, 1f);
        for (int i = 0; i < 3; i++)
        {
            float t0 = i * period;
            Synth.Sweep(b, t0, pulse, 640, 610, 0.2f, env, 0.45f, wave: 2);
            Synth.Sweep(b, t0, pulse, 320, 305, 0.2f, env, 0.3f, wave: 1);
            Synth.Sweep(b, t0 + 0.32f, pulse, 480, 455, 0.2f, env, 0.45f, wave: 2);
            Synth.Sweep(b, t0 + 0.32f, pulse, 240, 228, 0.2f, env, 0.3f, wave: 1);
        }
        Synth.Saturate(b, 2.2f);
        return b;
    }

    /// <summary>Super-heavy cannon shot: muzzle crack, a very low boom and a long rolling rumble with echoes.</summary>
    private static float[] SuperHeavyFire(ref SynthRng r)
    {
        var b = Synth.Alloc(3.4f);
        Synth.Noise(b, ref r, 0, 0.0005f, 0.02f, 1f, 8000, 1500, 0.03f, hp: 300);
        Synth.Thump(b, 0, r.Range(58, 66), 17, 0.22f, 0.004f, 0.6f, 1.7f);
        Synth.Thump(b, 0.01f, 115, 32, 0.08f, 0.002f, 0.22f, 0.6f);
        Synth.Noise(b, ref r, 0.003f, 0.01f, 0.55f, 1.1f, 1800, 110, 0.35f);
        Synth.Rumble(b, ref r, 0, 0.06f, 0.8f, 1.4f, 85, length: 3.3f);
        for (int i = 0; i < 3; i++)
        {
            float t0 = r.Range(0.5f, 1.9f);
            Synth.Rumble(b, ref r, t0, r.Range(0.08f, 0.2f), r.Range(0.3f, 0.5f), r.Range(0.3f, 0.5f), r.Range(160, 260));
        }
        Synth.Saturate(b, 2f);
        return b;
    }

    /// <summary>Experimental death blast: a big explosion with a sub-bass thump, cook-offs and falling debris.</summary>
    private static float[] ExplosionHuge(ref SynthRng r)
    {
        var b = Synth.Alloc(3.8f);
        var e = Explosion(ref r, 3f);
        for (int i = 0; i < e.Length && i < b.Length; i++) b[i] += e[i];
        Synth.Thump(b, 0, 52, 15, 0.3f, 0.004f, 0.7f, 1.3f);
        Synth.Rumble(b, ref r, 0.1f, 0.3f, 1.2f, 1.2f, 100, length: 3.6f);
        for (int i = 0; i < 5; i++)
        {
            float t0 = r.Range(0.25f, 1.8f);
            Synth.Noise(b, ref r, t0, 0.002f, r.Range(0.08f, 0.2f), r.Range(0.3f, 0.6f), 3000, 300, 0.1f);
            Synth.Thump(b, t0, r.Range(90, 130), 35, 0.05f, 0.002f, 0.15f, 0.4f);
        }
        Synth.Crackle(b, ref r, 0.2f, 2.8f, 90, 0.3f, 1f);
        for (int i = 0; i < 6; i++)
            Synth.Ping(b, r.Range(0.4f, 2.6f), r.Range(350, 1100), r.Range(0.04f, 0.1f), r.Range(0.1f, 0.25f));
        Synth.Saturate(b, 1.8f);
        return b;
    }
}
