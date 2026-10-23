using System.IO.Ports;
using System.Text;

using SiameseCat.Communication;
using SiameseCat.Communication.Diagnostics;
using SiameseCat.Communication.Drivers;
using SiameseCat.Communication.Transport;
using SiameseCat.Communication.Transport.Serial;
using SiameseCat.Communication.Transport.Tcp;

namespace SiameseCat.Communication.Modbus;

/// <summary>
/// Builds Modbus clients from a pluggable <see cref="DriverConfiguration"/>.
/// </summary>
/// <remarks>
/// Shared by the generic Modbus driver and by vendor drivers that speak Modbus, so transport, unit id
/// and serial settings are configured in exactly one place.
/// </remarks>
public static class ModbusClientFactory
{
    /// <summary>Gets the Modbus TCP default port.</summary>
    public const int DefaultTcpPort = 502;

    /// <summary>Reads the unit id from the <c>unitId</c> option, defaulting to 1.</summary>
    /// <param name="configuration">Driver configuration.</param>
    /// <remarks>0 selects broadcast mode: writes are sent without waiting for a response and reads fail.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The unit id is above 255.</exception>
    public static byte GetUnitId(DriverConfiguration configuration)
    {
        Guard.NotNull(configuration, nameof(configuration));

        return configuration.GetByteOption("unitId", 1);
    }

    /// <summary>Reads the <c>encoding</c> option, defaulting to ASCII.</summary>
    /// <param name="configuration">Driver configuration.</param>
    public static Encoding GetEncoding(DriverConfiguration configuration)
    {
        Guard.NotNull(configuration, nameof(configuration));
        return ParseEncoding(configuration.GetOption("encoding"));
    }

    /// <summary>Reads the <c>swapBytes</c> option, defaulting to <see langword="false"/>.</summary>
    /// <param name="configuration">Driver configuration.</param>
    public static bool GetSwapBytes(DriverConfiguration configuration)
    {
        Guard.NotNull(configuration, nameof(configuration));
        return configuration.GetBooleanOption("swapBytes", false);
    }

    /// <summary>Creates a Modbus TCP or RTU client for the transport in <paramref name="configuration"/>.</summary>
    /// <param name="configuration">Driver configuration.</param>
    /// <param name="unitId">Unit identifier used for requests.</param>
    /// <param name="defaultPort">Port used when the configuration does not specify one.</param>
    /// <param name="logger">Optional logger that receives frame dumps when <see cref="CommunicationOptions.LogFrames"/> is enabled.</param>
    public static ModbusClientBase Create(
        DriverConfiguration configuration,
        byte unitId,
        int defaultPort = DefaultTcpPort,
        ICommunicationLogger? logger = null)
    {
        Guard.NotNull(configuration, nameof(configuration));

        return configuration.Transport switch
        {
            TransportKind.Tcp => CreateTcp(configuration, unitId, defaultPort, logger),
            TransportKind.Serial => CreateRtu(configuration, unitId, logger),
            TransportKind.Custom => CreateCustom(configuration, unitId, defaultPort, logger),
            _ => throw new NotSupportedException(
                $"Modbus does not support transport '{configuration.Transport}'. Use Tcp, Serial or Custom."),
        };
    }

    /// <summary>Parses the encoding name accepted by the <c>encoding</c> option.</summary>
    /// <param name="name">Option value.</param>
    public static Encoding ParseEncoding(string? name) => name?.ToUpperInvariant() switch
    {
        null or "" or "ASCII" => Encoding.ASCII,
        "UTF8" or "UTF-8" => Encoding.UTF8,
        "LATIN1" or "ISO-8859-1" => Encoding.Latin1,
        _ => throw new ArgumentException($"Unknown string encoding '{name}'.", nameof(name)),
    };

    private static ModbusTcpClient CreateTcp(
        DriverConfiguration configuration,
        byte unitId,
        int defaultPort,
        ICommunicationLogger? logger)
    {
        var options = CreateTcpOptions(configuration, unitId, defaultPort, logger);
        var transport = configuration.TransportFactory?.Create(configuration);

        return transport is null
            ? new ModbusTcpClient(options)
            : new ModbusTcpClient(transport, options);
    }

    private static ModbusRtuClient CreateRtu(
        DriverConfiguration configuration,
        byte unitId,
        ICommunicationLogger? logger)
    {
        var options = CreateRtuOptions(configuration, unitId, logger);
        var transport = configuration.TransportFactory?.Create(configuration);

        return transport is null
            ? new ModbusRtuClient(options)
            : new ModbusRtuClient(transport, options);
    }

    /// <summary>
    /// Builds a client over an injected transport. The framing is chosen by the <c>framing</c> option:
    /// <c>tcp</c> (default, MBAP header) or <c>rtu</c> (address + CRC).
    /// </summary>
    private static ModbusClientBase CreateCustom(
        DriverConfiguration configuration,
        byte unitId,
        int defaultPort,
        ICommunicationLogger? logger)
    {
        if (configuration.TransportFactory is null)
        {
            throw new ArgumentException(
                "A TransportFactory is required for the Custom transport.",
                nameof(configuration));
        }

        return configuration.GetOption("framing")?.ToUpperInvariant() switch
        {
            null or "" or "TCP" or "MBAP" => CreateTcp(configuration, unitId, defaultPort, logger),
            "RTU" => CreateRtu(configuration, unitId, logger),
            var framing => throw new ArgumentException(
                $"Unknown Modbus framing '{framing}'. Use tcp or rtu.",
                nameof(configuration)),
        };
    }

    private static ModbusTcpOptions CreateTcpOptions(
        DriverConfiguration configuration,
        byte unitId,
        int defaultPort,
        ICommunicationLogger? logger) => new()
        {
            UnitId = unitId,
            Timeout = configuration.Timeout,
            RetryCount = 0,
            RetryDelay = TimeSpan.Zero,
            LogFrames = configuration.LogFrames,
            Logger = logger,
            Tcp = new TcpTransportOptions
            {
                Host = configuration.Host,
                Port = configuration.Port == 0 ? defaultPort : configuration.Port,
                ConnectTimeout = configuration.ConnectTimeout,
            },
        };

    private static ModbusRtuOptions CreateRtuOptions(
        DriverConfiguration configuration,
        byte unitId,
        ICommunicationLogger? logger)
    {
        var serial = configuration.Serial;
        var interFrameDelayMs = configuration.GetOption("interFrameDelayMs", 0, 0, 60_000);

        return new ModbusRtuOptions
        {
            UnitId = unitId,
            Timeout = configuration.Timeout,
            RetryCount = 0,
            RetryDelay = TimeSpan.Zero,
            LogFrames = configuration.LogFrames,
            Logger = logger,
            DiscardEcho = configuration.GetBooleanOption("discardEcho", false),
            InterFrameDelay = interFrameDelayMs > 0 ? TimeSpan.FromMilliseconds(interFrameDelayMs) : null,
            SerialPort = new SerialPortOptions
            {
                PortName = serial.PortName,
                BaudRate = serial.BaudRate,
                DataBits = serial.DataBits,
                Parity = ParseParity(serial.Parity),
                StopBits = ParseStopBits(serial.StopBits),
                Handshake = ParseHandshake(serial.Handshake),
                ReadTimeout = configuration.Timeout,
            },
        };
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
        _ => throw new ArgumentException($"Unknown stop bits '{value}'. Use one, onePointFive or two.", nameof(value)),
    };

    private static Handshake ParseHandshake(string value) => value?.ToUpperInvariant() switch
    {
        null or "" or "NONE" => Handshake.None,
        "REQUESTTOSEND" or "RTS" => Handshake.RequestToSend,
        "REQUESTTOSENDXONXOFF" => Handshake.RequestToSendXOnXOff,
        "XONXOFF" => Handshake.XOnXOff,
        _ => throw new ArgumentException($"Unknown handshake '{value}'.", nameof(value)),
    };
}
