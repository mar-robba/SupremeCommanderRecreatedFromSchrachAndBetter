namespace Rts.Client.Audio;

/// <summary>
/// Commander sound recipes: the ACU Overcharge shot (energy discharge on top of the regular gun) and its impact
/// (electric blast on top of the regular explosion), and the commander death blast (a quantum core bursting: a
/// sizzling electric shriek over a nuke-like boom, shorter than <see cref="SoundId.NukeExplosion"/>).
/// </summary>
public static partial class SoundBank
{
    private static SoundInfo CommanderInfo(SoundId id) => id switch
    {
        SoundId.OverchargeFire => new(0.7f, 3, 1.6f, 2),
        SoundId.OverchargeHit => new(0.75f, 3, 1.7f, 2),
        SoundId.CommanderBlast => new(1f, 2, 4f, 1),
        _ => new(0.4f, 2, 1f, 1),
    };

    private static float[] GenerateCommander(SoundId id, ref SynthRng rng) => id switch
    {
        SoundId.OverchargeFire => OverchargeFire(ref rng),
        SoundId.OverchargeHit => OverchargeHit(ref rng),
        SoundId.CommanderBlast => CommanderBlast(ref rng),
        _ => Synth.Alloc(0.05f),
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
}
