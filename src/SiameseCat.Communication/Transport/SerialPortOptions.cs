using System.IO.Ports;

namespace SiameseCat.Communication.Transport;

/// <summary>Options for a serial port transport.</summary>
public sealed class SerialPortOptions
{
    /// <summary>Serial port name, for example <c>COM3</c> or <c>/dev/ttyUSB0</c>.</summary>
    public string PortName { get; set; } = "COM1";

    /// <summary>Bits per second. Defaults to 9600.</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>Parity mode. Defaults to <see cref="Parity.None"/>.</summary>
    public Parity Parity { get; set; } = Parity.None;

    /// <summary>Data bits per character. Defaults to 8.</summary>
    public int DataBits { get; set; } = 8;

    /// <summary>Stop bits. Defaults to <see cref="StopBits.One"/>.</summary>
    public StopBits StopBits { get; set; } = StopBits.One;

    /// <summary>Flow control. Defaults to <see cref="Handshake.None"/>.</summary>
    public Handshake Handshake { get; set; } = Handshake.None;

    /// <summary>
    /// Maximum time a single read blocks. Keep this at or below the protocol request timeout.
    /// Defaults to 1 second.
    /// </summary>
    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum time a single write blocks. Defaults to 1 second.</summary>
    public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Asserts the DTR line while the port is open.</summary>
    public bool DtrEnable { get; set; }

    /// <summary>Asserts the RTS line while the port is open.</summary>
    public bool RtsEnable { get; set; }

    /// <summary>Size of the receive buffer maintained by the operating system. Defaults to 4096 bytes.</summary>
    public int ReadBufferSize { get; set; } = 4096;

    /// <summary>Validates the option values and throws when a value is out of range.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PortName))
        {
            throw new ArgumentException("PortName must be specified.", nameof(PortName));
        }

        if (BaudRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BaudRate), BaudRate, "BaudRate must be greater than zero.");
        }

        if (DataBits is < 5 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(DataBits), DataBits, "DataBits must be between 5 and 8.");
        }

        if (!Enum.IsDefined(Parity))
        {
            throw new ArgumentOutOfRangeException(nameof(Parity), Parity, "Unknown parity mode.");
        }

        if (StopBits is not (StopBits.One or StopBits.OnePointFive or StopBits.Two))
        {
            throw new ArgumentOutOfRangeException(nameof(StopBits), StopBits, "Unknown stop bits value.");
        }

        if (ReadTimeout <= TimeSpan.Zero || ReadTimeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(ReadTimeout), ReadTimeout, "ReadTimeout must be greater than zero and fit in an Int32.");
        }

        if (WriteTimeout <= TimeSpan.Zero || WriteTimeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(WriteTimeout), WriteTimeout, "WriteTimeout must be greater than zero and fit in an Int32.");
        }

        if (ReadBufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ReadBufferSize), ReadBufferSize, "ReadBufferSize must be greater than zero.");
        }
    }
}
