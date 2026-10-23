using SiameseCat.Communication.Transport;

namespace SiameseCat.Communication.Tcp;

/// <summary>
/// Client for a vendor-specific or in-house TCP protocol. It owns the connection, the request timeout,
/// optional retries and frame logging; the caller supplies the protocol payloads.
/// </summary>
/// <remarks>
/// <para>
/// Operations on one client are serialized, so a request and the read of its response cannot
/// interleave with another exchange. Use one client per connection.
/// </para>
/// <para>The transport, including an injected one, is disposed with the client.</para>
/// </remarks>
public sealed class CustomTcpClient : IAsyncDisposable
{
    private readonly CustomTcpClientOptions _options;
    private readonly ITransport _transport;
    private readonly TcpFrameConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _disposed;

    /// <summary>Creates a client that opens its own TCP connection on <see cref="CustomTcpClientOptions.Tcp"/>.</summary>
    /// <param name="options">Client settings; defaults are used when omitted.</param>
    public CustomTcpClient(CustomTcpClientOptions? options = null)
    {
        _options = options ?? new CustomTcpClientOptions();
        _options.Validate();
        _transport = new TcpTransport(_options.Tcp);
        _connection = CreateConnection(_transport);
    }

    /// <summary>Creates a client over an existing transport, for example a TLS stream or a test double.</summary>
    /// <param name="transport">Transport that carries the byte stream; disposed with the client.</param>
    /// <param name="options">Client settings; defaults are used when omitted.</param>
    public CustomTcpClient(ITransport transport, CustomTcpClientOptions? options = null)
    {
        _options = options ?? new CustomTcpClientOptions();
        _options.Validate();
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _connection = CreateConnection(_transport);
    }

    /// <summary>Gets whether the underlying transport is connected.</summary>
    public bool IsOpen => _transport.IsOpen;

    /// <summary>Opens the connection and resets the frame reader.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Guard.NotDisposed(_disposed, this);

        await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);
        _connection.ResetReader();
    }

    /// <summary>Closes the connection. The client can be connected again afterwards.</summary>
    public Task DisconnectAsync() => _transport.CloseAsync();

    /// <summary>Sends one frame without waiting for an answer.</summary>
    /// <param name="payload">Protocol payload; the codec adds the framing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        Guard.NotDisposed(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _connection.SendAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Sends one frame and waits for the next frame from the peer.</summary>
    /// <param name="payload">Protocol payload; the codec adds the framing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response payload without framing.</returns>
    /// <remarks>
    /// A timed-out request closes the connection, because a late response would otherwise be read as
    /// the answer to the next request. Retries only happen when <see cref="CustomTcpClientOptions.RetryRequests"/>
    /// is enabled, since the request may have side effects.
    /// </remarks>
    /// <exception cref="CommunicationTimeoutException">The peer did not answer within the configured timeout.</exception>
    /// <exception cref="ConnectionClosedException">The connection was closed before the answer arrived.</exception>
    /// <exception cref="ProtocolException">The response violates the framing.</exception>
    public async Task<byte[]> RequestAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        Guard.NotDisposed(_disposed, this);

        var attempts = _options.RetryRequests ? _options.RetryCount + 1 : 1;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Exception? failure = null;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.Timeout);

                try
                {
                    await _connection.SendAsync(payload, timeout.Token).ConfigureAwait(false);

                    var frame = await _connection.ReadFrameAsync(timeout.Token).ConfigureAwait(false);
                    if (frame is null)
                    {
                        throw new ConnectionClosedException("The peer closed the connection before answering.");
                    }

                    return frame;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await _transport.CloseAsync().ConfigureAwait(false);

                    failure = new CommunicationTimeoutException(
                        $"The custom TCP request timed out after {_options.Timeout}.");
                    if (attempt >= attempts)
                    {
                        throw failure;
                    }
                }
                catch (ConnectionClosedException ex)
                {
                    failure = ex;
                    await _transport.CloseAsync().ConfigureAwait(false);
                    if (attempt >= attempts)
                    {
                        throw;
                    }
                }
                catch (Exception ex) when (attempt < attempts && RetryPolicy.IsTransient(ex))
                {
                    failure = ex;
                    await _transport.CloseAsync().ConfigureAwait(false);
                }

                await DelayBeforeRetryAsync(cancellationToken).ConfigureAwait(false);
                await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);
                _connection.ResetReader();
            }

            throw failure ?? new CommunicationException("The custom TCP request failed.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Waits for the next frame from the peer, for example unsolicited text or a notification.</summary>
    /// <param name="cancellationToken">Cancellation token; the wait is cancelled by it or by a closed connection.</param>
    /// <returns>The frame payload, or <see langword="null"/> when the peer closed the connection.</returns>
    public async ValueTask<byte[]?> ReadFrameAsync(CancellationToken cancellationToken = default)
    {
        Guard.NotDisposed(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_transport.IsOpen)
            {
                throw new ConnectionClosedException("The custom TCP client is not connected.");
            }

            return await _connection.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private TcpFrameConnection CreateConnection(ITransport transport) =>
        new(
            transport,
            _options.Codec ?? new TcpFrameCodec(_options.Framing),
            _options.Logger,
            _options.LogFrames,
            _options.LogName);

    private async Task DelayBeforeRetryAsync(CancellationToken cancellationToken)
    {
        if (_options.RetryDelay > TimeSpan.Zero)
        {
            await Task.Delay(_options.RetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }
}
