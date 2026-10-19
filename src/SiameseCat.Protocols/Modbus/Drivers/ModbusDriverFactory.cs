using SiameseCat.Core;

namespace SiameseCat.Protocols.Modbus;

/// <summary>
/// Creates Modbus drivers over TCP or a serial port. Both variants share the same PDU and
/// data-conversion logic inside <see cref="ModbusDriver"/>.
/// </summary>
public sealed class ModbusDriverFactory : IPlcDriverFactory
{
    /// <summary>Gets the descriptor shared by all Modbus drivers.</summary>
    public static DriverDescriptor DefaultDescriptor { get; } = new(
        "modbus",
        "Modbus",
        "Modbus RTU (serial) and Modbus TCP master",
        TransportKind.Tcp | TransportKind.Serial,
        defaultPort: ModbusClientFactory.DefaultTcpPort,
        addressExamples: new[] { "40001", "HR10", "IR0", "C0", "DI0" });

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
        var client = ModbusClientFactory.Create(configuration, unitId, DefaultDescriptor.DefaultPort, logger);

        return new ModbusDriver(client, configuration, unitId, encoding, swapBytes, logger, metrics);
    }
}
