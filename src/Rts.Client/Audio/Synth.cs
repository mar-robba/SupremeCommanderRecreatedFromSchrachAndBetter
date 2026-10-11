namespace Rts.Client.Audio;

/// <summary>Small xorshift RNG for sound synthesis (client-side, deterministic per seed so tests are stable).</summary>
public struct SynthRng
{
    private uint _s;

    public SynthRng(uint seed) => _s = seed == 0 ? 0x9E3779B9u : seed * 2654435761u + 0x6A09E667u;

    /// <summary>Uniform [0, 1).</summary>
    public float Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 17;
        _s ^= _s << 5;
        return (_s & 0xFFFFFF) / 16777216f;
    }

    /// <summary>Uniform [-1, 1).</summary>
    public float Bipolar() => Next() * 2f - 1f;

    public float Range(float a, float b) => a + (b - a) * Next();
}

/// <summary>
/// Topology-preserving state variable filter (Zavalishin/Simper). Stable when the cutoff changes every sample,
/// so it is used for sweeps. Call <see cref="Set"/> then <see cref="Process"/>; read Low/Band/High.
/// </summary>
public struct Svf
{
    private float _ic1, _ic2, _a1, _a2, _a3, _k;
    public float Low, Band, High;

    public void Set(float cutoffHz, float q, int sampleRate = Synth.SampleRate)
    {
        float fc = Math.Clamp(cutoffHz, 10f, sampleRate * 0.45f);
        float g = MathF.Tan(MathF.PI * fc / sampleRate);
        _k = 1f / MathF.Max(0.05f, q);
        _a1 = 1f / (1f + g * (g + _k));
        _a2 = g * _a1;
        _a3 = g * _a2;
    }

    public void Process(float x)
    {
        float v3 = x - _ic2;
        float v1 = _a1 * _ic1 + _a2 * v3;
        float v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
        _ic1 = 2f * v1 - _ic1;
        _ic2 = 2f * v2 - _ic2;
        Low = v2;
        Band = v1;
        High = x - _k * v1 - v2;
    }

    public float Lp(float x) { Process(x); return Low; }
    public float Bp(float x) { Process(x); return Band; }
    public float Hp(float x) { Process(x); return High; }
}

/// <summary>One-pole smoother / low-pass (also used as a DC-blocking high-pass: x - Lp(x)).</summary>
public struct OnePole
{
    public float Z;

    public static float Coef(float cutoffHz, int sampleRate = Synth.SampleRate) =>
        1f - MathF.Exp(-2f * MathF.PI * MathF.Min(cutoffHz, sampleRate * 0.45f) / sampleRate);

    public float Lp(float x, float a) => Z += a * (x - Z);
    public float Hp(float x, float a) { Z += a * (x - Z); return x - Z; }
}

/// <summary>
/// Offline synthesis helpers: every function ADDS a layer into a mono float buffer at <see cref="SampleRate"/>.
/// Recipes in <see cref="SoundBank"/> combine them (thumps, filtered noise bursts, metallic pings, sweeps...).
/// Pure C#, no raylib (unit-tested).
/// </summary>
public static class Synth
{
    public const int SampleRate = 44100;
    public const float Dt = 1f / SampleRate;

    public static float[] Alloc(float seconds) => new float[Math.Max(1, (int)(seconds * SampleRate))];

    public static int Index(float t) => (int)(t * SampleRate);

    /// <summary>Attack (linear) then exponential decay with time constant tau.</summary>
    public static float AD(float t, float attack, float tau) =>
        t < 0 ? 0 : t < attack ? t / MathF.Max(1e-5f, attack) : MathF.Exp(-(t - attack) / MathF.Max(1e-5f, tau));

    /// <summary>Exponential interpolation from a to b with time constant tau (a at t=0, -> b).</summary>
    public static float Glide(float a, float b, float t, float tau) => b + (a - b) * MathF.Exp(-t / MathF.Max(1e-5f, tau));

    /// <summary>Sine with an exponential pitch drop f0 -> f1 (time constant sweep) and an AD envelope.</summary>
    public static void Thump(float[] buf, float start, float f0, float f1, float sweep, float attack, float decay, float amp, float length = 0)
    {
        int i0 = Index(start);
        int n = length > 0 ? Index(length) : Index(attack + decay * 7f);
        double phase = 0;
        for (int k = 0; k < n && i0 + k < buf.Length; k++)
        {
            float t = k * Dt;
            float f = Glide(f0, f1, t, sweep);
            phase += f * Dt;
            buf[i0 + k] += MathF.Sin((float)(phase * 2 * Math.PI)) * AD(t, attack, decay) * amp;
        }
    }

    /// <summary>White noise through a low-pass whose cutoff glides lp0 -> lp1, optional high-pass, AD envelope.</summary>
    public static void Noise(float[] buf, ref SynthRng rng, float start, float attack, float decay, float amp,
                             float lp0, float lp1, float lpTau, float hp = 0, float q = 0.7f, float length = 0)
    {
        int i0 = Index(start);
        int n = length > 0 ? Index(length) : Index(attack + decay * 7f);
        var f = new Svf();
        var h = new OnePole();
        float ha = hp > 0 ? OnePole.Coef(hp) : 0;
        for (int k = 0; k < n && i0 + k < buf.Length; k++)
        {
            float t = k * Dt;
            if ((k & 15) == 0) f.Set(Glide(lp0, lp1, t, lpTau), q);
            float x = f.Lp(rng.Bipolar());
            if (hp > 0) x = h.Hp(x, ha);
            buf[i0 + k] += x * AD(t, attack, decay) * amp;
        }
    }

    /// <summary>Band-passed noise with a center frequency gliding fc0 -> fc1 and a custom envelope.</summary>
    public static void BandNoise(float[] buf, ref SynthRng rng, float start, float length, float fc0, float fc1, float fcTau, float q,
                                 Func<float, float> env, float amp)
    {
        int i0 = Index(start);
        int n = Index(length);
        var f = new Svf();
        for (int k = 0; k < n && i0 + k < buf.Length; k++)
        {
            float t = k * Dt;
            if ((k & 15) == 0) f.Set(Glide(fc0, fc1, t, fcTau), q);
            buf[i0 + k] += f.Bp(rng.Bipolar()) * env(t) * amp;
        }
    }

    /// <summary>Brown (integrated) noise, low-passed: rumbles and debris tails.</summary>
    public static void Rumble(float[] buf, ref SynthRng rng, float start, float attack, float decay, float amp, float lp, float length = 0)
    {
        int i0 = Index(start);
        int n = length > 0 ? Index(length) : Index(attack + decay * 6f);
        var f = new Svf();
        f.Set(lp, 0.6f);
        var dc = new OnePole();
        float dca = OnePole.Coef(20f);
        float b = 0;
        for (int k = 0; k < n && i0 + k < buf.Length; k++)
        {
            float t = k * Dt;
            b = b * 0.995f + rng.Bipolar() * 0.12f;
            float x = dc.Hp(f.Lp(b), dca);
            buf[i0 + k] += x * AD(t, attack, decay) * amp;
        }
    }

    /// <summary>Metallic ping: inharmonic partials (free-bar modes) with individual decays.</summary>
    public static void Ping(float[] buf, float start, float freq, float decay, float amp)
    {
        ReadOnlySpan<float> ratios = [1f, 2.756f, 5.404f, 8.933f];
        ReadOnlySpan<float> gains = [1f, 0.55f, 0.3f, 0.15f];
        int i0 = Index(start);
        int n = Index(decay * 6f);
        for (int p = 0; p < ratios.Length; p++)
        {
            float f = freq * ratios[p];
            if (f > SampleRate * 0.45f) continue;
            float d = decay / (1f + p * 0.8f);
            float w = 2f * MathF.PI * f * Dt;
            for (int k = 0; k < n && i0 + k < buf.Length; k++)
            {
                float t = k * Dt;
                buf[i0 + k] += MathF.Sin(w * k) * AD(t, 0.0005f, d) * amp * gains[p];
            }
        }
    }

    /// <summary>Sparse random clicks (crackling fire / debris), each a tiny filtered impulse.</summary>
    public static void Crackle(float[] buf, ref SynthRng rng, float start, float length, float perSecond, float amp, float decay)
    {
        int i0 = Index(start);
        int n = Index(length);
        var f = new Svf();
        f.Set(3200f, 1.2f);
        float level = 0;
        for (int k = 0; k < n && i0 + k < buf.Length; k++)
        {
            float t = k * Dt;
            float density = perSecond * MathF.Exp(-t / MathF.Max(1e-3f, decay));
            if (rng.Next() < density * Dt) level = rng.Range(0.3f, 1f);
            level *= 0.993f;
            buf[i0 + k] += f.Bp(rng.Bipolar() * level) * amp;
        }
    }

    /// <summary>Additive tone (harmonics 1..n with given gains), AD envelope, optional vibrato.</summary>
    public static void Tone(float[] buf, float start, float length, float freq, float attack, float decay, float amp,
                            ReadOnlySpan<float> harmonics, float vibratoHz = 0, float vibratoDepth = 0)
    {
        int i0 = Index(start);
        int n = Index(length);
        double phase = 0;
        for (int k = 0; k < n && i0 + k < buf.Length; k++)
        {
            float t = k * Dt;
            float f = freq * (1f + (vibratoDepth > 0 ? MathF.Sin(2f * MathF.PI * vibratoHz * t) * vibratoDepth : 0));
            phase += f * Dt;
            float ph = (float)(phase * 2 * Math.PI);
            float s = 0;
            for (int h = 0; h < harmonics.Length; h++)
                if (freq * (h + 1) < SampleRate * 0.45f) s += MathF.Sin(ph * (h + 1)) * harmonics[h];
            buf[i0 + k] += s * AD(t, attack, decay) * amp;
        }
    }

    /// <summary>Frequency sweep f0 -> f1 (exponential, time constant tau) with a custom envelope and waveform
    /// (0 = sine, 1 = soft square, 2 = soft saw).</summary>
    public static void Sweep(float[] buf, float start, float length, float f0, float f1, float tau, Func<float, float> env, float amp, int wave = 0)
    {
        int i0 = Index(start);
        int n = Index(length);
        double phase = 0;
        for (int k = 0; k < n && i0 + k < buf.Length; k++)
        {
            float t = k * Dt;
            phase += Glide(f0, f1, t, tau) * Dt;
            float ph = (float)(phase * 2 * Math.PI);
            float s = wave switch
            {
                1 => MathF.Tanh(MathF.Sin(ph) * 3f) * 0.8f,
                2 => MathF.Sin(ph) + 0.4f * MathF.Sin(2 * ph) + 0.2f * MathF.Sin(3 * ph),
                _ => MathF.Sin(ph),
            };
            buf[i0 + k] += s * env(t) * amp;
        }
    }

    /// <summary>FM bell: carrier f, modulator at f*ratio, modulation index decaying with the amplitude.</summary>
    public static void Bell(float[] buf, float start, float freq, float decay, float amp, float ratio = 3.5f, float index = 2.2f)
    {
        int i0 = Index(start);
        int n = Index(decay * 6f);
        float wc = 2f * MathF.PI * freq * Dt, wm = wc * ratio;
        for (int k = 0; k < n && i0 + k < buf.Length; k++)
        {
            float t = k * Dt;
            float e = AD(t, 0.002f, decay);
            float m = MathF.Sin(wm * k) * index * MathF.Exp(-t / (decay * 0.4f));
            buf[i0 + k] += MathF.Sin(wc * k + m) * e * amp;
        }
    }

    // ------------------------------------------------------------------ post-processing

    /// <summary>Soft clipping (tanh) for punch; drive 1 = gentle.</summary>
    public static void Saturate(float[] buf, float drive)
    {
        float norm = 1f / MathF.Tanh(drive);
        for (int i = 0; i < buf.Length; i++) buf[i] = MathF.Tanh(buf[i] * drive) * norm;
    }

    /// <summary>Removes DC offset (one-pole high-pass at ~15 Hz).</summary>
    public static void RemoveDc(float[] buf)
    {
        var h = new OnePole();
        float a = OnePole.Coef(15f);
        for (int i = 0; i < buf.Length; i++) buf[i] = h.Hp(buf[i], a);
    }

    /// <summary>Short fade-in/out so sounds never start or end with a click.</summary>
    public static void FadeEdges(float[] buf, float fadeIn = 0.0015f, float fadeOut = 0.03f)
    {
        int a = Math.Min(buf.Length / 2, Index(fadeIn));
        int b = Math.Min(buf.Length / 2, Index(fadeOut));
        for (int i = 0; i < a; i++) buf[i] *= i / (float)a;
        for (int i = 0; i < b; i++) buf[buf.Length - 1 - i] *= i / (float)b;
    }

    public static float Peak(ReadOnlySpan<float> buf)
    {
        float p = 0;
        foreach (float x in buf) p = MathF.Max(p, MathF.Abs(x));
        return p;
    }

    /// <summary>Scales the buffer so its peak is <paramref name="peak"/> (NaN/Inf are zeroed first).</summary>
    public static void Normalize(float[] buf, float peak = 0.9f)
    {
        for (int i = 0; i < buf.Length; i++) if (!float.IsFinite(buf[i])) buf[i] = 0;
        float p = Peak(buf);
        if (p < 1e-9f) return;
        float g = peak / p;
        for (int i = 0; i < buf.Length; i++) buf[i] *= g;
    }

    /// <summary>Trims the silent tail (below threshold) keeping a short fade.</summary>
    public static float[] TrimTail(float[] buf, float threshold = 0.0015f)
    {
        int end = buf.Length;
        while (end > 1 && MathF.Abs(buf[end - 1]) < threshold) end--;
        end = Math.Min(buf.Length, end + Index(0.01f));
        if (end == buf.Length) return buf;
        var r = new float[end];
        Array.Copy(buf, r, end);
        return r;
    }

    public static short[] ToPcm16(ReadOnlySpan<float> buf)
    {
        var r = new short[buf.Length];
        for (int i = 0; i < buf.Length; i++) r[i] = (short)Math.Clamp((int)MathF.Round(buf[i] * 32767f), -32768, 32767);
        return r;
    }

    /// <summary>MIDI note number to frequency (A4 = 69 = 440 Hz).</summary>
    public static float Midi(float note) => 440f * MathF.Pow(2f, (note - 69f) / 12f);
}
