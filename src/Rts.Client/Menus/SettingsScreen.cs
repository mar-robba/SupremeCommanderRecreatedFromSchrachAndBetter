using Raylib_cs;
using Rts.Client.UI;

namespace Rts.Client.Menus;

/// <summary>Settings, three tabs: Audio &amp; Display (volumes applied live to the app-wide audio, fullscreen
/// (borderless), window resolution), Graphics (shadows, particles, terrain detail, VSync, FPS cap, UI scale) and
/// Gameplay (edge scroll, pan / zoom speed, autosave). Everything applies immediately (<see cref="GraphicsSettings"/>)
/// and is saved when the screen closes. Opened from the pause menu it owns the paused game (<c>returnTo</c>): Back
/// returns to it.</summary>
public sealed class SettingsScreen : IScreen
{
    public static readonly string[] TabNames = ["Audio & Display", "Graphics", "Gameplay"];
    /// <summary>Selected tab (kept for the session; <c>--settings-tab</c> sets it for screenshots).</summary>
    public static int Tab;

    private readonly AppHost _app;
    private readonly AppSettings _s = AppSettings.Current;
    private readonly IScreen? _returnTo;
    private bool _returned;
    private bool _dirty;

    public SettingsScreen(AppHost app, IScreen? returnTo = null)
    {
        _app = app;
        _returnTo = returnTo;
    }

    public static int ParseTab(string s) => s.ToLowerInvariant() switch
    {
        "audio" or "display" => 0,
        "graphics" => 1,
        "gameplay" => 2,
        _ => throw new ArgumentException("unknown settings tab " + s + " (audio, graphics, gameplay)"),
    };

    // Layout of the current frame.
    private float _lx, _cx, _cw, _rowH, _ry, _rowW;

    public void Frame(float dt)
    {
        _app.Backdrop.Draw(dt, 0.75f);
        MenuUi.BeginFrame();
        float panelW = Ui.P(620), panelH = Ui.P(300), tabsH = Ui.P(30);
        float x = Ui.P(80);
        // Title, tabs, panel, Back button; moved up when a large UI scale would push the button off a small window.
        float total = Ui.P(72) + tabsH + Ui.P(10) + panelH + Ui.P(20) + Ui.P(40);
        float y = MathF.Max(Ui.P(8), MathF.Min(Ui.Sh * 0.12f, Ui.Sh - total - Ui.P(8)));
        MenuUi.ScreenTitle("Settings", "Changes apply immediately and are saved", x, y);

        int tab = MenuUi.Segmented(Ui.R(x, y + Ui.P(72), Ui.P(440), tabsH), TabNames, Math.Clamp(Tab, 0, TabNames.Length - 1), true, 12.5f);
        Tab = tab;

        var panel = Ui.R(x, y + Ui.P(72) + tabsH + Ui.P(10), panelW, panelH);
        Ui.Panel(panel);
        _lx = panel.X + Ui.P(20);
        _cx = panel.X + Ui.P(210);
        _cw = panel.X + panel.Width - Ui.P(20) - _cx;
        _rowW = panel.Width - Ui.P(40);
        _rowH = Ui.P(30);
        _ry = panel.Y + Ui.P(20);

        switch (tab)
        {
            case 0: AudioDisplayTab(); break;
            case 1: GraphicsTab(); break;
            default: GameplayTab(); break;
        }

        string back = _returnTo != null ? "Back to Game" : "Back";
        if (MenuUi.BigButton(Ui.R(x, panel.Y + panel.Height + Ui.P(20), Ui.P(160), Ui.P(40)), back) || MenuUi.Key(KeyboardKey.Escape))
        {
            if (_returnTo != null)
            {
                _returned = true;
                _app.Go(_returnTo);
            }
            else _app.ToMainMenu();
        }
        Ui.DrawTooltip();
    }

    // ------------------------------------------------------------------ tabs

    private void AudioDisplayTab()
    {
        float master = VolumeRow(1, "Master volume", _s.MasterVolume);
        float sfx = VolumeRow(2, "Effects volume", _s.SfxVolume);
        float music = VolumeRow(3, "Music volume", _s.MusicVolume);
        if (master != _s.MasterVolume || sfx != _s.SfxVolume || music != _s.MusicVolume)
        {
            (_s.MasterVolume, _s.SfxVolume, _s.MusicVolume) = (master, sfx, music);
            _s.ApplyVolume(_app.Audio.Settings);
            _dirty = true;
        }

        Row("Fullscreen", "Borderless window covering the whole monitor.");
        bool fs = MenuUi.Toggle(Ui.R(_cx, _ry, Ui.P(90), _rowH), _s.Fullscreen);
        if (fs != _s.Fullscreen)
        {
            _s.Fullscreen = fs;
            _s.ApplyFullscreen();
            if (!fs) _s.ApplyResolution();
            _dirty = true;
        }
        NextRow();

        Row("Resolution", "Window size when fullscreen is off.");
        var res = AppSettings.Resolutions;
        int cur = Array.FindIndex(res, r => r.w == _s.Width && r.h == _s.Height);
        int sel = MenuUi.Segmented(Ui.R(_cx, _ry, _cw, _rowH), res.Select(r => $"{r.w}×{r.h}").ToArray(), cur, !_s.Fullscreen, 11f);
        if (sel != cur && sel >= 0)
        {
            (_s.Width, _s.Height) = res[sel];
            _s.ApplyResolution();
            _dirty = true;
        }
        if (_s.Fullscreen) Note("Window resolution is used when fullscreen is off.");
    }

    private void GraphicsTab()
    {
        Row("Shadows", "Unit shadows on the terrain, props and other units.\nOff saves a depth pass every frame.");
        bool shadows = ToggleControl(_s.Shadows);
        if (shadows != _s.Shadows) { _s.Shadows = shadows; Changed(); }
        NextRow();

        Row("Particles", "Amount of explosion, smoke, spark and wake particles.\nHigh = full effects; Low = about a third.");
        int pq = MenuUi.Segmented(Ui.R(_cx, _ry, Ui.P(240), _rowH), GraphicsSettings.ParticleNames, _s.ParticleQuality);
        if (pq != _s.ParticleQuality) { _s.ParticleQuality = pq; Changed(); }
        NextRow();

        Row("Terrain detail", "Terrain mesh resolution. Auto picks the detail per chunk from its distance;\n"
            + "Max..Low force one level everywhere (Max = full detail, Low = cheapest).");
        bool lodCli = GraphicsSettings.TerrainLodFromCli;
        int td = MenuUi.Segmented(Ui.R(_cx, _ry, _cw, _rowH), GraphicsSettings.TerrainNames, _s.TerrainDetail + 1, !lodCli) - 1;
        if (!lodCli && td != _s.TerrainDetail) { _s.TerrainDetail = td; Changed(); }
        if (lodCli) Note("--terrain-lod given: that level is used for this run.");
        NextRow();

        Row("VSync", "Synchronize frames with the monitor refresh (no tearing).");
        bool vsync = ToggleControl(_s.VSync);
        if (vsync != _s.VSync) { _s.VSync = vsync; Changed(); }
        if (GraphicsSettings.VsyncFromCli) Note("--novsync given: VSync stays off for this run.", Ui.P(110));
        NextRow();

        Row("FPS cap", "Upper limit on frames per second (Off = no limit besides VSync).");
        var caps = GraphicsSettings.FpsCaps;
        int ci = MenuUi.Segmented(Ui.R(_cx, _ry, _cw, _rowH), FpsLabels, Array.IndexOf(caps, _s.FpsCap));
        if (ci >= 0 && caps[ci] != _s.FpsCap) { _s.FpsCap = caps[ci]; Changed(); }
        NextRow();

        Row("UI scale", "Size of menus and the in-game interface, on top of the automatic\nscaling with the window size.");
        var scales = GraphicsSettings.UiScales;
        int si = MenuUi.Segmented(Ui.R(_cx, _ry, _cw, _rowH), ScaleLabels, AppSettings.NearestIndex(scales, _s.UiScale), true, 11.5f);
        if (scales[si] != _s.UiScale) { _s.UiScale = scales[si]; Changed(); }
        NextRow();
    }

    private static readonly string[] FpsLabels = GraphicsSettings.FpsCaps.Select(c => c == 0 ? "Off" : c.ToString()).ToArray();
    private static readonly string[] ScaleLabels = GraphicsSettings.UiScales.Select(v => $"{v * 100:0}%").ToArray();

    private void GameplayTab()
    {
        Row("Edge scroll", "Pan the camera when the mouse touches the screen edge.");
        bool edge = ToggleControl(_s.EdgeScroll);
        if (edge != _s.EdgeScroll) { _s.EdgeScroll = edge; Changed(); }
        NextRow();

        Row("Camera pan speed", "Arrow-key and edge panning speed (100% = default).");
        float pan = SpeedSlider(4, _s.PanSpeed);
        if (pan != _s.PanSpeed) { _s.PanSpeed = pan; Changed(); }
        NextRow();

        Row("Zoom speed", "Mouse-wheel zoom step (100% = default).");
        float zoom = SpeedSlider(5, _s.ZoomSpeed);
        if (zoom != _s.ZoomSpeed) { _s.ZoomSpeed = zoom; Changed(); }
        NextRow();

        Row("Autosave", "Every 5 minutes of game time, 3 rolling slots.");
        bool autosave = ToggleControl(_s.Autosave);
        if (autosave != _s.Autosave) { _s.Autosave = autosave; _dirty = true; }
        NextRow();
    }

    // ------------------------------------------------------------------ rows

    /// <summary>Label of the current row; hovering the row shows <paramref name="tip"/>.</summary>
    private void Row(string label, string tip)
    {
        MenuUi.Label(label, _lx, _ry, _rowH);
        var r = Ui.R(_lx, _ry, _cx - _lx - Ui.P(10), _rowH);
        if (Ui.Hover(r)) Ui.Tooltip(r, label, tip);
    }

    private void NextRow() => _ry += _rowH + Ui.P(14);

    private void Note(string text, float dx = 0) =>
        Ui.Text(text, _cx + dx, dx > 0 ? _ry + (_rowH - Ui.F(10.5f)) * 0.5f : _ry + _rowH + Ui.P(3), Ui.F(10.5f), Ui.TextFaint);

    private bool ToggleControl(bool on) => MenuUi.Toggle(Ui.R(_cx, _ry, Ui.P(90), _rowH), on);

    /// <summary>A graphics/gameplay value changed: apply it live, save on close.</summary>
    private void Changed()
    {
        GraphicsSettings.Apply(_s);
        _dirty = true;
    }

    private float VolumeRow(int id, string label, float value)
    {
        MenuUi.Label(label, _lx, _ry, _rowH);
        float v = MenuUi.Slider(id, Ui.R(_cx, _ry + Ui.P(9), Ui.P(250), Ui.P(12)), value, $"{value * 100:0}%");
        NextRow();
        return MathF.Abs(v - value) > 1e-4f ? v : value;
    }

    /// <summary>50%..200% in 10% steps.</summary>
    private float SpeedSlider(int id, float value)
    {
        float t = (value - 0.5f) / 1.5f;
        float nt = MenuUi.Slider(id, Ui.R(_cx, _ry + Ui.P(9), Ui.P(250), Ui.P(12)), t, $"{value * 100:0}%");
        if (MathF.Abs(nt - t) <= 1e-4f) return value;
        return MathF.Round((0.5f + 1.5f * nt) * 10f) / 10f;
    }

    public void Dispose()
    {
        if (_dirty) _s.Save();
        if (!_returned) _returnTo?.Dispose();
    }
}
