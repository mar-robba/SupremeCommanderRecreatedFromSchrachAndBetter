namespace Rts.Client.Audio;

/// <summary>
/// App-wide audio: the device (<see cref="AudioEngine"/>), the synthesized sound bank (generated once on a worker
/// thread at startup) and the real-time ambient/music stream (<see cref="AmbientSynth"/>). Created once by
/// <see cref="GameApp"/> and shared by every game: each <see cref="GameSession"/>'s <see cref="GameAudio"/> drives it
/// in game; menus call <see cref="MenuUpdate"/> (UI clicks + music). Volumes live in <see cref="Settings"/>
/// (saved by the settings screen). Silent no-op with --nosound, in screenshot runs (unless --sound) or without
/// an audio device. Main thread only.
/// </summary>
public sealed class AudioSystem : IDisposable
{
    public readonly AudioSettings Settings;
    public readonly AudioEngine Engine = new();
    public readonly AmbientSynth Ambient = new(7);
    private Task<float[][][]>? _bank;
    private double _synthMs;

    /// <summary>Music fade-in after startup (0..1).</summary>
    public float MusicFade { get; private set; }

    /// <summary>True when the audio device is open (sounds may still be loading for a moment).</summary>
    public bool Active => Engine.Ready;

    /// <summary>(11b-1) The spoken announcer (inert without a device).</summary>
    public VoiceAnnouncer Voice { get; private set; }

    public AudioSystem(AudioSettings settings, bool automated = false)
    {
        Settings = settings;
        Voice = new VoiceAnnouncer(settings, device: false, voiceDir: ""); // no table, no files: silent until the device opens
        if (!settings.Enabled || (automated && !settings.ForceOn)) return;
        if (!Engine.Init()) return;
        Voice = new VoiceAnnouncer(settings, device: true);
        _bank = Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var all = SoundBank.GenerateAll();
            _synthMs = sw.Elapsed.TotalMilliseconds;
            return all;
        });
        Engine.OpenStream();
    }

    /// <summary>Once per frame (game or menu): uploads the sound bank when ready, applies the master volume.</summary>
    public void Update(float dt)
    {
        if (!Engine.Ready) return;
        if (_bank is { IsCompleted: true } task)
        {
            if (task.IsCompletedSuccessfully)
            {
                Engine.LoadBank(task.Result);
                Console.WriteLine($"Audio: {task.Result.Sum(v => v.Length)} sounds synthesized in {_synthMs:F0} ms");
            }
            else Console.Error.WriteLine("Sound synthesis failed: " + task.Exception?.GetBaseException().Message);
            _bank = null;
        }
        Engine.SetMasterVolume(Settings.Muted ? 0f : Settings.Master);
        Voice.Update(Raylib_cs.Raylib.GetTime());
        MusicFade = MathF.Min(1f, MusicFade + dt / 2.5f);
    }

    /// <summary>Plays a non-positional (interface) sound now.</summary>
    public void PlayUi(SoundId id) => Engine.Play(id, SoundBank.Info(id).Gain);

    /// <summary>Menu frame: UI clicks and music only (no wind, no construction hums).</summary>
    public void MenuUpdate(float dt)
    {
        var ui = UiSounds.Drain();
        Update(dt);
        if (!Engine.Ready) return;
        if (ui.click > 0) PlayUi(SoundId.UiClick);
        if (ui.confirm > 0) PlayUi(SoundId.UiConfirm);
        if (ui.error > 0) PlayUi(SoundId.UiError);
        float k = MathF.Min(1f, dt * 2f);
        Ambient.WindGain += (0f - Ambient.WindGain) * k;
        Ambient.ReclaimGain = 0;
        Ambient.BuildGain = 0;
        Ambient.Tension += (0f - Ambient.Tension) * k;
        Ambient.MusicGain = Settings.MusicOn ? Settings.Music * MusicFade : 0f;
        Engine.PumpStream(Ambient);
    }

    public void Dispose()
    {
        try { _bank?.Wait(2000); } catch { /* synthesis errors are irrelevant at shutdown */ }
        Voice.Dispose(); // before the device closes
        Engine.Dispose();
    }
}
