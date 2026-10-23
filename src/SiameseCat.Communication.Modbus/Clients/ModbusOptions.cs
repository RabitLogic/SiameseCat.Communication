using SiameseCat.Communication;
using SiameseCat.Communication.Transport;
using SiameseCat.Communication.Transport.Serial;
using SiameseCat.Communication.Transport.Tcp;

namespace SiameseCat.Communication.Modbus;

/// <summary>Common options for Modbus clients.</summary>
public abstract class ModbusOptions : CommunicationOptions
{
    /// <summary>
    /// Default unit identifier used by the convenience overloads. Defaults to 1.
    /// Standard Modbus devices use 1..247; 0 selects broadcast, where writes are sent without
    /// waiting for a response and reads are rejected.
    /// </summary>
    public byte UnitId { get; set; } = 1;
}

/// <summary>Options for a Modbus RTU client.</summary>
public sealed class ModbusRtuOptions : ModbusOptions
{
    /// <summary>Serial port settings used when the client creates its own transport.</summary>
    public SerialPortOptions SerialPort { get; set; } = new();

    /// <summary>
    /// Delay inserted before each request. When <see langword="null"/>, the Modbus 3.5-character
    /// silent interval is calculated from the serial settings.
    /// </summary>
    public TimeSpan? InterFrameDelay { get; set; }

    /// <summary>
    /// Reads back the request frame before reading the response. Enable this for RS-485 adapters
    /// that echo transmitted data. Defaults to <see langword="false"/>.
    /// </summary>
    public bool DiscardEcho { get; set; }

    /// <summary>Validates the option values and throws when a value is out of range.</summary>
    public override void Validate()
    {
        base.Validate();
        SerialPort.Validate();

        if (InterFrameDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(InterFrameDelay), InterFrameDelay, "InterFrameDelay cannot be negative.");
        }
    }
}

/// <summary>Options for a Modbus TCP client.</summary>
public sealed class ModbusTcpOptions : ModbusOptions
{
    /// <summary>TCP endpoint settings. The default port is 502.</summary>
    public TcpTransportOptions Tcp { get; set; } = new() { Port = 502 };

    /// <summary>Validates the option values and throws when a value is out of range.</summary>
    public override void Validate()
    {
        base.Validate();
        Tcp.Validate();
    }
}
