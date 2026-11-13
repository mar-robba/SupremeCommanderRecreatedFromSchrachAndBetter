using System.Diagnostics;
using Raylib_cs;

namespace Rts.Client.Audio;

/// <summary>
/// (11b-1) <c>--voice-test [--voice-only id,id] [--voice-lang it] [--volume F] [--nosound]</c>: plays every voice line in table
/// order, printing its id, text and length, then exits (no window). For the owner's ear after regenerating the files
/// (tools/voice/generate.py). Exit code 1 = a line has no readable file. --nosound only prints.
/// </summary>
public static class VoiceTest
{
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (!args.Contains("--voice-test")) return false;
        string? Opt(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var only = Opt("--voice-only")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string lang = Opt("--voice-lang") ?? VoiceLineTable.DefaultLanguage;
        var settings = new AudioSettings { Voice = 1f };
        if (Opt("--volume") is { } v) settings.Master = Math.Clamp(float.Parse(v, System.Globalization.CultureInfo.InvariantCulture), 0f, 1f);
        bool device = !args.Contains("--nosound");
        if (device)
        {
            Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
            Raylib.InitAudioDevice();
            device = Raylib.IsAudioDeviceReady();
            if (!device) Console.WriteLine("No audio device: printing only.");
            else Raylib.SetMasterVolume(settings.Master);
        }
        try
        {
            using var voice = new VoiceAnnouncer(settings, device, language: lang, loadNow: true);
            if (voice.Table == null) { Console.WriteLine("No line table (data/audio/voice/lines.json)."); exitCode = 1; return true; }
            voice.Queue.Gap = 0.6;
            int missing = 0;
            voice.LineStarted += (line, dur) =>
            {
                string text = line.Text.GetValueOrDefault(lang, "?");
                Console.WriteLine(dur > 0 ? $"{line.Id,-28} {dur,4:F2} s  {text}" : $"{line.Id,-28} MISSING  {text}");
                if (dur <= 0) missing++;
            };
            var clock = Stopwatch.StartNew();
            double fake = 0; // --nosound: a simulated clock, so printing does not wait
            double Now() => device ? clock.Elapsed.TotalSeconds : fake;
            foreach (var line in voice.Table.Lines)
            {
                if (only != null && !only.Contains(line.Id)) continue;
                voice.Queue.Enqueue(line.Id, Now());
                while (voice.Queue.WaitingLines.Count > 0 || voice.Queue.Busy(Now()))
                {
                    voice.Update(Now());
                    if (device) Thread.Sleep(15);
                    else fake += 0.05;
                }
            }
            Console.WriteLine($"{voice.LoadedCount} file(s) loaded, {missing} missing ({lang}).");
            exitCode = missing > 0 ? 1 : 0;
        }
        finally
        {
            if (device) Raylib.CloseAudioDevice();
        }
        return true;
    }
}
