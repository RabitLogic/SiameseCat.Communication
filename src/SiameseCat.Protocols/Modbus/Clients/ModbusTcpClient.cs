using System.Buffers;

using SiameseCat.Core;
using SiameseCat.Transport;

namespace SiameseCat.Protocols.Modbus;

/// <summary>Modbus TCP master.</summary>
public sealed class ModbusTcpClient : ModbusClientBase
{
    private const int MaxResponseLength = 254;

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
        var pool = ArrayPool<byte>.Shared;
        var frame = pool.Rent(7 + pdu.Length + MaxResponseLength);

        try
        {
            BuildFrame(frame, transactionId, unitId, pdu);

            Transport.DiscardInput();
            await Transport
                .WriteAsync(frame.AsMemory(0, 7 + pdu.Length), cancellationToken)
                .ConfigureAwait(false);

            // Header and response body are read into the following bytes of the same buffer.
            var headerOffset = 7 + pdu.Length;
            await Transport
                .ReadExactlyAsync(frame.AsMemory(headerOffset, 7), cancellationToken)
                .ConfigureAwait(false);

            ushort responseTransactionId = (ushort)((frame[headerOffset] << 8) | frame[headerOffset + 1]);
            if (responseTransactionId != transactionId)
            {
                throw new ProtocolException(
                    $"The Modbus TCP response has transaction id {responseTransactionId} but {transactionId} was expected.");
            }

            ushort protocolId = (ushort)((frame[headerOffset + 2] << 8) | frame[headerOffset + 3]);
            if (protocolId != 0)
            {
                throw new ProtocolException($"The Modbus TCP response has protocol id {protocolId} instead of 0.");
            }

            ushort length = (ushort)((frame[headerOffset + 4] << 8) | frame[headerOffset + 5]);
            if (length is < 2 or > MaxResponseLength)
            {
                throw new ProtocolException($"The Modbus TCP response has invalid length {length}.");
            }

            if (frame[headerOffset + 6] != unitId)
            {
                throw new ProtocolException(
                    $"The Modbus TCP response has unit id {frame[headerOffset + 6]} but {unitId} was expected.");
            }

            var pduLength = length - 1;
            var bodyOffset = headerOffset + 7;
            await Transport
                .ReadExactlyAsync(frame.AsMemory(bodyOffset, pduLength), cancellationToken)
                .ConfigureAwait(false);

            return frame.AsSpan(bodyOffset, pduLength).ToArray();
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
            await Transport
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
