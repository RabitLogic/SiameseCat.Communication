using System.Buffers;
using System.IO.Ports;
using SiameseCat.Core;
using SiameseCat.Transport;

namespace SiameseCat.Protocols.Modbus;

/// <summary>Modbus RTU master over a serial port.</summary>
public sealed class ModbusRtuClient : ModbusClientBase
{
    private const int MaxFrameLength = 256;

    private readonly ModbusRtuOptions _rtuOptions;
    private TransportReader? _reader;

    /// <summary>Creates an RTU client that opens the serial port configured in <paramref name="options"/>.</summary>
    /// <param name="options">RTU and serial port options.</param>
    public ModbusRtuClient(ModbusRtuOptions options)
        : this(CreateTransport(options), options)
    {
    }

    /// <summary>
    /// Creates an RTU client over a custom transport. Intended for testing or for transports
    /// that are not based on <see cref="SerialPortTransport"/>.
    /// </summary>
    /// <param name="transport">Transport that carries RTU frames.</param>
    /// <param name="options">RTU options, used for timeouts, retries and frame spacing.</param>
    public ModbusRtuClient(ITransport transport, ModbusRtuOptions options)
        : base(transport, options)
    {
        Guard.NotNull(options, nameof(options));
        options.SerialPort.Validate();
        _rtuOptions = options;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteBroadcastCoreAsync(
        byte unitId,
        byte[] pdu,
        CancellationToken cancellationToken)
    {
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(MaxFrameLength);

        try
        {
            var frameLength = BuildFrame(buffer, unitId, pdu);

            var interFrameDelay = _rtuOptions.InterFrameDelay ?? CalculateInterFrameDelay();
            if (interFrameDelay > TimeSpan.Zero)
            {
                await Task.Delay(interFrameDelay, cancellationToken).ConfigureAwait(false);
            }

            Transport.DiscardInput();
            await Transport.WriteAsync(buffer.AsMemory(0, frameLength), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    /// <inheritdoc/>
    protected override async Task<byte[]> ExecutePduCoreAsync(
        byte unitId,
        byte[] pdu,
        CancellationToken cancellationToken)
    {
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(MaxFrameLength);

        try
        {
            // Frames live in the first half of the buffer, responses in the second half.
            const int ResponseOffset = MaxFrameLength / 2;
            var frameLength = BuildFrame(buffer, unitId, pdu);

            var interFrameDelay = _rtuOptions.InterFrameDelay ?? CalculateInterFrameDelay();
            if (interFrameDelay > TimeSpan.Zero)
            {
                await Task.Delay(interFrameDelay, cancellationToken).ConfigureAwait(false);
            }

            Transport.DiscardInput();
            await Transport.WriteAsync(buffer.AsMemory(0, frameLength), cancellationToken).ConfigureAwait(false);

            var reader = _reader ??= new TransportReader(Transport);

            if (_rtuOptions.DiscardEcho)
            {
                var echo = buffer.AsMemory(ResponseOffset, frameLength);
                await reader.ReadExactlyAsync(echo, cancellationToken).ConfigureAwait(false);
                if (!echo.Span.SequenceEqual(buffer.AsSpan(0, frameLength)))
                {
                    reader.Prepend(echo.Span);
                }
            }

            await reader.ReadExactlyAsync(
                buffer.AsMemory(ResponseOffset, 2), cancellationToken).ConfigureAwait(false);

            if (buffer[ResponseOffset] != unitId)
            {
                throw new ProtocolException(
                    $"The Modbus RTU response has unit id {buffer[ResponseOffset]} but {unitId} was expected.");
            }

            byte functionCode = buffer[ResponseOffset + 1];

            if (functionCode == (byte)(pdu[0] | 0x80))
            {
                await reader.ReadExactlyAsync(
                    buffer.AsMemory(ResponseOffset + 2, 3), cancellationToken).ConfigureAwait(false);
                ValidateCrc(buffer.AsSpan(ResponseOffset, 5));

                // The base client turns this PDU into a ModbusException with request context.
                return new[] { functionCode, buffer[ResponseOffset + 2] };
            }

            if (functionCode != pdu[0])
            {
                throw new ProtocolException(
                    $"The Modbus RTU response has function code 0x{functionCode:X2} instead of 0x{pdu[0]:X2}.");
            }

            int responseLength;
            if (functionCode is 0x01 or 0x02 or 0x03 or 0x04 or 0x17)
            {
                await reader.ReadExactlyAsync(
                    buffer.AsMemory(ResponseOffset + 2, 1), cancellationToken).ConfigureAwait(false);

                var byteCount = buffer[ResponseOffset + 2];
                await reader.ReadExactlyAsync(
                    buffer.AsMemory(ResponseOffset + 3, byteCount + 2), cancellationToken).ConfigureAwait(false);
                responseLength = 3 + byteCount + 2;
            }
            else
            {
                await reader.ReadExactlyAsync(
                    buffer.AsMemory(ResponseOffset + 2, 6), cancellationToken).ConfigureAwait(false);
                responseLength = 8;
            }

            ValidateCrc(new ReadOnlySpan<byte>(buffer, ResponseOffset, responseLength));
            return new ReadOnlySpan<byte>(buffer, ResponseOffset + 1, responseLength - 3).ToArray();
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    private static SerialPortTransport CreateTransport(ModbusRtuOptions options)
    {
        Guard.NotNull(options, nameof(options));
        return new SerialPortTransport(options.SerialPort);
    }

    private static int BuildFrame(Span<byte> frame, byte unitId, ReadOnlySpan<byte> pdu)
    {
        var length = pdu.Length + 3;
        frame[0] = unitId;
        pdu.CopyTo(frame[1..]);
        ModbusCrc16.Write(frame.Slice(0, length - 2), frame.Slice(length - 2));
        return length;
    }

    private static void ValidateCrc(ReadOnlySpan<byte> frame)
    {
        if (!ModbusCrc16.Validate(frame))
        {
            throw new ProtocolException("The Modbus RTU response failed the CRC check.");
        }
    }

    private TimeSpan CalculateInterFrameDelay()
    {
        var serial = _rtuOptions.SerialPort;
        double stopBits = serial.StopBits switch
        {
            StopBits.OnePointFive => 1.5,
            StopBits.Two => 2.0,
            _ => 1.0,
        };

        double bitsPerCharacter = 1 + serial.DataBits + (serial.Parity == Parity.None ? 0 : 1) + stopBits;
        double milliseconds = Math.Max(2.0, 3.5 * bitsPerCharacter * 1000.0 / serial.BaudRate);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
