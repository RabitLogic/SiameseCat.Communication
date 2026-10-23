using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SiameseCat.Communication.Tcp;

namespace SiameseCat.Communication.Server.Tcp;

/// <summary>Handles one accepted custom TCP connection until it closes or the server stops.</summary>
/// <param name="connection">Connection to read from and write to.</param>
/// <param name="cancellationToken">Cancelled when the server stops.</param>
public delegate Task CustomTcpConnectionHandler(
    CustomTcpConnection connection,
    CancellationToken cancellationToken);

/// <summary>
/// TCP server for a vendor-specific or in-house protocol. The handler reads and writes whole frames,
/// so request/response and push protocols share the same code path. Use it for simulators,
/// integration tests and protocol experiments.
/// </summary>
/// <remarks>
/// Every connection runs in its own task. Framing is shared by all connections; the codec must be
/// stateless.
/// </remarks>
public sealed class CustomTcpServer : IAsyncDisposable
{
    private static readonly Action<ILogger, EndPoint?, Exception?> LogConnected =
        LoggerMessage.Define<EndPoint?>(
            LogLevel.Debug,
            new EventId(101, "TcpConnected"),
            "Custom TCP client connected: {Endpoint}.");

    private static readonly Action<ILogger, EndPoint?, int, Exception?> LogRejected =
        LoggerMessage.Define<EndPoint?, int>(
            LogLevel.Warning,
            new EventId(102, "TcpRejected"),
            "Custom TCP server rejected {Endpoint}: {MaxConnections} connections are already open.");

    private static readonly Action<ILogger, string, Exception?> LogDisconnectedWithReason =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(103, "TcpDisconnected"),
            "Custom TCP client disconnected: {Reason}.");

    private static readonly Action<ILogger, Exception?> LogDisconnected =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(104, "TcpDisconnected"),
            "Custom TCP client disconnected.");

    private static readonly Action<ILogger, Exception?> LogHandlerFault =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(105, "TcpHandlerFault"),
            "The custom TCP connection handler failed.");

    private readonly CustomTcpServerOptions _options;
    private readonly CustomTcpConnectionHandler _handler;
    private readonly ITcpFrameCodec _codec;
    private readonly TcpListener _listener;
    private readonly List<TcpClient> _clients = new();
    private readonly object _clientSync = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private int _port;
    private bool _disposed;

    /// <summary>Creates the server. Call <see cref="StartAsync"/> to open the listener.</summary>
    /// <param name="handler">Handler invoked once per accepted connection.</param>
    /// <param name="options">Endpoint and framing settings; defaults are used when omitted.</param>
    public CustomTcpServer(CustomTcpConnectionHandler handler, CustomTcpServerOptions? options = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? new CustomTcpServerOptions();
        _options.Validate();
        _codec = _options.Codec ?? new TcpFrameCodec(_options.Framing);
        Statistics = new CustomTcpServerStatistics();
        _listener = new TcpListener(ResolveAddress(_options.Host), _options.Port);
    }

    /// <summary>Gets the connection and frame counters of the server.</summary>
    public CustomTcpServerStatistics Statistics { get; }

    /// <summary>Gets the port the listener is bound to. Useful when <see cref="CustomTcpServerOptions.Port"/> is 0.</summary>
    public int Port => _port;

    /// <summary>Gets whether the listener is currently accepting connections.</summary>
    public bool IsRunning => _acceptTask is { IsCompleted: false };

    /// <summary>Starts listening for clients.</summary>
    /// <param name="cancellationToken">Cancels the accept loop.</param>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Guard.NotDisposed(_disposed, this);

        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptTask = AcceptLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>Stops listening and closes every connected client.</summary>
    public async Task StopAsync()
    {
        var acceptTask = _acceptTask;
        _acceptTask = null;

        _cts?.Cancel();
        _listener.Stop();

        lock (_clientSync)
        {
            foreach (var client in _clients)
            {
                try
                {
                    client.Close();
                }
                catch (ObjectDisposedException)
                {
                    // Already closed.
                }
            }

            _clients.Clear();
        }

        if (acceptTask is not null)
        {
            try
            {
                await acceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
#if NET8_0_OR_GREATER
                client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
#else
                client = await _listener.AcceptTcpClientAsync().WaitAsync(token).ConfigureAwait(false);
#endif
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
            catch (InvalidOperationException)
            {
                return;
            }

            lock (_clientSync)
            {
                if (_options.MaxConnections > 0 && _clients.Count >= _options.MaxConnections)
                {
                    if (_options.Logger is not null)
                    {
                        LogRejected(_options.Logger, client.Client.RemoteEndPoint, _options.MaxConnections, null);
                    }

                    client.Dispose();
                    continue;
                }

                _clients.Add(client);
            }

            Statistics.RecordConnectionAccepted();
            if (_options.Logger is not null)
            {
                LogConnected(_options.Logger, client.Client.RemoteEndPoint, null);
            }

            var connection = new CustomTcpConnection(
                client.GetStream(),
                client.Client.RemoteEndPoint,
                _codec,
                Statistics,
                _options.Logger,
                _options.LogFrames);

            _ = Task.Run(() => ServeAsync(client, connection, token), CancellationToken.None);
        }
    }

    private async Task ServeAsync(TcpClient client, CustomTcpConnection connection, CancellationToken token)
    {
        try
        {
            using (client)
            {
                await _handler(connection, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Server is stopping.
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or ConnectionClosedException)
        {
            if (_options.Logger is not null)
            {
                LogDisconnectedWithReason(_options.Logger, ex.Message, null);
            }
        }
        catch (Exception ex)
        {
            Statistics.RecordHandlerFault();
            if (_options.Logger is not null)
            {
                LogHandlerFault(_options.Logger, ex);
            }
        }
        finally
        {
            lock (_clientSync)
            {
                _clients.Remove(client);
            }

            if (_options.Logger is not null)
            {
                LogDisconnected(_options.Logger, null);
            }
        }
    }

    private static IPAddress ResolveAddress(string host)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return address;
        }

        var addresses = Dns.GetHostAddresses(host);
        if (addresses.Length == 0)
        {
            throw new ArgumentException($"Host '{host}' could not be resolved.", nameof(host));
        }

        return addresses[0];
    }
}
