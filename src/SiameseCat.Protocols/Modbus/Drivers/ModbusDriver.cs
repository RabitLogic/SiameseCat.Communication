using System.Globalization;
using System.Text;
using SiameseCat.Core;

namespace SiameseCat.Protocols.Modbus;

/// <summary>
/// Modbus driver implementing the pluggable <see cref="IPlcDriver"/> contract on top of the
/// Modbus PDU client. TCP and RTU share all address and data conversion logic; only the client
/// (framing) differs.
/// </summary>
public class ModbusDriver : DriverBase
{
    private const int MaxRegistersPerRequest = 125;
    private const int MaxCoilsPerRequest = 2000;
    private const int MaxRegistersPerWrite = 123;
    private const int MaxCoilsPerWrite = 1968;

    private readonly ModbusClientBase _client;
    private readonly byte _unitId;
    private readonly Encoding _stringEncoding;
    private readonly bool _swapBytes;

    /// <summary>Creates a driver over an existing Modbus client, primarily for testing or custom transports.</summary>
    /// <param name="client">Modbus client that performs the framed exchange.</param>
    /// <param name="configuration">Connection and retry settings.</param>
    /// <param name="unitId">Unit identifier used for requests.</param>
    /// <param name="stringEncoding">Encoding used for <see cref="PlcDataType.String"/>; defaults to ASCII.</param>
    /// <param name="swapBytes">Swaps the two bytes inside every register for strings and raw data.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="metrics">Optional metrics collector.</param>
    public ModbusDriver(
        ModbusClientBase client,
        DriverConfiguration configuration,
        byte unitId,
        Encoding? stringEncoding = null,
        bool swapBytes = false,
        IPlcLogger? logger = null,
        PlcMetrics? metrics = null)
        : this(
            client,
            configuration,
            unitId,
            ModbusDriverFactory.DefaultDescriptor,
            addressParser: null,
            stringEncoding,
            swapBytes,
            logger,
            metrics)
    {
    }

    /// <summary>
    /// Creates a Modbus driver with a vendor-specific descriptor and address parser, used by brand
    /// drivers such as 汇川/Inovance that only differ in address mapping.
    /// </summary>
    /// <param name="client">Modbus client that performs the framed exchange.</param>
    /// <param name="configuration">Connection and retry settings.</param>
    /// <param name="unitId">Unit identifier used for requests.</param>
    /// <param name="descriptor">Descriptor reported by the driver.</param>
    /// <param name="addressParser">Parser that maps native addresses to Modbus regions; defaults to <see cref="ModbusAddressParser"/>.</param>
    /// <param name="stringEncoding">Encoding used for <see cref="PlcDataType.String"/>; defaults to ASCII.</param>
    /// <param name="swapBytes">Swaps the two bytes inside every register for strings and raw data.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="metrics">Optional metrics collector.</param>
    protected ModbusDriver(
        ModbusClientBase client,
        DriverConfiguration configuration,
        byte unitId,
        DriverDescriptor descriptor,
        IPlcAddressParser? addressParser,
        Encoding? stringEncoding,
        bool swapBytes,
        IPlcLogger? logger,
        PlcMetrics? metrics)
        : base(descriptor, addressParser ?? new ModbusAddressParser(), configuration, logger, metrics)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _unitId = unitId;
        _stringEncoding = stringEncoding ?? Encoding.ASCII;
        _swapBytes = swapBytes;
    }

    /// <summary>
    /// Gets whether the driver is configured for broadcast (unit id 0). Broadcast drivers can only
    /// write: reads never receive a response.
    /// </summary>
    public bool IsBroadcast => _unitId == 0;

    /// <summary>
    /// Writes one contiguous block of holding registers and reads another block in a single
    /// transaction (function code 23).
    /// </summary>
    /// <param name="writeAddress">First holding register to write.</param>
    /// <param name="writeValues">Registers to write, 1..121 items.</param>
    /// <param name="readAddress">First holding register to read.</param>
    /// <param name="readQuantity">Number of registers to read, 1..125.</param>
    /// <param name="timeout">Per-call timeout; <see langword="null"/> uses the configured timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registers read before the write is applied.</returns>
    /// <exception cref="ArgumentException">A region is not a holding register region.</exception>
    public Task<ushort[]> ReadWriteRegistersAsync(
        PlcAddress writeAddress,
        IReadOnlyList<ushort> writeValues,
        PlcAddress readAddress,
        ushort readQuantity,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(writeValues, nameof(writeValues));

        if (writeAddress.Region != ModbusAddressParser.HoldingRegisterRegion)
        {
            throw new ArgumentException(
                $"Function 23 writes holding registers only, not '{writeAddress.Region}'.",
                nameof(writeAddress));
        }

        if (readAddress.Region != ModbusAddressParser.HoldingRegisterRegion)
        {
            throw new ArgumentException(
                $"Function 23 reads holding registers only, not '{readAddress.Region}'.",
                nameof(readAddress));
        }

        return ExecuteProtocolAsync(
            PlcOperation.ReadWrite,
            isWrite: true,
            timeout,
            token => _client.ReadWriteMultipleRegistersAsync(
                _unitId,
                ToUShort(readAddress.Offset),
                readQuantity,
                ToUShort(writeAddress.Offset),
                writeValues,
                token),
            cancellationToken);
    }

    /// <inheritdoc/>
    protected override Task DoConnectAsync(CancellationToken cancellationToken) =>
        _client.ConnectAsync(cancellationToken);

    /// <inheritdoc/>
    protected override Task DoDisconnectAsync() => _client.DisconnectAsync();

    /// <inheritdoc/>
    protected override async Task<PlcValue> DoReadAsync(PlcAddress address, CancellationToken cancellationToken)
    {
        switch (address.Region)
        {
            case ModbusAddressParser.CoilRegion:
                EnsureBool(address);
                var coils = await _client.ReadCoilsAsync(_unitId, ToUShort(address.Offset), 1, cancellationToken)
                    .ConfigureAwait(false);
                return PlcValue.FromBool(coils[0]);

            case ModbusAddressParser.DiscreteInputRegion:
                EnsureBool(address);
                var inputs = await _client.ReadDiscreteInputsAsync(_unitId, ToUShort(address.Offset), 1, cancellationToken)
                    .ConfigureAwait(false);
                return PlcValue.FromBool(inputs[0]);

            case ModbusAddressParser.HoldingRegisterRegion:
            case ModbusAddressParser.InputRegisterRegion:
                var count = GetRegisterCount(address);
                var registers = address.Region == ModbusAddressParser.HoldingRegisterRegion
                    ? await _client.ReadHoldingRegistersAsync(_unitId, ToUShort(address.Offset), ToUShort(count), cancellationToken).ConfigureAwait(false)
                    : await _client.ReadInputRegistersAsync(_unitId, ToUShort(address.Offset), ToUShort(count), cancellationToken).ConfigureAwait(false);
                return FromRegisters(address, registers);

            default:
                throw new NotSupportedException($"Unknown Modbus region '{address.Region}'.");
        }
    }

    /// <inheritdoc/>
    protected override async Task DoWriteAsync(
        PlcAddress address,
        PlcValue value,
        CancellationToken cancellationToken)
    {
        switch (address.Region)
        {
            case ModbusAddressParser.CoilRegion:
                EnsureBool(address);
                await _client.WriteSingleCoilAsync(_unitId, ToUShort(address.Offset), value.AsBool(), cancellationToken)
                    .ConfigureAwait(false);
                return;

            case ModbusAddressParser.HoldingRegisterRegion:
                var registers = ToRegisters(address, value);
                if (registers.Length == 1)
                {
                    await _client.WriteSingleRegisterAsync(_unitId, ToUShort(address.Offset), registers[0], cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _client.WriteMultipleRegistersAsync(_unitId, ToUShort(address.Offset), registers, cancellationToken)
                        .ConfigureAwait(false);
                }

                return;

            case ModbusAddressParser.InputRegisterRegion:
            case ModbusAddressParser.DiscreteInputRegion:
                throw new NotSupportedException($"Modbus region '{address.Region}' is read-only.");

            default:
                throw new NotSupportedException($"Unknown Modbus region '{address.Region}'.");
        }
    }

    /// <inheritdoc/>
    protected override async Task DoWriteBatchAsync(
        IReadOnlyList<PlcWriteRequest> requests,
        CancellationToken cancellationToken)
    {
        foreach (var group in GroupRequestsByRegion(requests))
        {
            switch (group.Key)
            {
                case ModbusAddressParser.HoldingRegisterRegion:
                    await WriteRegisterRunsAsync(group.Value, cancellationToken).ConfigureAwait(false);
                    break;

                case ModbusAddressParser.CoilRegion:
                    await WriteCoilRunsAsync(group.Value, cancellationToken).ConfigureAwait(false);
                    break;

                case ModbusAddressParser.InputRegisterRegion:
                case ModbusAddressParser.DiscreteInputRegion:
                    throw new NotSupportedException($"Modbus region '{group.Key}' is read-only.");

                default:
                    throw new NotSupportedException($"Unknown Modbus region '{group.Key}'.");
            }
        }
    }

    private async Task WriteRegisterRunsAsync(
        List<PlcWriteRequest> items,
        CancellationToken cancellationToken)
    {
        items.Sort(static (left, right) => left.Address.Offset.CompareTo(right.Address.Offset));

        var index = 0;
        while (index < items.Count)
        {
            var start = items[index].Address.Offset;
            var registers = new List<ushort>(ToRegisters(items[index].Address, items[index].Value));
            index++;

            while (index < items.Count)
            {
                var next = items[index].Address;
                if (next.Offset != start + registers.Count)
                {
                    break;
                }

                var nextRegisters = ToRegisters(next, items[index].Value);
                if (registers.Count + nextRegisters.Length > MaxRegistersPerWrite)
                {
                    break;
                }

                registers.AddRange(nextRegisters);
                index++;
            }

            if (registers.Count == 1)
            {
                await _client.WriteSingleRegisterAsync(_unitId, ToUShort(start), registers[0], cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _client.WriteMultipleRegistersAsync(_unitId, ToUShort(start), registers.ToArray(), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task WriteCoilRunsAsync(
        List<PlcWriteRequest> items,
        CancellationToken cancellationToken)
    {
        items.Sort(static (left, right) => left.Address.Offset.CompareTo(right.Address.Offset));

        var index = 0;
        while (index < items.Count)
        {
            var start = items[index].Address.Offset;
            EnsureBool(items[index].Address);
            var values = new List<bool> { items[index].Value.AsBool() };
            index++;

            while (index < items.Count && items[index].Address.Offset == start + values.Count)
            {
                EnsureBool(items[index].Address);
                if (values.Count == MaxCoilsPerWrite)
                {
                    break;
                }

                values.Add(items[index].Value.AsBool());
                index++;
            }

            if (values.Count == 1)
            {
                await _client.WriteSingleCoilAsync(_unitId, ToUShort(start), values[0], cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _client.WriteMultipleCoilsAsync(_unitId, ToUShort(start), values.ToArray(), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static Dictionary<string, List<PlcWriteRequest>> GroupRequestsByRegion(
        IReadOnlyList<PlcWriteRequest> requests)
    {
        var groups = new Dictionary<string, List<PlcWriteRequest>>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            if (!groups.TryGetValue(request.Address.Region, out var list))
            {
                list = new List<PlcWriteRequest>();
                groups[request.Address.Region] = list;
            }

            list.Add(request);
        }

        return groups;
    }

    /// <inheritdoc/>
    protected override async Task<PlcValue[]> DoReadBatchAsync(
        IReadOnlyList<PlcAddress> addresses,
        CancellationToken cancellationToken)
    {
        var results = new PlcValue[addresses.Count];

        foreach (var group in GroupByRegion(addresses))
        {
            if (group.Key is ModbusAddressParser.CoilRegion or ModbusAddressParser.DiscreteInputRegion)
            {
                await ReadBitsBatchAsync(group.Key, group.Value, results, cancellationToken).ConfigureAwait(false);
            }
            else if (group.Key is ModbusAddressParser.HoldingRegisterRegion or ModbusAddressParser.InputRegisterRegion)
            {
                await ReadRegistersBatchAsync(group.Key, group.Value, results, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new NotSupportedException($"Unknown Modbus region '{group.Key}'.");
            }
        }

        return results;
    }

    /// <inheritdoc/>
    protected override async ValueTask DisposeCoreAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
        await base.DisposeCoreAsync().ConfigureAwait(false);
    }

    private async Task ReadBitsBatchAsync(
        string region,
        List<(int Index, PlcAddress Address)> items,
        PlcValue[] results,
        CancellationToken cancellationToken)
    {
        items.Sort((left, right) => left.Address.Offset.CompareTo(right.Address.Offset));

        var runStart = 0;
        while (runStart < items.Count)
        {
            var start = items[runStart].Address.Offset;
            var runEnd = runStart + 1;
            var expected = start + 1;

            while (runEnd < items.Count
                   && items[runEnd].Address.Offset == expected
                   && items[runEnd].Address.Offset - start < MaxCoilsPerRequest)
            {
                expected++;
                runEnd++;
            }

            var count = runEnd - runStart;
            var values = region == ModbusAddressParser.CoilRegion
                ? await _client.ReadCoilsAsync(_unitId, ToUShort(start), ToUShort(count), cancellationToken).ConfigureAwait(false)
                : await _client.ReadDiscreteInputsAsync(_unitId, ToUShort(start), ToUShort(count), cancellationToken).ConfigureAwait(false);

            for (var i = runStart; i < runEnd; i++)
            {
                results[items[i].Index] = PlcValue.FromBool(values[items[i].Address.Offset - start]);
            }

            runStart = runEnd;
        }
    }

    private async Task ReadRegistersBatchAsync(
        string region,
        List<(int Index, PlcAddress Address)> items,
        PlcValue[] results,
        CancellationToken cancellationToken)
    {
        items.Sort((left, right) => left.Address.Offset.CompareTo(right.Address.Offset));

        var runStart = 0;
        while (runStart < items.Count)
        {
            var start = items[runStart].Address.Offset;
            var runEnd = runStart + 1;
            var expected = start + GetRegisterCount(items[runStart].Address);

            while (runEnd < items.Count
                   && items[runEnd].Address.DataType == items[runStart].Address.DataType
                   && items[runEnd].Address.Offset == expected)
            {
                var span = GetRegisterCount(items[runEnd].Address);
                if (expected - start + span > MaxRegistersPerRequest)
                {
                    break;
                }

                expected += span;
                runEnd++;
            }

            var count = expected - start;
            var registers = region == ModbusAddressParser.HoldingRegisterRegion
                ? await _client.ReadHoldingRegistersAsync(_unitId, ToUShort(start), ToUShort(count), cancellationToken).ConfigureAwait(false)
                : await _client.ReadInputRegistersAsync(_unitId, ToUShort(start), ToUShort(count), cancellationToken).ConfigureAwait(false);

            for (var i = runStart; i < runEnd; i++)
            {
                var address = items[i].Address;
                var offset = address.Offset - start;
                var span = GetRegisterCount(address);
                results[items[i].Index] = FromRegisters(address, registers.AsSpan(offset, span));
            }

            runStart = runEnd;
        }
    }

    private static Dictionary<string, List<(int Index, PlcAddress Address)>> GroupByRegion(
        IReadOnlyList<PlcAddress> addresses)
    {
        var groups = new Dictionary<string, List<(int, PlcAddress)>>(StringComparer.Ordinal);
        for (var i = 0; i < addresses.Count; i++)
        {
            if (!groups.TryGetValue(addresses[i].Region, out var list))
            {
                list = new List<(int, PlcAddress)>();
                groups[addresses[i].Region] = list;
            }

            list.Add((i, addresses[i]));
        }

        return groups;
    }

    private static void EnsureBool(PlcAddress address)
    {
        if (address.DataType != PlcDataType.Bool)
        {
            throw new NotSupportedException(
                $"Modbus region '{address.Region}' only supports Bool values.");
        }
    }

    private static int GetRegisterCount(PlcAddress address) => address.DataType switch
    {
        PlcDataType.Bool => throw new NotSupportedException(
            $"Region '{address.Region}' cannot be accessed as Bool; use coils or discrete inputs."),
        PlcDataType.Byte or PlcDataType.Int16 or PlcDataType.UInt16 => 1,
        PlcDataType.Int32 or PlcDataType.UInt32 or PlcDataType.Float => 2,
        PlcDataType.Int64 or PlcDataType.UInt64 or PlcDataType.Double => 4,
        PlcDataType.String or PlcDataType.Raw => (address.Length + 1) / 2,
        _ => throw new NotSupportedException($"Unsupported data type {address.DataType}."),
    };

    private static ushort ToUShort(int value)
    {
        if (value is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The Modbus offset must be between 0 and 65535.");
        }

        return (ushort)value;
    }

    private PlcValue FromRegisters(PlcAddress address, ReadOnlySpan<ushort> registers)
    {
        switch (address.DataType)
        {
            case PlcDataType.Byte:
                return PlcValue.FromByte((byte)registers[0]);

            case PlcDataType.Int16:
                return PlcValue.FromInt16(ModbusRegisterConverter.ReadInt16(registers, 0));

            case PlcDataType.UInt16:
                return PlcValue.FromUInt16(ModbusRegisterConverter.ReadUInt16(registers, 0));

            case PlcDataType.Int32:
                return PlcValue.FromInt32(ModbusRegisterConverter.ReadInt32(registers, 0));

            case PlcDataType.UInt32:
                return PlcValue.FromUInt32(ModbusRegisterConverter.ReadUInt32(registers, 0));

            case PlcDataType.Int64:
                return PlcValue.FromInt64(ModbusRegisterConverter.ReadInt64(registers, 0));

            case PlcDataType.UInt64:
                return PlcValue.FromUInt64(ModbusRegisterConverter.ReadUInt64(registers, 0));

            case PlcDataType.Float:
                return PlcValue.FromFloat(ModbusRegisterConverter.ReadSingle(registers, 0));

            case PlcDataType.Double:
                return PlcValue.FromDouble(ModbusRegisterConverter.ReadDouble(registers, 0));

            case PlcDataType.String:
                return PlcValue.FromString(ModbusRegisterConverter.ReadString(
                    registers,
                    0,
                    registers.Length,
                    _stringEncoding,
                    _swapBytes));

            case PlcDataType.Raw:
                {
                    var bytes = ModbusRegisterConverter.ReadBytes(registers, 0, registers.Length, _swapBytes);
                    return PlcValue.FromBytes(bytes.Length > address.Length ? bytes.AsSpan(0, address.Length) : bytes);
                }

            default:
                throw new NotSupportedException($"Unsupported data type {address.DataType}.");
        }
    }

    private ushort[] ToRegisters(PlcAddress address, PlcValue value)
    {
        var count = GetRegisterCount(address);
        var registers = new ushort[count];

        switch (address.DataType)
        {
            case PlcDataType.Byte:
                registers[0] = value.AsByte();
                break;
            case PlcDataType.Int16:
                ModbusRegisterConverter.WriteInt16(registers, 0, value.AsInt16());
                break;
            case PlcDataType.UInt16:
                ModbusRegisterConverter.WriteUInt16(registers, 0, value.AsUInt16());
                break;
            case PlcDataType.Int32:
                ModbusRegisterConverter.WriteInt32(registers, 0, value.AsInt32());
                break;
            case PlcDataType.UInt32:
                ModbusRegisterConverter.WriteUInt32(registers, 0, value.AsUInt32());
                break;
            case PlcDataType.Int64:
                ModbusRegisterConverter.WriteInt64(registers, 0, value.AsInt64());
                break;
            case PlcDataType.UInt64:
                ModbusRegisterConverter.WriteUInt64(registers, 0, value.AsUInt64());
                break;
            case PlcDataType.Float:
                ModbusRegisterConverter.WriteSingle(registers, 0, value.AsFloat());
                break;
            case PlcDataType.Double:
                ModbusRegisterConverter.WriteDouble(registers, 0, value.AsDouble());
                break;
            case PlcDataType.String:
                ModbusRegisterConverter.WriteString(
                    registers, 0, count, value.AsString(), _stringEncoding, _swapBytes);
                break;
            case PlcDataType.Raw:
                ModbusRegisterConverter.WriteBytes(registers, 0, count, value.AsBytes(), _swapBytes);
                break;
            default:
                throw new NotSupportedException($"Unsupported data type {address.DataType}.");
        }

        return registers;
    }
}
