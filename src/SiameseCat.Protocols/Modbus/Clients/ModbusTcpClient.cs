using SiameseCat.Core;
using SiameseCat.Transport;

namespace SiameseCat.Protocols.Modbus;

/// <summary>Modbus TCP master.</summary>
public sealed class ModbusTcpClient : ModbusClientBase
{
    private ushort _transactionId;

    /// <summary>Creates a Modbus TCP client that connects to the endpoint configured in <paramref name="options"/>.</summary>
    /// <param name="options">TCP and protocol options.</param>
    public ModbusTcpClient(ModbusTcpOptions options)
        : this(CreateTransport(options), options)
    {
    }

    /// <summary>Creates a Modbus TCP client over a custom transport, primarily for testing.</summary>
    /// <param name="transport">Transport that carries MBAP frames.</param>
    /// <param name="options">Protocol options, used for timeouts and retries.</param>
    public ModbusTcpClient(ITransport transport, ModbusTcpOptions options)
        : base(transport, options)
    {
        Guard.NotNull(options, nameof(options));
        options.Tcp.Validate();
    }

    /// <inheritdoc/>
    protected override bool CloseOnTimeout => true;

    /// <inheritdoc/>
    protected override async Task<byte[]> ExecutePduCoreAsync(
        byte unitId,
        byte[] pdu,
        CancellationToken cancellationToken)
    {
        ushort transactionId = unchecked(++_transactionId);
        byte[] frame = BuildFrame(transactionId, unitId, pdu);

        Transport.DiscardInput();
        await Transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);

        byte[] header = await Transport.ReadExactlyAsync(7, cancellationToken).ConfigureAwait(false);

        ushort responseTransactionId = (ushort)((header[0] << 8) | header[1]);
        if (responseTransactionId != transactionId)
        {
            throw new ProtocolException(
                $"The Modbus TCP response has transaction id {responseTransactionId} but {transactionId} was expected.");
        }

        ushort protocolId = (ushort)((header[2] << 8) | header[3]);
        if (protocolId != 0)
        {
            throw new ProtocolException($"The Modbus TCP response has protocol id {protocolId} instead of 0.");
        }

        ushort length = (ushort)((header[4] << 8) | header[5]);
        if (length is < 2 or > 254)
        {
            throw new ProtocolException($"The Modbus TCP response has invalid length {length}.");
        }

        if (header[6] != unitId)
        {
            throw new ProtocolException(
                $"The Modbus TCP response has unit id {header[6]} but {unitId} was expected.");
        }

        return await Transport.ReadExactlyAsync(length - 1, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteBroadcastCoreAsync(
        byte unitId,
        byte[] pdu,
        CancellationToken cancellationToken)
    {
        ushort transactionId = unchecked(++_transactionId);
        byte[] frame = BuildFrame(transactionId, unitId, pdu);

        Transport.DiscardInput();
        await Transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private static TcpTransport CreateTransport(ModbusTcpOptions options)
    {
        Guard.NotNull(options, nameof(options));
        return new TcpTransport(options.Tcp);
    }

    private static byte[] BuildFrame(ushort transactionId, byte unitId, byte[] pdu)
    {
        byte[] frame = new byte[7 + pdu.Length];
        frame[0] = (byte)(transactionId >> 8);
        frame[1] = (byte)transactionId;
        frame[2] = 0;
        frame[3] = 0;
        ushort length = (ushort)(pdu.Length + 1);
        frame[4] = (byte)(length >> 8);
        frame[5] = (byte)length;
        frame[6] = unitId;
        pdu.CopyTo(frame, 7);
        return frame;
    }
}
