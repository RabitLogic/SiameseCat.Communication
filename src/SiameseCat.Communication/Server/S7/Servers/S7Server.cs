using System.Net;
using System.Net.Sockets;

using SiameseCat.Communication;

namespace SiameseCat.Communication.Server.S7;

/// <summary>
/// S7 (ISO-on-TCP) slave that exposes an in-process <see cref="S7DataStore"/>.
/// Use it for integration tests, demos and offline development when no real device is available.
/// </summary>
/// <remarks>
/// <para>Handles the COTP connection handshake, Setup Communication and read/write variable services.</para>
/// <para>Supported areas: M, I, Q and data blocks; data blocks are created on demand by default.</para>
/// <para>All connections are served concurrently, one task per client.</para>
/// </remarks>
public sealed class S7Server : IS7Server
{
    private const byte TpktVersion = 0x03;
    private const byte CotpConnectionRequest = 0xE0;
    private const byte CotpConnectionConfirm = 0xD0;
    private const byte CotpDataTransfer = 0xF0;

    private readonly S7ServerOptions _options;
    private readonly S7PduProcessor _processor;
    private readonly TcpListener _listener;
    private readonly List<TcpClient> _clients = new();
    private readonly object _clientSync = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private int _port;
    private bool _disposed;

    /// <summary>Creates the server. Call <see cref="StartAsync"/> to open the listener.</summary>
    /// <param name="options">Endpoint and memory settings; defaults are used when omitted.</param>
    public S7Server(S7ServerOptions? options = null)
    {
        _options = options ?? new S7ServerOptions();
        _options.Validate();
        Data = new S7DataStore(
            _options.MemorySize,
            _options.InputSize,
            _options.OutputSize,
            _options.DefaultDataBlockSize,
            _options.AutoCreateDataBlocks);
        Statistics = new S7ServerStatistics();
        _processor = new S7PduProcessor(Data, Statistics, _options.PduSize, _options.Logger);
        _listener = new TcpListener(ResolveAddress(_options.Host), _options.Port);
    }

    /// <summary>Gets the memory areas and data blocks that the slave exposes.</summary>
    public S7DataStore Data { get; }

    /// <summary>Gets the request and frame counters of the slave.</summary>
    public S7ServerStatistics Statistics { get; }

    /// <summary>Gets the port the listener is bound to. Useful when <see cref="S7ServerOptions.Port"/> is 0.</summary>
    public int Port => _port;

    /// <summary>Gets whether the listener is currently accepting connections.</summary>
    public bool IsRunning => _acceptTask is { IsCompleted: false };

    /// <summary>Starts listening for ISO-on-TCP clients.</summary>
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
        GC.SuppressFinalize(this);
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
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or InvalidOperationException)
            {
                return;
            }

            lock (_clientSync)
            {
                _clients.Add(client);
            }

            Statistics.RecordConnection();
            _options.Logger?.Log(CommunicationLogLevel.Debug, $"S7 client connected: {client.Client.RemoteEndPoint}.");
            _ = Task.Run(() => ServeAsync(client, token), CancellationToken.None);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        var header = new byte[4];
        var body = new byte[4096];
        var frame = new byte[4096];

        try
        {
            using (client)
            {
                var stream = client.GetStream();

                while (!token.IsCancellationRequested)
                {
                    if (!await ReadExactlyAsync(stream, header, token).ConfigureAwait(false))
                    {
                        return;
                    }

                    if (header[0] != TpktVersion)
                    {
                        Statistics.RecordDiscardedFrame();
                        _options.Logger?.Warning($"S7 slave dropped a TPKT frame with version 0x{header[0]:X2}.");
                        return;
                    }

                    var length = (header[2] << 8) | header[3];
                    if (length is < 7 or > 4096)
                    {
                        Statistics.RecordDiscardedFrame();
                        _options.Logger?.Warning($"S7 slave dropped a TPKT frame with length {length}.");
                        return;
                    }

                    if (!await ReadExactlyAsync(
                        stream,
                        body.AsMemory(0, length - 4),
                        token).ConfigureAwait(false))
                    {
                        Statistics.RecordDiscardedFrame();
                        return;
                    }

                    var payloadLength = HandleBody(body.AsSpan(0, length - 4), frame);
                    if (payloadLength == 0)
                    {
                        continue;
                    }

                    var total = 4 + payloadLength;
                    frame[0] = TpktVersion;
                    frame[1] = 0x00;
                    frame[2] = (byte)(total >> 8);
                    frame[3] = (byte)total;

                    await stream.WriteAsync(frame.AsMemory(0, total), token).ConfigureAwait(false);
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

            Statistics.RecordDisconnection();
            _options.Logger?.Log(CommunicationLogLevel.Debug, "S7 client disconnected.");
        }
    }

    private int HandleBody(ReadOnlySpan<byte> body, Span<byte> frame)
    {
        if (body.Length < 2)
        {
            Statistics.RecordDiscardedFrame();
            return 0;
        }

        if (body[1] == CotpConnectionRequest)
        {
            ConnectionConfirm.CopyTo(frame.Slice(4));
            return ConnectionConfirm.Length;
        }

        if (body[1] != CotpDataTransfer || body.Length < 3)
        {
            Statistics.RecordDiscardedFrame();
            _options.Logger?.Warning($"S7 slave dropped a COTP frame of type 0x{body[1]:X2}.");
            return 0;
        }

        frame[4] = 0x02;
        frame[5] = CotpDataTransfer;
        frame[6] = 0x80;

        var written = _processor.Process(body.Slice(3), frame.Slice(7));
        return written == 0 ? 0 : 3 + written;
    }

    private static readonly byte[] ConnectionConfirm =
    {
        0x11, CotpConnectionConfirm, 0x00, 0x01, 0x00, 0x01, 0x00,
        0xC0, 0x01, 0x0A,
        0xC1, 0x02, 0x01, 0x00,
        0xC2, 0x02, 0x01, 0x01,
    };

    private static async Task<bool> ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken token) =>
        await ReadExactlyAsync(stream, buffer.AsMemory(), token).ConfigureAwait(false);

    private static async Task<bool> ReadExactlyAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.Slice(offset), token).ConfigureAwait(false);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static IPAddress ResolveAddress(string host) =>
        host switch
        {
            "0.0.0.0" or "*" => IPAddress.Any,
            "::" => IPAddress.IPv6Any,
            _ => IPAddress.TryParse(host, out var address) ? address : ResolveHost(host),
        };

    private static IPAddress ResolveHost(string host)
    {
        var addresses = Dns.GetHostAddresses(host);
        foreach (var address in addresses)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return address;
            }
        }

        if (addresses.Length == 0)
        {
            throw new ArgumentException($"Host '{host}' could not be resolved.", nameof(host));
        }

        return addresses[0];
    }
}
