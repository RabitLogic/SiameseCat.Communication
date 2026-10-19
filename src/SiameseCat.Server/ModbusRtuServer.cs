using System.IO.Ports;

using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Transport;

namespace SiameseCat.Server;

/// <summary>
/// Modbus RTU slave that exposes an in-process <see cref="ModbusDataStore"/> over a serial port.
/// The PDU handling is shared with <see cref="ModbusTcpServer"/>; only the framing differs.
/// </summary>
/// <remarks>
/// <para>Only frames addressed to <see cref="ModbusRtuServerOptions.UnitId"/> are answered.</para>
/// <para>Unit id 0 is treated as a broadcast: writes are applied and no response is sent.</para>
/// <para>Frames with an invalid CRC are ignored, as required by the Modbus specification.</para>
/// <para>
/// An <see cref="ITransport"/> can be injected instead of a serial port, which allows the slave to be
/// used over a tunnel or in tests.
/// </para>
/// </remarks>
public sealed class ModbusRtuServer : IAsyncDisposable
{
    private readonly ModbusRtuServerOptions _options;
    private readonly ModbusPduProcessor _processor;
    private readonly ITransport _transport;
    private readonly bool _ownsTransport;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private bool _disposed;

    /// <summary>Creates a slave that opens the serial port described by <paramref name="options"/>.</summary>
    /// <param name="options">Serial and buffer settings.</param>
    public ModbusRtuServer(ModbusRtuServerOptions options)
        : this(CreateTransport(options), options, ownsTransport: true)
    {
    }

    /// <summary>Creates a slave over a custom transport, for example a serial tunnel or a test double.</summary>
    /// <param name="transport">Byte-oriented transport that carries RTU frames.</param>
    /// <param name="options">Station address and buffer settings.</param>
    public ModbusRtuServer(ITransport transport, ModbusRtuServerOptions options)
        : this(transport, options, ownsTransport: false)
    {
    }

    private ModbusRtuServer(ITransport transport, ModbusRtuServerOptions options, bool ownsTransport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _ownsTransport = ownsTransport;
        Data = new ModbusDataStore(
            _options.HoldingRegisters,
            _options.InputRegisters,
            _options.Coils,
            _options.DiscreteInputs);
        _processor = new ModbusPduProcessor(Data);
    }

    /// <summary>Gets the register and coil storage that the slave exposes.</summary>
    public ModbusDataStore Data { get; }

    /// <summary>Gets whether the slave is currently listening for frames.</summary>
    public bool IsRunning => _runTask is { IsCompleted: false };

    /// <summary>Opens the transport and starts processing frames.</summary>
    /// <param name="cancellationToken">Cancels the frame loop.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Guard.NotDisposed(_disposed, this);

        if (IsRunning)
        {
            return;
        }

        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await _transport.OpenAsync(_cts.Token).ConfigureAwait(false);
        _runTask = RunAsync(_cts.Token);
    }

    /// <summary>Stops the frame loop and closes the transport.</summary>
    public async Task StopAsync()
    {
        var runTask = _runTask;
        _runTask = null;

        _cts?.Cancel();

        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        if (_ownsTransport)
        {
            await _transport.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();

        if (_ownsTransport)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        var buffer = new byte[512];

        while (!token.IsCancellationRequested)
        {
            int length;
            try
            {
                length = await ReadFrameAsync(buffer, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ConnectionClosedException)
            {
                return;
            }

            if (length < 4)
            {
                continue;
            }

            var frame = buffer.AsSpan(0, length);
            if (!ModbusCrc16.Validate(frame))
            {
                continue;
            }

            var address = frame[0];
            if (address != _options.UnitId && address != 0)
            {
                continue;
            }

            var pdu = frame.Slice(1, length - 3).ToArray();
            var response = _processor.Process(address, pdu);
            if (response is null)
            {
                continue;
            }

            var responseFrame = new byte[1 + response.Length + 2];
            responseFrame[0] = address;
            response.CopyTo(responseFrame, 1);
            ModbusCrc16.Write(
                responseFrame.AsSpan(0, responseFrame.Length - 2),
                responseFrame.AsSpan(responseFrame.Length - 2));

            try
            {
                await _transport.WriteAsync(responseFrame, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ConnectionClosedException or CommunicationTimeoutException)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<int> ReadFrameAsync(byte[] buffer, CancellationToken token)
    {
        if (!await TryReadAsync(buffer, 0, 2, token).ConfigureAwait(false))
        {
            return 0;
        }

        var function = buffer[1];
        int expected;

        switch (function)
        {
            case 0x0F:
            case 0x10:
                if (!await TryReadAsync(buffer, 2, 5, token).ConfigureAwait(false))
                {
                    return 0;
                }

                expected = 9 + buffer[6];
                break;

            case 0x17:
                if (!await TryReadAsync(buffer, 2, 9, token).ConfigureAwait(false))
                {
                    return 0;
                }

                expected = 13 + buffer[10];
                break;

            default:
                // Fixed-length requests (FC01..FC06) and unknown functions, which are answered
                // with an illegal-function exception as soon as the header is available.
                expected = function is >= 0x01 and <= 0x06 ? 8 : 2;
                break;
        }

        if (expected > buffer.Length)
        {
            return 0;
        }

        var count = function is 0x0F or 0x10 ? 7 : function == 0x17 ? 11 : 2;
        if (count < expected && !await TryReadAsync(buffer, count, expected - count, token).ConfigureAwait(false))
        {
            return 0;
        }

        return expected;
    }

    private async Task<bool> TryReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
    {
        var read = 0;
        while (read < count)
        {
            try
            {
                var received = await _transport
                    .ReadAsync(buffer.AsMemory(offset + read, count - read), token)
                    .ConfigureAwait(false);

                if (received <= 0)
                {
                    return false;
                }

                read += received;
            }
            catch (CommunicationTimeoutException)
            {
                return false;
            }
        }

        return true;
    }

    private static SerialPortTransport CreateTransport(ModbusRtuServerOptions options)
    {
        Guard.NotNull(options, nameof(options));
        var serial = options.Serial;

        return new SerialPortTransport(new SerialPortOptions
        {
            PortName = serial.PortName,
            BaudRate = serial.BaudRate,
            DataBits = serial.DataBits,
            Parity = ParseParity(serial.Parity),
            StopBits = ParseStopBits(serial.StopBits),
            Handshake = ParseHandshake(serial.Handshake),
            ReadTimeout = serial.ReadTimeout,
        });
    }

    private static Parity ParseParity(string value) => value?.ToUpperInvariant() switch
    {
        null or "" or "NONE" => Parity.None,
        "ODD" => Parity.Odd,
        "EVEN" => Parity.Even,
        "MARK" => Parity.Mark,
        "SPACE" => Parity.Space,
        _ => throw new ArgumentException(
            $"Unknown parity '{value}'. Use none, odd, even, mark or space.", nameof(value)),
    };

    private static StopBits ParseStopBits(string value) => value?.ToUpperInvariant() switch
    {
        null or "" or "ONE" => StopBits.One,
        "ONEPOINTFIVE" or "1.5" => StopBits.OnePointFive,
        "TWO" or "2" => StopBits.Two,
        _ => throw new ArgumentException(
            $"Unknown stop bits '{value}'. Use one, onePointFive or two.", nameof(value)),
    };

    private static Handshake ParseHandshake(string value) => value?.ToUpperInvariant() switch
    {
        null or "" or "NONE" => Handshake.None,
        "REQUESTTOSEND" or "RTS" => Handshake.RequestToSend,
        "REQUESTTOSENDXONXOFF" or "RTSXONXOFF" => Handshake.RequestToSendXOnXOff,
        "XONXOFF" => Handshake.XOnXOff,
        _ => throw new ArgumentException(
            $"Unknown handshake '{value}'. Use none, requestToSend, requestToSendXOnXOff or xOnXOff.",
            nameof(value)),
    };
}
