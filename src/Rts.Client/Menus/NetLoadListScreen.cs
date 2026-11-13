using Raylib_cs;
using Rts.Client.Persistence;
using Rts.Client.UI;
using Rts.Net.Session;
using Rts.Sim.Persistence;

namespace Rts.Client.Menus;

/// <summary>
/// (10a-3) Multiplayer → Load Saved Game: the multiplayer saves of the saves folder (newest first). "Host This Save"
/// hosts on the port of the Multiplayer screen and opens the load lobby: the players who join take their saved slots.
/// </summary>
public sealed class NetLoadListScreen : IScreen
{
    private readonly AppHost _app;
    private readonly string _name;
    private readonly int _port;
    private readonly List<SaveEntry> _saves;
    private int _sel;
    private float _scroll;
    private string? _error;

    public NetLoadListScreen(AppHost app, string playerName, int port)
    {
        _app = app;
        _name = playerName;
        _port = port;
        try { _saves = NetSession.ListMultiplayerSaves(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _saves = new(); _error = e.Message; }
    }

    public string ScreenshotInfo => $"({_saves.Count} multiplayer saves)";

    public void Frame(float dt)
    {
        _app.Backdrop.Draw(dt, 1.0f);
        MenuUi.Dim(70);
        MenuUi.BeginFrame();
        float x0 = Ui.P(48), y0 = Ui.P(30);
        MenuUi.ScreenTitle("Load Multiplayer Game", $"Host a saved game on port {_port}: every player takes a saved slot", x0, y0);
        float top = y0 + Ui.P(84);
        var p = Ui.R(x0, top, MathF.Min(Ui.P(760), Ui.Sw - 2 * x0), Ui.Sh - top - Ui.P(90));
        Ui.Panel(p);
        float pad = Ui.P(14), rowH = Ui.P(48);
        if (_saves.Count == 0)
            Ui.Text("No multiplayer saves yet: in a multiplayer game, Esc → Save Game or F5 saves for every player.",
                p.X + pad, p.Y + pad, Ui.F(12.5f), Ui.TextDim);
        int visible = Math.Max(1, (int)((p.Height - 2 * pad) / rowH));
        _scroll = Math.Clamp(_scroll - Raylib.GetMouseWheelMove() * (Ui.Hover(p) ? 1 : 0), 0, Math.Max(0, _saves.Count - visible));
        int first = (int)_scroll;
        for (int k = 0; k < visible && first + k < _saves.Count; k++)
        {
            int i = first + k;
            var e = _saves[i];
            var r = Ui.R(p.X + pad, p.Y + pad + k * rowH, p.Width - 2 * pad, rowH - Ui.P(4));
            bool hover = Ui.Hover(r);
            Raylib.DrawRectangleRec(r, i == _sel ? new Color(40, 64, 92, 235) : hover ? new Color(30, 44, 60, 220) : new Color(18, 25, 34, 200));
            var mp = e.Info?.Multiplayer;
            string title = SaveText.Title(e) + (mp?.Auto == true ? "  (autosave)" : "");
            Ui.Text(Ui.Fit(title, r.Width * 0.6f, Ui.F(13), true), r.X + Ui.P(10), r.Y + Ui.P(5), Ui.F(13), Ui.TextColor, true);
            Ui.TextRight(SaveText.Date(e.Info?.CreatedUtc ?? e.Modified.ToUniversalTime()), r.X + r.Width - Ui.P(10), r.Y + Ui.P(6), Ui.F(11), Ui.TextDim);
            string who = mp != null ? $"{mp.Humans} players · saved by {mp.SavedBy}" : "";
            Ui.Text(Ui.Fit(SaveText.Details(e) + (who.Length > 0 ? "  ·  " + who : ""), r.Width - Ui.P(20), Ui.F(10.5f)),
                r.X + Ui.P(10), r.Y + Ui.P(25), Ui.F(10.5f), Ui.TextFaint);
            if (Ui.LeftClickIn(r))
            {
                if (_sel == i && Raylib.GetTime() - _lastClick < 0.4) Host();
                _sel = i;
                _lastClick = Raylib.GetTime();
            }
        }

        float by = Ui.Sh - Ui.P(62);
        if (MenuUi.BigButton(Ui.R(x0, by, Ui.P(150), Ui.P(42)), "Back") || MenuUi.Key(KeyboardKey.Escape))
        {
            _app.Go(new MultiplayerScreen(_app));
            return;
        }
        bool can = _sel >= 0 && _sel < _saves.Count && _saves[_sel].Info != null;
        if (MenuUi.BigButton(Ui.R(p.X + p.Width - Ui.P(200), by, Ui.P(200), Ui.P(42)), "Host This Save", can, true)
            || (can && MenuUi.Key(KeyboardKey.Enter))) Host();
        if (_error != null)
            Ui.Text(Ui.Fit(_error, p.Width - Ui.P(420), Ui.F(12.5f)), x0 + Ui.P(170), by + Ui.P(13), Ui.F(12.5f), Ui.Bad, true);
        Ui.DrawTooltip();
    }

    private double _lastClick = -1;

    private void Host()
    {
        if (_sel < 0 || _sel >= _saves.Count) return;
        var lobby = MultiplayerScreen.HostLoadLobby(_app, _name, _port, _saves[_sel].Path, out _error);
        if (lobby != null) _app.Go(lobby);
    }

    public void Dispose() { }
}
