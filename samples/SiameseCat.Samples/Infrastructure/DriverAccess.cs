using SiameseCat.Communication.Drivers;

namespace SiameseCat.Samples.Infrastructure;

/// <summary>
/// Resolves a capability interface from an <see cref="SiameseCat.Communication.Drivers.IDeviceDriver"/>.
/// </summary>
/// <remarks>
/// The driver contract is deliberately split per capability (<c>IReadable</c>, <c>IWritable</c>,
/// <c>IBatchReadable</c>, ...), and <see cref="SiameseCat.Communication.Drivers.DriverDescriptor.Capabilities"/> says which
/// ones a driver declares. These helpers turn a missing capability into a readable exception instead of
/// an <see cref="InvalidCastException"/>.
/// </remarks>
public static class DriverAccess
{
    /// <summary>Gets the single-address read capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.Drivers.IReadable Reader(SiameseCat.Communication.Drivers.IDeviceDriver driver) =>
        driver as SiameseCat.Communication.Drivers.IReadable ?? throw Missing(driver, "read");

    /// <summary>Gets the single-address write capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.Drivers.IWritable Writer(SiameseCat.Communication.Drivers.IDeviceDriver driver) =>
        driver as SiameseCat.Communication.Drivers.IWritable ?? throw Missing(driver, "write");

    /// <summary>Gets the batch read capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.Drivers.IBatchReadable BatchReader(SiameseCat.Communication.Drivers.IDeviceDriver driver) =>
        driver as SiameseCat.Communication.Drivers.IBatchReadable ?? throw Missing(driver, "batch read");

    /// <summary>Gets the batch write capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.Drivers.IBatchWritable BatchWriter(SiameseCat.Communication.Drivers.IDeviceDriver driver) =>
        driver as SiameseCat.Communication.Drivers.IBatchWritable ?? throw Missing(driver, "batch write");

    /// <summary>Gets the single-address subscription capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.Drivers.ISubscribable Subscriber(SiameseCat.Communication.Drivers.IDeviceDriver driver) =>
        driver as SiameseCat.Communication.Drivers.ISubscribable ?? throw Missing(driver, "subscribe");

    /// <summary>Gets the grouped subscription capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.Drivers.IBatchSubscribable BatchSubscriber(SiameseCat.Communication.Drivers.IDeviceDriver driver) =>
        driver as SiameseCat.Communication.Drivers.IBatchSubscribable ?? throw Missing(driver, "batch subscribe");

    /// <summary>Gets the health probe capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.Drivers.IHealthCheckable Health(SiameseCat.Communication.Drivers.IDeviceDriver driver) =>
        driver as SiameseCat.Communication.Drivers.IHealthCheckable ?? throw Missing(driver, "health probe");

    private static NotSupportedException Missing(SiameseCat.Communication.Drivers.IDeviceDriver driver, string capability) =>
        new($"Driver '{driver.Descriptor.Id}' does not support '{capability}'. "
            + $"Declared capabilities: {driver.Descriptor.Capabilities}.");
}
