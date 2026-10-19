using System.IO.Ports;
using SiameseCat.Core;
using SiameseCat.Transport;

namespace SiameseCat.Protocols.Modbus;

/// <summary>Modbus RTU master over a serial port.</summary>
public sealed class ModbusRtuClient : ModbusClientBase
{
    private readonly ModbusRtuOptions _rtuOptions;

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
        ArgumentNullException.ThrowIfNull(options);
        options.SerialPort.Validate();
        _rtuOptions = options;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteBroadcastCoreAsync(
        byte unitId,
        byte[] pdu,
        CancellationToken cancellationToken)
    {
        byte[] frame = BuildFrame(unitId, pdu);

        var interFrameDelay = _rtuOptions.InterFrameDelay ?? CalculateInterFrameDelay();
        if (interFrameDelay > TimeSpan.Zero)
        {
            await Task.Delay(interFrameDelay, cancellationToken).ConfigureAwait(false);
        }

        Transport.DiscardInput();
        await Transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task<byte[]> ExecutePduCoreAsync(
        byte unitId,
        byte[] pdu,
        CancellationToken cancellationToken)
    {
        byte[] frame = BuildFrame(unitId, pdu);

        var interFrameDelay = _rtuOptions.InterFrameDelay ?? CalculateInterFrameDelay();
        if (interFrameDelay > TimeSpan.Zero)
        {
            await Task.Delay(interFrameDelay, cancellationToken).ConfigureAwait(false);
        }

        Transport.DiscardInput();
        await Transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);

        var reader = new TransportReader(Transport);

        if (_rtuOptions.DiscardEcho)
        {
            byte[] echo = await reader.ReadExactlyAsync(frame.Length, cancellationToken).ConfigureAwait(false);
            if (!echo.AsSpan().SequenceEqual(frame))
            {
                reader.Prepend(echo);
            }
        }

        byte[] header = await reader.ReadExactlyAsync(2, cancellationToken).ConfigureAwait(false);
        if (header[0] != unitId)
        {
            throw new ProtocolException(
                $"The Modbus RTU response has unit id {header[0]} but {unitId} was expected.");
        }

        byte functionCode = header[1];

        if (functionCode == (byte)(pdu[0] | 0x80))
        {
            byte[] tail = await reader.ReadExactlyAsync(3, cancellationToken).ConfigureAwait(false);
            byte[] exceptionFrame = new byte[5];
            header.CopyTo(exceptionFrame, 0);
            tail.CopyTo(exceptionFrame, 2);
            ValidateCrc(exceptionFrame);

            // The base client turns this PDU into a ModbusException with request context.
            return new[] { functionCode, tail[0] };
        }

        if (functionCode != pdu[0])
        {
            throw new ProtocolException(
                $"The Modbus RTU response has function code 0x{functionCode:X2} instead of 0x{pdu[0]:X2}.");
        }

        byte[] responseFrame;
        if (functionCode is 0x01 or 0x02 or 0x03 or 0x04 or 0x17)
        {
            byte[] byteCount = await reader.ReadExactlyAsync(1, cancellationToken).ConfigureAwait(false);
            byte[] payload = await reader.ReadExactlyAsync(byteCount[0] + 2, cancellationToken).ConfigureAwait(false);

            responseFrame = new byte[2 + 1 + payload.Length];
            header.CopyTo(responseFrame, 0);
            byteCount.CopyTo(responseFrame, 2);
            payload.CopyTo(responseFrame, 3);
        }
        else
        {
            byte[] tail = await reader.ReadExactlyAsync(6, cancellationToken).ConfigureAwait(false);
            responseFrame = new byte[8];
            header.CopyTo(responseFrame, 0);
            tail.CopyTo(responseFrame, 2);
        }

        ValidateCrc(responseFrame);
        return responseFrame[1..^2];
    }

    private static SerialPortTransport CreateTransport(ModbusRtuOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new SerialPortTransport(options.SerialPort);
    }

    private static byte[] BuildFrame(byte unitId, byte[] pdu)
    {
        byte[] frame = new byte[pdu.Length + 3];
        frame[0] = unitId;
        pdu.CopyTo(frame, 1);
        ModbusCrc16.Write(frame.AsSpan(0, frame.Length - 2), frame.AsSpan(frame.Length - 2));
        return frame;
    }

    private static void ValidateCrc(byte[] frame)
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
