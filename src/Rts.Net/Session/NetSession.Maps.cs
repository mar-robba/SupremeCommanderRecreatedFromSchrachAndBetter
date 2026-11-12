using Rts.Sim.Map;

namespace Rts.Net.Session;

/// <summary>(8b-3) State of the lobby's map file on this peer. Append-only.</summary>
public enum MapTransferState : byte { Present, Downloading, Verifying, Failed }

/// <summary>(8b-3) Immutable snapshot of the local map file state (UI: progress line, Ready button).</summary>
public sealed record MapTransferStatus(string Id, string Name, ulong Hash, MapTransferState State, long Received, long Total,
    string? Path = null, string? Error = null)
{
    public int Percent => Total <= 0 ? 0 : (int)Math.Clamp(Received * 100 / Total, 0, 100);
    public bool Ready => State == MapTransferState.Present;
}

public abstract partial class NetSession
{
    /// <summary>Chunk size of a map upload.</summary>
    public const int MapChunkBytes = 32 * 1024;
    /// <summary>The host keeps at most this many bytes in flight beyond what the client acknowledged.</summary>
    public const int MapWindowBytes = 256 * 1024;
    /// <summary>Largest map file accepted from a host.</summary>
    public const long MaxMapBytes = 64L << 20;

    /// <summary>(8b-3) Where received maps are stored and looked up (besides data/maps). Tests use a temp folder.</summary>
    public string MapDir = MapLibrary.DownloadDir;

    private volatile MapTransferStatus? _mapTransfer;

    /// <summary>(8b-3) The lobby's map file on this peer (null = generated map). Any thread.</summary>
    public MapTransferStatus? MapTransfer { get => _mapTransfer; protected set => _mapTransfer = value; }

    /// <summary>(8b-3) Path of the lobby's map file on this peer (null = generated map or not here).</summary>
    public string? MapPath => MapTransfer is { Ready: true } t ? t.Path : null;

    /// <summary>A map id safe as a file name (it comes from the network).</summary>
    internal static string SafeMapId(string id)
    {
        var chars = id.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.').Take(64).ToArray();
        var s = new string(chars).Trim('.');
        return s.Length == 0 ? "map" : s;
    }
}
