using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Rts.Net.Wire;
using Rts.Sim.Persistence;

namespace Rts.Net.Transport;

/// <summary>
/// One TCP connection: a receive thread (frames → <c>onMessage</c>, on that thread) and a send thread (queued frames,
/// so callers - e.g. the sim thread sending its turn - never block on the socket). Ping/Pong are answered here and
/// give <see cref="RttMs"/>. <c>onClosed</c> runs once, on whichever thread noticed the close.
/// </summary>
public sealed class NetConnection : IDisposable
{
    private readonly Socket _socket;
    private readonly BlockingCollection<byte[]> _out = new(new ConcurrentQueue<byte[]>());
    private readonly Thread _recv, _send;
    private readonly Action<NetConnection, MsgType, ByteReader> _onMessage;
    private readonly Action<NetConnection, string> _onClosed;
    private int _closed;
    private long _lastReceived = Stopwatch.GetTimestamp();

    /// <summary>Player id (host side: assigned at Welcome; -1 before).</summary>
    public int PlayerId = -1;
    public string Name = "";
    /// <summary>Smoothed round-trip time in ms (-1 until the first pong).</summary>
    public volatile float RttMs = -1;
    public string? CloseReason { get; private set; }
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public EndPoint? Remote { get; }
    public long BytesSent, BytesReceived;

    /// <summary>Seconds since the last received byte.</summary>
    public double SilentSeconds => Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastReceived)).TotalSeconds;

    public NetConnection(Socket socket, Action<NetConnection, MsgType, ByteReader> onMessage, Action<NetConnection, string> onClosed, string threadName = "Net")
    {
        _socket = socket;
        _socket.NoDelay = true;
        Remote = socket.RemoteEndPoint;
        _onMessage = onMessage;
        _onClosed = onClosed;
        _recv = new Thread(RecvLoop) { Name = threadName + " recv", IsBackground = true };
        _send = new Thread(SendLoop) { Name = threadName + " send", IsBackground = true };
        _recv.Start();
        _send.Start();
    }

    /// <summary>Connects to host:port (throws on failure/timeout).</summary>
    public static Socket Connect(string host, int port, int timeoutMs = 5000)
    {
        var s = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            var task = s.ConnectAsync(host, port);
            if (!task.Wait(timeoutMs)) throw new TimeoutException($"No answer from {host}:{port}");
            return s;
        }
        catch (AggregateException e) when (e.InnerException != null)
        {
            s.Dispose();
            throw e.InnerException;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    public void Send(byte[] frame)
    {
        if (IsClosed) return;
        try { _out.Add(frame); }
        catch (InvalidOperationException) { } // closing
    }

    public void SendPing() => Send(Msg.Build(MsgType.Ping, w => w.Raw(Stopwatch.GetTimestamp())));

    /// <summary>Closes the connection; <paramref name="finalFrame"/> (e.g. Leave/Reject) is sent after the queued frames.</summary>
    public void Close(string reason, byte[]? finalFrame = null)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        CloseReason = reason;
        if (finalFrame != null) _out.Add(finalFrame);
        _out.CompleteAdding(); // the send thread drains the queue, then shuts the socket down
        try { _onClosed(this, reason); }
        catch { /* never let a handler kill the network thread */ }
    }

    public void Dispose()
    {
        Close("closed");
        if (Thread.CurrentThread != _send) _send.Join(1000);
        try { _socket.Close(); } catch { }
        if (Thread.CurrentThread != _recv) _recv.Join(1000);
    }

    private void RecvLoop()
    {
        var buf = new byte[16384];
        var decoder = new FrameDecoder();
        var payloads = new List<byte[]>();
        string reason = "connection closed";
        try
        {
            while (true)
            {
                int n = _socket.Receive(buf);
                if (n == 0) break;
                Interlocked.Exchange(ref _lastReceived, Stopwatch.GetTimestamp());
                BytesReceived += n;
                decoder.Push(buf.AsSpan(0, n), payloads);
                foreach (var p in payloads)
                {
                    if (IsClosed) return;
                    Dispatch(p);
                }
                payloads.Clear();
            }
        }
        catch (SocketException e) { reason = e.SocketErrorCode == SocketError.ConnectionReset ? "connection lost" : e.Message; }
        catch (ObjectDisposedException) { }
        catch (Exception e) { reason = "protocol error: " + e.Message; }
        Close(reason);
    }

    private void Dispatch(byte[] payload)
    {
        var type = (MsgType)payload[0];
        var r = new ByteReader(payload, 1, payload.Length - 1);
        switch (type)
        {
            case MsgType.Ping:
                Send(Msg.Build(MsgType.Pong, w => w.Raw(r.Raw<long>())));
                return;
            case MsgType.Pong:
            {
                double ms = Stopwatch.GetElapsedTime(r.Raw<long>()).TotalMilliseconds;
                float prev = RttMs;
                RttMs = prev < 0 ? (float)ms : prev * 0.7f + (float)ms * 0.3f;
                return;
            }
            default:
                _onMessage(this, type, r);
                return;
        }
    }

    private void SendLoop()
    {
        try
        {
            foreach (var frame in _out.GetConsumingEnumerable())
            {
                int off = 0;
                while (off < frame.Length) off += _socket.Send(frame, off, frame.Length - off, SocketFlags.None);
                BytesSent += frame.Length;
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            Close("connection lost");
        }
        try { _socket.Shutdown(SocketShutdown.Both); } catch { }
        try { _socket.Close(); } catch { }
    }
}
