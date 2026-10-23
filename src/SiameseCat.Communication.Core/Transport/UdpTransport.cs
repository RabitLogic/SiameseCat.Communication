using System.Buffers;
using System.Net;
using System.Net.Sockets;
using SiameseCat.Communication;

namespace SiameseCat.Communication.Transport;

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
        Guard.NotDisposed(_disposed, this);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Guard.NotDisposed(_disposed, this);

            if (_isOpen)
            {
                return;
            }

            var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ConnectTimeout);

            try
            {
#if NET8_0_OR_GREATER
                await socket.ConnectAsync(_options.Host, _options.Port, timeoutCts.Token).ConfigureAwait(false);
#else
                var addresses = await Dns.GetHostAddressesAsync(_options.Host).WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                if (addresses.Length == 0)
                {
                    socket.Dispose();
                    throw new CommunicationException($"Host '{_options.Host}' could not be resolved.");
                }

                await socket.ConnectAsync(new IPEndPoint(addresses[0], _options.Port))
                    .WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
#endif
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
        Guard.NotDisposed(_disposed, this);

        var socket = _socket;
        if (!_isOpen || socket is null)
        {
            throw new ConnectionClosedException("The UDP transport is not connected.");
        }

        try
        {
            return await ReceiveAsync(socket, buffer, cancellationToken).ConfigureAwait(false);
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
        Guard.NotDisposed(_disposed, this);

        var socket = _socket;
        if (!_isOpen || socket is null)
        {
            throw new ConnectionClosedException("The UDP transport is not connected.");
        }

        try
        {
            var sent = await SendAsync(socket, buffer, cancellationToken).ConfigureAwait(false);
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
            var scratch = new byte[256];
            while (socket.Available > 0)
            {
                if (socket.Receive(scratch, 0, scratch.Length, SocketFlags.None) <= 0)
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

    private static async ValueTask<int> ReceiveAsync(Socket socket, Memory<byte> buffer, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        return await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
#else
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var received = await socket
                .ReceiveAsync(new ArraySegment<byte>(rented, 0, buffer.Length), SocketFlags.None)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            new ReadOnlySpan<byte>(rented, 0, received).CopyTo(buffer.Span);
            return received;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
#endif
    }

    private static async ValueTask<int> SendAsync(Socket socket, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        return await socket.SendAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
#else
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            buffer.Span.CopyTo(rented);
            return await socket
                .SendAsync(new ArraySegment<byte>(rented, 0, buffer.Length), SocketFlags.None)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
#endif
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
