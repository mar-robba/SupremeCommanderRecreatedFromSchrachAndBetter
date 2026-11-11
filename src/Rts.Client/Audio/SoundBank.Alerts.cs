namespace Rts.Client.Audio;

/// <summary>(7b) Notification sounds: map ping (bright double blip), notice chime (construction / upgrade complete),
/// structure lost (low falling two-note warning).</summary>
public static partial class SoundBank
{
    private static SoundInfo AlertInfo(SoundId id) => id switch
    {
        SoundId.AlertPing => new(0.4f, 2, 1f, 1, Ui: true),
        SoundId.AlertNotice => new(0.26f, 1, 1f, 1, Ui: true),
        SoundId.AlertStructureLost => new(0.42f, 1, 1f, 1, Ui: true),
        _ => DeathInfo(id), // SoundBank.Death.cs
    };

    private static float[] GenerateAlert(SoundId id, ref SynthRng rng) => id switch
    {
        SoundId.AlertPing => AlertPing(),
        SoundId.AlertNotice => AlertNotice(),
        SoundId.AlertStructureLost => Warning(67, 60),
        _ => GenerateDeath(id, ref rng), // SoundBank.Death.cs
    };

    private static float[] AlertPing()
    {
        var b = Synth.Alloc(0.7f);
        ReadOnlySpan<float> h = [1f, 0.35f, 0.1f];
        Synth.Tone(b, 0.00f, 0.12f, Synth.Midi(93), 0.002f, 0.06f, 0.5f, h);
        Synth.Tone(b, 0.13f, 0.35f, Synth.Midi(93), 0.002f, 0.16f, 0.5f, h);
        return b;
    }

    private static float[] AlertNotice()
    {
        var b = Synth.Alloc(0.9f);
        Synth.Bell(b, 0.0f, Synth.Midi(81), 0.4f, 0.4f, 2f, 1.2f);
        Synth.Bell(b, 0.1f, Synth.Midi(88), 0.5f, 0.4f, 2f, 1.2f);
        return b;
    }
}
