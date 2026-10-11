namespace Rts.Client.Audio;

/// <summary>Every procedurally generated one-shot sound.</summary>
public enum SoundId
{
    // Weapons (fire)
    GunLight,
    Cannon,
    CannonHeavy,
    Artillery,
    MissileLaunch,
    FlakFire,
    Laser,
    BombWhistle,
    // Hits / explosions
    Impact,
    ImpactLaser,
    FlakBurst,
    ExplosionSmall,
    ExplosionLarge,
    UnitDeath,
    AirDeath,
    BuildingCollapse,
    // Construction
    BuildStart,
    BuildComplete,
    UnitReady,
    ReclaimDone,
    // UI / alerts
    UiClick,
    UiConfirm,
    UiError,
    WarnMass,
    WarnEnergy,
    UnderAttack,
    CommanderAlert,
}

/// <summary>Mixing info for a sound: playback gain, max simultaneous voices, hearing range multiplier.</summary>
public readonly record struct SoundInfo(float Gain, int Voices, float Range, int Variants, bool Ui = false);

/// <summary>
/// Recipes for all one-shot sounds, synthesized at startup from <see cref="Synth"/> layers (no asset files).
/// Each recipe is deterministic for a variant seed; several variants per sound avoid machine-gun repetition.
/// Output: mono float PCM at <see cref="Synth.SampleRate"/>, peak-normalized to 0.9.
/// </summary>
public static class SoundBank
{
    public static readonly int Count = Enum.GetValues<SoundId>().Length;

    public static SoundInfo Info(SoundId id) => id switch
    {
        SoundId.GunLight => new(0.32f, 6, 0.8f, 3),
        SoundId.Cannon => new(0.42f, 8, 1f, 3),
        SoundId.CannonHeavy => new(0.6f, 3, 1.3f, 2),
        SoundId.Artillery => new(0.55f, 5, 1.4f, 3),
        SoundId.MissileLaunch => new(0.36f, 6, 1f, 3),
        SoundId.FlakFire => new(0.38f, 5, 1.1f, 3),
        SoundId.Laser => new(0.3f, 6, 0.9f, 3),
        SoundId.BombWhistle => new(0.32f, 3, 1.2f, 2),
        SoundId.Impact => new(0.22f, 6, 0.7f, 4),
        SoundId.ImpactLaser => new(0.2f, 4, 0.7f, 2),
        SoundId.FlakBurst => new(0.3f, 5, 1.2f, 3),
        SoundId.ExplosionSmall => new(0.5f, 6, 1.2f, 3),
        SoundId.ExplosionLarge => new(0.7f, 4, 1.6f, 2),
        SoundId.UnitDeath => new(0.62f, 6, 1.3f, 3),
        SoundId.AirDeath => new(0.6f, 3, 1.4f, 2),
        SoundId.BuildingCollapse => new(0.8f, 3, 1.7f, 2),
        SoundId.BuildStart => new(0.3f, 3, 0.8f, 1),
        SoundId.BuildComplete => new(0.38f, 3, 1f, 1),
        SoundId.UnitReady => new(0.3f, 2, 1f, 1, Ui: true),
        SoundId.ReclaimDone => new(0.4f, 3, 0.9f, 2),
        SoundId.UiClick => new(0.28f, 3, 1f, 1, Ui: true),
        SoundId.UiConfirm => new(0.24f, 3, 1f, 1, Ui: true),
        SoundId.UiError => new(0.32f, 2, 1f, 1, Ui: true),
        SoundId.WarnMass => new(0.42f, 1, 1f, 1, Ui: true),
        SoundId.WarnEnergy => new(0.42f, 1, 1f, 1, Ui: true),
        SoundId.UnderAttack => new(0.45f, 1, 1f, 1, Ui: true),
        SoundId.CommanderAlert => new(0.5f, 1, 1f, 1, Ui: true),
        _ => new(0.4f, 2, 1f, 1),
    };

    /// <summary>Synthesizes all variants of all sounds (≈0.1–0.3 s on one core; safe to run on a worker thread).</summary>
    public static float[][][] GenerateAll()
    {
        var all = new float[Count][][];
        for (int i = 0; i < Count; i++)
        {
            var id = (SoundId)i;
            int n = Info(id).Variants;
            all[i] = new float[n][];
            for (int v = 0; v < n; v++) all[i][v] = Generate(id, v);
        }
        return all;
    }

    public static float[] Generate(SoundId id, int variant)
    {
        var rng = new SynthRng((uint)((int)id * 7919 + variant * 104729 + 17));
        float[] b = id switch
        {
            SoundId.GunLight => GunLight(ref rng),
            SoundId.Cannon => Cannon(ref rng),
            SoundId.CannonHeavy => CannonHeavy(ref rng),
            SoundId.Artillery => Artillery(ref rng),
            SoundId.MissileLaunch => MissileLaunch(ref rng),
            SoundId.FlakFire => FlakFire(ref rng),
            SoundId.Laser => Laser(ref rng),
            SoundId.BombWhistle => BombWhistle(ref rng),
            SoundId.Impact => Impact(ref rng),
            SoundId.ImpactLaser => ImpactLaser(ref rng),
            SoundId.FlakBurst => FlakBurst(ref rng),
            SoundId.ExplosionSmall => Explosion(ref rng, 1f),
            SoundId.ExplosionLarge => Explosion(ref rng, 2.2f),
            SoundId.UnitDeath => UnitDeath(ref rng),
            SoundId.AirDeath => AirDeath(ref rng),
            SoundId.BuildingCollapse => BuildingCollapse(ref rng),
            SoundId.BuildStart => BuildStart(ref rng),
            SoundId.BuildComplete => BuildComplete(),
            SoundId.UnitReady => UnitReady(),
            SoundId.ReclaimDone => ReclaimDone(ref rng),
            SoundId.UiClick => UiClick(ref rng),
            SoundId.UiConfirm => UiConfirm(),
            SoundId.UiError => UiError(),
            SoundId.WarnMass => Warning(74, 69),
            SoundId.WarnEnergy => Warning(81, 76),
            SoundId.UnderAttack => Alarm(76, 71, 3),
            SoundId.CommanderAlert => Alarm(79, 72, 4),
            _ => Synth.Alloc(0.05f),
        };
        Synth.RemoveDc(b);
        b = Synth.TrimTail(b);
        Synth.FadeEdges(b);
        Synth.Normalize(b, 0.9f);
        return b;
    }

    // ------------------------------------------------------------------ weapons

    private static float[] GunLight(ref SynthRng r)
    {
        var b = Synth.Alloc(0.22f);
        Synth.Noise(b, ref r, 0, 0.0005f, 0.018f, 0.9f, 7000, 2500, 0.02f, hp: 400);
        Synth.Thump(b, 0, r.Range(260, 300), 110, 0.012f, 0.001f, 0.03f, 0.8f);
        Synth.Noise(b, ref r, 0.004f, 0.002f, 0.05f, 0.25f, 2200, 800, 0.05f);
        Synth.Saturate(b, 1.6f);
        return b;
    }

    private static float[] Cannon(ref SynthRng r)
    {
        var b = Synth.Alloc(0.6f);
        // Sharp crack, body, low thump, short gauss "ring".
        Synth.Noise(b, ref r, 0, 0.0005f, 0.008f, 1.0f, 12000, 5000, 0.01f, hp: 1500);
        Synth.Noise(b, ref r, 0.001f, 0.002f, 0.085f, 0.8f, 3800, 700, 0.06f);
        Synth.Thump(b, 0, r.Range(150, 170), 48, 0.03f, 0.002f, 0.11f, 1.1f);
        Synth.Sweep(b, 0, 0.12f, r.Range(2600, 3000), 1500, 0.04f, t => Synth.AD(t, 0.001f, 0.035f), 0.12f);
        Synth.Rumble(b, ref r, 0.01f, 0.01f, 0.15f, 0.5f, 400);
        Synth.Saturate(b, 1.8f);
        return b;
    }

    private static float[] CannonHeavy(ref SynthRng r)
    {
        var b = Synth.Alloc(1.1f);
        Synth.Noise(b, ref r, 0, 0.0005f, 0.012f, 1.0f, 11000, 3000, 0.015f, hp: 1000);
        Synth.Noise(b, ref r, 0.002f, 0.003f, 0.16f, 0.9f, 2600, 400, 0.12f);
        Synth.Thump(b, 0, r.Range(110, 125), 32, 0.05f, 0.003f, 0.22f, 1.3f);
        // Anti-matter "zap" tail.
        Synth.Sweep(b, 0.005f, 0.4f, 1600, 260, 0.08f, t => Synth.AD(t, 0.004f, 0.11f), 0.22f, wave: 2);
        Synth.Rumble(b, ref r, 0.02f, 0.02f, 0.35f, 0.7f, 260);
        Synth.Saturate(b, 2f);
        return b;
    }

    private static float[] Artillery(ref SynthRng r)
    {
        var b = Synth.Alloc(1.6f);
        Synth.Noise(b, ref r, 0, 0.0005f, 0.01f, 0.9f, 9000, 2500, 0.02f, hp: 600);
        Synth.Thump(b, 0, r.Range(85, 95), 26, 0.07f, 0.004f, 0.32f, 1.4f);
        Synth.Noise(b, ref r, 0.003f, 0.004f, 0.22f, 0.8f, 1400, 250, 0.2f);
        Synth.Rumble(b, ref r, 0.03f, 0.05f, 0.5f, 0.9f, 180);
        Synth.Saturate(b, 2.2f);
        return b;
    }

    private static float[] MissileLaunch(ref SynthRng r)
    {
        var b = Synth.Alloc(0.9f);
        Synth.Thump(b, 0, 220, 90, 0.02f, 0.001f, 0.035f, 0.6f);
        float start = r.Range(500, 700);
        Synth.BandNoise(b, ref r, 0, 0.85f, start, r.Range(2600, 3400), 0.25f, 1.4f, t => Synth.AD(t, 0.03f, 0.22f), 1.1f);
        Synth.Noise(b, ref r, 0, 0.02f, 0.25f, 0.35f, 5000, 1500, 0.2f, hp: 1500);
        return b;
    }

    private static float[] FlakFire(ref SynthRng r)
    {
        var b = Synth.Alloc(0.3f);
        Synth.Thump(b, 0, r.Range(190, 220), 70, 0.015f, 0.001f, 0.045f, 1f);
        Synth.Noise(b, ref r, 0, 0.0008f, 0.03f, 0.8f, 5000, 1200, 0.03f, hp: 300);
        Synth.Saturate(b, 1.8f);
        return b;
    }

    private static float[] Laser(ref SynthRng r)
    {
        var b = Synth.Alloc(0.32f);
        float f0 = r.Range(2200, 2700);
        Synth.Sweep(b, 0, 0.3f, f0, r.Range(380, 480), 0.06f, t => Synth.AD(t, 0.002f, 0.07f), 0.7f, wave: 1);
        Synth.Sweep(b, 0, 0.3f, f0 * 1.5f, 700, 0.05f, t => Synth.AD(t, 0.002f, 0.045f), 0.25f);
        Synth.Noise(b, ref r, 0, 0.001f, 0.02f, 0.25f, 9000, 4000, 0.02f, hp: 2000);
        return b;
    }

    private static float[] BombWhistle(ref SynthRng r)
    {
        var b = Synth.Alloc(1.3f);
        float f0 = r.Range(1500, 1700);
        Synth.Sweep(b, 0, 1.25f, f0, 520, 0.9f, t => MathF.Min(1, t / 0.25f) * MathF.Min(1, (1.25f - t) / 0.2f), 0.55f);
        Synth.BandNoise(b, ref r, 0, 1.25f, f0 * 0.9f, 600, 0.9f, 6f, t => MathF.Min(1, t / 0.3f) * MathF.Min(1, (1.25f - t) / 0.2f), 0.4f);
        return b;
    }

    // ------------------------------------------------------------------ hits / explosions

    private static float[] Impact(ref SynthRng r)
    {
        var b = Synth.Alloc(0.25f);
        Synth.Noise(b, ref r, 0, 0.0005f, 0.012f, 0.9f, 8000, 2000, 0.02f, hp: 800);
        Synth.Ping(b, 0, r.Range(900, 1500), 0.035f, 0.5f);
        Synth.Thump(b, 0, 180, 80, 0.01f, 0.001f, 0.025f, 0.5f);
        return b;
    }

    private static float[] ImpactLaser(ref SynthRng r)
    {
        var b = Synth.Alloc(0.25f);
        Synth.BandNoise(b, ref r, 0, 0.22f, 5000, 2500, 0.05f, 2.5f, t => Synth.AD(t, 0.002f, 0.05f), 1f);
        Synth.Sweep(b, 0, 0.12f, 1800, 900, 0.05f, t => Synth.AD(t, 0.001f, 0.025f), 0.3f);
        return b;
    }

    private static float[] FlakBurst(ref SynthRng r)
    {
        var b = Synth.Alloc(0.7f);
        Synth.Noise(b, ref r, 0, 0.0004f, 0.01f, 1f, 12000, 4000, 0.01f, hp: 1200);
        Synth.Noise(b, ref r, 0.002f, 0.003f, 0.12f, 0.7f, 2500, 500, 0.1f);
        Synth.Thump(b, 0, 140, 60, 0.02f, 0.002f, 0.06f, 0.5f);
        Synth.Crackle(b, ref r, 0.02f, 0.4f, 60, 0.25f, 0.1f);
        return b;
    }

    private static float[] Explosion(ref SynthRng r, float scale)
    {
        var b = Synth.Alloc(0.9f + scale * 0.7f);
        Synth.Noise(b, ref r, 0, 0.0008f, 0.012f * scale, 0.9f, 9000, 2500, 0.02f, hp: 500);
        Synth.Thump(b, 0, 95f / MathF.Sqrt(scale), 24, 0.06f * scale, 0.003f, 0.16f * scale, 1.3f);
        Synth.Noise(b, ref r, 0.002f, 0.006f, 0.18f * scale, 1f, 2400f / scale, 220, 0.18f * scale);
        Synth.Rumble(b, ref r, 0.02f, 0.04f, 0.35f * scale, 0.9f, 160);
        Synth.Crackle(b, ref r, 0.05f, 0.6f * scale, 70, 0.35f, 0.25f * scale);
        Synth.Saturate(b, 2.2f);
        return b;
    }

    private static float[] UnitDeath(ref SynthRng r)
    {
        var b = Explosion(ref r, 1.5f);
        // Metal debris clanks falling after the blast.
        int clanks = 3 + (int)(r.Next() * 3);
        for (int i = 0; i < clanks; i++)
            Synth.Ping(b, r.Range(0.15f, 0.9f), r.Range(500, 1400), r.Range(0.03f, 0.08f), r.Range(0.12f, 0.3f));
        return b;
    }

    private static float[] AirDeath(ref SynthRng r)
    {
        var b = Synth.Alloc(2.2f);
        // Engine scream falling in pitch, then the blast.
        Synth.Sweep(b, 0, 0.7f, 900, 260, 0.35f, t => MathF.Min(1, t / 0.05f) * MathF.Max(0, 1 - t / 0.7f), 0.35f, wave: 2);
        var e = Explosion(ref r, 1.8f);
        int off = Synth.Index(0.1f);
        for (int i = 0; i < e.Length && i + off < b.Length; i++) b[i + off] += e[i] * 0.9f;
        return b;
    }

    private static float[] BuildingCollapse(ref SynthRng r)
    {
        var b = Synth.Alloc(3.4f);
        var e = Explosion(ref r, 2.4f);
        for (int i = 0; i < e.Length && i < b.Length; i++) b[i] += e[i];
        // Long debris rumble with falling chunks and metal groans.
        Synth.Rumble(b, ref r, 0.2f, 0.3f, 1.1f, 1.1f, 120);
        Synth.Sweep(b, 0.25f, 1.6f, 210, 95, 0.9f, t => MathF.Min(1, t / 0.2f) * MathF.Exp(-t / 0.6f), 0.18f, wave: 2);
        Synth.Sweep(b, 0.3f, 1.4f, 223, 101, 0.9f, t => MathF.Min(1, t / 0.2f) * MathF.Exp(-t / 0.6f), 0.14f, wave: 2);
        for (int i = 0; i < 7; i++)
        {
            float t0 = r.Range(0.3f, 2.4f);
            Synth.Noise(b, ref r, t0, 0.002f, r.Range(0.05f, 0.12f), r.Range(0.25f, 0.5f), 1600, 300, 0.08f);
            if (r.Next() < 0.5f) Synth.Ping(b, t0, r.Range(300, 900), 0.08f, 0.15f);
        }
        Synth.Saturate(b, 1.6f);
        return b;
    }

    // ------------------------------------------------------------------ construction

    private static float[] BuildStart(ref SynthRng r)
    {
        var b = Synth.Alloc(0.8f);
        Func<float, float> env = t => MathF.Min(1, t / 0.08f) * MathF.Exp(-t / 0.25f);
        Synth.Sweep(b, 0, 0.75f, 320, 960, 0.35f, env, 0.4f);
        Synth.Sweep(b, 0.02f, 0.73f, 480, 1440, 0.35f, env, 0.25f);
        Synth.Tone(b, 0, 0.75f, 120, 0.02f, 0.2f, 0.25f, [1f, 0.3f], 9f, 0.02f);
        Synth.BandNoise(b, ref r, 0, 0.75f, 2000, 6000, 0.3f, 3f, t => Synth.AD(t, 0.05f, 0.2f), 0.4f);
        return b;
    }

    private static float[] BuildComplete()
    {
        var b = Synth.Alloc(1.4f);
        Synth.Thump(b, 0, 160, 70, 0.03f, 0.002f, 0.08f, 0.6f);
        Synth.Bell(b, 0.0f, Synth.Midi(76), 0.5f, 0.45f, 2f, 1.2f);
        Synth.Bell(b, 0.12f, Synth.Midi(83), 0.6f, 0.45f, 2f, 1.2f);
        return b;
    }

    private static float[] UnitReady()
    {
        var b = Synth.Alloc(0.9f);
        Synth.Tone(b, 0.00f, 0.4f, Synth.Midi(84), 0.004f, 0.09f, 0.4f, [1f, 0.2f, 0.08f]);
        Synth.Tone(b, 0.07f, 0.4f, Synth.Midi(88), 0.004f, 0.09f, 0.4f, [1f, 0.2f, 0.08f]);
        Synth.Tone(b, 0.14f, 0.6f, Synth.Midi(91), 0.004f, 0.18f, 0.45f, [1f, 0.2f, 0.08f]);
        return b;
    }

    private static float[] ReclaimDone(ref SynthRng r)
    {
        var b = Synth.Alloc(0.7f);
        Synth.BandNoise(b, ref r, 0, 0.6f, 4000, 350, 0.18f, 3f, t => MathF.Min(1, t / 0.05f) * MathF.Exp(-t / 0.18f), 1.2f);
        Synth.Sweep(b, 0, 0.6f, 1000, 180, 0.15f, t => MathF.Min(1, t / 0.03f) * MathF.Exp(-t / 0.15f), 0.35f);
        Synth.Thump(b, 0.18f, 120, 60, 0.03f, 0.002f, 0.06f, 0.4f);
        return b;
    }

    // ------------------------------------------------------------------ UI / alerts

    private static float[] UiClick(ref SynthRng r)
    {
        var b = Synth.Alloc(0.06f);
        Synth.Tone(b, 0, 0.05f, 2100, 0.0005f, 0.007f, 0.6f, [1f]);
        Synth.Noise(b, ref r, 0, 0.0003f, 0.003f, 0.3f, 9000, 6000, 0.01f, hp: 2000);
        return b;
    }

    private static float[] UiConfirm()
    {
        var b = Synth.Alloc(0.18f);
        Synth.Tone(b, 0, 0.08f, 1046.5f, 0.002f, 0.025f, 0.5f, [1f, 0.15f]);
        Synth.Tone(b, 0.05f, 0.12f, 1568f, 0.002f, 0.03f, 0.5f, [1f, 0.15f]);
        return b;
    }

    private static float[] UiError()
    {
        var b = Synth.Alloc(0.32f);
        Synth.Sweep(b, 0, 0.12f, 240, 230, 1f, t => Synth.AD(t, 0.004f, 0.06f), 0.6f, wave: 1);
        Synth.Sweep(b, 0.14f, 0.16f, 190, 180, 1f, t => Synth.AD(t, 0.004f, 0.07f), 0.6f, wave: 1);
        return b;
    }

    /// <summary>Two-note falling warning (resource stall).</summary>
    private static float[] Warning(int note1, int note2)
    {
        var b = Synth.Alloc(0.9f);
        ReadOnlySpan<float> h = [1f, 0f, 0.25f, 0f, 0.08f];
        Synth.Tone(b, 0, 0.3f, Synth.Midi(note1), 0.01f, 0.12f, 0.5f, h);
        Synth.Tone(b, 0.22f, 0.6f, Synth.Midi(note2), 0.01f, 0.2f, 0.5f, h);
        return b;
    }

    /// <summary>Two-tone alarm repeated n times (under attack).</summary>
    private static float[] Alarm(int hi, int lo, int repeats)
    {
        var b = Synth.Alloc(repeats * 0.36f + 0.3f);
        ReadOnlySpan<float> h = [1f, 0.3f, 0.2f, 0.1f];
        for (int i = 0; i < repeats; i++)
        {
            Synth.Tone(b, i * 0.36f, 0.17f, Synth.Midi(hi), 0.006f, 0.12f, 0.5f, h);
            Synth.Tone(b, i * 0.36f + 0.18f, 0.17f, Synth.Midi(lo), 0.006f, 0.12f, 0.5f, h);
        }
        return b;
    }
}
