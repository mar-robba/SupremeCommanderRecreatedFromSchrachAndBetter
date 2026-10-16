using System.Text.Json;
using Raylib_cs;
using Rts.Client.Audio;

namespace Rts.Client.Menus;

/// <summary>
/// User settings (persisted as JSON in the user's config folder): volumes, fullscreen, window resolution and the
/// last skirmish setup. Volumes are copied into the live <see cref="AudioSettings"/> with <see cref="ApplyVolume"/>
/// (the app-wide <see cref="AudioSystem"/> applies them every frame).
/// </summary>
public sealed class AppSettings
{
    public static readonly (int w, int h)[] Resolutions = [(1280, 720), (1600, 900), (1920, 1080), (2560, 1440)];

    public float MasterVolume { get; set; } = 0.8f;
    public float SfxVolume { get; set; } = 1f;
    public float MusicVolume { get; set; } = 0.45f;
    public bool Fullscreen { get; set; }
    public int Width { get; set; } = 1600;
    public int Height { get; set; } = 900;
    // Last skirmish setup.
    public int MapSize { get; set; } = 256;
    public string Preset { get; set; } = "Plains";
    public float WaterAmount { get; set; } = -1;
    // Multiplayer (Menus/MultiplayerScreen.cs): player name ("" = the OS user name), last address joined, host port.
    public string PlayerName { get; set; } = "";
    public string JoinAddress { get; set; } = "";
    public int HostPort { get; set; } = 47624;

    /// <summary>The settings in use (loaded at startup).</summary>
    public static AppSettings Current { get; private set; } = new();

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RtsGame", "settings.json");

    /// <summary>Automated runs (screenshots) never read or write the user's file.</summary>
    public static bool Persist = true;

    public static AppSettings Load()
    {
        if (Persist)
        {
            try
            {
                if (File.Exists(FilePath)) Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Settings not loaded: " + e.Message);
                Current = new AppSettings();
            }
        }
        return Current;
    }

    public void Save()
    {
        if (!Persist) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Settings not saved: " + e.Message);
        }
    }

    /// <summary>Copies the saved volumes into the live audio settings.</summary>
    public void ApplyVolume(AudioSettings audio)
    {
        audio.Master = Math.Clamp(MasterVolume, 0f, 1f);
        audio.Sfx = Math.Clamp(SfxVolume, 0f, 1f);
        audio.Music = Math.Clamp(MusicVolume, 0f, 1f);
    }

    /// <summary>Applies fullscreen (borderless) on/off.</summary>
    public void ApplyFullscreen()
    {
        bool isFs = Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode);
        if (isFs != Fullscreen) Raylib.ToggleBorderlessWindowed();
    }

    /// <summary>Applies the window resolution (ignored while fullscreen).</summary>
    public void ApplyResolution()
    {
        if (Fullscreen) return;
        Raylib.SetWindowSize(Width, Height);
        int m = Raylib.GetCurrentMonitor();
        int mw = Raylib.GetMonitorWidth(m), mh = Raylib.GetMonitorHeight(m);
        if (mw > 0 && mh > 0) Raylib.SetWindowPosition(Math.Max(0, (mw - Width) / 2), Math.Max(0, (mh - Height) / 2));
    }
}
