using Rts.Client.Audio;
using Rts.Sim.Data;

namespace Rts.Client.Menus;

/// <summary>A client state (main menu, setup, loading, in-game). Frame() updates and draws one frame.</summary>
public interface IScreen : IDisposable
{
    void Frame(float dt);
    /// <summary>Extra text printed after an automated screenshot.</summary>
    string ScreenshotInfo => "";
}

/// <summary>
/// Owns the current <see cref="IScreen"/> and switches screens at the end of a frame (the old one is disposed).
/// Flow: MainMenu → (Settings) / SkirmishSetup → Loading → InGame (pause menu, end screen) → MainMenu.
/// </summary>
public sealed class AppHost : IDisposable
{
    public readonly BlueprintDb Db;
    /// <summary>Command-line options (screenshot mode, debug flags).</summary>
    public readonly ClientOptions Options;
    /// <summary>App-wide audio (device, sound bank, music); volumes in <c>Audio.Settings</c>.</summary>
    public readonly AudioSystem Audio;
    public IScreen Current = null!;
    private IScreen? _next;
    private MenuBackdrop? _backdrop;
    public bool QuitRequested;
    /// <summary>The skirmish setup being edited (kept between visits of the setup screen).</summary>
    public GameConfig SetupConfig;

    public AppHost(BlueprintDb db, ClientOptions options, AudioSystem audio)
    {
        Db = db;
        Options = options;
        Audio = audio;
        var s = AppSettings.Current;
        SetupConfig = new GameConfig
        {
            MapSize = options.MapSizeGiven ? options.MapSize : s.MapSize,
            Preset = options.PresetGiven ? options.Preset : Rts.Sim.Map.MapPresets.TryParse(s.Preset, out var p) ? p : default,
            WaterAmount = options.PresetGiven || options.WaterAmount >= 0 ? options.WaterAmount : s.WaterAmount,
            Seed = options.Seed,
        };
        if (options.PlayersGiven)
            for (int i = 0; i < GameConfig.MaxSlots; i++)
                SetupConfig.Slots[i].Kind = i == 0 ? SlotKind.Human : i < options.Players ? SlotKind.AI : SlotKind.Closed;
    }

    /// <summary>Shared animated menu background (created on first use).</summary>
    public MenuBackdrop Backdrop => _backdrop ??= new MenuBackdrop();

    public void Go(IScreen next) => _next = next;

    public void ToMainMenu() => Go(new MainMenuScreen(this));

    /// <summary>Options for games started from the menus (no CLI startup helpers).</summary>
    public ClientOptions MenuGameOptions() => new()
    {
        FxTest = Options.FxTest, Gallery = Options.Gallery, ScreenshotPath = Options.ScreenshotPath,
        Frames = Options.Frames, PauseMenu = Options.PauseMenu, EndScreen = Options.EndScreen,
        FlowTest = Options.FlowTest, AutoStart = Options.AutoStart,
        Audio = Options.Audio, VolumeGiven = Options.VolumeGiven, NoFog = Options.NoFog,
    };

    public void StartGame(GameConfig cfg, ClientOptions opt) => Go(new LoadingScreen(this, cfg.Clone(), opt));

    /// <summary>Applies a pending screen switch. Call after EndDrawing.</summary>
    public void EndFrame()
    {
        if (_next == null) return;
        var old = Current;
        Current = _next;
        _next = null;
        if (!ReferenceEquals(old, Current)) old.Dispose();
    }

    public void Dispose()
    {
        Current.Dispose();
        _next?.Dispose();
        _backdrop?.Dispose();
    }
}
