using System.Buffers;

using SiameseCat.Communication;
using SiameseCat.Communication.Exceptions;
using SiameseCat.Communication.Transport;
using SiameseCat.Communication.Transport.Tcp;

namespace SiameseCat.Communication.Modbus;

/// <summary>Modbus TCP master.</summary>
public sealed class ModbusTcpClient : ModbusClientBase
{
    private const int MaxResponseLength = 254;

    private readonly TcpFrameConnection _connection;

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
        _connection = new TcpFrameConnection(Transport, new MbapFrameCodec());
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
        var pool = ArrayPool<byte>.Shared;
        var frame = pool.Rent(7 + pdu.Length);

        try
        {
            BuildFrame(frame, transactionId, unitId, pdu);

            Transport.DiscardInput();
            var response = await _connection
                .ExchangeAsync(frame.AsMemory(0, 7 + pdu.Length), cancellationToken)
                .ConfigureAwait(false);

            ushort responseTransactionId = (ushort)((response[0] << 8) | response[1]);
            if (responseTransactionId != transactionId)
            {
                throw new ProtocolException(
                    $"The Modbus TCP response has transaction id {responseTransactionId} but {transactionId} was expected.");
            }

            ushort protocolId = (ushort)((response[2] << 8) | response[3]);
            if (protocolId != 0)
            {
                throw new ProtocolException($"The Modbus TCP response has protocol id {protocolId} instead of 0.");
            }

            ushort length = (ushort)((response[4] << 8) | response[5]);
            if (length is < 2 or > MaxResponseLength)
            {
                throw new ProtocolException($"The Modbus TCP response has invalid length {length}.");
            }

            if (response[6] != unitId)
            {
                throw new ProtocolException(
                    $"The Modbus TCP response has unit id {response[6]} but {unitId} was expected.");
            }

            return response.AsSpan(7, length - 1).ToArray();
        }
        finally
        {
            pool.Return(frame);
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteBroadcastCoreAsync(
        byte unitId,
        byte[] pdu,
        CancellationToken cancellationToken)
    {
        ushort transactionId = unchecked(++_transactionId);
        var pool = ArrayPool<byte>.Shared;
        var frame = pool.Rent(7 + pdu.Length);

        try
        {
            BuildFrame(frame, transactionId, unitId, pdu);

            Transport.DiscardInput();
            await _connection
                .WriteAsync(frame.AsMemory(0, 7 + pdu.Length), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            pool.Return(frame);
        }
    }

    private static TcpTransport CreateTransport(ModbusTcpOptions options)
    {
        Guard.NotNull(options, nameof(options));
        return new TcpTransport(options.Tcp);
    }

    private static void BuildFrame(Span<byte> frame, ushort transactionId, byte unitId, ReadOnlySpan<byte> pdu)
    {
        frame[0] = (byte)(transactionId >> 8);
        frame[1] = (byte)transactionId;
        frame[2] = 0;
        frame[3] = 0;
        ushort length = (ushort)(pdu.Length + 1);
        frame[4] = (byte)(length >> 8);
        frame[5] = (byte)length;
        frame[6] = unitId;
        pdu.CopyTo(frame[7..]);
    }
}
