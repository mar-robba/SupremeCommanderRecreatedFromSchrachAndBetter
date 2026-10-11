using System.Runtime.InteropServices;
using Raylib_cs;

namespace Rts.Client.Audio;

/// <summary>
/// Thin raylib audio layer: opens the device (never throws: <see cref="Ready"/> is false without a device),
/// turns synthesized PCM into Sounds, and plays them through per-sound voice pools (sound aliases) with
/// volume / pan / pitch per play, voice stealing and a real-time stereo stream for <see cref="AmbientSynth"/>.
/// All calls are no-ops when not ready. Main thread only.
/// </summary>
public sealed unsafe class AudioEngine : IDisposable
{
    private sealed class Voice
    {
        public Sound Sound;
        public bool Alias;
        public double Started;
    }

    public bool Ready { get; private set; }
    public string Status { get; private set; } = "not initialized";
    private readonly List<Voice>[] _voices = new List<Voice>[SoundBank.Count];
    private readonly Random _rand = new();
    private AudioStream _stream;
    private bool _streamOk;
    private float[] _streamBuf = [];
    private int _streamFrames;
    private bool _ownsDevice;

    public bool SoundsLoaded { get; private set; }

    /// <summary>Opens the audio device. Returns false (and stays silent) if there is none.</summary>
    public bool Init()
    {
        try
        {
            if (!Raylib.IsAudioDeviceReady())
            {
                Raylib.InitAudioDevice();
                _ownsDevice = true;
            }
            Ready = Raylib.IsAudioDeviceReady();
            Status = Ready ? "ok" : "no audio device";
        }
        catch (Exception e)
        {
            Ready = false;
            Status = "audio init failed: " + e.Message;
        }
        if (!Ready) Console.Error.WriteLine("Audio disabled: " + Status);
        return Ready;
    }

    /// <summary>Uploads the synthesized sounds (from <see cref="SoundBank.GenerateAll"/>) into voice pools.</summary>
    public void LoadBank(float[][][] bank)
    {
        if (!Ready || SoundsLoaded) return;
        for (int id = 0; id < bank.Length && id < _voices.Length; id++)
        {
            var list = new List<Voice>();
            var variants = new List<Sound>();
            foreach (var pcm in bank[id])
            {
                var s = Upload(pcm);
                if (Raylib.IsSoundValid(s)) variants.Add(s);
            }
            if (variants.Count == 0) { _voices[id] = list; continue; }
            int voices = Math.Max(variants.Count, SoundBank.Info((SoundId)id).Voices);
            for (int v = 0; v < voices; v++)
            {
                var src = variants[v % variants.Count];
                if (v < variants.Count) list.Add(new Voice { Sound = src });
                else
                {
                    var alias = Raylib.LoadSoundAlias(src);
                    if (Raylib.IsSoundValid(alias)) list.Add(new Voice { Sound = alias, Alias = true });
                }
            }
            _voices[id] = list;
        }
        SoundsLoaded = true;
    }

    private static Sound Upload(float[] pcm)
    {
        var data = Synth.ToPcm16(pcm);
        fixed (short* p = data)
        {
            var wave = new Wave { FrameCount = (uint)data.Length, SampleRate = Synth.SampleRate, SampleSize = 16, Channels = 1, Data = p };
            return Raylib.LoadSoundFromWave(wave);
        }
    }

    /// <summary>
    /// Plays a sound on a free voice (random variant); if all voices are busy, steals the oldest one that has
    /// played at least <paramref name="minStealAge"/> seconds. Returns false if nothing was played.
    /// </summary>
    public bool Play(SoundId id, float volume, float pan = 0, float pitch = 1, double minStealAge = 0.05)
    {
        if (!SoundsLoaded || volume <= 0.001f) return false;
        var list = _voices[(int)id];
        if (list == null || list.Count == 0) return false;
        double now = Raylib.GetTime();
        Voice? pick = null;
        int start = _rand.Next(list.Count);
        for (int k = 0; k < list.Count; k++)
        {
            var v = list[(start + k) % list.Count];
            if (!Raylib.IsSoundPlaying(v.Sound)) { pick = v; break; }
        }
        if (pick == null)
        {
            foreach (var v in list)
                if (pick == null || v.Started < pick.Started) pick = v;
            if (pick == null || now - pick.Started < minStealAge) return false;
            Raylib.StopSound(pick.Sound);
        }
        Raylib.SetSoundVolume(pick.Sound, Math.Clamp(volume, 0f, 1f));
        Raylib.SetSoundPan(pick.Sound, Math.Clamp(pan, -1f, 1f));
        Raylib.SetSoundPitch(pick.Sound, Math.Clamp(pitch, 0.25f, 4f));
        Raylib.PlaySound(pick.Sound);
        pick.Started = now;
        return true;
    }

    /// <summary>Number of voices of a sound currently playing.</summary>
    public int Playing(SoundId id)
    {
        var list = _voices[(int)id];
        if (!SoundsLoaded || list == null) return 0;
        int n = 0;
        foreach (var v in list) if (Raylib.IsSoundPlaying(v.Sound)) n++;
        return n;
    }

    public void SetMasterVolume(float v)
    {
        if (Ready) Raylib.SetMasterVolume(Math.Clamp(v, 0f, 1f));
    }

    // ------------------------------------------------------------------ real-time stream

    /// <summary>Opens a stereo float stream fed by <see cref="PumpStream"/> (sub-buffers of <paramref name="frames"/>).</summary>
    public void OpenStream(int frames = 4096)
    {
        if (!Ready || _streamOk) return;
        try
        {
            Raylib.SetAudioStreamBufferSizeDefault(frames);
            _stream = Raylib.LoadAudioStream((uint)Synth.SampleRate, 32, 2);
            Raylib.SetAudioStreamBufferSizeDefault(0);
            _streamOk = Raylib.IsAudioStreamValid(_stream);
            _streamFrames = frames;
            _streamBuf = new float[frames * 2];
            if (_streamOk) Raylib.PlayAudioStream(_stream);
        }
        catch (Exception e)
        {
            _streamOk = false;
            Console.Error.WriteLine("Audio stream disabled: " + e.Message);
        }
    }

    /// <summary>Refills the stream's processed sub-buffers by calling the generator (main thread, once per frame).</summary>
    public void PumpStream(AmbientSynth synth)
    {
        if (!_streamOk) return;
        for (int guard = 0; guard < 2 && Raylib.IsAudioStreamProcessed(_stream); guard++)
        {
            synth.Render(_streamBuf, _streamFrames);
            fixed (float* p = _streamBuf) Raylib.UpdateAudioStream(_stream, p, _streamFrames);
        }
        if (!Raylib.IsAudioStreamPlaying(_stream)) Raylib.PlayAudioStream(_stream);
    }

    public void Dispose()
    {
        if (!Ready) return;
        if (_streamOk)
        {
            Raylib.StopAudioStream(_stream);
            Raylib.UnloadAudioStream(_stream);
            _streamOk = false;
        }
        foreach (var list in _voices)
        {
            if (list == null) continue;
            foreach (var v in list) if (v.Alias) { Raylib.StopSound(v.Sound); Raylib.UnloadSoundAlias(v.Sound); }
            foreach (var v in list) if (!v.Alias) { Raylib.StopSound(v.Sound); Raylib.UnloadSound(v.Sound); }
            list.Clear();
        }
        SoundsLoaded = false;
        if (_ownsDevice) Raylib.CloseAudioDevice();
        Ready = false;
    }
}
