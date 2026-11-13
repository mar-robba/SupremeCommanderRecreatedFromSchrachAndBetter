using Raylib_cs;

namespace Rts.Client.Audio;

/// <summary>
/// (11b-1) The voice announcer: plays the recorded lines of <c>data/audio/voice/&lt;language&gt;/&lt;id&gt;.wav</c> through
/// a <see cref="VoiceQueue"/> (one line at a time, priorities, cooldowns, stale lines dropped). Owned by
/// <see cref="AudioSystem"/>; game code calls <see cref="Say"/> (GameAudio.Voice.cs) and <see cref="Update"/> runs every frame.
/// <para>Loading: the line table is read at once (small json); the .wav files are decoded on a worker thread at startup
/// and each one is uploaded to raylib the first time it is spoken (a ~80 KB copy, no frame hitch). A missing,
/// unreadable or not-yet-decoded file = silence for that line (the alert beep stays). Without an audio device
/// (<c>device: false</c>: tests, --nosound) the queue still runs, nothing is played.</para>
/// <para>Volume: <see cref="AudioSettings.Voice"/> per line, under the engine's master volume (so Mute / --nosound also
/// silence it); <see cref="AudioSettings.VoiceOn"/> off drops what is queued and stops the current line.</para>
/// </summary>
public sealed unsafe class VoiceAnnouncer : IDisposable
{
    private readonly AudioSettings _settings;
    private readonly bool _device;
    private readonly string _dir;
    private Task<Dictionary<string, VoiceWav>>? _load;
    private Dictionary<string, VoiceWav> _wavs = new();
    private readonly Dictionary<string, Sound> _sounds = new();
    private Sound _current;
    private bool _hasCurrent;

    public readonly string Language;
    public readonly VoiceLineTable? Table;
    public readonly VoiceQueue Queue;

    /// <summary>Raised when a line starts (with its length in seconds; 0 = no file). --voice-test prints it; tests count.</summary>
    public event Action<VoiceLine, double>? LineStarted;

    /// <param name="device">An open raylib audio device: lines are played. False = queue logic only.</param>
    /// <param name="voiceDir">Folder with lines.json and the language folders; null = <c>data/audio/voice</c>.</param>
    /// <param name="loadNow">Decode every file now on the calling thread (tests, --voice-test) instead of a worker thread.</param>
    public VoiceAnnouncer(AudioSettings settings, bool device, string? voiceDir = null, string language = VoiceLineTable.DefaultLanguage,
        bool loadNow = false)
    {
        _settings = settings;
        _device = device;
        Language = language;
        try { _dir = voiceDir ?? VoiceLineTable.DefaultDir(); }
        catch (DirectoryNotFoundException) { _dir = ""; }
        Table = _dir.Length > 0 ? VoiceLineTable.TryLoad(_dir) : null;
        Queue = new VoiceQueue(id => Table?[id]);
        if (Table == null) return;
        if (loadNow) _wavs = Decode(Table, _dir, language);
        else _load = Task.Run(() => Decode(Table, _dir, language));
    }

    /// <summary>The decoded files (id → wave); missing or broken files are absent.</summary>
    public static Dictionary<string, VoiceWav> Decode(VoiceLineTable table, string dir, string language)
    {
        var d = new Dictionary<string, VoiceWav>();
        foreach (var line in table.Lines)
            if (VoiceWav.TryRead(VoiceLineTable.FilePath(dir, language, line.Id)) is { } w && w.Frames > 0) d[line.Id] = w;
        return d;
    }

    /// <summary>Number of decoded files (0 while the worker thread is still running).</summary>
    public int LoadedCount => _wavs.Count;

    /// <summary>Asks for a line (ignored when the voice is off or the id unknown; cooldowns apply). True = queued.</summary>
    public bool Say(string id, double now) => _settings.VoiceOn && _settings.Enabled && Queue.Enqueue(id, now);

    /// <summary>Once per frame (main thread): finishes loading, starts the next line, applies the voice volume.</summary>
    public void Update(double now)
    {
        if (_load is { IsCompleted: true } task)
        {
            if (task.IsCompletedSuccessfully) _wavs = task.Result;
            else Console.Error.WriteLine("Voice files not loaded: " + task.Exception?.GetBaseException().Message);
            _load = null;
        }
        if (!_settings.VoiceOn)
        {
            Queue.Clear();
            StopCurrent();
            return;
        }
        if (_device && _hasCurrent) Raylib.SetSoundVolume(_current, Math.Clamp(_settings.Voice, 0f, 1f));
        var line = Queue.Next(now);
        if (line == null) return;
        double duration = _wavs.TryGetValue(line.Id, out var wav) ? wav.Duration : 0;
        if (duration > 0 && _device && !Play(line.Id, wav!)) duration = 0;
        Queue.Started(line, now, duration);
        LineStarted?.Invoke(line, duration);
    }

    private bool Play(string id, VoiceWav wav)
    {
        if (!_sounds.TryGetValue(id, out var s))
        {
            fixed (short* p = wav.Samples)
            {
                var w = new Wave { FrameCount = (uint)wav.Frames, SampleRate = (uint)wav.SampleRate, SampleSize = 16, Channels = (uint)wav.Channels, Data = p };
                s = Raylib.LoadSoundFromWave(w);
            }
            if (s.FrameCount == 0) return false;
            _sounds[id] = s;
        }
        StopCurrent();
        Raylib.SetSoundVolume(s, Math.Clamp(_settings.Voice, 0f, 1f));
        Raylib.PlaySound(s);
        _current = s;
        _hasCurrent = true;
        return true;
    }

    private void StopCurrent()
    {
        if (_device && _hasCurrent) Raylib.StopSound(_current);
        _hasCurrent = false;
    }

    /// <summary>Game left: forget what is waiting (cooldowns stay).</summary>
    public void Reset()
    {
        Queue.Clear();
        StopCurrent();
    }

    /// <summary>Unloads the uploaded sounds; call before the audio device closes.</summary>
    public void Dispose()
    {
        try { _load?.Wait(2000); } catch { /* load errors are irrelevant at shutdown */ }
        StopCurrent();
        if (_device) foreach (var s in _sounds.Values) Raylib.UnloadSound(s);
        _sounds.Clear();
    }
}
