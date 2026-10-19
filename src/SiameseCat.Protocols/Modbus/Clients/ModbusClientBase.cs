using SiameseCat.Core;

namespace SiameseCat.Protocols.Modbus;

/// <summary>
/// Base class for Modbus master (client) implementations. All requests are serialized,
/// so a single client instance can be shared by multiple callers.
/// </summary>
public abstract class ModbusClientBase : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _disposed;
    private byte _defaultUnitId;

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

    /// <summary>Reads coils (function code 01) from <see cref="DefaultUnitId"/>.</summary>
    /// <param name="startAddress">Zero-based coil address.</param>
    /// <param name="quantity">Number of coils to read, 1..2000.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One flag per coil.</returns>
    public Task<bool[]> ReadCoilsAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default) =>
        ReadCoilsAsync(DefaultUnitId, startAddress, quantity, cancellationToken);

    /// <summary>Reads coils (function code 01) from a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="startAddress">Zero-based coil address.</param>
    /// <param name="quantity">Number of coils to read, 1..2000.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One flag per coil.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task<bool[]> ReadCoilsAsync(
        byte unitId,
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default)
    {
        ValidateCount(quantity, 2000, nameof(quantity));
        byte[] pdu = BuildReadRequest(ModbusFunctionCode.ReadCoils, startAddress, quantity);
        byte[] response = await ExecutePduAsync(unitId, pdu, isWrite: false, cancellationToken).ConfigureAwait(false);
        return ParseBitResponse(response, ModbusFunctionCode.ReadCoils, quantity);
    }

    /// <summary>Reads discrete inputs (function code 02) from <see cref="DefaultUnitId"/>.</summary>
    /// <param name="startAddress">Zero-based input address.</param>
    /// <param name="quantity">Number of inputs to read, 1..2000.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One flag per discrete input.</returns>
    public Task<bool[]> ReadDiscreteInputsAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default) =>
        ReadDiscreteInputsAsync(DefaultUnitId, startAddress, quantity, cancellationToken);

    /// <summary>Reads discrete inputs (function code 02) from a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="startAddress">Zero-based input address.</param>
    /// <param name="quantity">Number of inputs to read, 1..2000.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One flag per discrete input.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task<bool[]> ReadDiscreteInputsAsync(
        byte unitId,
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default)
    {
        ValidateCount(quantity, 2000, nameof(quantity));
        byte[] pdu = BuildReadRequest(ModbusFunctionCode.ReadDiscreteInputs, startAddress, quantity);
        byte[] response = await ExecutePduAsync(unitId, pdu, isWrite: false, cancellationToken).ConfigureAwait(false);
        return ParseBitResponse(response, ModbusFunctionCode.ReadDiscreteInputs, quantity);
    }

    /// <summary>Reads holding registers (function code 03) from <see cref="DefaultUnitId"/>.</summary>
    /// <param name="startAddress">Zero-based register address.</param>
    /// <param name="quantity">Number of registers to read, 1..125.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One 16-bit register per element.</returns>
    public Task<ushort[]> ReadHoldingRegistersAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default) =>
        ReadHoldingRegistersAsync(DefaultUnitId, startAddress, quantity, cancellationToken);

    /// <summary>Reads holding registers (function code 03) from a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="startAddress">Zero-based register address.</param>
    /// <param name="quantity">Number of registers to read, 1..125.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One 16-bit register per element.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task<ushort[]> ReadHoldingRegistersAsync(
        byte unitId,
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default)
    {
        ValidateCount(quantity, 125, nameof(quantity));
        byte[] pdu = BuildReadRequest(ModbusFunctionCode.ReadHoldingRegisters, startAddress, quantity);
        byte[] response = await ExecutePduAsync(unitId, pdu, isWrite: false, cancellationToken).ConfigureAwait(false);
        return ParseRegisterResponse(response, ModbusFunctionCode.ReadHoldingRegisters, quantity);
    }

    /// <summary>Reads input registers (function code 04) from <see cref="DefaultUnitId"/>.</summary>
    /// <param name="startAddress">Zero-based register address.</param>
    /// <param name="quantity">Number of registers to read, 1..125.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One 16-bit register per element.</returns>
    public Task<ushort[]> ReadInputRegistersAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default) =>
        ReadInputRegistersAsync(DefaultUnitId, startAddress, quantity, cancellationToken);

    /// <summary>Reads input registers (function code 04) from a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="startAddress">Zero-based register address.</param>
    /// <param name="quantity">Number of registers to read, 1..125.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One 16-bit register per element.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task<ushort[]> ReadInputRegistersAsync(
        byte unitId,
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default)
    {
        ValidateCount(quantity, 125, nameof(quantity));
        byte[] pdu = BuildReadRequest(ModbusFunctionCode.ReadInputRegisters, startAddress, quantity);
        byte[] response = await ExecutePduAsync(unitId, pdu, isWrite: false, cancellationToken).ConfigureAwait(false);
        return ParseRegisterResponse(response, ModbusFunctionCode.ReadInputRegisters, quantity);
    }

    /// <summary>Writes a single coil (function code 05) to <see cref="DefaultUnitId"/>.</summary>
    /// <param name="address">Zero-based coil address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteSingleCoilAsync(
        ushort address,
        bool value,
        CancellationToken cancellationToken = default) =>
        WriteSingleCoilAsync(DefaultUnitId, address, value, cancellationToken);

    /// <summary>Writes a single coil (function code 05) to a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="address">Zero-based coil address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task WriteSingleCoilAsync(
        byte unitId,
        ushort address,
        bool value,
        CancellationToken cancellationToken = default)
    {
        byte[] pdu = new byte[]
        {
            (byte)ModbusFunctionCode.WriteSingleCoil,
            High(address),
            Low(address),
            value ? (byte)0xFF : (byte)0x00,
            0x00,
        };

        await ExecuteWriteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a single holding register (function code 06) to <see cref="DefaultUnitId"/>.</summary>
    /// <param name="address">Zero-based register address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteSingleRegisterAsync(
        ushort address,
        ushort value,
        CancellationToken cancellationToken = default) =>
        WriteSingleRegisterAsync(DefaultUnitId, address, value, cancellationToken);

    /// <summary>Writes a single holding register (function code 06) to a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="address">Zero-based register address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task WriteSingleRegisterAsync(
        byte unitId,
        ushort address,
        ushort value,
        CancellationToken cancellationToken = default)
    {
        byte[] pdu = new byte[]
        {
            (byte)ModbusFunctionCode.WriteSingleRegister,
            High(address),
            Low(address),
            High(value),
            Low(value),
        };

        await ExecuteWriteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes multiple coils (function code 0F) to <see cref="DefaultUnitId"/>.</summary>
    /// <param name="startAddress">Zero-based address of the first coil.</param>
    /// <param name="values">Values to write, 1..1968 items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteMultipleCoilsAsync(
        ushort startAddress,
        IReadOnlyList<bool> values,
        CancellationToken cancellationToken = default) =>
        WriteMultipleCoilsAsync(DefaultUnitId, startAddress, values, cancellationToken);

    /// <summary>Writes multiple coils (function code 0F) to a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="startAddress">Zero-based address of the first coil.</param>
    /// <param name="values">Values to write, 1..1968 items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task WriteMultipleCoilsAsync(
        byte unitId,
        ushort startAddress,
        IReadOnlyList<bool> values,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(values, nameof(values));
        ValidateCount(values.Count, 1968, nameof(values));

        int byteCount = (values.Count + 7) / 8;
        byte[] pdu = new byte[6 + byteCount];
        pdu[0] = (byte)ModbusFunctionCode.WriteMultipleCoils;
        pdu[1] = High(startAddress);
        pdu[2] = Low(startAddress);
        pdu[3] = High((ushort)values.Count);
        pdu[4] = Low((ushort)values.Count);
        pdu[5] = (byte)byteCount;

        for (int i = 0; i < values.Count; i++)
        {
            if (values[i])
            {
                pdu[6 + i / 8] |= (byte)(1 << (i % 8));
            }
        }

        await ExecuteWriteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes multiple holding registers (function code 10) to <see cref="DefaultUnitId"/>.</summary>
    /// <param name="startAddress">Zero-based address of the first register.</param>
    /// <param name="values">Registers to write, 1..123 items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteMultipleRegistersAsync(
        ushort startAddress,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default) =>
        WriteMultipleRegistersAsync(DefaultUnitId, startAddress, values, cancellationToken);

    /// <summary>Writes multiple holding registers (function code 10) to a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="startAddress">Zero-based address of the first register.</param>
    /// <param name="values">Registers to write, 1..123 items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task WriteMultipleRegistersAsync(
        byte unitId,
        ushort startAddress,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(values, nameof(values));
        ValidateCount(values.Count, 123, nameof(values));

        byte[] pdu = new byte[6 + values.Count * 2];
        pdu[0] = (byte)ModbusFunctionCode.WriteMultipleRegisters;
        pdu[1] = High(startAddress);
        pdu[2] = Low(startAddress);
        pdu[3] = High((ushort)values.Count);
        pdu[4] = Low((ushort)values.Count);
        pdu[5] = (byte)(values.Count * 2);

        for (int i = 0; i < values.Count; i++)
        {
            pdu[6 + i * 2] = High(values[i]);
            pdu[7 + i * 2] = Low(values[i]);
        }

        await ExecuteWriteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads and writes registers in one transaction (function code 17) for <see cref="DefaultUnitId"/>.</summary>
    /// <param name="readStartAddress">Zero-based address of the first register to read.</param>
    /// <param name="readQuantity">Number of registers to read, 1..125.</param>
    /// <param name="writeStartAddress">Zero-based address of the first register to write.</param>
    /// <param name="writeValues">Registers to write, 1..121 items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registers read before the write is applied.</returns>
    public Task<ushort[]> ReadWriteMultipleRegistersAsync(
        ushort readStartAddress,
        ushort readQuantity,
        ushort writeStartAddress,
        IReadOnlyList<ushort> writeValues,
        CancellationToken cancellationToken = default) =>
        ReadWriteMultipleRegistersAsync(
            DefaultUnitId, readStartAddress, readQuantity, writeStartAddress, writeValues, cancellationToken);

    /// <summary>Reads and writes registers in one transaction (function code 17) for a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="readStartAddress">Zero-based address of the first register to read.</param>
    /// <param name="readQuantity">Number of registers to read, 1..125.</param>
    /// <param name="writeStartAddress">Zero-based address of the first register to write.</param>
    /// <param name="writeValues">Registers to write, 1..121 items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registers read before the write is applied.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task<ushort[]> ReadWriteMultipleRegistersAsync(
        byte unitId,
        ushort readStartAddress,
        ushort readQuantity,
        ushort writeStartAddress,
        IReadOnlyList<ushort> writeValues,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(writeValues, nameof(writeValues));
        ValidateCount(readQuantity, 125, nameof(readQuantity));
        ValidateCount(writeValues.Count, 121, nameof(writeValues));

        byte[] pdu = new byte[10 + writeValues.Count * 2];
        pdu[0] = (byte)ModbusFunctionCode.ReadWriteMultipleRegisters;
        pdu[1] = High(readStartAddress);
        pdu[2] = Low(readStartAddress);
        pdu[3] = High(readQuantity);
        pdu[4] = Low(readQuantity);
        pdu[5] = High(writeStartAddress);
        pdu[6] = Low(writeStartAddress);
        pdu[7] = High((ushort)writeValues.Count);
        pdu[8] = Low((ushort)writeValues.Count);
        pdu[9] = (byte)(writeValues.Count * 2);

        for (int i = 0; i < writeValues.Count; i++)
        {
            pdu[10 + i * 2] = High(writeValues[i]);
            pdu[11 + i * 2] = Low(writeValues[i]);
        }

        byte[] response = await ExecutePduAsync(
            unitId, pdu, isWrite: true, cancellationToken).ConfigureAwait(false);
        return ParseRegisterResponse(
            response, ModbusFunctionCode.ReadWriteMultipleRegisters, readQuantity);
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

            return await RetryPolicy.ExecuteAsync(
                token => ExecuteAttemptAsync(unitId, pdu, token),
                Options,
                cancellationToken,
                isWrite,
                reconnect: token => Transport.OpenAsync(token)).ConfigureAwait(false);
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

            await RetryPolicy.ExecuteAsync(
                async token =>
                {
                    await ExecuteBroadcastAttemptAsync(pdu, token).ConfigureAwait(false);
                    return true;
                },
                Options,
                cancellationToken,
                isWrite: true,
                reconnect: token => Transport.OpenAsync(token)).ConfigureAwait(false);
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

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Options.Timeout);

        try
        {
            await ExecuteBroadcastCoreAsync(0, pdu, timeoutCts.Token).ConfigureAwait(false);
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

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Options.Timeout);

        try
        {
            byte[] response = await ExecutePduCoreAsync(unitId, pdu, timeoutCts.Token).ConfigureAwait(false);
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
