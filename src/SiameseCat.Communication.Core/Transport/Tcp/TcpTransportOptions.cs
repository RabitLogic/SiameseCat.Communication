namespace SiameseCat.Communication.Transport.Tcp;

/// <summary>Options for a TCP client transport.</summary>
public sealed class TcpTransportOptions
{
    /// <summary>Remote host name or IP address.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Remote TCP port.</summary>
    public int Port { get; set; } = 502;

    /// <summary>Maximum time allowed for establishing the connection. Defaults to 5 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Disables Nagle's algorithm. Defaults to <see langword="true"/>, which is desirable for device traffic.</summary>
    public bool NoDelay { get; set; } = true;

    /// <summary>Enables TCP keep-alive probes.</summary>
    public bool KeepAlive { get; set; }

    /// <summary>Idle time before the first keep-alive probe. Defaults to 30 seconds.</summary>
    public TimeSpan KeepAliveTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Interval between keep-alive probes. Defaults to 5 seconds.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Validates the option values and throws when a value is out of range.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("Host must be specified.", nameof(Host));
        }

        if (Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between 1 and 65535.");
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
    }
}
