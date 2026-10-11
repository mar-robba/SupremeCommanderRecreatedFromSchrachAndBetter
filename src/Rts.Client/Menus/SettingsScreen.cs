using Raylib_cs;
using Rts.Client.UI;

namespace Rts.Client.Menus;

/// <summary>Settings: master volume (stored; used by audio), fullscreen (borderless) and window resolution.</summary>
public sealed class SettingsScreen : IScreen
{
    private readonly AppHost _app;
    private readonly AppSettings _s = AppSettings.Current;
    private bool _dirty;

    public SettingsScreen(AppHost app) => _app = app;

    public void Frame(float dt)
    {
        _app.Backdrop.Draw(dt, 0.75f);
        MenuUi.BeginFrame();
        float x = Ui.P(80), y = Ui.Sh * 0.12f;
        MenuUi.ScreenTitle("Settings", "Changes apply immediately and are saved", x, y);

        var panel = Ui.R(x, y + Ui.P(80), Ui.P(560), Ui.P(230));
        Ui.Panel(panel);
        float lx = panel.X + Ui.P(20), cx = panel.X + Ui.P(200), rowH = Ui.P(32), cw = Ui.P(320);
        float ry = panel.Y + Ui.P(24);

        MenuUi.Label("Master volume", lx, ry, rowH);
        float vol = MenuUi.Slider(1, Ui.R(cx, ry + Ui.P(10), Ui.P(250), Ui.P(12)), _s.MasterVolume, $"{_s.MasterVolume * 100:0}%");
        if (MathF.Abs(vol - _s.MasterVolume) > 1e-4f)
        {
            _s.MasterVolume = vol;
            _s.ApplyVolume();
            _dirty = true;
        }
        ry += rowH + Ui.P(18);

        MenuUi.Label("Fullscreen", lx, ry, rowH);
        bool fs = MenuUi.Toggle(Ui.R(cx, ry, Ui.P(90), rowH), _s.Fullscreen);
        if (fs != _s.Fullscreen)
        {
            _s.Fullscreen = fs;
            _s.ApplyFullscreen();
            if (!fs) _s.ApplyResolution();
            _dirty = true;
        }
        ry += rowH + Ui.P(18);

        MenuUi.Label("Resolution", lx, ry, rowH);
        var res = AppSettings.Resolutions;
        int cur = Array.FindIndex(res, r => r.w == _s.Width && r.h == _s.Height);
        int sel = MenuUi.Segmented(Ui.R(cx, ry, cw, rowH), res.Select(r => $"{r.w}×{r.h}").ToArray(), cur, !_s.Fullscreen, 11f);
        if (sel != cur && sel >= 0)
        {
            (_s.Width, _s.Height) = res[sel];
            _s.ApplyResolution();
            _dirty = true;
        }
        if (_s.Fullscreen) Ui.Text("Window resolution is used when fullscreen is off.", cx, ry + rowH + Ui.P(6), Ui.F(10.5f), Ui.TextFaint);

        if (MenuUi.BigButton(Ui.R(x, panel.Y + panel.Height + Ui.P(20), Ui.P(160), Ui.P(40)), "Back") || MenuUi.Key(KeyboardKey.Escape))
            _app.ToMainMenu();
        Ui.DrawTooltip();
    }

    public void Dispose()
    {
        if (_dirty) _s.Save();
    }
}
