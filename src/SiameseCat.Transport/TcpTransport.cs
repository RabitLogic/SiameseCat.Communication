using System.Net.Sockets;
using SiameseCat.Core;
namespace SiameseCat.Transport;

/// <summary>Raw TCP client transport.</summary>
public sealed class TcpTransport : ITransport
{
    private readonly TcpTransportOptions _options;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private volatile bool _isOpen;
    private volatile bool _disposed;

    /// <inheritdoc/>
    public TcpTransport(TcpTransportOptions options)
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

            var client = new TcpClient
            {
                NoDelay = _options.NoDelay,
            };

            ConfigureKeepAlive(client.Client);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ConnectTimeout);

            try
            {
                await client.ConnectAsync(_options.Host, _options.Port, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                client.Dispose();
                throw new CommunicationTimeoutException(
                    $"Connecting to {_options.Host}:{_options.Port} timed out after {_options.ConnectTimeout}.");
            }
            catch (OperationCanceledException)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                client.Dispose();
                throw new CommunicationException(
                    $"Unable to connect to {_options.Host}:{_options.Port}: {ex.Message}", ex);
            }

            _client = client;
            _stream = client.GetStream();
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

        var stream = _stream;
        if (!_isOpen || stream is null)
        {
            throw new ConnectionClosedException("The TCP transport is not connected.");
        }

        try
        {
            return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MarkClosed(stream);
            throw new ConnectionClosedException($"The TCP connection was lost: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var stream = _stream;
        if (!_isOpen || stream is null)
        {
            throw new ConnectionClosedException("The TCP transport is not connected.");
        }

        try
        {
            await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MarkClosed(stream);
            throw new ConnectionClosedException($"The TCP connection was lost: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public void DiscardInput()
    {
        var client = _client;
        if (!_isOpen || client is null)
        {
            return;
        }

        try
        {
            Span<byte> scratch = stackalloc byte[256];
            for (var i = 0; i < 64; i++)
            {
                var available = client.Available;
                if (available <= 0)
                {
                    return;
                }

                if (client.Client.Receive(scratch[..Math.Min(available, scratch.Length)], SocketFlags.None) <= 0)
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

    private void ConfigureKeepAlive(Socket socket)
    {
        if (!_options.KeepAlive)
        {
            return;
        }

        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(
                SocketOptionLevel.Tcp,
                SocketOptionName.TcpKeepAliveTime,
                (int)Math.Min(int.MaxValue, _options.KeepAliveTime.TotalSeconds));
            socket.SetSocketOption(
                SocketOptionLevel.Tcp,
                SocketOptionName.TcpKeepAliveInterval,
                (int)Math.Min(int.MaxValue, _options.KeepAliveInterval.TotalSeconds));
        }
        catch (Exception ex) when (ex is SocketException or PlatformNotSupportedException or NotSupportedException)
        {
            // Keep-alive tuning is best effort; the socket remains usable without it.
        }
    }

    private void CloseCore()
    {
        _isOpen = false;
        var stream = Interlocked.Exchange(ref _stream, null);
        var client = Interlocked.Exchange(ref _client, null);

        stream?.Dispose();
        client?.Dispose();
    }

    private void MarkClosed(NetworkStream expected)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _stream, null, expected), expected))
        {
            // A newer connection replaced this one; leave it untouched.
            return;
        }

        _isOpen = false;
        var client = Interlocked.Exchange(ref _client, null);

        expected.Dispose();
        client?.Dispose();
    }
}
