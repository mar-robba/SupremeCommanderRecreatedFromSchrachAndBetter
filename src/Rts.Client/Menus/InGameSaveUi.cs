using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Client.Persistence;
using Rts.Client.Sandbox;
using Rts.Client.UI;
using Rts.Sim.Persistence;

namespace Rts.Client.Menus;

/// <summary>
/// Saving and loading inside a game (owned by <see cref="InGameScreen"/>): F5 quicksave, F6 quickload, the pause
/// menu's "Save Game" dialog (name field, existing saves, overwrite warning) and --save-at N (quicksave at frame N,
/// for tests). Works in skirmish and sandbox games; the world is captured on the sim thread, files are written on a worker.
/// Autosave every 5 minutes of game time (<see cref="AutosaveStep"/>, rules in Persistence/Autosave.cs).
/// </summary>
public sealed class InGameSaveUi
{
    private readonly AppHost _app;
    private readonly GameSession _s;
    private readonly ClientOptions _opt;
    private SandboxTextField? _field;
    private List<SaveEntry> _existing = new();
    private Autosave? _autosave;

    public InGameSaveUi(AppHost app, GameSession session, ClientOptions opt)
    {
        _app = app;
        _s = session;
        _opt = opt;
    }

    public bool DialogOpen => _field != null;

    /// <summary>(10a-3) Multiplayer: a save goes to the host as a request (every peer writes the same save) instead of
    /// this peer's recording.</summary>
    public Action<string>? NetSave;

    /// <summary>Playing state: F5 / F6 and the --save-at helper.</summary>
    public void HandleHotkeys(int frame)
    {
        if (_opt.SaveAt == frame) QuickSave();
        if (!MenuUi.Automated && Hotkeys.QuickSave.Pressed()) QuickSave();
        if (!MenuUi.Automated && Hotkeys.QuickLoad.Pressed()) QuickLoad();
    }

    public void QuickSave() => Save(SaveStore.QuickSaveName);

    public void Save(string name, string? toast = null)
    {
        if (_s.Recording == null) return;
        if (NetSave != null) { NetSave(name); return; }
        var cam = _s.Ctx.Camera;
        _s.Recording.Save(_s.Runner, name, ClientGameData.From(_s.Config, new Vector3(cam.Focus.X, cam.Focus.Z, cam.TargetDistance)), toast: toast);
    }

    /// <summary>
    /// Playing state, every frame: writes the next rolling autosave (autosave_1..3) when 5 minutes of game time
    /// (--autosave-every TICKS) passed since the game started / was loaded / the last autosave. <paramref name="ended"/>:
    /// the local player was defeated or the game is over.
    /// </summary>
    public void AutosaveStep(bool ended)
    {
        bool forced = _opt.AutosaveEvery > 0;
        var snap = _s.Snapshots.Current;
        if (snap == null || !Autosave.Allowed(AppSettings.Current.Autosave, forced, _s.Recording != null, _s.Ctx.ReadOnly,
                _s.Config.Sandbox, _s.Config.Scene != null, _opt.ScreenshotPath != null, ended || snap.WinningTeam >= 0)) return;
        _autosave ??= new Autosave(forced ? _opt.AutosaveEvery : Autosave.DefaultIntervalTicks);
        if (_autosave.Due(snap.Tick)) Save(_autosave.NextSlot(), "Autosaved");
    }

    /// <summary>Loads the quicksave (its header is checked first so a bad file doesn't end the current game).</summary>
    public void QuickLoad()
    {
        string path = SaveStore.SavePath(SaveStore.QuickSaveName);
        GameRecording.WaitForPendingWrites(TimeSpan.FromSeconds(10)); // a quicksave may still be being written
        if (!File.Exists(path))
        {
            Toasts.Show("No quicksave yet (F5 saves)", error: true);
            return;
        }
        try
        {
            var info = SaveContainer.ReadInfo(path, out _);
            if (info.BlueprintHash != GameInfo.BlueprintHashOf(_app.Db))
                throw new IncompatibleSaveException("The unit blueprints changed since this file was made (different game version)");
        }
        catch (Exception e) when (e is SaveFormatException or IOException or UnauthorizedAccessException)
        {
            Toasts.Show("Quickload failed: " + SaveText.ErrorMessage(e), error: true);
            return;
        }
        _app.Go(new FileLoadingScreen(_app, path, replay: false, _opt));
    }

    // ------------------------------------------------------------------ save dialog (pause menu)

    public void OpenDialog()
    {
        _field = new SandboxTextField { Text = SaveText.DefaultSaveName(_s.Config, DateTime.Now), MaxLength = 60 };
        _field.Focus();
        try { _existing = SaveStore.ListSaves().Where(e => e.Info != null).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _existing = new(); }
    }

    public void CloseDialog() => _field = null;

    /// <summary>Draws the dialog in place of the pause menu panel (Enter saves, Esc cancels).</summary>
    public void DrawDialog()
    {
        var field = _field!;
        bool enter = field.HandleInput();
        float w = Ui.P(480), pad = Ui.P(22);
        int shownSaves = Math.Min(5, _existing.Count);
        float rowH = Ui.P(26);
        float h = Ui.P(150) + (shownSaves > 0 ? Ui.P(26) + shownSaves * rowH : 0) + Ui.P(60);
        var p = Ui.R((Ui.Sw - w) * 0.5f, (Ui.Sh - h) * 0.42f, w, h);
        Ui.Panel(p);
        Ui.Text("SAVE GAME", p.X + pad, p.Y + Ui.P(16), Ui.F(20), Ui.TextColor, true);
        float y = p.Y + Ui.P(56);
        MenuUi.Label("Name", p.X + pad, y, Ui.P(16));
        y += Ui.P(20);
        var fr = Ui.R(p.X + pad, y, w - 2 * pad, Ui.P(32));
        if (Ui.LeftClickIn(fr)) field.Focus();
        field.Draw(fr, "save name");
        y += fr.Height + Ui.P(8);

        string name = field.Text.Trim();
        bool overwrite = name.Length > 0 && SaveText.Overwrites(name, _existing);
        if (overwrite) Ui.Text("A save with this name exists: it will be overwritten", p.X + pad, y, Ui.F(11.5f), Ui.Warn);
        else Ui.Text($"File: {Path.GetFileName(SaveStore.SavePath(name.Length > 0 ? name : "save"))}", p.X + pad, y, Ui.F(11.5f), Ui.TextFaint);
        y += Ui.P(26);

        if (shownSaves > 0)
        {
            MenuUi.Label("Existing saves (click to overwrite)", p.X + pad, y, Ui.P(16));
            y += Ui.P(24);
            for (int i = 0; i < shownSaves; i++)
            {
                var e = _existing[i];
                var r = Ui.R(p.X + pad, y, w - 2 * pad, rowH - Ui.P(3));
                bool hover = Ui.Hover(r);
                Raylib.DrawRectangleRec(r, hover ? new Color(36, 52, 72, 230) : new Color(18, 25, 34, 200));
                int fs = Ui.F(11.5f);
                Ui.Text(Ui.Fit(SaveText.Title(e), r.Width * 0.62f, fs, true), r.X + Ui.P(8), r.Y + (r.Height - fs) * 0.5f - 1, fs, Ui.TextColor, true);
                Ui.TextRight(SaveText.Date(e.Info!.CreatedUtc), r.X + r.Width - Ui.P(8), r.Y + (r.Height - fs) * 0.5f - 1, fs, Ui.TextDim);
                if (Ui.LeftClickIn(r)) { field.Text = e.Info.Name.Length > 0 ? e.Info.Name : e.FileName; field.Focus(); }
                y += rowH;
            }
        }

        float bw = Ui.P(130), bh = Ui.P(38);
        var saveR = Ui.R(p.X + p.Width - pad - bw, p.Y + p.Height - bh - Ui.P(16), bw, bh);
        var cancelR = Ui.R(saveR.X - bw - Ui.P(10), saveR.Y, bw, bh);
        bool can = name.Length > 0 && _s.Recording != null;
        if (MenuUi.BigButton(saveR, overwrite ? "Overwrite" : "Save", can, primary: true) || (enter && can))
        {
            Save(name);
            CloseDialog();
            return;
        }
        if (MenuUi.BigButton(cancelR, "Cancel") || MenuUi.Key(KeyboardKey.Escape)) CloseDialog();
    }
}
