using System.Numerics;
using Raylib_cs;
using Rts.Client.UI;
using Rts.Net.Session;
using Rts.Net.Transport;

namespace Rts.Client.Menus;

/// <summary>
/// Multiplayer menu: player name, Host (port), Join (address) and the LAN games found by UDP broadcast
/// (<see cref="LanBrowser"/>). Hosting or joining opens the <see cref="LobbyScreen"/>, which then owns the session.
/// Joining runs on a worker (it blocks until the host answers). CLI: --host [port], --join address, --name N.
/// </summary>
public sealed class MultiplayerScreen : IScreen
{
    private readonly AppHost _app;
    private readonly NetTextField _name, _address, _port;
    private LanBrowser? _lan;
    private Task<ClientSession>? _joining;
    private string _joiningWhat = "";
    private string? _error;
    private bool _cliDone;

    public MultiplayerScreen(AppHost app, string? error = null)
    {
        _app = app;
        _error = error;
        if (error != null) Console.WriteLine("multiplayer: " + error);
        var s = AppSettings.Current;
        string name = app.Options.PlayerName ?? (s.PlayerName.Length > 0 ? s.PlayerName : DefaultName());
        _name = new NetTextField(name, 24, NetTextField.IsNameChar);
        _address = new NetTextField(app.Options.JoinAddress ?? (s.JoinAddress.Length > 0 ? s.JoinAddress : "127.0.0.1"), 64, NetTextField.IsAddressChar);
        _port = new NetTextField((s.HostPort > 0 ? s.HostPort : NetSession.DefaultPort).ToString(), 5, NetTextField.IsDigit);
        try { _lan = new LanBrowser(); }
        catch (Exception e) { _error ??= "LAN search unavailable: " + e.Message; }
    }

    private static string DefaultName()
    {
        string n = Environment.UserName;
        return string.IsNullOrWhiteSpace(n) ? "Player" : n.Length > 24 ? n[..24] : n;
    }

    private string PlayerName => _name.Text.Trim().Length > 0 ? _name.Text.Trim() : DefaultName();

    public string ScreenshotInfo => _lan == null ? "(no LAN browser)" : $"({_lan.Games.Count} LAN games)";

    public void Frame(float dt)
    {
        _app.Backdrop.Draw(dt, 1.0f);
        MenuUi.Dim(70);
        MenuUi.BeginFrame();
        if (!_cliDone)
        {
            _cliDone = true;
            // --host / --join act once per run (consumed: coming back to this screen later does nothing).
            int hostPort = _app.Options.HostPort;
            string? join = _app.Options.JoinAddress;
            _app.Options.HostPort = 0;
            _app.Options.JoinAddress = null;
            if (hostPort > 0) Host(hostPort);
            else if (join != null) Join(join);
        }
        PollJoin();
        bool focused = AnyFocused; // Esc in a field only unfocuses it

        float x0 = Ui.P(48), y0 = Ui.P(30);
        MenuUi.ScreenTitle("Multiplayer", "Host a game or join one on the network", x0, y0);
        float top = y0 + Ui.P(84);
        float leftW = Ui.P(440);
        var left = Ui.R(x0, top, leftW, Ui.P(330));
        var right = Ui.R(x0 + leftW + Ui.P(20), top, MathF.Min(Ui.P(620), Ui.Sw - x0 * 2 - leftW - Ui.P(20)), Ui.Sh - top - Ui.P(90));
        bool busy = _joining != null;
        DrawConnect(left, busy);
        DrawLan(right, busy);

        float by = Ui.Sh - Ui.P(62);
        if (MenuUi.BigButton(Ui.R(x0, by, Ui.P(150), Ui.P(42)), "Back", !busy)
            || (!busy && MenuUi.Key(KeyboardKey.Escape) && !focused))
        {
            _app.ToMainMenu();
            return;
        }
        if (busy)
        {
            MenuUi.Spinner(new Vector2(x0 + Ui.P(184), by + Ui.P(21)), Ui.P(9), Ui.Accent);
            Ui.Text($"Connecting to {_joiningWhat}…", x0 + Ui.P(204), by + Ui.P(13), Ui.F(13), Ui.TextColor);
        }
        else if (_error != null)
            Ui.Text(Ui.Fit(_error, Ui.Sw - x0 - Ui.P(230), Ui.F(12.5f)), x0 + Ui.P(170), by + Ui.P(13), Ui.F(12.5f), Ui.Bad, true);
        Ui.DrawTooltip();
    }

    private bool AnyFocused => _name.Focused || _address.Focused || _port.Focused;

    private void DrawConnect(Rectangle p, bool busy)
    {
        Ui.Panel(p);
        Ui.PanelTitle(p, "Connect");
        float x = p.X + Ui.P(16), y = p.Y + Ui.P(36), w = p.Width - Ui.P(32), rowH = Ui.P(30);
        int ls = Ui.F(10.5f);

        Ui.Text("YOUR NAME", x, y, ls, Ui.TextFaint, true);
        y += Ui.P(16);
        var nr = Ui.R(x, y, w, rowH);
        _name.Update(nr, !busy);
        _name.Draw(nr, DefaultName(), "", 13, !busy);
        y += rowH + Ui.P(22);

        Ui.Text("HOST A GAME", x, y, ls, Ui.TextFaint, true);
        y += Ui.P(16);
        Ui.Text("Port", x, y + (rowH - Ui.F(12)) * 0.5f, Ui.F(12), Ui.TextDim);
        var pr = Ui.R(x + Ui.P(44), y, Ui.P(90), rowH);
        bool hostEnter = _port.Update(pr, !busy);
        _port.Draw(pr, NetSession.DefaultPort.ToString(), "", 13, !busy);
        var hb = Ui.R(x + w - Ui.P(150), y, Ui.P(150), rowH);
        if (MenuUi.BigButton(hb, "Host Game", !busy, true, 13) || (hostEnter && !busy))
            Host(int.TryParse(_port.Text, out int port) && port is > 0 and < 65536 ? port : NetSession.DefaultPort);
        y += rowH + Ui.P(6);
        Ui.Text("Internet: forward this TCP port on your router.", x, y, Ui.F(10.5f), Ui.TextFaint);
        y += Ui.P(28);

        Ui.Text("JOIN BY ADDRESS", x, y, ls, Ui.TextFaint, true);
        y += Ui.P(16);
        var ar = Ui.R(x, y, w - Ui.P(160), rowH);
        bool joinEnter = _address.Update(ar, !busy);
        _address.Draw(ar, "ip or ip:port", "", 13, !busy);
        var jb = Ui.R(x + w - Ui.P(150), y, Ui.P(150), rowH);
        if (MenuUi.BigButton(jb, "Join", !busy && _address.Text.Trim().Length > 0, false, 13) || (joinEnter && !busy))
            Join(_address.Text.Trim());
        y += rowH + Ui.P(6);
        Ui.Text($"Default port {NetSession.DefaultPort}. Both players need the same game version.", x, y, Ui.F(10.5f), Ui.TextFaint);
    }

    private void DrawLan(Rectangle p, bool busy)
    {
        Ui.Panel(p);
        Ui.PanelTitle(p, "LAN games");
        float x = p.X + Ui.P(12), y = p.Y + Ui.P(34), w = p.Width - Ui.P(24), rowH = Ui.P(40);
        if (_lan == null)
        {
            Ui.Text("LAN search is not available on this machine.", x, y, Ui.F(12), Ui.TextDim);
            return;
        }
        var games = _lan.Games;
        if (games.Count == 0)
        {
            MenuUi.Spinner(new Vector2(x + Ui.P(10), y + Ui.P(9)), Ui.P(7), Ui.TextDim);
            Ui.Text("Searching the local network…", x + Ui.P(26), y + Ui.P(2), Ui.F(12), Ui.TextDim);
            return;
        }
        foreach (var g in games)
        {
            if (y + rowH > p.Y + p.Height - Ui.P(8)) break;
            var row = Ui.R(x, y, w, rowH);
            bool can = !busy && !g.InGame && g.SameBuild && g.Players < g.MaxPlayers;
            var st = Ui.Button(row, can);
            Ui.Text(Ui.Fit(g.Name, w * 0.55f, Ui.F(13), true), row.X + Ui.P(10), row.Y + Ui.P(5), Ui.F(13), can ? Ui.TextColor : Ui.TextDim, true);
            Ui.Text($"{g.Endpoint} · {g.Map}", row.X + Ui.P(10), row.Y + Ui.P(22), Ui.F(10.5f), Ui.TextFaint);
            string state = g.InGame ? "in game" : !g.SameBuild ? "other version" : $"{g.Players}/{g.MaxPlayers} players";
            Ui.TextRight(state, row.X + row.Width - Ui.P(10), row.Y + (rowH - Ui.F(12)) * 0.5f, Ui.F(12),
                g.InGame || !g.SameBuild ? Ui.Warn : Ui.Good, true);
            if (can && st.Left) Join(g.Endpoint);
            y += rowH + Ui.P(6);
        }
    }

    // ------------------------------------------------------------------ actions

    private void Remember()
    {
        var s = AppSettings.Current;
        s.PlayerName = _name.Text.Trim();
        if (_address.Text.Trim().Length > 0) s.JoinAddress = _address.Text.Trim();
        if (int.TryParse(_port.Text, out int port) && port is > 0 and < 65536) s.HostPort = port;
        s.Save();
    }

    private void Host(int port)
    {
        Remember();
        try
        {
            var host = HostSession.Start(PlayerName, port);
            host.FactionKnown = id => _app.Db.Factions.Get(id) != null;
            _app.Go(new LobbyScreen(_app, host, newHost: true));
        }
        catch (Exception e)
        {
            _error = $"Can't host on port {port}: {e.Message}";
        }
    }

    private void Join(string address)
    {
        if (!NetAddress.TryParse(address, out string host, out int port))
        {
            _error = $"Bad address '{address}' (use ip or ip:port)";
            return;
        }
        Remember();
        _error = null;
        string name = PlayerName;
        _joiningWhat = $"{host}:{port}";
        _joining = Task.Run(() => ClientSession.Join(host, port, name));
    }

    private void PollJoin()
    {
        if (_joining is not { IsCompleted: true } t) return;
        _joining = null;
        if (t.IsCompletedSuccessfully) _app.Go(new LobbyScreen(_app, t.Result));
        else
        {
            _error = t.Exception?.GetBaseException().Message ?? "Join failed";
            Console.WriteLine("multiplayer: " + _error);
        }
    }

    public void Dispose()
    {
        _lan?.Dispose();
        // A join still running: leave the session it returns.
        _joining?.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Leave("cancelled"); });
    }
}
