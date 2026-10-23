namespace SiameseCat.Communication.Transport.Udp;

/// <summary>Settings for <see cref="UdpTransport"/>.</summary>
public sealed class UdpTransportOptions
{
    /// <summary>Gets or sets the remote host name or IP address.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Gets or sets the remote UDP port.</summary>
    public int Port { get; set; }

    /// <summary>Gets or sets the time allowed for name resolution and socket connect. Defaults to 5 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Validates the settings.</summary>
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
            throw new ArgumentOutOfRangeException(
                nameof(ConnectTimeout),
                ConnectTimeout,
                "ConnectTimeout must be greater than zero.");
        }
    }
}
