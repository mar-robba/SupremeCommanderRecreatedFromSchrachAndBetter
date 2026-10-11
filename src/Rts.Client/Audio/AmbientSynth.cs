namespace Rts.Client.Audio;

/// <summary>
/// Real-time generator for the continuous layers (pure C#, rendered in blocks into a raylib AudioStream):
/// <list type="bullet">
/// <item>wind: two decorrelated noise channels through gust-modulated band-pass filters;</item>
/// <item>generative music: a slow pad (Markov chain over D-minor chords, wavetable voices with long
///   attack/release crossfades, drifting low-pass), sparse FM bells and a small Freeverb-style reverb;</item>
/// <item>loops: reclaim "vacuum" hum and engineer build "nano-lathe" hum, with gain + pan from the game.</item>
/// </list>
/// Parameters are plain fields set by the main thread before each <see cref="Render"/>; they are smoothed per
/// sample so changes never click.
/// </summary>
public sealed class AmbientSynth
{
    public const int SampleRate = Synth.SampleRate;
    private const float Dt = 1f / SampleRate;

    // Targets (set by GameAudio).
    public float WindGain, MusicGain, ReclaimGain, ReclaimPan, BuildGain, BuildPan;
    /// <summary>0..1 battle intensity: brightens the pad and adds bells.</summary>
    public float Tension;

    // Smoothed values.
    private float _wind, _music, _recl, _reclPan, _build, _buildPan, _tension;

    private SynthRng _rng;
    private double _time;

    public AmbientSynth(uint seed = 1)
    {
        _rng = new SynthRng(seed);
        BuildWavetable();
        InitReverb();
        _chordTimer = 0;
    }

    /// <summary>Renders interleaved stereo float samples (frames * 2).</summary>
    public void Render(Span<float> stereo, int frames)
    {
        float sm = 1f - MathF.Exp(-Dt / 0.06f); // ~60 ms parameter smoothing
        for (int k = 0; k < frames; k++)
        {
            _wind += sm * (WindGain - _wind);
            _music += sm * (MusicGain - _music);
            _recl += sm * (ReclaimGain - _recl);
            _reclPan += sm * (ReclaimPan - _reclPan);
            _build += sm * (BuildGain - _build);
            _buildPan += sm * (BuildPan - _buildPan);
            _tension += sm * 0.02f * (Tension - _tension);

            float l = 0, r = 0;
            if (_wind > 1e-4f) Wind(ref l, ref r, _wind);
            if (_music > 1e-4f) Music(ref l, ref r, _music);
            if (_recl > 1e-4f) ReclaimHum(ref l, ref r, _recl, _reclPan);
            if (_build > 1e-4f) BuildHum(ref l, ref r, _build, _buildPan);

            if (!float.IsFinite(l) || !float.IsFinite(r)) { l = r = 0; ResetFilters(); }
            stereo[2 * k] = SoftClip(l);
            stereo[2 * k + 1] = SoftClip(r);
            _time += Dt;
        }
    }

    private static float SoftClip(float x) => x > 0.8f || x < -0.8f ? MathF.Tanh(x) : x;

    private void ResetFilters()
    {
        _windL = _windR = _windLowL = _windLowR = default;
        _padL = _padR = default;
        InitReverb();
    }

    private static void Pan(float pan, out float gl, out float gr)
    {
        float a = (Math.Clamp(pan, -1f, 1f) + 1f) * MathF.PI * 0.25f;
        gl = MathF.Cos(a);
        gr = MathF.Sin(a);
    }

    // ------------------------------------------------------------------ wind

    private Svf _windL, _windR, _windLowL, _windLowR;
    private float _gust, _gustFrom, _gustTo = 0.5f, _gustT, _gustLen = 3f;
    private int _windCtr;

    private void Wind(ref float l, ref float r, float gain)
    {
        // Smooth random gusts (new target every 2–6 s, smoothstep interpolation).
        _gustT += Dt;
        if (_gustT >= _gustLen)
        {
            _gustT = 0;
            _gustFrom = _gustTo;
            _gustTo = _rng.Range(0.1f, 1f);
            _gustLen = _rng.Range(2f, 6f);
        }
        float u = _gustT / _gustLen;
        _gust = _gustFrom + (_gustTo - _gustFrom) * u * u * (3 - 2 * u);
        if ((_windCtr++ & 31) == 0)
        {
            float fc = 260f + 520f * _gust;
            _windL.Set(fc, 0.9f);
            _windR.Set(fc * 1.13f, 0.9f);
            _windLowL.Set(140f, 0.5f);
            _windLowR.Set(150f, 0.5f);
        }
        float nl = _rng.Bipolar(), nr = _rng.Bipolar();
        float amp = gain * (0.35f + 0.65f * _gust);
        l += (_windL.Bp(nl) * 0.55f + _windLowL.Lp(nl) * 0.5f) * amp;
        r += (_windR.Bp(nr) * 0.55f + _windLowR.Lp(nr) * 0.5f) * amp;
    }

    // ------------------------------------------------------------------ music

    private const int TableSize = 2048;
    private readonly float[] _table = new float[TableSize + 1];

    private void BuildWavetable()
    {
        // Soft saw: harmonics 1..7 with 1/h^1.5 roll-off.
        float max = 0;
        for (int i = 0; i <= TableSize; i++)
        {
            float x = i / (float)TableSize * 2f * MathF.PI, s = 0;
            for (int h = 1; h <= 7; h++) s += MathF.Sin(x * h) / MathF.Pow(h, 1.5f);
            _table[i] = s;
            max = MathF.Max(max, MathF.Abs(s));
        }
        for (int i = 0; i <= TableSize; i++) _table[i] /= max;
    }

    private float Table(float phase)
    {
        float p = phase * TableSize;
        int i = (int)p;
        float f = p - i;
        i &= TableSize - 1;
        return _table[i] + (_table[i + 1] - _table[i]) * f;
    }

    private struct PadVoice
    {
        public bool Active;
        public float Freq, Level, Target, Rate, Pan;
        public float P0, P1, P2;
        public bool Sine; // low drone notes are pure sines
    }

    private struct BellVoice
    {
        public bool Active;
        public float Freq, Age, Decay, Amp, Pan;
    }

    // Chords as MIDI notes (D minor with a borrowed bII): Dm9, Bbmaj7, Gm7, Fmaj7/A, Cadd9, Am7, Ebmaj7.
    private static readonly int[][] Chords =
    [
        [38, 50, 57, 60, 64, 65],
        [34, 46, 53, 57, 62],
        [31, 43, 50, 53, 58, 62],
        [33, 45, 53, 60, 64],
        [36, 48, 55, 62, 64],
        [33, 45, 52, 55, 60],
        [39, 51, 55, 58, 62],
    ];

    // Markov transitions (next chord candidates).
    private static readonly int[][] Next =
    [
        [1, 2, 3, 6],
        [4, 0, 2],
        [0, 3, 5],
        [4, 1, 0],
        [0, 5, 3],
        [1, 2, 6],
        [4, 0],
    ];

    private readonly PadVoice[] _pad = new PadVoice[20];
    private readonly BellVoice[] _bells = new BellVoice[6];
    private int _activeVoices;
    private int _chord = -1;
    private float _chordTimer, _bellTimer = 3f;
    private Svf _padL, _padR;
    private int _musicCtr;

    private void NextChord()
    {
        int c = _chord < 0 ? 0 : Next[_chord][(int)(_rng.Next() * Next[_chord].Length) % Next[_chord].Length];
        _chord = c;
        _chordTimer = _rng.Range(10f, 16f);
        for (int i = 0; i < _pad.Length; i++)
            if (_pad[i].Active) { _pad[i].Target = 0; _pad[i].Rate = Dt / 5f; }
        foreach (int note in Chords[c])
        {
            int slot = -1;
            for (int i = 0; i < _pad.Length && slot < 0; i++) if (!_pad[i].Active) slot = i;
            if (slot < 0) break;
            _pad[slot] = new PadVoice
            {
                Active = true, Freq = Synth.Midi(note), Level = 0, Target = 1, Rate = Dt / 4f,
                Pan = _rng.Range(-0.6f, 0.6f), P0 = _rng.Next(), P1 = _rng.Next(), P2 = _rng.Next(), Sine = note < 40,
            };
        }
    }

    private void Music(ref float l, ref float r, float gain)
    {
        _chordTimer -= Dt;
        if (_chordTimer <= 0) NextChord();

        // Sparse bells from the current chord, two octaves up.
        _bellTimer -= Dt;
        if (_bellTimer <= 0 && _chord >= 0)
        {
            _bellTimer = _rng.Range(1.8f, 6f) * (1f - 0.5f * _tension);
            if (_rng.Next() < 0.7f)
            {
                var notes = Chords[_chord];
                int n = notes[1 + (int)(_rng.Next() * (notes.Length - 1)) % (notes.Length - 1)];
                while (n < 72) n += 12;
                if (_rng.Next() < 0.3f) n += 12;
                for (int i = 0; i < _bells.Length; i++)
                    if (!_bells[i].Active)
                    {
                        _bells[i] = new BellVoice { Active = true, Freq = Synth.Midi(n), Age = 0, Decay = _rng.Range(1.6f, 3f), Amp = _rng.Range(0.05f, 0.09f), Pan = _rng.Range(-0.8f, 0.8f) };
                        break;
                    }
            }
        }

        if ((_musicCtr++ & 63) == 0)
        {
            float lfo = 0.5f + 0.5f * MathF.Sin((float)(_time * 2 * Math.PI / 37.0));
            float fc = 650f + 1000f * lfo + 1800f * _tension;
            _padL.Set(fc, 0.8f);
            _padR.Set(fc * 1.07f, 0.8f);
        }

        float pl = 0, pr = 0, drone = 0;
        int active = 0;
        for (int i = 0; i < _pad.Length; i++)
        {
            ref var v = ref _pad[i];
            if (!v.Active) continue;
            active++;
            if (v.Level < v.Target) v.Level = MathF.Min(v.Target, v.Level + v.Rate);
            else if (v.Level > v.Target) v.Level = MathF.Max(v.Target, v.Level - v.Rate);
            if (v.Level <= 0 && v.Target <= 0) { v.Active = false; continue; }
            float f = v.Freq * Dt;
            v.P0 += f * 0.9965f; v.P1 += f; v.P2 += f * 1.0035f;
            if (v.P0 >= 1) v.P0 -= 1;
            if (v.P1 >= 1) v.P1 -= 1;
            if (v.P2 >= 1) v.P2 -= 1;
            float e = v.Level * v.Level;
            if (v.Sine)
            {
                drone += MathF.Sin(v.P1 * 2f * MathF.PI) * e * 0.5f;
                continue;
            }
            float a = Table(v.P0), b = Table(v.P1), c = Table(v.P2);
            Pan(v.Pan, out float gl, out float gr);
            pl += (a * 0.8f + b * 0.6f + c * 0.3f) * e * gl;
            pr += (a * 0.3f + b * 0.6f + c * 0.8f) * e * gr;
        }
        _activeVoices = active;
        pl = _padL.Lp(pl) * 0.07f + drone * 0.12f;
        pr = _padR.Lp(pr) * 0.07f + drone * 0.12f;

        float bl = 0, br = 0;
        for (int i = 0; i < _bells.Length; i++)
        {
            ref var b = ref _bells[i];
            if (!b.Active) continue;
            b.Age += Dt;
            if (b.Age > b.Decay * 5f) { b.Active = false; continue; }
            float t = b.Age;
            float env = MathF.Min(1f, t / 0.003f) * MathF.Exp(-t / b.Decay);
            float m = MathF.Sin(2f * MathF.PI * b.Freq * 3.5f * t) * 1.8f * MathF.Exp(-t / (b.Decay * 0.3f));
            float s = MathF.Sin(2f * MathF.PI * b.Freq * t + m) * env * b.Amp;
            Pan(b.Pan, out float gl, out float gr);
            bl += s * gl;
            br += s * gr;
        }

        float dryL = pl + bl, dryR = pr + br;
        Reverb(dryL * 0.5f + bl * 0.6f, dryR * 0.5f + br * 0.6f, out float wl, out float wr);
        l += (dryL + wl * 0.6f) * gain;
        r += (dryR + wr * 0.6f) * gain;
    }

    // Freeverb-lite: 4 damped combs + 2 allpasses per channel.
    private static readonly int[] CombLens = [1116, 1188, 1277, 1356];
    private static readonly int[] ApLens = [556, 441];
    private const int StereoSpread = 23;
    private float[][] _combL = [], _combR = [], _apL = [], _apR = [];
    private int[] _combIdxL = [], _combIdxR = [], _apIdxL = [], _apIdxR = [];
    private float[] _combStoreL = [], _combStoreR = [];

    private void InitReverb()
    {
        _combL = CombLens.Select(n => new float[n]).ToArray();
        _combR = CombLens.Select(n => new float[n + StereoSpread]).ToArray();
        _apL = ApLens.Select(n => new float[n]).ToArray();
        _apR = ApLens.Select(n => new float[n + StereoSpread]).ToArray();
        _combIdxL = new int[4]; _combIdxR = new int[4];
        _apIdxL = new int[2]; _apIdxR = new int[2];
        _combStoreL = new float[4]; _combStoreR = new float[4];
    }

    private void Reverb(float inL, float inR, out float outL, out float outR)
    {
        const float feedback = 0.86f, damp = 0.35f;
        float input = (inL + inR) * 0.5f * 0.3f;
        outL = Channel(input, _combL, _combIdxL, _combStoreL, _apL, _apIdxL);
        outR = Channel(input, _combR, _combIdxR, _combStoreR, _apR, _apIdxR);

        static float Channel(float x, float[][] combs, int[] ci, float[] store, float[][] aps, int[] ai)
        {
            float o = 0;
            for (int c = 0; c < combs.Length; c++)
            {
                var buf = combs[c];
                int i = ci[c];
                float y = buf[i];
                store[c] = y * (1 - damp) + store[c] * damp;
                buf[i] = x + store[c] * feedback;
                ci[c] = i + 1 == buf.Length ? 0 : i + 1;
                o += y;
            }
            for (int a = 0; a < aps.Length; a++)
            {
                var buf = aps[a];
                int i = ai[a];
                float bo = buf[i];
                buf[i] = o + bo * 0.5f;
                o = bo - o;
                ai[a] = i + 1 == buf.Length ? 0 : i + 1;
            }
            return o;
        }
    }

    // ------------------------------------------------------------------ construction loops

    private Svf _reclNoise;
    private float _reclPh, _reclPh2;
    private int _reclCtr;

    private void ReclaimHum(ref float l, ref float r, float gain, float pan)
    {
        float t = (float)_time;
        if ((_reclCtr++ & 31) == 0) _reclNoise.Set(1100f + 500f * MathF.Sin(t * 2.1f), 3f);
        _reclPh += 92f * Dt; if (_reclPh >= 1) _reclPh -= 1;
        _reclPh2 += 276f * Dt; if (_reclPh2 >= 1) _reclPh2 -= 1;
        float trem = 0.7f + 0.3f * MathF.Sin(t * 2f * MathF.PI * 6.5f);
        float s = (MathF.Sin(_reclPh * 2f * MathF.PI) * 0.5f + MathF.Sin(_reclPh2 * 2f * MathF.PI) * 0.2f) * trem
                  + _reclNoise.Bp(_rng.Bipolar()) * 0.6f;
        Pan(pan, out float gl, out float gr);
        l += s * gain * gl * 0.5f;
        r += s * gain * gr * 0.5f;
    }

    private Svf _buildNoise;
    private float _bPh1, _bPh2;
    private int _buildCtr;

    private void BuildHum(ref float l, ref float r, float gain, float pan)
    {
        float t = (float)_time;
        if ((_buildCtr++ & 63) == 0) _buildNoise.Set(5200f, 1.5f);
        _bPh1 += 523f * Dt; if (_bPh1 >= 1) _bPh1 -= 1;
        _bPh2 += 786f * Dt; if (_bPh2 >= 1) _bPh2 -= 1;
        float am = 0.55f + 0.45f * MathF.Sin(t * 2f * MathF.PI * 11f);
        float s = (MathF.Sin(_bPh1 * 2f * MathF.PI) * 0.35f + MathF.Sin(_bPh2 * 2f * MathF.PI) * 0.2f) * am
                  + _buildNoise.Bp(_rng.Bipolar()) * 0.35f * (0.5f + 0.5f * MathF.Sin(t * 17f));
        Pan(pan, out float gl, out float gr);
        l += s * gain * gl * 0.35f;
        r += s * gain * gr * 0.35f;
    }
}
