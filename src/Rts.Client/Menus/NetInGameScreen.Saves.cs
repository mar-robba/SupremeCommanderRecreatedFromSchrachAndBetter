using System.Numerics;
using Rts.Client.Input;
using Rts.Client.Persistence;
using Rts.Client.UI;
using Rts.Net.Session;

namespace Rts.Client.Menus;

/// <summary>
/// (10a-3) Saving a multiplayer game: Esc → Save Game (the single-player dialog) and F5 send a request to the host, which
/// decides; every peer then writes the same save into its saves folder. Toasts report the order and the result (a failure
/// also arrives as a system chat line naming who differs). This peer's camera is handed to the recording every second so
/// the sim thread can store it in this peer's file. The host's autosave is its own (HostSession.Autosave).
/// </summary>
public sealed partial class NetInGameScreen
{
    /// <summary>F5 name of a multiplayer quicksave (kept apart from the single-player quicksave that F6 loads).</summary>
    public const string NetQuickSaveName = "mp_quicksave";

    private InGameSaveUi? _saveUiNet;

    private InGameSaveUi SaveUi => _saveUiNet ??= new InGameSaveUi(_app, _s, _opt) { NetSave = name => _game.RequestSave(name) };

    /// <summary>Every frame, after the events: the camera for this peer's save file, F5 and --save-at.</summary>
    private void SaveFrame()
    {
        if (_s.Recording is { } rec && _frames % 10 == 1)
        {
            var cam = _s.Ctx.Camera;
            rec.SetNetClientData(ClientGameData.From(_s.Config, new Vector3(cam.Focus.X, cam.Focus.Z, cam.TargetDistance)));
        }
        if (_opt.SaveAt == _frames) SaveUi.Save(NetQuickSaveName);
        if (_state == State.Playing && !_chat.Focused && !MenuUi.Automated && Hotkeys.QuickSave.Pressed())
            SaveUi.Save(NetQuickSaveName);
    }

    /// <summary>The menu's "Save Game" button: opens the name dialog (drawn in place of the menu).</summary>
    private void OpenSaveDialog() => SaveUi.OpenDialog();

    private bool SaveDialogOpen => _saveUiNet?.DialogOpen == true;

    private void DrawSaveDialog()
    {
        MenuUi.BeginFrame();
        MenuUi.Dim(110);
        SaveUi.DrawDialog();
        Ui.DrawTooltip();
    }

    /// <summary>Save events of the session; false = not a save event.</summary>
    private static bool HandleSaveEvent(NetEvent e)
    {
        switch (e)
        {
            case SaveOrderedEvent o:
                if (!o.Auto) Toasts.Show($"Saving \"{o.Name}\"" + (o.By.Length > 0 ? $" (asked by {o.By})" : "") + "…");
                return true;
            case SaveResultEvent r:
                if (r.Ok) Toasts.Show(r.Auto ? "Autosaved" : r.Text);
                else Toasts.Show(r.Text, error: true);
                return true;
            case SaveRefusedEvent f:
                Toasts.Show("Save refused: " + f.Reason, error: true);
                return true;
        }
        return false;
    }
}
