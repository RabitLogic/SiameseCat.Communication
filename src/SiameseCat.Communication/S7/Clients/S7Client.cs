using SiameseCat.Communication;
using SiameseCat.Communication.Transport;

namespace SiameseCat.Communication.S7;

/// <summary>
/// S7 client using ISO-on-TCP (RFC 1006), COTP and S7 communication.
/// Supports the DB, M, I and Q areas with automatic PDU chunking.
/// </summary>
/// <remarks>
/// <para>All requests are serialized, so a single instance can be shared between callers.</para>
/// <para>
/// Dropped connections are re-established automatically when <see cref="CommunicationOptions.RetryCount"/>
/// is greater than zero, including the COTP connection and the setup communication handshake.
/// </para>
/// </remarks>
public sealed partial class S7Client : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly S7Options _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Requests are serialised by the gate, so the client owns its frame buffers instead of allocating
    // one per request. The send buffer holds TPKT + COTP + S7 job header + the negotiated PDU.
    private readonly byte[] _sendBuffer = new byte[TpktMaxLength + 4];
    private readonly byte[] _receiveBuffer = new byte[TpktMaxLength];

    private ushort _pduReference;
    private int _pduSize = 240;
    private volatile bool _disposed;

    /// <summary>Reused timeout source for attempts that carry no caller token; guarded by <see cref="_gate"/>.</summary>
    private CancellationTokenSource? _timeoutCts;

    /// <summary>Creates a client that connects to the endpoint configured in <paramref name="options"/>.</summary>
    public S7Client(S7Options options)
        : this(CreateTransport(options), options)
    {
    }

    /// <summary>
    /// Creates a client over a custom transport. Intended for testing or for tunneling S7
    /// traffic through a transport that is not a direct TCP connection.
    /// </summary>
    public S7Client(ITransport transport, S7Options options)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        if (options.LogFrames)
        {
            _transport = new LoggingTransport(
                _transport,
                options.Logger ?? NullPlcLogger.Instance,
                "S7Client");
        }
    }

    /// <summary>Gets whether the underlying transport is currently connected.</summary>
    public bool IsConnected => _transport.IsOpen;

    /// <summary>Gets the PDU size negotiated with the PLC during the last successful connection.</summary>
    public int PduSize => _pduSize;

    /// <summary>Connects to the PLC, establishing the COTP connection and negotiating the S7 PDU size.</summary>
    /// <exception cref="CommunicationTimeoutException">The connection or handshake timed out.</exception>
    /// <exception cref="CommunicationException">The endpoint could not be reached.</exception>
    /// <exception cref="S7Exception">The PLC rejected the connection or returned an invalid handshake.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_transport.IsOpen)
            {
                return;
            }

            _pduReference = 0;
            _pduSize = 240;
            await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await ConnectPlcAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await _transport.CloseAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Closes the connection. The instance can be connected again afterwards.</summary>
    public Task DisconnectAsync()
    {
        ThrowIfDisposed();
        return _transport.CloseAsync();
    }

    /// <summary>Closes the connection and releases the transport. Safe to call more than once.</summary>
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

        _timeoutCts?.Dispose();
        _timeoutCts = null;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static TcpTransport CreateTransport(S7Options options)
    {
        Guard.NotNull(options, nameof(options));
        return new TcpTransport(new TcpTransportOptions
        {
            Host = options.Host,
            Port = options.Port,
            ConnectTimeout = options.ConnectTimeout,
            NoDelay = options.NoDelay,
            KeepAlive = options.KeepAlive,
            KeepAliveTime = options.KeepAliveTime,
            KeepAliveInterval = options.KeepAliveInterval,
        });
    }

    private async Task ConnectPlcAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.ConnectTimeout);
        var token = timeoutCts.Token;

        try
        {
            await _transport.WriteAsync(BuildConnectionRequest(), token).ConfigureAwait(false);
            var confirmLength = await ReceiveTpktAsync(token).ConfigureAwait(false);

            if (confirmLength < 11 || _receiveBuffer[5] != CotpConnectionConfirm)
            {
                throw new S7Exception("The PLC rejected the ISO-on-TCP connection request.");
            }

            var response = await ExecuteJobAttemptAsync(
                BuildSetupCommunicationRequest(), ReadOnlyMemory<byte>.Empty, token).ConfigureAwait(false);

            if (response.Parameter.Length < 8 || response.Parameter[0] != SetupCommunicationFunction)
            {
                throw new S7Exception("The PLC returned an unexpected setup communication response.");
            }

            var negotiatedPduSize = (response.Parameter[6] << 8) | response.Parameter[7];
            if (negotiatedPduSize < MinimumPduSize)
            {
                throw new S7Exception($"The PLC negotiated an invalid PDU size ({negotiatedPduSize}).");
            }

            _pduSize = Math.Min(negotiatedPduSize, MaximumPduSize);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CommunicationTimeoutException(
                $"Connecting to {_options.Host}:{_options.Port} timed out after {_options.ConnectTimeout}.");
        }
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        await _transport.CloseAsync().ConfigureAwait(false);
        _pduReference = 0;
        _pduSize = 240;
        await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConnectPlcAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureConnected()
    {
        if (!_transport.IsOpen)
        {
            throw new CommunicationException("The S7 client is not connected. Call ConnectAsync first.");
        }
    }

    private void ThrowIfDisposed()
    {
        Guard.NotDisposed(_disposed, this);
    }
}
