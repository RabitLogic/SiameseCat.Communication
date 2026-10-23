using Microsoft.Extensions.Logging;
using SiameseCat.Communication.Transport;

namespace SiameseCat.Communication.Tcp;

/// <summary>Settings for <see cref="CustomTcpClient"/>.</summary>
public sealed class CustomTcpClientOptions
{
    /// <summary>Default port used when the caller does not specify one.</summary>
    public const int DefaultPort = 5000;

    /// <summary>Gets or sets the endpoint and socket settings. Defaults to port <see cref="DefaultPort"/>.</summary>
    public TcpTransportOptions Tcp { get; set; } = new() { Port = DefaultPort };

    /// <summary>
    /// Gets or sets the framing used when <see cref="Codec"/> is <see langword="null"/>.
    /// Defaults to <see cref="TcpFrameMode.Raw"/>.
    /// </summary>
    public TcpFrameOptions Framing { get; set; } = new();

    /// <summary>Gets or sets a custom frame codec. When set, it takes precedence over <see cref="Framing"/>.</summary>
    public ITcpFrameCodec? Codec { get; set; }

    /// <summary>Gets or sets the maximum time to wait for a response. Defaults to 3 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Gets or sets the number of additional attempts for transient failures. Defaults to 0.</summary>
    public int RetryCount { get; set; }

    /// <summary>Gets or sets the delay between retries. Defaults to 200 milliseconds.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets whether requests may be retried. Defaults to <see langword="false"/> because a
    /// request may already have side effects on the peer.
    /// </summary>
    public bool RetryRequests { get; set; }

    /// <summary>Gets or sets whether every frame is logged at trace level.</summary>
    public bool LogFrames { get; set; }

    /// <summary>Gets or sets the logger that receives frame dumps when <see cref="LogFrames"/> is enabled.</summary>
    public ILogger? Logger { get; set; }

    /// <summary>Gets or sets the short label used in log entries. Defaults to <c>custom-tcp</c>.</summary>
    public string LogName { get; set; } = "custom-tcp";

    /// <summary>Validates the option values and throws when a value is out of range.</summary>
    public void Validate()
    {
        Tcp.Validate();
        if (Codec is null)
        {
            Framing.Validate();
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout), Timeout, "Timeout must be greater than zero.");
        }

        if (RetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryCount), RetryCount, "RetryCount cannot be negative.");
        }

        if (RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryDelay), RetryDelay, "RetryDelay cannot be negative.");
        }
    }
}
