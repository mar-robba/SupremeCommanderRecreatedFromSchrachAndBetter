using Raylib_cs;
using Rts.Client.UI;

namespace Rts.Client.Menus;

/// <summary>Main menu: Skirmish, Settings, Quit over the animated map backdrop.</summary>
public sealed class MainMenuScreen : IScreen
{
    private readonly AppHost _app;

    private int _frames;

    public MainMenuScreen(AppHost app) => _app = app;

    public void Frame(float dt)
    {
        _app.Backdrop.Draw(dt);
        Ui.BeginDraw();
        float x = Ui.P(80), y = Ui.Sh * 0.17f;

        int big = Ui.F(54);
        Ui.TextShadow("RTS", x, y, big, Ui.TextColor, true);
        float tw = Ui.Measure("RTS", big, true);
        Raylib.DrawRectangle((int)x, (int)(y + big + Ui.P(4)), (int)(tw + Ui.P(150)), (int)Ui.P(3), Ui.Accent);
        Ui.TextShadow("SUPREME COMMANDER-LIKE STRATEGY", x, y + big + Ui.P(14), Ui.F(14), Ui.TextDim, true);

        float w = Ui.P(330), h = Ui.P(56), gap = Ui.P(10);
        float by = y + big + Ui.P(70);
        if (MenuUi.MenuEntry(Ui.R(x, by, w, h), "Skirmish", "Play against AI on a generated map")) _app.Go(new SkirmishSetupScreen(_app));
        by += h + gap;
        if (MenuUi.MenuEntry(Ui.R(x, by, w, h), "Settings", "Volume, fullscreen, resolution")) _app.Go(new SettingsScreen(_app));
        by += h + gap;
        if (MenuUi.MenuEntry(Ui.R(x, by, w, h), "Quit", "Back to the desktop")) _app.QuitRequested = true;

        Ui.Text("Prototype · C# / .NET 10 · raylib", x, Ui.Sh - Ui.P(34), Ui.F(11), Ui.TextFaint);
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || (_app.Options.FlowTest && ++_frames == 20))
            _app.Go(new SkirmishSetupScreen(_app));
        Ui.DrawTooltip();
    }

    public void Dispose() { }
}
