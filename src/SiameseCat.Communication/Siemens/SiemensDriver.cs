using System.Text;
using SiameseCat.Communication;
using SiameseCat.Communication.S7;

namespace SiameseCat.Communication.Siemens;

/// <summary>
/// S7 driver implementing the pluggable <see cref="IPlcDriver"/> contract on top of the S7 client.
/// </summary>
public sealed class SiemensDriver : DriverBase
{
    private const int WriteRequestOverhead = 12;

    private static readonly byte[] OneByte = { 1 };
    private static readonly byte[] ZeroByte = { 0 };

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
                return PlcValueCodec.FromBytes(address.DataType, bytes, address.Length);
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
        var items = new List<S7WriteItem>(requests.Count);
        foreach (var request in requests)
        {
            var s7 = SiemensAddressParser.ToS7Address(request.Address);
            if (request.Address.DataType == PlcDataType.Bool)
            {
                items.Add(new S7WriteItem(s7, request.Value.AsBool() ? OneByte : ZeroByte));
                continue;
            }

            byte[] bytes;
            if (request.Address.DataType == PlcDataType.String)
            {
                bytes = BuildStringBuffer(request.Value.AsString(), request.Address.Length);
            }
            else
            {
                bytes = PlcValueCodec.ToBytes(request.Value);
                if (bytes.Length != request.Address.ByteSize)
                {
                    throw new ArgumentException(
                        $"The value for '{request.Address.Region}:{request.Address.Offset}' needs {bytes.Length} bytes "
                        + $"but the address expects {request.Address.ByteSize}.",
                        nameof(requests));
                }
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
        var items = new S7ReadItem[addresses.Count];
        for (var i = 0; i < addresses.Count; i++)
        {
            var length = addresses[i].DataType switch
            {
                PlcDataType.Bool => 1,
                PlcDataType.String => addresses[i].Length + 2,
                _ => addresses[i].ByteSize,
            };
            items[i] = new S7ReadItem(SiemensAddressParser.ToS7Address(addresses[i]), length);
        }

        var raw = await _client.ReadMultipleAsync(items, cancellationToken).ConfigureAwait(false);
        var values = new PlcValue[addresses.Count];
        for (var i = 0; i < addresses.Count; i++)
        {
            values[i] = addresses[i].DataType switch
            {
                PlcDataType.Bool => PlcValue.FromBool(raw[i][0] != 0),
                PlcDataType.String => PlcValue.FromString(DecodeString(raw[i], addresses[i].Length)),
                _ => PlcValueCodec.FromBytes(addresses[i].DataType, raw[i], addresses[i].Length),
            };
        }

        return values;
    }

    private byte[] BuildStringBuffer(string value, int maxLength)
    {
        if (maxLength is < 0 or > 254)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "S7 string length must be between 0 and 254.");
        }

        var encoded = _encoding.GetBytes(value);
        if (encoded.Length > maxLength)
        {
            throw new ArgumentException(
                $"The encoded string is {encoded.Length} bytes but the maximum length is {maxLength}.",
                nameof(value));
        }

        var buffer = new byte[maxLength + 2];
        buffer[0] = (byte)maxLength;
        buffer[1] = (byte)encoded.Length;
        encoded.CopyTo(buffer, 2);
        return buffer;
    }

    private string DecodeString(byte[] bytes, int maxLength)
    {
        var currentLength = bytes[1];
        if (currentLength > maxLength || currentLength > bytes.Length - 2)
        {
            throw new ProtocolException($"The PLC returned an invalid S7 string length ({currentLength}).");
        }

        return _encoding.GetString(bytes, 2, currentLength);
    }

    /// <inheritdoc/>
    protected override async ValueTask DisposeCoreAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
        await base.DisposeCoreAsync().ConfigureAwait(false);
    }
}
