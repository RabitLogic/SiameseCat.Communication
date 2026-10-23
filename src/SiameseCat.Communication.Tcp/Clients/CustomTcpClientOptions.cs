using SiameseCat.Communication.Transport;

namespace SiameseCat.Communication.Transport.Tcp;

/// <summary>Settings for <see cref="CustomTcpClient"/>.</summary>
/// <remarks>
/// Timeout, retry, frame-logging and logger settings come from <see cref="CommunicationOptions"/>, so a
/// custom TCP client is configured the same way as the Modbus and S7 clients.
/// </remarks>
public sealed class CustomTcpClientOptions : CommunicationOptions
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

    /// <summary>
    /// Gets or sets whether requests may be retried. Defaults to <see langword="false"/> because a
    /// request may already have side effects on the peer. This is an alias of
    /// <see cref="CommunicationOptions.RetryWrites"/> for protocols without a read/write distinction.
    /// </summary>
    public bool RetryRequests
    {
        get => RetryWrites;
        set => RetryWrites = value;
    }

    /// <summary>Gets or sets the short label used in log entries. Defaults to <c>custom-tcp</c>.</summary>
    public string LogName { get; set; } = "custom-tcp";

    /// <inheritdoc/>
    public override void Validate()
    {
        base.Validate();
        Tcp.Validate();

        if (Codec is null)
        {
            Framing.Validate();
        }
    }
}
