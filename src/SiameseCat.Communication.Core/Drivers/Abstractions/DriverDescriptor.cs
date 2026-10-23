namespace SiameseCat.Communication;

/// <summary>Transport kinds a driver can use.</summary>
[Flags]
public enum TransportKind
{
    /// <summary>No transport.</summary>
    None = 0,

    /// <summary>TCP stream.</summary>
    Tcp = 1,

    /// <summary>Serial port (RS-232 / RS-485).</summary>
    Serial = 2,

    /// <summary>UDP datagrams.</summary>
    Udp = 4,

    /// <summary>
    /// Transport supplied by <see cref="DriverConfiguration.TransportFactory"/>; the driver has no
    /// built-in transport and only speaks its protocol over the injected <see cref="ITransport"/>.
    /// </summary>
    Custom = 8,
}

/// <summary>Static information about a driver, used for discovery and diagnostics.</summary>
public sealed class DriverDescriptor
{
    /// <summary>Creates a descriptor.</summary>
    /// <param name="id">Stable identifier, for example <c>modbus</c> or <c>s7</c>.</param>
    /// <param name="name">Display name.</param>
    /// <param name="description">Short description.</param>
    /// <param name="transports">Supported transports.</param>
    /// <param name="defaultPort">Default TCP/UDP port, or 0 when not applicable.</param>
    /// <param name="addressExamples">Examples of protocol-native addresses.</param>
    /// <param name="capabilities">Capabilities implemented by the driver. Defaults to <see cref="DriverCapabilities.All"/>.</param>
    public DriverDescriptor(
        string id,
        string name,
        string description,
        TransportKind transports,
        int defaultPort = 0,
        IReadOnlyList<string>? addressExamples = null,
        DriverCapabilities capabilities = DriverCapabilities.All)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Driver id must be specified.", nameof(id));
        }

        Id = id;
        Name = name;
        Description = description;
        Transports = transports;
        DefaultPort = defaultPort;
        AddressExamples = addressExamples ?? Array.Empty<string>();
        Capabilities = capabilities;
    }

    /// <summary>Gets the stable identifier used for registration.</summary>
    public string Id { get; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; }

    /// <summary>Gets the short description.</summary>
    public string Description { get; }

    /// <summary>Gets the supported transports.</summary>
    public TransportKind Transports { get; }

    /// <summary>Gets the default TCP/UDP port, or 0 when not applicable.</summary>
    public int DefaultPort { get; }

    /// <summary>Gets examples of protocol-native addresses.</summary>
    public IReadOnlyList<string> AddressExamples { get; }

    /// <summary>Gets the capabilities implemented by the driver.</summary>
    public DriverCapabilities Capabilities { get; }

    /// <summary>Returns a short description of the driver.</summary>
    public override string ToString() => $"{Id} ({Name})";
}
