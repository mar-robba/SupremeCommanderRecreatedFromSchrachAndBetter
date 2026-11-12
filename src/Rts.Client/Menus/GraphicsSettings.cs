using Raylib_cs;
using Rts.Client.Rendering;
using Rts.Client.UI;

namespace Rts.Client.Menus;

/// <summary>
/// Applies the graphics and gameplay options of <see cref="AppSettings"/> (wave 7a) to the static switches read by the
/// renderers, the UI and the camera: <see cref="ShadowMap.Enabled"/>, <see cref="ParticleSystem.Quality"/>,
/// <see cref="TerrainRenderer.ForceLod"/>, <see cref="Ui.UserScale"/>, <see cref="CameraSettings"/>, plus VSync and the FPS cap on the
/// window. Called once after InitWindow and again whenever the settings screen changes something (everything is live).
/// Command-line switches win for that run: <c>--novsync</c> keeps VSync off, <c>--terrain-lod N</c> keeps its LOD.
/// The defaults reproduce the behavior before these settings existed (screenshot runs use defaults).
/// </summary>
public static class GraphicsSettings
{
    public static readonly string[] ParticleNames = ["Low", "Medium", "High"];
    public static readonly float[] ParticleScales = [0.35f, 0.65f, 1f];
    public static readonly int[] FpsCaps = [0, 30, 60, 120, 144];
    public static readonly float[] UiScales = [0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f];
    /// <summary>Terrain detail labels for <see cref="AppSettings.TerrainDetail"/> -1 (Auto) .. 3.</summary>
    public static readonly string[] TerrainNames = ["Auto", "Max", "High", "Medium", "Low"];

    private static bool _captured;
    private static int _cliLod = -1;
    private static bool _cliNoVsync;
    // raylib starts uncapped (target 0): SetTargetFPS is only called once the user picks another cap.
    private static int _appliedFps;

    /// <summary>True when <c>--terrain-lod</c> was given: the saved terrain detail is ignored this run.</summary>
    public static bool TerrainLodFromCli => _cliLod >= 0;
    /// <summary>True when <c>--novsync</c> was given: VSync stays off this run.</summary>
    public static bool VsyncFromCli => _cliNoVsync;

    /// <summary>Startup: remembers the command-line overrides, then applies everything.</summary>
    /// <summary>(9b-2) CLI <c>--minimap SPEC</c> (<see cref="MinimapView.ApplySpec"/>), applied to the settings at startup.</summary>
    public static string? MinimapCli;

    public static void Init(AppSettings s, ClientOptions opt)
    {
        if (MinimapCli != null) MinimapView.ApplySpec(s, MinimapCli);
        _cliLod = TerrainRenderer.ForceLod;
        _cliNoVsync = opt.NoVsync;
        _captured = true;
        Apply(s);
    }

    public static void Apply(AppSettings s)
    {
        ShadowMap.Enabled = s.Shadows;
        ParticleSystem.Quality = ParticleScales[Math.Clamp(s.ParticleQuality, 0, ParticleScales.Length - 1)];
        if (!TerrainLodFromCli) TerrainRenderer.ForceLod = Math.Clamp(s.TerrainDetail, -1, 3);
        Ui.UserScale = Math.Clamp(s.UiScale, UiScales[0], UiScales[^1]);
        CameraSettings.EdgeScroll = s.EdgeScroll;
        CameraSettings.PanSpeed = Math.Clamp(s.PanSpeed, 0.5f, 2f);
        CameraSettings.ZoomSpeed = Math.Clamp(s.ZoomSpeed, 0.5f, 2f);
        TerrainRenderer.SelfShadows = s.TerrainShadows; // (9b-2) CLI --terrain-shadows / --unit-lod win (…Cli fields)
        UnitRenderer.LodEnabled = s.UnitLod;
        SelectionRangeOverlay.Enabled = s.RangeRings; // (10b-2)
        if (!_captured || !Raylib.IsWindowReady()) return;

        bool vsync = s.VSync && !_cliNoVsync;
        if (vsync != Raylib.IsWindowState(ConfigFlags.VSyncHint))
        {
            if (vsync) Raylib.SetWindowState(ConfigFlags.VSyncHint);
            else Raylib.ClearWindowState(ConfigFlags.VSyncHint);
        }
        int fps = Math.Max(0, s.FpsCap);
        if (fps != _appliedFps)
        {
            Raylib.SetTargetFPS(fps);
            _appliedFps = fps;
        }
    }
}
