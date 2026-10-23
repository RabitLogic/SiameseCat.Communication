using System.Net;
using System.Net.Sockets;

using SiameseCat.Communication.Diagnostics;
using SiameseCat.Communication.Exceptions;
using SiameseCat.Communication.Transport.Tcp;

namespace SiameseCat.Communication.Simulation.Tcp;

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
public sealed class CustomTcpSimulator : IAsyncDisposable
{
    private readonly CustomTcpSimulatorOptions _options;
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
    public CustomTcpSimulator(CustomTcpConnectionHandler handler, CustomTcpSimulatorOptions? options = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? new CustomTcpSimulatorOptions();
        _options.Validate();
        _codec = _options.Codec ?? new TcpFrameCodec(_options.Framing);
        Statistics = new CustomTcpSimulatorStatistics();
        _listener = new TcpListener(ResolveAddress(_options.Host), _options.Port);
    }

    /// <summary>Gets the connection and frame counters of the server.</summary>
    public CustomTcpSimulatorStatistics Statistics { get; }

    /// <summary>Gets the port the listener is bound to. Useful when <see cref="CustomTcpSimulatorOptions.Port"/> is 0.</summary>
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
                    _options.Logger?.Warning(
                        $"Custom TCP server rejected {client.Client.RemoteEndPoint}: {_options.MaxConnections} connections are already open.");

                    client.Dispose();
                    continue;
                }

                _clients.Add(client);
            }

            Statistics.RecordConnectionAccepted();
            _options.Logger?.Debug($"Custom TCP client connected: {client.Client.RemoteEndPoint}.");

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
            // Simulator is stopping.
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or ConnectionClosedException)
        {
            _options.Logger?.Debug($"Custom TCP client disconnected: {ex.Message}.");
        }
        catch (Exception ex)
        {
            Statistics.RecordHandlerFault();
            _options.Logger?.Error("The custom TCP connection handler failed.", ex);
        }
        finally
        {
            lock (_clientSync)
            {
                _clients.Remove(client);
            }

            _options.Logger?.Debug("Custom TCP client disconnected.");
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
