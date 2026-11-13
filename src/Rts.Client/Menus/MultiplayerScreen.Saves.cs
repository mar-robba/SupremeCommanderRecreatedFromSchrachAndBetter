using Raylib_cs;
using Rts.Client.UI;
using Rts.Net.Session;
using Rts.Sim.Persistence;

namespace Rts.Client.Menus;

/// <summary>(10a-3) Multiplayer → "Load Saved Game": the host picks a multiplayer save (<see cref="NetLoadListScreen"/>)
/// and opens a load lobby on it; <c>--host [port] --load-mp FILE</c> does the same from the command line.</summary>
public sealed partial class MultiplayerScreen
{
    private void DrawLoadButton(Rectangle r, bool busy)
    {
        if (MenuUi.BigButton(r, "Load Saved Game", !busy, false, 11))
        {
            Remember();
            int port = int.TryParse(_port.Text, out int p) && p is > 0 and < 65536 ? p : NetSession.DefaultPort;
            _app.Go(new NetLoadListScreen(_app, PlayerName, port));
        }
    }

    /// <summary>The host's Autosave setting (--autosave-every TICKS forces it on with that interval, for tests).</summary>
    private static void ApplyAutosave(AppHost app, HostSession host)
    {
        host.Autosave = AppSettings.Current.Autosave || app.Options.AutosaveEvery > 0;
        if (app.Options.AutosaveEvery > 0) host.AutosaveIntervalTicks = app.Options.AutosaveEvery;
    }

    /// <summary>A path, or a save name of the saves folder (with or without the extension).</summary>
    private static string ResolveMpSave(string arg)
    {
        if (File.Exists(arg)) return Path.GetFullPath(arg);
        string name = arg.EndsWith(SaveStore.SaveExt, StringComparison.OrdinalIgnoreCase) ? arg[..^SaveStore.SaveExt.Length] : arg;
        return SaveStore.SavePath(name);
    }

    private void HostLoad(int port, string path)
    {
        Remember();
        var lobby = HostLoadLobby(_app, PlayerName, port, path, out string? error);
        if (lobby != null) _app.Go(lobby);
        else _error = error;
    }

    /// <summary>Hosts on <paramref name="port"/> and opens the load lobby of <paramref name="path"/>; null + an error
    /// text if the port is taken or the save can't be loaded (wrong version, map file missing, unreadable).</summary>
    public static LobbyScreen? HostLoadLobby(AppHost app, string name, int port, string path, out string? error)
    {
        HostSession host;
        try { host = HostSession.Start(name, port); }
        catch (Exception e)
        {
            error = $"Can't host on port {port}: {e.Message}";
            return null;
        }
        try
        {
            host.FactionKnown = id => app.Db.Factions.Get(id) != null;
            ApplyAutosave(app, host);
            host.OpenLoadLobby(path);
            error = null;
            return new LobbyScreen(app, host);
        }
        catch (Exception e)
        {
            host.Leave();
            error = "Can't load this save: " + Persistence.SaveText.ErrorMessage(e);
            return null;
        }
    }
}
