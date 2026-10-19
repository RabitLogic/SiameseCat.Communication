using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace SiameseCat.Soak;

/// <summary>
/// TCP proxy that can sever the link on demand, which is how the soak run simulates a switch reboot,
/// a cable pull or a gateway that stops forwarding.
/// </summary>
/// <remarks>
/// A real outage is not "the server answers slowly": the connection dies and new connections are refused
/// for a while. The proxy reproduces exactly that, without touching the slave.
/// </remarks>
internal sealed class FaultProxy : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly int _targetPort;
    private readonly ConcurrentDictionary<TcpClient, byte> _connections = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;
    private volatile bool _faulted;
    private long _dropped;
    private long _rejected;

    /// <summary>Creates the proxy in front of <paramref name="targetPort"/>.</summary>
    /// <param name="targetPort">Port of the real slave.</param>
    public FaultProxy(int targetPort)
    {
        _targetPort = targetPort;
        _listener = new TcpListener(IPAddress.Loopback, 0);
    }

    /// <summary>Gets the port clients connect to.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Gets whether the link is currently down.</summary>
    public bool IsFaulted => _faulted;

    /// <summary>Gets how many established connections were dropped by a fault.</summary>
    public long DroppedConnections => Interlocked.Read(ref _dropped);

    /// <summary>Gets how many connection attempts were rejected while the link was down.</summary>
    public long RejectedConnections => Interlocked.Read(ref _rejected);

    /// <summary>Starts listening.</summary>
    public void Start()
    {
        _listener.Start();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
    }

    /// <summary>Turns the link down or back up.</summary>
    /// <param name="faulted"><see langword="true"/> to drop and refuse connections.</param>
    public void SetFault(bool faulted)
    {
        _faulted = faulted;
        if (!faulted)
        {
            return;
        }

        foreach (var connection in _connections.Keys)
        {
            try
            {
                connection.Dispose();
            }
            catch (SocketException)
            {
            }
        }

        _connections.Clear();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();

        foreach (var connection in _connections.Keys)
        {
            connection.Dispose();
        }

        _connections.Clear();

        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            if (_faulted)
            {
                Interlocked.Increment(ref _rejected);
                client.Dispose();
                continue;
            }

            _connections.TryAdd(client, 0);
            _ = PumpAsync(client, token);
        }
    }

    private async Task PumpAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            using (client)
            {
                using var upstream = new TcpClient();
                await upstream.ConnectAsync(IPAddress.Loopback, _targetPort, token).ConfigureAwait(false);

                var downstream = client.GetStream();
                var other = upstream.GetStream();

                var forward = CopyAsync(downstream, other, token);
                var backward = CopyAsync(other, downstream, token);
                await Task.WhenAny(forward, backward).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            // A dropped or refused connection is the point of this proxy.
        }
        finally
        {
            _connections.TryRemove(client, out _);
            Interlocked.Increment(ref _dropped);
        }
    }

    private static async Task CopyAsync(NetworkStream from, NetworkStream to, CancellationToken token)
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var read = await from.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                await to.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Either side went away; the other direction ends with it.
        }
    }
}
