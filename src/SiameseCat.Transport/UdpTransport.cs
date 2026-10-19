using System.Net.Sockets;
using SiameseCat.Core;

namespace SiameseCat.Transport;

/// <summary>
/// Connected UDP socket transport.
/// </summary>
/// <remarks>
/// The socket is connected to a single remote endpoint, so callers use the same request/response
/// contract as <see cref="TcpTransport"/>. Datagrams are not retransmitted: protocol timeouts and
/// the driver retry policy handle loss.
/// </remarks>
public sealed class UdpTransport : ITransport
{
    private readonly UdpTransportOptions _options;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private Socket? _socket;
    private volatile bool _isOpen;
    private volatile bool _disposed;

    /// <summary>Creates the transport.</summary>
    /// <param name="options">Remote endpoint and timeout settings.</param>
    public UdpTransport(UdpTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc/>
    public bool IsOpen => _isOpen;

    /// <inheritdoc/>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_isOpen)
            {
                return;
            }

            var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ConnectTimeout);

            try
            {
                await socket.ConnectAsync(_options.Host, _options.Port, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                socket.Dispose();
                throw new CommunicationTimeoutException(
                    $"Resolving {_options.Host}:{_options.Port} timed out after {_options.ConnectTimeout}.");
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                socket.Dispose();
                throw new CommunicationException(
                    $"Unable to connect UDP endpoint {_options.Host}:{_options.Port}: {ex.Message}", ex);
            }

            _socket = socket;
            _isOpen = true;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task CloseAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            CloseCore();
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var socket = _socket;
        if (!_isOpen || socket is null)
        {
            throw new ConnectionClosedException("The UDP transport is not connected.");
        }

        try
        {
            return await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MarkClosed(socket);
            throw new ConnectionClosedException($"The UDP socket was lost: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var socket = _socket;
        if (!_isOpen || socket is null)
        {
            throw new ConnectionClosedException("The UDP transport is not connected.");
        }

        try
        {
            var sent = await socket.SendAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
            if (sent != buffer.Length)
            {
                throw new ProtocolException($"Only {sent} of {buffer.Length} bytes were sent as a UDP datagram.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProtocolException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MarkClosed(socket);
            throw new ConnectionClosedException($"The UDP socket was lost: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public void DiscardInput()
    {
        var socket = _socket;
        if (!_isOpen || socket is null)
        {
            return;
        }

        try
        {
            Span<byte> scratch = stackalloc byte[256];
            while (socket.Available > 0)
            {
                if (socket.Receive(scratch, SocketFlags.None) <= 0)
                {
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // The next read or write will surface the real connection state.
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CloseCore();
        }
        finally
        {
            _stateGate.Release();
        }

        _stateGate.Dispose();
    }

    private void MarkClosed(Socket socket)
    {
        if (ReferenceEquals(_socket, socket))
        {
            _isOpen = false;
            CloseCore();
        }
    }

    private void CloseCore()
    {
        _isOpen = false;
        var socket = _socket;
        _socket = null;
        socket?.Dispose();
    }
}
