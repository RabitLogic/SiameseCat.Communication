using Microsoft.Extensions.Logging;
using SiameseCat.Communication.Tcp;

namespace SiameseCat.Communication.Server.Tcp;

/// <summary>Settings for <see cref="CustomTcpServer"/>.</summary>
public sealed class CustomTcpServerOptions
{
    /// <summary>Gets or sets the address to listen on. Defaults to loopback so the simulator is not exposed.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Gets or sets the TCP port. Defaults to 5000; use 0 to let the operating system choose a free port.</summary>
    public int Port { get; set; } = 5000;

    /// <summary>
    /// Gets or sets the framing used when <see cref="Codec"/> is <see langword="null"/>.
    /// Defaults to <see cref="TcpFrameMode.Raw"/>.
    /// </summary>
    public TcpFrameOptions Framing { get; set; } = new();

    /// <summary>Gets or sets a custom frame codec. When set, it takes precedence over <see cref="Framing"/>.</summary>
    public ITcpFrameCodec? Codec { get; set; }

    /// <summary>Gets or sets how many clients may be connected at once. Defaults to 64; 0 means unlimited.</summary>
    public int MaxConnections { get; set; } = 64;

    /// <summary>Gets or sets the logger used for connection and frame diagnostics. Defaults to no logging.</summary>
    public ILogger? Logger { get; set; }

    /// <summary>Gets or sets whether every frame is logged at trace level. Defaults to <see langword="false"/>.</summary>
    public bool LogFrames { get; set; }

    /// <summary>Validates the options and throws when a value is out of range.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("Host must be specified.", nameof(Host));
        }

        if (Port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between 0 and 65535.");
        }

        if (MaxConnections < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConnections), MaxConnections, "MaxConnections cannot be negative.");
        }

        if (Codec is null)
        {
            Framing.Validate();
        }
    }
}
