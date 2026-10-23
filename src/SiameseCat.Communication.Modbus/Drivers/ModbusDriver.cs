using System.Text;

using SiameseCat.Communication;
using SiameseCat.Communication.Diagnostics;
using SiameseCat.Communication.Drivers;

namespace SiameseCat.Communication.Modbus;

/// <summary>
/// Modbus driver implementing the pluggable <see cref="IDeviceDriver"/> contract on top of the
/// Modbus PDU client. TCP and RTU share all address and data conversion logic; only the client
/// (framing) differs.
/// </summary>
public partial class ModbusDriver : DriverBase
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
    /// <param name="stringEncoding">Encoding used for <see cref="DeviceDataType.String"/>; defaults to ASCII.</param>
    /// <param name="swapBytes">Swaps the two bytes inside every register for strings and raw data.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="metrics">Optional metrics collector.</param>
    public ModbusDriver(
        ModbusClientBase client,
        DriverConfiguration configuration,
        byte unitId,
        Encoding? stringEncoding = null,
        bool swapBytes = false,
        ICommunicationLogger? logger = null,
        CommunicationMetrics? metrics = null)
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
    /// drivers that only differ in address mapping.
    /// </summary>
    /// <param name="client">Modbus client that performs the framed exchange.</param>
    /// <param name="configuration">Connection and retry settings.</param>
    /// <param name="unitId">Unit identifier used for requests.</param>
    /// <param name="descriptor">Descriptor reported by the driver.</param>
    /// <param name="addressParser">Parser that maps native addresses to Modbus regions; defaults to <see cref="ModbusAddressParser"/>.</param>
    /// <param name="stringEncoding">Encoding used for <see cref="DeviceDataType.String"/>; defaults to ASCII.</param>
    /// <param name="swapBytes">Swaps the two bytes inside every register for strings and raw data.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="metrics">Optional metrics collector.</param>
    protected ModbusDriver(
        ModbusClientBase client,
        DriverConfiguration configuration,
        byte unitId,
        DriverDescriptor descriptor,
        IDeviceAddressParser? addressParser,
        Encoding? stringEncoding,
        bool swapBytes,
        ICommunicationLogger? logger,
        CommunicationMetrics? metrics)
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
        DeviceAddress writeAddress,
        IReadOnlyList<ushort> writeValues,
        DeviceAddress readAddress,
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
            CommunicationOperation.ReadWrite,
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
    protected override async ValueTask DisposeCoreAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
        await base.DisposeCoreAsync().ConfigureAwait(false);
    }
}
