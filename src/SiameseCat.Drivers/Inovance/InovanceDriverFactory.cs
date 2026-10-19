using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;

namespace SiameseCat.Drivers.Inovance;

/// <summary>Creates Inovance drivers over Modbus TCP or a serial port.</summary>
public sealed class InovanceDriverFactory : IPlcDriverFactory
{
    /// <summary>Gets the descriptor shared by all Inovance drivers.</summary>
    public static DriverDescriptor DefaultDescriptor { get; } = new(
        "inovance",
        "Inovance",
        "Inovance soft elements (D/M/X/Y) over Modbus RTU/TCP",
        TransportKind.Tcp | TransportKind.Serial,
        defaultPort: ModbusClientFactory.DefaultTcpPort,
        addressExamples: new[] { "D100", "M10", "X17", "Y10" });

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

        var unitId = ModbusClientFactory.GetUnitId(configuration);
        var encoding = ModbusClientFactory.GetEncoding(configuration);
        var swapBytes = ModbusClientFactory.GetSwapBytes(configuration);
        var mapping = InovanceMapping.FromConfiguration(configuration);
        var client = ModbusClientFactory.Create(configuration, unitId, DefaultDescriptor.DefaultPort, logger);

        return new InovanceDriver(client, configuration, unitId, mapping, encoding, swapBytes, logger, metrics);
    }
}
