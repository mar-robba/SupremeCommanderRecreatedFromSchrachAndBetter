using System.Buffers.Binary;

namespace Rts.Net.Wire;

/// <summary>
/// Length-framed binary protocol over TCP: every message = [u32 little-endian payload length][payload], payload[0] =
/// <see cref="MsgType"/>. <see cref="FrameDecoder"/> reassembles frames from arbitrary read chunks (partial frames,
/// several frames per read).
/// </summary>
public static class Frame
{
    public const int HeaderSize = 4;
    /// <summary>Largest accepted payload (lobby/turn messages are tiny; this only bounds damaged/hostile input).</summary>
    public const int MaxPayload = 4 << 20;

    /// <summary>A complete frame (header + type byte + body), ready to send.</summary>
    public static byte[] Make(MsgType type, ReadOnlySpan<byte> body)
    {
        var f = new byte[HeaderSize + 1 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(f, 1 + body.Length);
        f[HeaderSize] = (byte)type;
        body.CopyTo(f.AsSpan(HeaderSize + 1));
        return f;
    }
}

/// <summary>Thrown for malformed frames/messages: the connection is closed.</summary>
public sealed class ProtocolException(string message) : Exception(message);

/// <summary>Accumulates received bytes and cuts them into frame payloads (type byte + body). Not thread-safe (one per connection).</summary>
public sealed class FrameDecoder
{
    private byte[] _buf = new byte[4096];
    private int _start, _end;

    /// <summary>Bytes received but not yet part of a complete frame.</summary>
    public int Buffered => _end - _start;

    /// <summary>Appends <paramref name="data"/> and adds every completed payload to <paramref name="payloads"/>.</summary>
    public void Push(ReadOnlySpan<byte> data, List<byte[]> payloads)
    {
        Append(data);
        while (_end - _start >= Frame.HeaderSize)
        {
            int n = BinaryPrimitives.ReadInt32LittleEndian(_buf.AsSpan(_start));
            if (n < 1 || n > Frame.MaxPayload) throw new ProtocolException($"bad frame length {n}");
            if (_end - _start < Frame.HeaderSize + n) break;
            payloads.Add(_buf.AsSpan(_start + Frame.HeaderSize, n).ToArray());
            _start += Frame.HeaderSize + n;
        }
        if (_start == _end) _start = _end = 0;
    }

    private void Append(ReadOnlySpan<byte> data)
    {
        if (_end + data.Length > _buf.Length)
        {
            int live = _end - _start;
            if (live + data.Length > _buf.Length)
            {
                var nb = new byte[Math.Max(_buf.Length * 2, live + data.Length)];
                _buf.AsSpan(_start, live).CopyTo(nb);
                _buf = nb;
            }
            else _buf.AsSpan(_start, live).CopyTo(_buf);
            _start = 0;
            _end = live;
        }
        data.CopyTo(_buf.AsSpan(_end));
        _end += data.Length;
    }
}
