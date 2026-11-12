using System.Reflection;
using Rts.Client.Menus;
using Rts.Client.Rendering;
using Rts.Client.UI;
using Xunit;

namespace Rts.Client.Tests;

/// <summary>settings.json: round trip of every field, old files without the new fields, clamping, and that the
/// defaults applied by <see cref="GraphicsSettings"/> equal the behavior before the settings existed. Uses temp files
/// only (never the user's settings).</summary>
public class AppSettingsTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "rts_settings_" + Guid.NewGuid().ToString("N"), "settings.json");

    private static void Cleanup(string path)
    {
        string dir = Path.GetDirectoryName(path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static IEnumerable<PropertyInfo> Fields() =>
        typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite);

    /// <summary>Valid, non-default values for every field (a new field without an entry here fails the test).</summary>
    private static AppSettings NonDefault() => new()
    {
        MasterVolume = 0.3f, SfxVolume = 0.6f, MusicVolume = 0.1f, Fullscreen = true, Width = 1920, Height = 1080,
        MapSize = 512, Preset = "Lakes", WaterAmount = 0.4f, PlayerName = "Tester", JoinAddress = "10.0.0.2:5000", HostPort = 50000,
        Shadows = false, ParticleQuality = 0, TerrainDetail = 2, VSync = false, FpsCap = 144, UiScale = 1.25f,
        EdgeScroll = false, PanSpeed = 1.7f, ZoomSpeed = 0.6f, Autosave = false,
        TerrainShadows = false, UnitLod = false, MinimapSize = 1.5f, MinimapZoom = 3f, MinimapResources = false, MinimapUnits = false, MinimapTrees = true, // (9b-2)
        KeyBindings = "v1;Stop=X", // (10b-1)
    };

    [Fact]
    public void RoundTripKeepsEveryField()
    {
        var a = NonDefault();
        var d = new AppSettings();
        foreach (var p in Fields())
            Assert.False(Equals(p.GetValue(a), p.GetValue(d)), $"test data must give {p.Name} a non-default value");
        string path = TempFile();
        try
        {
            a.SaveTo(path);
            var b = AppSettings.LoadFrom(path);
            foreach (var p in Fields()) Assert.Equal(p.GetValue(a), p.GetValue(b));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void OldFileWithoutNewFieldsLoadsDefaults()
    {
        string path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                { "MasterVolume": 0.5, "SfxVolume": 1, "MusicVolume": 0.45, "Fullscreen": false, "Width": 1280, "Height": 720,
                  "MapSize": 256, "Preset": "Plains", "WaterAmount": -1, "PlayerName": "", "JoinAddress": "", "HostPort": 47624 }
                """);
            var s = AppSettings.LoadFrom(path);
            Assert.Equal(0.5f, s.MasterVolume);
            Assert.Equal(1280, s.Width);
            var d = new AppSettings();
            Assert.True(s.Shadows);
            Assert.Equal(2, s.ParticleQuality);
            Assert.Equal(-1, s.TerrainDetail);
            Assert.True(s.VSync);
            Assert.Equal(0, s.FpsCap);
            Assert.Equal(1f, s.UiScale);
            Assert.True(s.EdgeScroll);
            Assert.Equal(1f, s.PanSpeed);
            Assert.Equal(1f, s.ZoomSpeed);
            Assert.True(s.Autosave);
            Assert.Equal((d.Shadows, d.ParticleQuality, d.TerrainDetail, d.VSync, d.FpsCap, d.UiScale, d.EdgeScroll, d.PanSpeed, d.ZoomSpeed, d.Autosave),
                (s.Shadows, s.ParticleQuality, s.TerrainDetail, s.VSync, s.FpsCap, s.UiScale, s.EdgeScroll, s.PanSpeed, s.ZoomSpeed, s.Autosave));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        string path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                { "MasterVolume": 4, "SfxVolume": -1, "ParticleQuality": 9, "TerrainDetail": -5, "FpsCap": 75,
                  "UiScale": 3, "PanSpeed": 0.1, "ZoomSpeed": 7 }
                """);
            var s = AppSettings.LoadFrom(path);
            Assert.Equal(1f, s.MasterVolume);
            Assert.Equal(0f, s.SfxVolume);
            Assert.Equal(2, s.ParticleQuality);
            Assert.Equal(-1, s.TerrainDetail);
            Assert.Equal(60, s.FpsCap);       // nearest allowed cap
            Assert.Equal(1.5f, s.UiScale);
            Assert.Equal(0.5f, s.PanSpeed);
            Assert.Equal(2f, s.ZoomSpeed);

            var n = new AppSettings { FpsCap = -3, TerrainDetail = 8, UiScale = 0.95f, PanSpeed = 1.234f, ZoomSpeed = float.NaN }.Normalize();
            Assert.Equal(0, n.FpsCap);
            Assert.Equal(3, n.TerrainDetail);
            Assert.Contains(n.UiScale, GraphicsSettings.UiScales);
            Assert.Equal(1.2f, n.PanSpeed);
            Assert.Equal(1f, n.ZoomSpeed);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void MissingOrCorruptFileGivesDefaults()
    {
        string path = TempFile();
        try
        {
            Assert.Equal(new AppSettings().PanSpeed, AppSettings.LoadFrom(path).PanSpeed);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ not json");
            var s = AppSettings.LoadFrom(path);
            Assert.True(s.Shadows);
            Assert.Equal(0.8f, s.MasterVolume);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void ApplySetsTheSwitchesAndDefaultsMatchThePreviousBehavior()
    {
        try
        {
            GraphicsSettings.Apply(NonDefault());
            Assert.False(ShadowMap.Enabled);
            Assert.Equal(0.35f, ParticleSystem.Quality);
            Assert.Equal(2, TerrainRenderer.ForceLod);
            Assert.Equal(1.25f, Ui.UserScale);
            Assert.False(CameraSettings.EdgeScroll);
            Assert.Equal(1.7f, CameraSettings.PanSpeed);
            Assert.Equal(0.6f, CameraSettings.ZoomSpeed);

            GraphicsSettings.Apply(new AppSettings());
            Assert.True(ShadowMap.Enabled);
            Assert.Equal(1f, ParticleSystem.Quality);
            Assert.Equal(-1, TerrainRenderer.ForceLod);
            Assert.Equal(1f, Ui.UserScale);
            Assert.True(CameraSettings.EdgeScroll);
            Assert.Equal(1f, CameraSettings.PanSpeed);
            Assert.Equal(1f, CameraSettings.ZoomSpeed);
        }
        finally { GraphicsSettings.Apply(new AppSettings()); }
    }

    [Fact]
    public void SettingsTabArgument()
    {
        Assert.Equal(1, SettingsScreen.ParseTab("graphics"));
        Assert.Equal(2, SettingsScreen.ParseTab("Gameplay"));
        Assert.Equal(0, SettingsScreen.ParseTab("audio"));
        Assert.Throws<ArgumentException>(() => SettingsScreen.ParseTab("video"));
    }
}
