using Rts.Client.Audio;
using Rts.Sim.Data;

namespace Rts.Client.Tests;

/// <summary>Procedural audio: every generated sound is finite, non-empty, peak-normalized and deterministic.</summary>
public class SynthTests
{
    public static IEnumerable<object[]> AllSounds() =>
        Enum.GetValues<SoundId>().SelectMany(id => Enumerable.Range(0, SoundBank.Info(id).Variants).Select(v => new object[] { id, v }));

    [Theory]
    [MemberData(nameof(AllSounds))]
    public void SoundIsFiniteNormalizedAndShort(SoundId id, int variant)
    {
        var b = SoundBank.Generate(id, variant);
        Assert.True(b.Length > Synth.SampleRate / 50, $"{id}: too short ({b.Length} samples)");
        Assert.True(b.Length <= Synth.SampleRate * 4, $"{id}: too long ({b.Length / (float)Synth.SampleRate:F2} s)");
        Assert.All(b, x => Assert.True(float.IsFinite(x)));
        Assert.InRange(Synth.Peak(b), 0.89f, 0.9001f);
        // Starts and ends silently (no clicks), and carries real signal.
        Assert.True(MathF.Abs(b[0]) < 0.05f && MathF.Abs(b[^1]) < 0.05f, $"{id}: clicks at the edges");
        double rms = Math.Sqrt(b.Average(x => (double)x * x));
        Assert.True(rms > 0.01, $"{id}: nearly silent (rms {rms:F4})");
        Assert.True(Math.Abs(b.Average(x => (double)x)) < 0.02, $"{id}: DC offset");
    }

    [Fact]
    public void GenerationIsDeterministicAndVariantsDiffer()
    {
        var a = SoundBank.Generate(SoundId.Cannon, 0);
        var b = SoundBank.Generate(SoundId.Cannon, 0);
        var c = SoundBank.Generate(SoundId.Cannon, 1);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void GenerateAllMatchesTheBankLayout()
    {
        var all = SoundBank.GenerateAll();
        Assert.Equal(SoundBank.Count, all.Length);
        for (int i = 0; i < all.Length; i++)
            Assert.Equal(SoundBank.Info((SoundId)i).Variants, all[i].Length);
    }

    [Fact]
    public void BufferHelpers()
    {
        Assert.Equal(Synth.SampleRate / 2, Synth.Alloc(0.5f).Length);
        var buf = new float[1000];
        Synth.Tone(buf, 0, 1f, 440, 0.001f, 0.01f, 3f, [1f]);
        Synth.Normalize(buf, 0.5f);
        Assert.InRange(Synth.Peak(buf), 0.4999f, 0.5001f);
        var pcm = Synth.ToPcm16([2f, -2f, 0.5f, 0f]);
        Assert.Equal(new short[] { 32767, -32768, 16384, 0 }, pcm);
        var nan = new[] { 0.1f, float.NaN, float.PositiveInfinity, -0.2f };
        Synth.Normalize(nan, 0.9f);
        Assert.All(nan, x => Assert.True(float.IsFinite(x)));
        Assert.Equal(440f, Synth.Midi(69), 3);
    }

    [Fact]
    public void AmbientStreamIsBoundedAndAudible()
    {
        var s = new AmbientSynth(3) { WindGain = 1, MusicGain = 1, ReclaimGain = 1, ReclaimPan = -0.5f, BuildGain = 1, BuildPan = 0.7f, Tension = 1 };
        const int frames = 4096;
        var buf = new float[frames * 2];
        double sumL = 0, sumR = 0, diff = 0;
        for (int block = 0; block < 64; block++) // ~6 s
        {
            s.Render(buf, frames);
            for (int k = 0; k < frames; k++)
            {
                float l = buf[2 * k], r = buf[2 * k + 1];
                Assert.True(float.IsFinite(l) && float.IsFinite(r));
                Assert.InRange(l, -1f, 1f);
                Assert.InRange(r, -1f, 1f);
                sumL += l * l; sumR += r * r; diff += (l - r) * (l - r);
            }
        }
        int n = frames * 64;
        Assert.True(Math.Sqrt(sumL / n) > 0.01 && Math.Sqrt(sumR / n) > 0.01, "ambient stream is silent");
        Assert.True(Math.Sqrt(diff / n) > 0.005, "ambient stream should be stereo");
    }

    [Fact]
    public void AmbientStreamSilentWhenAllGainsZero()
    {
        var s = new AmbientSynth(5);
        var buf = new float[2048 * 2];
        s.Render(buf, 2048);
        Assert.All(buf, x => Assert.Equal(0f, x));
    }

    [Fact]
    public void WeaponsMapToFireSounds()
    {
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        SoundId Fire(string id) => GameAudio.FireSound(db[id].Weapons[0]);
        Assert.Equal(SoundId.Artillery, Fire("uef_t1_artillery"));
        Assert.Equal(SoundId.MissileLaunch, Fire("uef_t1_mobile_aa"));
        Assert.Equal(SoundId.FlakFire, Fire("uef_t1_aa_turret"));
        Assert.Equal(SoundId.BombWhistle, Fire("uef_t1_bomber"));
        Assert.Equal(SoundId.Cannon, Fire("uef_t1_tank"));
        Assert.Equal(SoundId.CannonHeavy, Fire("uef_commander"));
        Assert.Equal(SoundId.GunLight, Fire("uef_t1_scout"));
        Assert.Equal(SoundId.Laser, GameAudio.FireSound(new WeaponBlueprint { ProjectileVisual = "laser" }));
    }
}
