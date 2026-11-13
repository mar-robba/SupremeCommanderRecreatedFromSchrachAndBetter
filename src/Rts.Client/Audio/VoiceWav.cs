namespace Rts.Client.Audio;

/// <summary>A decoded 16-bit PCM WAV file (interleaved samples).</summary>
public sealed class VoiceWav
{
    public required short[] Samples;
    public required int SampleRate;
    public required int Channels;

    public int Frames => Channels > 0 ? Samples.Length / Channels : 0;
    public double Duration => SampleRate > 0 ? (double)Frames / SampleRate : 0;

    /// <summary>RMS level, 0..1 of full scale.</summary>
    public double Rms()
    {
        if (Samples.Length == 0) return 0;
        double sum = 0;
        foreach (short s in Samples) sum += (double)s * s;
        return Math.Sqrt(sum / Samples.Length) / 32768.0;
    }

    /// <summary>Peak level, 0..1 of full scale.</summary>
    public double Peak()
    {
        int m = 0;
        foreach (short s in Samples) m = Math.Max(m, Math.Abs((int)s));
        return m / 32768.0;
    }

    /// <summary>
    /// (11b-1) Minimal RIFF/WAVE reader for the voice files: PCM (format 1 or WAVE_FORMAT_EXTENSIBLE), 16 bit, any rate and
    /// channel count. Returns null (never throws) for a missing, truncated or unsupported file.
    /// </summary>
    public static VoiceWav? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllBytes(path)) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static VoiceWav? Parse(ReadOnlySpan<byte> b)
    {
        if (b.Length < 12 || !Tag(b, 0, "RIFF") || !Tag(b, 8, "WAVE")) return null;
        int pos = 12, format = 0, channels = 0, rate = 0, bits = 0;
        bool fmt = false;
        while (pos + 8 <= b.Length)
        {
            int size = BitConverter.ToInt32(b.Slice(pos + 4, 4));
            int body = pos + 8;
            if (size < 0 || body + size > b.Length) size = b.Length - body; // truncated data chunk: take what is there
            if (Tag(b, pos, "fmt ") && size >= 16)
            {
                format = BitConverter.ToUInt16(b.Slice(body, 2));
                channels = BitConverter.ToUInt16(b.Slice(body + 2, 2));
                rate = BitConverter.ToInt32(b.Slice(body + 4, 4));
                bits = BitConverter.ToUInt16(b.Slice(body + 14, 2));
                fmt = true;
            }
            else if (Tag(b, pos, "data"))
            {
                if (!fmt || (format != 1 && format != 0xFFFE) || bits != 16 || channels < 1 || rate <= 0) return null;
                int n = size / 2 / channels * channels;
                var samples = new short[n];
                for (int i = 0; i < n; i++) samples[i] = BitConverter.ToInt16(b.Slice(body + 2 * i, 2));
                return new VoiceWav { Samples = samples, SampleRate = rate, Channels = channels };
            }
            pos = body + size + (size & 1);
        }
        return null;
    }

    private static bool Tag(ReadOnlySpan<byte> b, int at, string tag) =>
        b[at] == tag[0] && b[at + 1] == tag[1] && b[at + 2] == tag[2] && b[at + 3] == tag[3];
}
