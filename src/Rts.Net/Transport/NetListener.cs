using System.Net;
using System.Net.Sockets;

namespace Rts.Net.Transport;

/// <summary>TCP accept loop on its own thread: every accepted socket goes to <c>onAccept</c> (on that thread).</summary>
public sealed class NetListener : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Thread _thread;
    private readonly Action<Socket> _onAccept;
    private volatile bool _stopped;

    /// <summary>The bound port (useful with port 0 = any free port, e.g. tests).</summary>
    public int Port { get; }

    /// <param name="port">TCP port (0 = pick a free one). Binds every interface (IPv4; IPv6 too when available).</param>
    public NetListener(int port, Action<Socket> onAccept)
    {
        _onAccept = onAccept;
        _listener = new TcpListener(IPAddress.IPv6Any, port);
        try
        {
            _listener.Server.DualMode = true;
            _listener.Start();
        }
        catch (Exception e) when (e is SocketException or NotSupportedException)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
        }
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _thread = new Thread(Loop) { Name = "Net accept", IsBackground = true };
        _thread.Start();
    }

    private void Loop()
    {
        while (!_stopped)
        {
            Socket s;
            try { s = _listener.AcceptSocket(); }
            catch (SocketException) { if (_stopped) return; continue; }
            catch (ObjectDisposedException) { return; }
            catch (InvalidOperationException) { return; }
            try { _onAccept(s); }
            catch { try { s.Dispose(); } catch { } }
        }
    }

    public void Dispose()
    {
        _stopped = true;
        try { _listener.Stop(); } catch { }
        _thread.Join(1000);
    }
}
