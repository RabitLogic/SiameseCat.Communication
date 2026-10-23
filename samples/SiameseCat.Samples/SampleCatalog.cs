using SiameseCat.Samples.Drivers;
using SiameseCat.Samples.Infrastructure;
using SiameseCat.Samples.Modbus;
using SiameseCat.Samples.S7;
using SiameseCat.Samples.Simulation;
using SiameseCat.Samples.Transports;

namespace SiameseCat.Samples;

/// <summary>
/// The ordered list of samples. The catalog is explicit, like everything else in the library, so the
/// tutorial order is obvious and Native AOT keeps working (no reflection over the assembly).
/// </summary>
public static class SampleCatalog
{
    /// <summary>Gets every sample, grouped by track and ordered as in the tutorial.</summary>
    public static IReadOnlyList<ISample> All { get; } = new ISample[]
    {
        // Modbus track
        new ModbusTcpSample(),
        new ModbusRtuSample(),
        new ModbusFunctionCodesSample(),
        new ModbusErrorsSample(),

        // S7 track
        new S7ClientSample(),
        new S7DriverSample(),

        // Driver track
        new DriverRegistrySample(),
        new DriverTagsSample(),
        new DriverBatchSample(),
        new DriverSubscriptionSample(),
        new DriverResilienceSample(),
        new DriverHostingSample(),

        // Slave track
        new ModbusTcpSimulatorSample(),
        new ModbusRtuSimulatorSample(),
        new S7SimulatorSample(),

        // Transport track
        new RawTcpTransportSample(),
        new CustomTransportSample(),
        new CustomTcpProtocolSample(),
    };

    /// <summary>Gets the group (the part of the id before the slash) of a sample.</summary>
    /// <param name="sample">Sample to inspect.</param>
    public static string GroupOf(ISample sample)
    {
        var index = sample.Id.IndexOf('/', StringComparison.Ordinal);
        return index < 0 ? "basics" : sample.Id[..index];
    }

    /// <summary>Finds a sample by id.</summary>
    /// <param name="id">Sample id such as <c>modbus/tcp</c>.</param>
    /// <returns>The sample, or <see langword="null"/> when no id matches.</returns>
    public static ISample? Find(string id) =>
        All.FirstOrDefault(sample => string.Equals(sample.Id, id, StringComparison.OrdinalIgnoreCase));
}
