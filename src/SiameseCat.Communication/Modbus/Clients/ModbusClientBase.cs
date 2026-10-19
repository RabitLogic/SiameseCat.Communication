using SiameseCat.Communication;

namespace SiameseCat.Communication.Modbus;

/// <summary>
/// Base class for Modbus master (client) implementations. All requests are serialized,
/// so a single client instance can be shared by multiple callers.
/// </summary>
public abstract partial class ModbusClientBase : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _disposed;
    private byte _defaultUnitId;

    /// <summary>Reused timeout source for attempts that carry no caller token; guarded by <see cref="_gate"/>.</summary>
    private CancellationTokenSource? _timeoutCts;

    /// <summary>Creates a client over <paramref name="transport"/> using <paramref name="options"/>.</summary>
    /// <param name="transport">Transport that carries the Modbus frames.</param>
    /// <param name="options">Protocol options, used for timeouts and retries.</param>
    protected ModbusClientBase(ITransport transport, ModbusOptions options)
    {
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
        DefaultUnitId = options.UnitId;

        if (options.LogFrames)
        {
            Transport = new LoggingTransport(
                Transport,
                options.Logger ?? NullPlcLogger.Instance,
                $"{GetType().Name}/{options.UnitId}");
        }
    }

    /// <summary>Gets the transport used to exchange Modbus frames.</summary>
    protected ITransport Transport { get; }

    /// <summary>Gets the options that control timeouts, retries and the default unit identifier.</summary>
    protected ModbusOptions Options { get; }

    /// <summary>
    /// Gets whether a request timeout must close the transport. Response-oriented transports
    /// (TCP) need this to resynchronize; serial transports keep the port open and discard stale input.
    /// </summary>
    protected virtual bool CloseOnTimeout => false;

    /// <summary>
    /// Gets or sets the unit identifier used by overloads that do not take one.
    /// 0 selects broadcast: writes are sent without waiting for a response and reads are rejected.
    /// </summary>
    public byte DefaultUnitId
    {
        get => _defaultUnitId;
        set => _defaultUnitId = value;
    }

    /// <summary>Gets whether the underlying transport is currently connected.</summary>
    public bool IsConnected => Transport.IsOpen;

    /// <summary>Opens the underlying transport. The call is a no-op when already connected.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public virtual async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await Transport.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Closes the underlying transport. Pending requests complete before the port is closed.</summary>
    public virtual async Task DisconnectAsync()
    {
        ThrowIfDisposed();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Transport.CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }


    /// <summary>Closes the transport and releases its resources. Safe to call more than once.</summary>
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
            await Transport.DisposeAsync().ConfigureAwait(false);
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

    /// <summary>Sends one PDU and returns the response PDU (starting with the function code).</summary>
    protected abstract Task<byte[]> ExecutePduCoreAsync(
        byte unitId,
        byte[] pdu,
        CancellationToken cancellationToken);

    /// <summary>Sends one write PDU for broadcast (unit id 0) without waiting for a response.</summary>
    /// <param name="unitId">Unit identifier, always 0.</param>
    /// <param name="pdu">PDU to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected abstract Task ExecuteBroadcastCoreAsync(
        byte unitId,
        byte[] pdu,
        CancellationToken cancellationToken);

    private async Task<byte[]> ExecutePduAsync(
        byte unitId,
        byte[] pdu,
        bool isWrite,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (unitId == 0)
        {
            if (!isWrite)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(unitId),
                    "Broadcast (unit id 0) only supports writes because no response is returned.");
            }

            await ExecuteBroadcastAsync(pdu, cancellationToken).ConfigureAwait(false);
            return Array.Empty<byte>();
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var attempts = isWrite && !Options.RetryWrites ? 1 : Options.RetryCount + 1;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await ExecuteAttemptAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (
                    attempt < attempts
                    && !cancellationToken.IsCancellationRequested
                    && RetryPolicy.IsTransient(ex))
                {
                    // Reconnecting is idempotent, so the hook can run even when the link is still open.
                    await Transport.OpenAsync(cancellationToken).ConfigureAwait(false);

                    if (Options.RetryDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(Options.RetryDelay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExecuteBroadcastAsync(byte[] pdu, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var attempts = !Options.RetryWrites ? 1 : Options.RetryCount + 1;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await ExecuteBroadcastAttemptAsync(pdu, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (
                    attempt < attempts
                    && !cancellationToken.IsCancellationRequested
                    && RetryPolicy.IsTransient(ex))
                {
                    await Transport.OpenAsync(cancellationToken).ConfigureAwait(false);

                    if (Options.RetryDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(Options.RetryDelay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExecuteBroadcastAttemptAsync(byte[] pdu, CancellationToken cancellationToken)
    {
        if (!Transport.IsOpen)
        {
            throw new CommunicationException("The transport is not connected. Call ConnectAsync first.");
        }

        var owned = CreateTimeoutSource(cancellationToken, out var token);
        try
        {
            await ExecuteBroadcastCoreAsync(0, pdu, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (CloseOnTimeout)
            {
                await Transport.CloseAsync().ConfigureAwait(false);
            }

            throw new CommunicationTimeoutException(
                $"Modbus broadcast timed out after {Options.Timeout} (function 0x{pdu[0]:X2}).");
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private async Task<byte[]> ExecuteAttemptAsync(
        byte unitId,
        byte[] pdu,
        CancellationToken cancellationToken)
    {
        if (!Transport.IsOpen)
        {
            throw new CommunicationException("The transport is not connected. Call ConnectAsync first.");
        }

        var owned = CreateTimeoutSource(cancellationToken, out var token);
        try
        {
            byte[] response = await ExecutePduCoreAsync(unitId, pdu, token).ConfigureAwait(false);
            if (response.Length > 0 && response[0] == (byte)(pdu[0] | 0x80))
            {
                if (response.Length < 2)
                {
                    throw new ProtocolException("The Modbus exception response is too short.");
                }

                throw new ModbusException(
                    response[1],
                    $"Modbus exception 0x{response[1]:X2} for unit {unitId}, function 0x{pdu[0]:X2}: "
                    + $"{ModbusException.GetExceptionName(response[1])}.");
            }

            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (CloseOnTimeout)
            {
                await Transport.CloseAsync().ConfigureAwait(false);
            }

            throw new CommunicationTimeoutException(
                $"Modbus request timed out after {Options.Timeout} (unit {unitId}, function 0x{pdu[0]:X2}).");
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>
    /// Creates the token source for one attempt. When the caller passes no token the instance-level
    /// source is reused (requests are serialised by the gate), which keeps the common path allocation free.
    /// Returns the source only when the caller owns it and must dispose it.
    /// </summary>
    private CancellationTokenSource? CreateTimeoutSource(
        CancellationToken cancellationToken,
        out CancellationToken token)
    {
        if (cancellationToken.CanBeCanceled)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (Options.Timeout > TimeSpan.Zero)
            {
                linked.CancelAfter(Options.Timeout);
            }

            token = linked.Token;
            return linked;
        }

#if NET6_0_OR_GREATER
        var cached = _timeoutCts;
        if (cached is null || cached.IsCancellationRequested || !cached.TryReset())
        {
            cached?.Dispose();
            cached = new CancellationTokenSource();
            _timeoutCts = cached;
        }

        if (Options.Timeout > TimeSpan.Zero)
        {
            cached.CancelAfter(Options.Timeout);
        }

        token = cached.Token;
        return null;
#else
        var source = new CancellationTokenSource();
        if (Options.Timeout > TimeSpan.Zero)
        {
            source.CancelAfter(Options.Timeout);
        }

        token = source.Token;
        return source;
#endif
    }

    private async Task ExecuteWriteAsync(
        byte unitId,
        byte[] pdu,
        CancellationToken cancellationToken)
    {
        byte[] response = await ExecutePduAsync(unitId, pdu, isWrite: true, cancellationToken)
            .ConfigureAwait(false);

        if (unitId == 0)
        {
            // Broadcast: the request was sent but no response is expected.
            return;
        }

        if (response.Length < 5 || response[0] != pdu[0])
        {
            throw new ProtocolException("The Modbus write response has an unexpected function code or length.");
        }

        for (int i = 1; i < 5; i++)
        {
            if (response[i] != pdu[i])
            {
                throw new ProtocolException("The Modbus write response does not echo the request address and value.");
            }
        }
    }

    private static byte[] BuildReadRequest(
        ModbusFunctionCode functionCode,
        ushort startAddress,
        ushort quantity) =>
        new[]
        {
            (byte)functionCode,
            High(startAddress),
            Low(startAddress),
            High(quantity),
            Low(quantity),
        };

    private static bool[] ParseBitResponse(
        byte[] response,
        ModbusFunctionCode functionCode,
        int quantity)
    {
        ValidateResponseHeader(response, functionCode);

        byte byteCount = response[1];
        int expected = (quantity + 7) / 8;
        if (byteCount != expected)
        {
            throw new ProtocolException(
                $"The Modbus response contains {byteCount} data bytes but {expected} were expected.");
        }

        if (response.Length != 2 + byteCount)
        {
            throw new ProtocolException("The Modbus response has an unexpected length.");
        }

        bool[] result = new bool[quantity];
        for (int i = 0; i < quantity; i++)
        {
            result[i] = (response[2 + i / 8] & (1 << (i % 8))) != 0;
        }

        return result;
    }

    private static ushort[] ParseRegisterResponse(
        byte[] response,
        ModbusFunctionCode functionCode,
        int quantity)
    {
        ValidateResponseHeader(response, functionCode);

        byte byteCount = response[1];
        int expected = quantity * 2;
        if (byteCount != expected)
        {
            throw new ProtocolException(
                $"The Modbus response contains {byteCount} data bytes but {expected} were expected.");
        }

        if (response.Length != 2 + byteCount)
        {
            throw new ProtocolException("The Modbus response has an unexpected length.");
        }

        ushort[] result = new ushort[quantity];
        for (int i = 0; i < quantity; i++)
        {
            result[i] = (ushort)((response[2 + i * 2] << 8) | response[3 + i * 2]);
        }

        return result;
    }

    private static void ValidateResponseHeader(byte[] response, ModbusFunctionCode functionCode)
    {
        if (response.Length < 2)
        {
            throw new ProtocolException("The Modbus response is too short.");
        }

        if (response[0] != (byte)functionCode)
        {
            throw new ProtocolException(
                $"The Modbus response has function code 0x{response[0]:X2} instead of 0x{(byte)functionCode:X2}.");
        }
    }

    private static void ValidateCount(int count, int maximum, string parameterName)
    {
        if (count < 1 || count > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, count, $"The count must be between 1 and {maximum}.");
        }
    }

    private static byte High(ushort value) => (byte)(value >> 8);

    private static byte Low(ushort value) => (byte)value;

    private void ThrowIfDisposed()
    {
        Guard.NotDisposed(_disposed, this);
    }
}
