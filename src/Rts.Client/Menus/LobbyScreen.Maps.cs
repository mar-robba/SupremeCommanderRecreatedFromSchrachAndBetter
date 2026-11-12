using Rts.Client.UI;
using Rts.Net.Lobby;
using Rts.Net.Session;
using Rts.Sim.Map;

namespace Rts.Client.Menus;

/// <summary>(8b-3) Lobby map file + biome: the host cycles Random | installed map files and the biome, clients see the
/// download progress (<see cref="NetSession.MapTransfer"/>); Ready is disabled until the file is present.</summary>
public sealed partial class LobbyScreen
{
    private MapTransferState? _lastMapState;

    /// <summary>Preview: the map file once it is here, else the generated map of the options.</summary>
    private void RequestPreview(LobbyState lobby)
    {
        var t = _s.MapTransfer;
        if (t?.State != _lastMapState)
        {
            if (t?.State == MapTransferState.Present && _lastMapState != null && !_s.IsHost) MapFileCache.Invalidate();
            _lastMapState = t?.State;
        }
        var o = lobby.Options;
        if (!o.HasMapFile) _preview.Request(NetGameConfig.From(lobby, _s.PlayerId).MapSettings());
        else if (_s.MapPath is string path) _preview.RequestFile(path, Math.Clamp(lobby.ArmyCount, 2, Math.Max(2, o.MapPlayers)));
    }

    private bool MapPresent(LobbyState l) => !l.Options.HasMapFile || _s.MapTransfer?.Ready == true;

    /// <summary>Download line (null = nothing to say).</summary>
    private string? MapStatusLine(LobbyState l)
    {
        if (!l.Options.HasMapFile) return null;
        var t = _s.MapTransfer;
        return t?.State switch
        {
            null => "Checking the map file…",
            MapTransferState.Downloading => $"Downloading map {t.Percent} % ({(t.Received + 1023) / 1024}/{(t.Total + 1023) / 1024} KB)",
            MapTransferState.Verifying => "Verifying the map file…",
            MapTransferState.Failed => "Map download failed: " + t.Error,
            _ => null,
        };
    }

    private static string BiomeLabel(LobbyOptions o) =>
        o.Biome == 0 ? $"Map default ({Biomes.Name(o.EffectiveBiome)})" : Biomes.Name(o.EffectiveBiome);

    /// <summary>Host rows "Map" and "Biome" (edits <paramref name="n"/>); returns the next row's y.</summary>
    private float DrawMapRows(LobbyOptions n, LobbyOptions o, float lx, float cx, float cw, float y, float rowH, float gap)
    {
        MenuUi.Label("Map", lx, y, rowH);
        string label = o.HasMapFile ? $"{o.MapName} · {o.MapPlayers} players" : "Random map (generated)";
        int md = MenuUi.Cycle(Ui.R(cx, y, cw, rowH), label, true);
        if (md != 0)
        {
            n.MapFile = MapFileCache.Next(o.HasMapFile ? o.MapFile : null, md) ?? "";
            n.MapHash = 0; // the host resolves the file
        }
        y += rowH + gap;
        MenuUi.Label("Biome", lx, y, rowH);
        int bd = MenuUi.Cycle(Ui.R(cx, y, cw, rowH), BiomeLabel(o), true);
        if (bd != 0) n.Biome = (byte)(((o.Biome + bd) % (Biomes.All.Length + 1) + Biomes.All.Length + 1) % (Biomes.All.Length + 1));
        return y + rowH + gap;
    }
}
