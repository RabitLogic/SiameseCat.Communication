using System.Text;
using SiameseCat.Core;
using SiameseCat.Protocols.S7;

namespace SiameseCat.Drivers.Siemens;

/// <summary>
/// S7 driver implementing the pluggable <see cref="IPlcDriver"/> contract on top of the S7 client.
/// </summary>
public sealed class SiemensDriver : DriverBase
{
    private const int WriteRequestOverhead = 12;

    private readonly S7Client _client;
    private readonly Encoding _encoding;

    /// <summary>Creates a driver over an existing client, primarily for testing or custom transports.</summary>
    /// <param name="client">S7 client that performs the ISO-on-TCP exchange.</param>
    /// <param name="configuration">Connection and retry settings.</param>
    /// <param name="stringEncoding">Encoding used for <see cref="PlcDataType.String"/>.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="metrics">Optional metrics collector.</param>
    public SiemensDriver(
        S7Client client,
        DriverConfiguration configuration,
        Encoding? stringEncoding = null,
        IPlcLogger? logger = null,
        PlcMetrics? metrics = null)
        : base(SiemensDriverFactory.DefaultDescriptor, new SiemensAddressParser(), configuration, logger, metrics)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _encoding = stringEncoding ?? Encoding.Latin1;
    }

    /// <inheritdoc/>
    protected override Task DoConnectAsync(CancellationToken cancellationToken) =>
        _client.ConnectAsync(cancellationToken);

    /// <inheritdoc/>
    protected override Task DoDisconnectAsync() => _client.DisconnectAsync();

    /// <inheritdoc/>
    protected override async Task<PlcValue> DoReadAsync(PlcAddress address, CancellationToken cancellationToken)
    {
        var s7 = SiemensAddressParser.ToS7Address(address);

        switch (address.DataType)
        {
            case PlcDataType.Bool:
                return PlcValue.FromBool(await _client.ReadBoolAsync(s7, cancellationToken).ConfigureAwait(false));

            case PlcDataType.String:
                return PlcValue.FromString(
                    await _client.ReadStringAsync(s7, address.Length, cancellationToken).ConfigureAwait(false));

            default:
                var bytes = await _client.ReadBytesAsync(s7, address.ByteSize, cancellationToken).ConfigureAwait(false);
                return PlcValueCodec.FromBytes(address.DataType, bytes);
        }
    }

    /// <inheritdoc/>
    protected override async Task DoWriteAsync(
        PlcAddress address,
        PlcValue value,
        CancellationToken cancellationToken)
    {
        var s7 = SiemensAddressParser.ToS7Address(address);

        switch (address.DataType)
        {
            case PlcDataType.Bool:
                await _client.WriteBoolAsync(s7, value.AsBool(), cancellationToken).ConfigureAwait(false);
                return;

            case PlcDataType.String:
                await _client.WriteStringAsync(s7, value.AsString(), address.Length, cancellationToken)
                    .ConfigureAwait(false);
                return;

            default:
                var bytes = PlcValueCodec.ToBytes(value);
                if (bytes.Length != address.ByteSize)
                {
                    throw new ArgumentException(
                        $"The value needs {bytes.Length} bytes but the address expects {address.ByteSize}.",
                        nameof(value));
                }

                await _client.WriteBytesAsync(s7, bytes, cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    /// <inheritdoc/>
    protected override async Task DoWriteBatchAsync(
        IReadOnlyList<PlcWriteRequest> requests,
        CancellationToken cancellationToken)
    {
        // S7 STRING needs the two-byte length header, so fall back to single writes.
        foreach (var request in requests)
        {
            if (request.Address.DataType == PlcDataType.String)
            {
                await base.DoWriteBatchAsync(requests, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var items = new List<S7WriteItem>(requests.Count);
        foreach (var request in requests)
        {
            var s7 = SiemensAddressParser.ToS7Address(request.Address);
            if (request.Address.DataType == PlcDataType.Bool)
            {
                items.Add(new S7WriteItem(s7, new[] { request.Value.AsBool() ? (byte)1 : (byte)0 }));
                continue;
            }

            var bytes = PlcValueCodec.ToBytes(request.Value);
            if (bytes.Length != request.Address.ByteSize)
            {
                throw new ArgumentException(
                    $"The value for '{request.Address.Region}:{request.Address.Offset}' needs {bytes.Length} bytes "
                    + $"but the address expects {request.Address.ByteSize}.",
                    nameof(requests));
            }

            items.Add(new S7WriteItem(s7, bytes));
        }

        // Chunk so each multi-write request fits in the negotiated PDU size.
        var budget = Math.Max(0, _client.PduSize - WriteRequestOverhead);
        var chunk = new List<S7WriteItem>();
        var cost = 0;
        foreach (var item in items)
        {
            var itemCost = WriteItemCost(item.Data.Length);
            if (chunk.Count > 0 && cost + itemCost > budget)
            {
                await _client.WriteMultipleAsync(chunk, cancellationToken).ConfigureAwait(false);
                chunk.Clear();
                cost = 0;
            }

            chunk.Add(item);
            cost += itemCost;
        }

        if (chunk.Count > 0)
        {
            await _client.WriteMultipleAsync(chunk, cancellationToken).ConfigureAwait(false);
        }
    }

    private static int WriteItemCost(int payloadLength) => 12 + 4 + (((payloadLength + 1) / 2) * 2);

    /// <inheritdoc/>
    protected override async Task<PlcValue[]> DoReadBatchAsync(
        IReadOnlyList<PlcAddress> addresses,
        CancellationToken cancellationToken)
    {
        // S7 STRING has a two-byte header and needs the dedicated API, so fall back to single reads.
        foreach (var address in addresses)
        {
            if (address.DataType == PlcDataType.String)
            {
                return await base.DoReadBatchAsync(addresses, cancellationToken).ConfigureAwait(false);
            }
        }

        var items = new S7ReadItem[addresses.Count];
        for (var i = 0; i < addresses.Count; i++)
        {
            var length = addresses[i].DataType == PlcDataType.Bool ? 1 : addresses[i].ByteSize;
            items[i] = new S7ReadItem(SiemensAddressParser.ToS7Address(addresses[i]), length);
        }

        var raw = await _client.ReadMultipleAsync(items, cancellationToken).ConfigureAwait(false);
        var values = new PlcValue[addresses.Count];
        for (var i = 0; i < addresses.Count; i++)
        {
            values[i] = addresses[i].DataType == PlcDataType.Bool
                ? PlcValue.FromBool(raw[i][0] != 0)
                : PlcValueCodec.FromBytes(addresses[i].DataType, raw[i]);
        }

        return values;
    }

    /// <inheritdoc/>
    protected override async ValueTask DisposeCoreAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
        await base.DisposeCoreAsync().ConfigureAwait(false);
    }
}
