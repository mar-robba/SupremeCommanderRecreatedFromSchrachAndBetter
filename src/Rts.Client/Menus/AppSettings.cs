using System.Text.Json;
using Raylib_cs;
using Rts.Client.Audio;

namespace Rts.Client.Menus;

/// <summary>
/// User settings (persisted as JSON in the user's config folder): volumes, fullscreen, window resolution, the
/// last skirmish setup, graphics and gameplay options (applied by <see cref="GraphicsSettings"/>). Volumes are copied into the live <see cref="AudioSettings"/> with <see cref="ApplyVolume"/>
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
    // Graphics (wave 7a, applied by GraphicsSettings.Apply; defaults = the behavior before these settings existed).
    public bool Shadows { get; set; } = true;
    /// <summary>0 Low, 1 Medium, 2 High (= <see cref="GraphicsSettings.ParticleScales"/> index).</summary>
    public int ParticleQuality { get; set; } = 2;
    /// <summary>-1 = automatic terrain LOD, 0..3 = force that LOD (0 = full detail).</summary>
    public int TerrainDetail { get; set; } = -1;
    public bool VSync { get; set; } = true;
    /// <summary>Frame-rate cap, 0 = off (one of <see cref="GraphicsSettings.FpsCaps"/>).</summary>
    public int FpsCap { get; set; }
    /// <summary>Multiplier on the automatic UI scale (one of <see cref="GraphicsSettings.UiScales"/>).</summary>
    public float UiScale { get; set; } = 1f;
    // Gameplay.
    public bool EdgeScroll { get; set; } = true;
    /// <summary>Camera keyboard/edge pan speed multiplier (0.5..2).</summary>
    public float PanSpeed { get; set; } = 1f;
    /// <summary>Mouse-wheel zoom step multiplier (0.5..2).</summary>
    public float ZoomSpeed { get; set; } = 1f;
    /// <summary>Autosave every 5 minutes of game time into 3 rolling slots (the autosave itself: Persistence).</summary>
    public bool Autosave { get; set; } = true;
    /// <summary>(9b-2) Terrain self-shadows (hills shade the valleys and the units in them), TerrainRenderer.Shadow.cs.</summary>
    public bool TerrainShadows { get; set; } = true;
    /// <summary>(9b-2) Low-poly meshes for far units (UnitRenderer.Lod.cs).</summary>
    public bool UnitLod { get; set; } = true;
    /// <summary>(9b-2) Minimap size multiplier, one of <see cref="UI.MinimapView.Sizes"/>.</summary>
    public float MinimapSize { get; set; } = 1f;
    /// <summary>(9b-2) Minimap zoom (1 = whole map .. <see cref="UI.MinimapView.MaxZoom"/>), centred on the camera.</summary>
    public float MinimapZoom { get; set; } = 1f;
    /// <summary>(9b-2) Minimap layers: mass/hydrocarbon deposits, unit dots, trees.</summary>
    public bool MinimapResources { get; set; } = true;
    public bool MinimapUnits { get; set; } = true;
    public bool MinimapTrees { get; set; }

    /// <summary>The settings in use (loaded at startup).</summary>
    public static AppSettings Current { get; private set; } = new();

    /// <summary><c>$XDG_CONFIG_HOME/RtsGame/settings.json</c> (default ~/.config; Windows %APPDATA%), see <see cref="Rts.Sim.Persistence.UserDirs"/>.</summary>
    public static string FilePath => Path.Combine(Rts.Sim.Persistence.UserDirs.Current.ConfigDir, "settings.json");

    /// <summary>Automated runs (screenshots) never read or write the user's file.</summary>
    public static bool Persist = true;

    public static AppSettings Load()
    {
        if (Persist) Current = LoadFrom(FilePath);
        return Current;
    }

    /// <summary>Reads a settings file (missing file or fields → defaults, out-of-range values clamped); never throws.</summary>
    public static AppSettings LoadFrom(string path)
    {
        try
        {
            if (File.Exists(path)) return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings()).Normalize();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Settings not loaded: " + e.Message);
        }
        return new AppSettings();
    }

    public void Save()
    {
        if (Persist) SaveTo(FilePath);
    }

    public void SaveTo(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Settings not saved: " + e.Message);
        }
    }

    /// <summary>Clamps every value into its valid range (hand-edited or corrupt files); returns this.</summary>
    public AppSettings Normalize()
    {
        var d = new AppSettings();
        static float Clamp(float v, float lo, float hi, float def) => float.IsFinite(v) ? Math.Clamp(v, lo, hi) : def;
        MasterVolume = Clamp(MasterVolume, 0f, 1f, d.MasterVolume);
        SfxVolume = Clamp(SfxVolume, 0f, 1f, d.SfxVolume);
        MusicVolume = Clamp(MusicVolume, 0f, 1f, d.MusicVolume);
        ParticleQuality = Math.Clamp(ParticleQuality, 0, GraphicsSettings.ParticleScales.Length - 1);
        TerrainDetail = Math.Clamp(TerrainDetail, -1, 3);
        FpsCap = Nearest(GraphicsSettings.FpsCaps, FpsCap <= 0 ? 0 : FpsCap);
        UiScale = GraphicsSettings.UiScales[NearestIndex(GraphicsSettings.UiScales, Clamp(UiScale, 0f, 10f, d.UiScale))];
        PanSpeed = MathF.Round(Clamp(PanSpeed, 0.5f, 2f, d.PanSpeed) * 10f) / 10f;
        ZoomSpeed = MathF.Round(Clamp(ZoomSpeed, 0.5f, 2f, d.ZoomSpeed) * 10f) / 10f;
        MinimapSize = UI.MinimapView.Sizes[NearestIndex(UI.MinimapView.Sizes, Clamp(MinimapSize, 0f, 10f, d.MinimapSize))];
        MinimapZoom = UI.MinimapView.ClampZoom(Clamp(MinimapZoom, 1f, UI.MinimapView.MaxZoom, d.MinimapZoom));
        return this;
    }

    private static int Nearest(int[] values, int v)
    {
        int best = values[0];
        foreach (int x in values) if (Math.Abs(x - v) < Math.Abs(best - v)) best = x;
        return best;
    }

    /// <summary>Index of the value closest to <paramref name="v"/>.</summary>
    public static int NearestIndex(float[] values, float v)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++) if (MathF.Abs(values[i] - v) < MathF.Abs(values[best] - v)) best = i;
        return best;
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
