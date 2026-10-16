using Raylib_cs;
using Rts.Client.Persistence;
using Rts.Client.UI;
using Rts.Sim.Persistence;

namespace Rts.Client.Menus;

/// <summary>
/// Main menu → "Load Game" / "Replays": the files of the saves/ or replays/ folder (headers only, newest first) with
/// date, name, map, players, length and tags (sandbox, winner, other build). Unreadable files are listed greyed with
/// the reason. Load/Play (double-click, Enter), Delete (with confirmation), Back (Esc).
/// Opened from the pause menu it owns the paused game (<c>returnTo</c>): Back returns to it, loading a save
/// disposes it.
/// </summary>
public sealed class SaveListScreen : IScreen
{
    private readonly AppHost _app;
    private readonly bool _replays;
    private List<SaveEntry> _entries = new();
    private int _selected = -1;
    private float _scroll;
    private double _lastClickTime;
    private int _lastClickIndex = -1;
    private SaveEntry? _confirmDelete;
    private string? _message;
    /// <summary>The paused game this list was opened from (pause menu "Load Game"), null from the main menu.</summary>
    private readonly IScreen? _returnTo;
    private bool _returned;

    public SaveListScreen(AppHost app, bool replays, string? message = null, IScreen? returnTo = null)
    {
        _app = app;
        _replays = replays;
        _message = message;
        _returnTo = returnTo;
        Refresh();
    }

    private void Refresh()
    {
        try { _entries = _replays ? SaveStore.ListReplays() : SaveStore.ListSaves(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _entries = new();
            _message = SaveText.ErrorMessage(e);
        }
        _selected = _entries.FindIndex(e => e.Info != null);
    }

    public void Frame(float dt)
    {
        _app.Backdrop.Draw(dt, 0.75f);
        MenuUi.BeginFrame();
        bool modal = _confirmDelete != null || _message != null;
        float x = Ui.P(60), y = Ui.P(40);
        int valid = _entries.Count(e => e.Info != null);
        string sub = _replays
            ? (_entries.Count == 0 ? "Every game is recorded automatically" : $"{valid} replay{(valid == 1 ? "" : "s")} · the newest {SaveStore.MaxReplays} are kept")
            : (_entries.Count == 0 ? "No saved games yet" : $"{valid} saved game{(valid == 1 ? "" : "s")}");
        MenuUi.ScreenTitle(_replays ? "Replays" : "Load Game", sub, x, y);

        var panel = Ui.R(x, Ui.P(118), Ui.Sw - 2 * x, Ui.Sh - Ui.P(118) - Ui.P(86));
        Ui.Panel(panel);
        DrawList(panel, modal);

        // Buttons.
        float bh = Ui.P(40), by = panel.Y + panel.Height + Ui.P(18), bw = Ui.P(150);
        var sel = (uint)_selected < (uint)_entries.Count ? _entries[_selected] : null;
        bool canOpen = sel?.Info != null && !modal;
        if (MenuUi.BigButton(Ui.R(x, by, bw, bh), _returnTo != null ? "Back to Game" : "Back", !modal) || (!modal && MenuUi.Key(KeyboardKey.Escape)))
        {
            if (_returnTo != null)
            {
                _returned = true;
                _app.Go(_returnTo);
            }
            else _app.ToMainMenu();
            return;
        }
        var openR = Ui.R(panel.X + panel.Width - bw, by, bw, bh);
        var delR = Ui.R(openR.X - bw - Ui.P(12), by, bw, bh);
        if (MenuUi.BigButton(openR, _replays ? "Play" : "Load", canOpen, primary: true) ||
            (canOpen && (MenuUi.Key(KeyboardKey.Enter) || MenuUi.Key(KeyboardKey.KpEnter))))
            Open(sel!);
        if (MenuUi.BigButton(delR, "Delete", sel != null && !modal) || (sel != null && !modal && MenuUi.Key(KeyboardKey.Delete)))
            _confirmDelete = sel;
        string folder = $"Folder: {Path.GetFullPath(_replays ? SaveStore.ReplaysDir : SaveStore.SavesDir)}";
        Ui.Text(Ui.Fit(folder, delR.X - x - bw - Ui.P(32), Ui.F(10.5f)), x + bw + Ui.P(16), by + (bh - Ui.F(10.5f)) * 0.5f, Ui.F(10.5f), Ui.TextFaint);

        if (!modal) KeyboardNavigation();
        DrawModal();
        Ui.DrawTooltip();
    }

    private void DrawList(Rectangle panel, bool modal)
    {
        float rowH = Ui.P(50), pad = Ui.P(8);
        var inner = Ui.Inset(panel, pad);
        if (_entries.Count == 0)
        {
            string hint = _replays
                ? "No replays yet. Replays are written when a game ends and when you quit it."
                : "No saved games yet. Save from the pause menu (Esc) or quicksave with F5 in game.";
            Ui.TextCentered(hint, inner, Ui.F(14), Ui.TextDim);
            return;
        }
        int visible = Math.Max(1, (int)(inner.Height / rowH));
        float maxScroll = Math.Max(0, _entries.Count - visible);
        if (!modal && Ui.Hover(panel)) _scroll -= Raylib.GetMouseWheelMove() * 3;
        _scroll = Math.Clamp(_scroll, 0, maxScroll);
        int first = (int)_scroll;

        Raylib.BeginScissorMode((int)inner.X, (int)inner.Y, (int)inner.Width, (int)inner.Height);
        for (int i = first; i < _entries.Count && i < first + visible + 1; i++)
        {
            var e = _entries[i];
            var r = Ui.R(inner.X, inner.Y + (i - first) * rowH, inner.Width - (maxScroll > 0 ? Ui.P(10) : 0), rowH - Ui.P(4));
            bool hover = !modal && Ui.Hover(r) && Ui.Hover(inner);
            bool sel = i == _selected;
            Raylib.DrawRectangleRec(r, sel ? new Color(38, 62, 92, 235) : hover ? new Color(30, 42, 58, 220) : new Color(18, 25, 34, 200));
            Raylib.DrawRectangleLinesEx(r, 1, sel ? Ui.Accent : new Color(52, 66, 84, 255));
            DrawRow(r, e);
            if (hover && Ui.LeftClickIn(r))
            {
                double now = Raylib.GetTime();
                bool dbl = i == _lastClickIndex && now - _lastClickTime < 0.4;
                _selected = i;
                _lastClickIndex = i;
                _lastClickTime = now;
                if (dbl && e.Info != null) Open(e);
            }
        }
        Raylib.EndScissorMode();
        if (maxScroll > 0)
        {
            float th = inner.Height * visible / _entries.Count;
            float ty = inner.Y + (inner.Height - th) * (_scroll / maxScroll);
            Raylib.DrawRectangle((int)(inner.X + inner.Width - Ui.P(6)), (int)ty, (int)Ui.P(5), (int)th, new Color(90, 110, 132, 200));
        }
    }

    private void DrawRow(Rectangle r, SaveEntry e)
    {
        bool ok = e.Info != null;
        int f1 = Ui.F(14), f2 = Ui.F(11.5f);
        float x = r.X + Ui.P(12), y1 = r.Y + Ui.P(6), y2 = y1 + f1 + Ui.P(5);
        string date = SaveText.Date(e.Info?.CreatedUtc ?? e.Modified);
        float dateW = Ui.Measure(date, f2);
        Ui.TextRight(date, r.X + r.Width - Ui.P(12), y1 + (f1 - f2) * 0.5f, f2, ok ? Ui.TextDim : Ui.TextFaint);
        string title = Ui.Fit(SaveText.Title(e), r.Width * 0.5f, f1, true);
        Ui.Text(title, x, y1, f1, ok ? Ui.TextColor : Ui.TextFaint, true);
        float tx = x + Ui.Measure(title, f1, true) + Ui.P(10);
        foreach (var tag in SaveText.Tags(e, _replays))
        {
            int tf = Ui.F(9.5f);
            float tw = Ui.Measure(tag, tf, true) + Ui.P(10);
            if (tx + tw > r.X + r.Width - dateW - Ui.P(24)) break;
            var tr = Ui.R(tx, y1 + (f1 - tf) * 0.5f - Ui.P(2), tw, tf + Ui.P(4));
            var c = tag == "SANDBOX" ? Ui.EnergyColor : tag == "OTHER BUILD" ? Ui.Warn : tag.StartsWith("WINNER") ? Ui.Good : Ui.Accent;
            Raylib.DrawRectangleLinesEx(tr, 1, c);
            Ui.TextCentered(tag, tr, tf, c, true);
            tx += tw + Ui.P(6);
        }
        string details = Ui.Fit(SaveText.Details(e), r.Width - Ui.P(24), f2);
        Ui.Text(details, x, y2, f2, ok ? Ui.TextDim : ColorUtil.WithAlpha(Ui.Bad, 200));
    }

    private void KeyboardNavigation()
    {
        if (_entries.Count == 0) return;
        if (MenuUi.Key(KeyboardKey.Down)) _selected = Math.Min(_entries.Count - 1, _selected + 1);
        if (MenuUi.Key(KeyboardKey.Up)) _selected = Math.Max(0, _selected - 1);
    }

    private void DrawModal()
    {
        if (_message != null)
        {
            if (MessageBox.Draw(_replays ? "Cannot play this replay" : "Cannot load this game", _message, ["OK"], error: true) >= 0) _message = null;
        }
        else if (_confirmDelete is SaveEntry d)
        {
            int k = MessageBox.Draw(_replays ? "Delete replay?" : "Delete saved game?", $"\"{SaveText.Title(d)}\" will be deleted. This cannot be undone.",
                ["Delete", "Cancel"], danger: true);
            if (k == 0)
            {
                try { File.Delete(d.Path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _message = SaveText.ErrorMessage(e); }
                int keep = _selected;
                _confirmDelete = null;
                Refresh();
                _selected = Math.Min(keep, _entries.Count - 1);
            }
            else if (k == 1) _confirmDelete = null;
        }
    }

    private void Open(SaveEntry e)
    {
        _app.Go(new FileLoadingScreen(_app, e.Path, _replays, _app.MenuGameOptions(), fromList: true));
    }

    /// <summary>Disposes the paused game it was opened from, unless Back returned to it.</summary>
    public void Dispose()
    {
        if (!_returned) _returnTo?.Dispose();
    }
}
