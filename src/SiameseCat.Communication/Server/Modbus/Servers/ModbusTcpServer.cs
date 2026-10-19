using System.Net;
using System.Net.Sockets;
using SiameseCat.Communication;

namespace SiameseCat.Communication.Server.Modbus;

/// <summary>
/// Modbus TCP slave that exposes an in-process <see cref="ModbusDataStore"/>.
/// Use it for integration tests, demos and offline development when no real PLC is available.
/// </summary>
/// <remarks>
/// <para>All requests are served concurrently, one task per client connection.</para>
/// <para>The unit identifier of a request is echoed back, so one server can answer several station ids.</para>
/// <para>Unit id 0 is treated as a broadcast: writes are applied and no response is sent.</para>
/// </remarks>
public sealed class ModbusTcpServer : IModbusServer
{
    private readonly ModbusServerOptions _options;
    private readonly ModbusPduProcessor _processor;
    private readonly TcpListener _listener;
    private readonly List<TcpClient> _clients = new();
    private readonly object _clientSync = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private int _port;
    private bool _disposed;

    /// <summary>Creates the server. Call <see cref="StartAsync"/> to open the listener.</summary>
    /// <param name="options">Endpoint and buffer settings; defaults are used when omitted.</param>
    public ModbusTcpServer(ModbusServerOptions? options = null)
    {
        _options = options ?? new ModbusServerOptions();
        _options.Validate();
        Data = new ModbusDataStore(
            _options.HoldingRegisters,
            _options.InputRegisters,
            _options.Coils,
            _options.DiscreteInputs);
        Statistics = new ModbusServerStatistics();
        _processor = new ModbusPduProcessor(
            Data,
            Statistics,
            _options.ServerIdentifier,
            _options.RunIndicator,
            _options.Logger,
            _options.DeviceIdentification);
        _listener = new TcpListener(ResolveAddress(_options.Host), _options.Port);
    }

    /// <summary>Gets the register and coil storage that the slave exposes.</summary>
    public ModbusDataStore Data { get; }

    /// <summary>Gets the request and frame counters of the slave.</summary>
    public ModbusServerStatistics Statistics { get; }

    /// <summary>Gets the port the listener is bound to. Useful when <see cref="ModbusServerOptions.Port"/> is 0.</summary>
    public int Port => _port;

    /// <summary>Gets whether the listener is currently accepting connections.</summary>
    public bool IsRunning => _acceptTask is { IsCompleted: false };

    /// <summary>Starts listening for Modbus TCP clients.</summary>
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
                _clients.Add(client);
            }

            _options.Logger?.Log(PlcLogLevel.Debug, $"Modbus TCP client connected: {client.Client.RemoteEndPoint}.");
            _ = Task.Run(() => ServeAsync(client, token), CancellationToken.None);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        var header = new byte[7];
        var request = new byte[254];
        var response = new byte[7 + 254];

        try
        {
            using (client)
            {
                var stream = client.GetStream();

                while (!token.IsCancellationRequested)
                {
                    if (!await ReadExactlyAsync(stream, header, header.Length, token).ConfigureAwait(false))
                    {
                        return;
                    }

                    var length = (header[4] << 8) | header[5];
                    var pduLength = length - 1;
                    if (pduLength is < 1 or > 253)
                    {
                        Statistics.RecordDiscardedFrame();
                        _options.Logger?.Warning($"Modbus TCP slave dropped a frame with invalid length {length}.");
                        return;
                    }

                    if (!await ReadExactlyAsync(stream, request, pduLength, token).ConfigureAwait(false))
                    {
                        Statistics.RecordDiscardedFrame();
                        return;
                    }

                    Statistics.RecordFrame();

                    var written = _processor.Process(
                        header[6],
                        request.AsSpan(0, pduLength),
                        response.AsSpan(7));
                    if (written == 0)
                    {
                        continue;
                    }

                    response[0] = header[0];
                    response[1] = header[1];
                    response[4] = (byte)((written + 1) >> 8);
                    response[5] = (byte)(written + 1);
                    response[6] = header[6];

                    await WriteAsync(stream, response, 7 + written, token).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            // Client went away or the server is stopping.
        }
        finally
        {
            lock (_clientSync)
            {
                _clients.Remove(client);
            }

            _options.Logger?.Log(PlcLogLevel.Debug, "Modbus TCP client disconnected.");
        }
    }

    private static async Task<bool> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken token)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = await ReadAsync(stream, buffer, offset, count - offset, token).ConfigureAwait(false);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static Task WriteAsync(NetworkStream stream, byte[] buffer, int length, CancellationToken token)
    {
#if NET8_0_OR_GREATER
        return stream.WriteAsync(buffer.AsMemory(0, length), token).AsTask();
#else
        return stream.WriteAsync(buffer, 0, length, token);
#endif
    }

    private static ValueTask<int> ReadAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken token)
    {
#if NET8_0_OR_GREATER
        return stream.ReadAsync(buffer.AsMemory(offset, count), token);
#else
        return new ValueTask<int>(stream.ReadAsync(buffer, offset, count, token));
#endif
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
