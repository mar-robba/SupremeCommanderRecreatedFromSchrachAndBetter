namespace Rts.Client.Audio;

/// <summary>
/// Strategic sound recipes: shield hits / collapse / restore, tactical and strategic missile launches,
/// mid-air intercepts, the nuclear detonation and the global "strategic launch" klaxon.
/// </summary>
public static partial class SoundBank
{
    /// <summary>
    /// <see cref="SoundId.NukeExplosion"/> is synthesized time-compressed (frequencies raised, times shortened by
    /// this factor) so the buffer stays under the 4 s bank limit, and must be played at this pitch: that slows it
    /// back down to a ≈5.4 s blast and rumble.
    /// </summary>
    public const float NukePitch = 0.72f;

    // ------------------------------------------------------------------ shields

    private static float[] ShieldHit(ref SynthRng r)
    {
        var b = Synth.Alloc(0.5f);
        // Transient, falling electric zap and fizzing band noise.
        Synth.Noise(b, ref r, 0, 0.0004f, 0.006f, 0.5f, 10000, 5000, 0.01f, hp: 1500);
        Synth.Sweep(b, 0, 0.18f, r.Range(1700, 2300), r.Range(420, 560), 0.04f, t => Synth.AD(t, 0.001f, 0.045f), 0.45f, wave: 1);
        Synth.BandNoise(b, ref r, 0, 0.2f, r.Range(4500, 6000), 2500, 0.06f, 4f, t => Synth.AD(t, 0.0008f, 0.035f), 0.9f);
        Synth.Crackle(b, ref r, 0, 0.25f, 220, 0.35f, 0.06f);
        // The dome's thrum: buzzing low tone with a fast wobble.
        Synth.Tone(b, 0, 0.45f, r.Range(95, 115), 0.004f, 0.11f, 0.55f, [1f, 0.6f, 0.35f, 0.2f, 0.1f], 28f, 0.04f);
        Synth.Saturate(b, 1.5f);
        return b;
    }

    private static float[] ShieldDown(ref SynthRng r)
    {
        var b = Synth.Alloc(2f);
        // Overload snap.
        Synth.Noise(b, ref r, 0, 0.0005f, 0.015f, 0.8f, 11000, 3000, 0.02f, hp: 900);
        Synth.Thump(b, 0, 140, 45, 0.05f, 0.002f, 0.12f, 0.8f);
        // Descending power-down whine (saw + detuned sine).
        Func<float, float> env = t => MathF.Min(1, t / 0.03f) * MathF.Pow(MathF.Max(0, 1 - t / 1.6f), 1.5f);
        Synth.Sweep(b, 0.01f, 1.6f, r.Range(1300, 1500), 70, 0.45f, env, 0.32f, wave: 2);
        Synth.Sweep(b, 0.01f, 1.6f, r.Range(1950, 2150), 105, 0.45f, env, 0.14f);
        // Sputtering electric crackle and a dying, wobbling hum.
        Synth.Crackle(b, ref r, 0, 1.4f, 160, 0.5f, 0.5f);
        Synth.BandNoise(b, ref r, 0, 1.2f, 5000, 900, 0.4f, 2.5f, t => Synth.AD(t, 0.002f, 0.3f), 0.8f);
        Synth.Tone(b, 0, 1.6f, 100, 0.005f, 0.35f, 0.3f, [1f, 0.5f, 0.3f], 11f, 0.06f);
        Synth.Saturate(b, 1.4f);
        return b;
    }

    private static float[] ShieldUp(ref SynthRng r)
    {
        var b = Synth.Alloc(2.2f);
        // Rising hum (saw + octave) and a rising shimmer, settling with a soft chime.
        Func<float, float> env = t => MathF.Min(1, t / 0.6f) * Math.Clamp((1.4f - t) / 0.5f, 0f, 1f);
        float f0 = r.Range(66, 74);
        Synth.Sweep(b, 0, 1.4f, f0, f0 * 3.2f, 0.45f, env, 0.4f, wave: 2);
        Synth.Sweep(b, 0, 1.4f, f0 * 2.01f, f0 * 6.45f, 0.45f, env, 0.18f);
        Synth.Sweep(b, 0.1f, 1.3f, 500, 1800, 0.5f, t => MathF.Min(1, t / 0.5f) * Math.Clamp((1.3f - t) / 0.4f, 0f, 1f), 0.08f, wave: 1);
        Synth.BandNoise(b, ref r, 0, 1.4f, 800, 5000, 0.5f, 3f, env, 0.5f);
        Synth.Bell(b, 0.95f, Synth.Midi(81), 0.35f, 0.18f, 2f, 1.2f);
        return b;
    }

    // ------------------------------------------------------------------ missiles

    private static float[] TacticalLaunch(ref SynthRng r)
    {
        var b = Synth.Alloc(2.4f);
        // Ignition thump and pop.
        Synth.Thump(b, 0, 180, 55, 0.04f, 0.002f, 0.09f, 0.9f);
        Synth.Noise(b, ref r, 0, 0.0008f, 0.02f, 0.7f, 9000, 2500, 0.03f, hp: 600);
        // Rocket roar fading as it climbs, and a whoosh sweeping up as it accelerates away.
        Synth.Noise(b, ref r, 0.01f, 0.05f, 0.6f, 0.9f, 2600, 700, 0.8f);
        Synth.Rumble(b, ref r, 0, 0.04f, 0.5f, 0.9f, 200);
        Synth.BandNoise(b, ref r, 0.02f, 2.2f, r.Range(350, 450), r.Range(2400, 3000), 0.5f, 1.6f,
            t => MathF.Min(1, t / 0.15f) * MathF.Exp(-t / 0.55f), 1.3f);
        Synth.Crackle(b, ref r, 0.03f, 1.2f, 90, 0.2f, 0.5f);
        Synth.Saturate(b, 1.7f);
        return b;
    }

    private static float[] NukeLaunch(ref SynthRng r)
    {
        var b = Synth.Alloc(3.8f);
        // Deep ignition boom.
        Synth.Thump(b, 0, 90, 28, 0.12f, 0.004f, 0.35f, 1.2f);
        Synth.Noise(b, ref r, 0, 0.001f, 0.03f, 0.6f, 7000, 1500, 0.05f, hp: 300);
        // Long deep roar: swells, holds ≈1.6 s, then recedes as the missile climbs.
        Func<float, float> roar = t => MathF.Min(1, t / 0.6f) * (t < 1.6f ? 1f : MathF.Exp(-(t - 1.6f) / 0.6f));
        Synth.Rumble(b, ref r, 0, 0.3f, 1.2f, 1.4f, 140, length: 3.7f);
        Synth.BandNoise(b, ref r, 0, 3.6f, 260, 900, 1.5f, 0.9f, roar, 1.4f);
        Synth.Noise(b, ref r, 0.05f, 0.4f, 0.9f, 0.6f, 1800, 500, 1.5f);
        // Engine drone (detuned low saws) and the climbing whoosh.
        Synth.Sweep(b, 0, 3.6f, 48, 62, 1.5f, roar, 0.35f, wave: 2);
        Synth.Sweep(b, 0, 3.6f, 49.3f, 63.5f, 1.5f, roar, 0.3f, wave: 2);
        Synth.BandNoise(b, ref r, 0.3f, 3.3f, 500, 2600, 1.2f, 1.8f, t => MathF.Min(1, t / 0.8f) * MathF.Exp(-t / 1.1f), 0.8f);
        Synth.Crackle(b, ref r, 0.1f, 2.5f, 120, 0.25f, 1.2f);
        Synth.Saturate(b, 2f);
        return b;
    }

    private static float[] Intercept(ref SynthRng r)
    {
        var b = Synth.Alloc(1.2f);
        // Sharp mid-air crack, short body, shrapnel fizz and a metallic snap (no ground rumble).
        Synth.Noise(b, ref r, 0, 0.0003f, 0.008f, 1f, 13000, 5000, 0.01f, hp: 1800);
        Synth.Noise(b, ref r, 0.001f, 0.002f, 0.07f, 0.8f, 4200, 600, 0.06f);
        Synth.Thump(b, 0, r.Range(170, 200), 60, 0.025f, 0.001f, 0.07f, 0.8f);
        Synth.Rumble(b, ref r, 0.01f, 0.02f, 0.2f, 0.5f, 300);
        Synth.Crackle(b, ref r, 0.02f, 0.5f, 90, 0.3f, 0.15f);
        Synth.Ping(b, 0.005f, r.Range(1100, 1600), 0.05f, 0.2f);
        Synth.Saturate(b, 2f);
        return b;
    }

    /// <summary>Nuclear blast, written in real time/frequency and compressed by <see cref="NukePitch"/>.</summary>
    private static float[] NukeExplosion(ref SynthRng r)
    {
        static float T(float seconds) => seconds * NukePitch;
        static float F(float hz) => hz / NukePitch;
        var b = Synth.Alloc(T(5.45f));
        // Flash crack and the massive low boom.
        Synth.Noise(b, ref r, 0, T(0.001f), T(0.03f), 1f, F(9000), F(2000), T(0.03f), hp: F(400));
        Synth.Thump(b, 0, F(70), F(20), T(0.25f), T(0.005f), T(0.9f), 1.5f);
        Synth.Thump(b, T(0.02f), F(105), F(32), T(0.15f), T(0.003f), T(0.4f), 0.6f);
        // Blast roar closing down to a long, deep rumble.
        Synth.Noise(b, ref r, T(0.005f), T(0.01f), T(1.2f), 1.2f, F(2500), F(150), T(0.8f));
        Synth.Rumble(b, ref r, 0, T(0.1f), T(1.6f), 1.6f, F(110), length: T(5.4f));
        // Rolling thunder echoing back from the terrain.
        for (int i = 0; i < 6; i++)
        {
            float t0 = r.Range(0.5f, 3.6f);
            Synth.Rumble(b, ref r, T(t0), T(r.Range(0.08f, 0.25f)), T(r.Range(0.35f, 0.7f)), r.Range(0.3f, 0.6f), F(r.Range(220, 420)));
            Synth.Noise(b, ref r, T(t0), T(0.05f), T(r.Range(0.2f, 0.4f)), r.Range(0.15f, 0.3f), F(900), F(250), T(0.3f));
        }
        // Debris crackle and the after-wind of the fireball.
        Synth.Crackle(b, ref r, T(0.15f), T(3.5f), 80, 0.3f, T(1.2f));
        Synth.BandNoise(b, ref r, T(0.6f), T(4.8f), F(450), F(140), T(2f), 1.2f,
            t => MathF.Min(1, t / T(1.2f)) * MathF.Exp(-t / T(1.6f)), 0.6f);
        Synth.Saturate(b, 2.4f);
        return b;
    }

    // ------------------------------------------------------------------ alerts

    /// <summary>"Strategic launch detected": three rising, buzzing klaxon blasts (≈2 s).</summary>
    private static float[] StrategicAlert()
    {
        var b = Synth.Alloc(2.15f);
        const float pulse = 0.55f, period = 0.66f;
        Func<float, float> env = t => MathF.Min(1, t / 0.02f) * Math.Clamp((pulse - t) / 0.06f, 0f, 1f);
        for (int i = 0; i < 3; i++)
        {
            float t0 = i * period;
            Synth.Sweep(b, t0, pulse, 330, 470, 0.15f, env, 0.5f, wave: 2);
            Synth.Sweep(b, t0, pulse, 495, 705, 0.15f, env, 0.22f, wave: 2);
            Synth.Sweep(b, t0, pulse, 165, 235, 0.15f, env, 0.3f, wave: 1);
        }
        Synth.Saturate(b, 2.5f);
        return b;
    }
}
