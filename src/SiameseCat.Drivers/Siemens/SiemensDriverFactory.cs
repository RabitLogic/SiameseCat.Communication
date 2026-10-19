using System.Text;
using SiameseCat.Core;
using SiameseCat.Protocols.S7;

namespace SiameseCat.Drivers.Siemens;

/// <summary>Creates Siemens S7 drivers.</summary>
public sealed class SiemensDriverFactory : IPlcDriverFactory
{
    /// <summary>Gets the descriptor shared by all Siemens drivers.</summary>
    public static DriverDescriptor DefaultDescriptor { get; } = new(
        "siemens",
        "Siemens S7",
        "S7comm over ISO-on-TCP (RFC 1006)",
        TransportKind.Tcp,
        defaultPort: 102,
        addressExamples: new[] { "DB1.DBX0.0", "DB1.DBW2", "M0.0", "IW4", "QW0" });

    /// <inheritdoc/>
    public DriverDescriptor Descriptor => DefaultDescriptor;

    /// <inheritdoc/>
    public IPlcDriver Create(
        DriverConfiguration configuration,
        IPlcLogger? logger = null,
        PlcMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();

        if (configuration.Transport != TransportKind.Tcp)
        {
            throw new NotSupportedException(
                $"S7 driver supports only the Tcp transport, not '{configuration.Transport}'.");
        }

        var encoding = ParseEncoding(configuration.GetOption("encoding"));
        var options = new S7Options
        {
            Host = configuration.Host,
            Port = configuration.Port == 0 ? DefaultDescriptor.DefaultPort : configuration.Port,
            Rack = configuration.GetOption("rack", 0, 0, 7),
            Slot = configuration.GetOption("slot", 1, 0, 31),
            ConnectTimeout = configuration.ConnectTimeout,
            Timeout = configuration.Timeout,
            RetryCount = 0,
            RetryDelay = TimeSpan.Zero,
            StringEncoding = encoding,
        };

        return new SiemensDriver(new S7Client(options), configuration, encoding, logger, metrics);
    }

    private static Encoding ParseEncoding(string? name) => name?.ToUpperInvariant() switch
    {
        null or "" or "LATIN1" or "ISO-8859-1" => Encoding.Latin1,
        "ASCII" => Encoding.ASCII,
        "UTF8" or "UTF-8" => Encoding.UTF8,
        _ => throw new ArgumentException($"Unknown string encoding '{name}'.", nameof(name)),
    };
}
