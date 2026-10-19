using System.Text;

using SiameseCat.Core;

namespace SiameseCat.Protocols.S7;

/// <summary>Options for an S7 client.</summary>
public sealed class S7Options : CommunicationOptions
{
    /// <summary>PLC host name or IP address.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>ISO-on-TCP port. Defaults to 102.</summary>
    public int Port { get; set; } = 102;

    /// <summary>PLC rack. Defaults to 0.</summary>
    public int Rack { get; set; }

    /// <summary>PLC slot. Use 1 for S7-1200/1500 and 2 for S7-300/400. Defaults to 1.</summary>
    public int Slot { get; set; } = 1;

    /// <summary>Maximum time allowed for establishing the connection. Defaults to 5 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Disables Nagle's algorithm. Defaults to <see langword="true"/>, which is desirable for PLC traffic.</summary>
    public bool NoDelay { get; set; } = true;

    /// <summary>Enables TCP keep-alive probes.</summary>
    public bool KeepAlive { get; set; }

    /// <summary>Idle time before the first keep-alive probe. Defaults to 30 seconds.</summary>
    public TimeSpan KeepAliveTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Interval between keep-alive probes. Defaults to 5 seconds.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Encoding used by <see cref="S7Client.ReadStringAsync"/> and <see cref="S7Client.WriteStringAsync"/>.</summary>
    public Encoding StringEncoding { get; set; } = Encoding.Latin1;

    /// <summary>Validates the option values and throws when a value is out of range.</summary>
    public override void Validate()
    {
        base.Validate();

        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("Host must be specified.", nameof(Host));
        }

        if (Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between 1 and 65535.");
        }

        if (Rack is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(Rack), Rack, "Rack must be between 0 and 7.");
        }

        if (Slot is < 0 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(Slot), Slot, "Slot must be between 0 and 31.");
        }

        if (ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout), ConnectTimeout, "ConnectTimeout must be greater than zero.");
        }

        if (KeepAliveTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(KeepAliveTime), KeepAliveTime, "KeepAliveTime must be greater than zero.");
        }

        if (KeepAliveInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(KeepAliveInterval), KeepAliveInterval, "KeepAliveInterval must be greater than zero.");
        }

        Guard.NotNull(StringEncoding, nameof(StringEncoding));
    }
}
