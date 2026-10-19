namespace SiameseCat.Samples.Infrastructure;

/// <summary>
/// Resolves a capability interface from an <see cref="SiameseCat.Core.IPlcDriver"/>.
/// </summary>
/// <remarks>
/// The driver contract is deliberately split per capability (<c>IReadable</c>, <c>IWritable</c>,
/// <c>IBatchReadable</c>, ...), and <see cref="SiameseCat.Core.DriverDescriptor.Capabilities"/> says which
/// ones a driver declares. These helpers turn a missing capability into a readable exception instead of
/// an <see cref="InvalidCastException"/>.
/// </remarks>
public static class DriverAccess
{
    /// <summary>Gets the single-address read capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Core.IReadable Reader(SiameseCat.Core.IPlcDriver driver) =>
        driver as SiameseCat.Core.IReadable ?? throw Missing(driver, "read");

    /// <summary>Gets the single-address write capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Core.IWritable Writer(SiameseCat.Core.IPlcDriver driver) =>
        driver as SiameseCat.Core.IWritable ?? throw Missing(driver, "write");

    /// <summary>Gets the batch read capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Core.IBatchReadable BatchReader(SiameseCat.Core.IPlcDriver driver) =>
        driver as SiameseCat.Core.IBatchReadable ?? throw Missing(driver, "batch read");

    /// <summary>Gets the batch write capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Core.IBatchWritable BatchWriter(SiameseCat.Core.IPlcDriver driver) =>
        driver as SiameseCat.Core.IBatchWritable ?? throw Missing(driver, "batch write");

    /// <summary>Gets the single-address subscription capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Core.ISubscribable Subscriber(SiameseCat.Core.IPlcDriver driver) =>
        driver as SiameseCat.Core.ISubscribable ?? throw Missing(driver, "subscribe");

    /// <summary>Gets the grouped subscription capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Core.IBatchSubscribable BatchSubscriber(SiameseCat.Core.IPlcDriver driver) =>
        driver as SiameseCat.Core.IBatchSubscribable ?? throw Missing(driver, "batch subscribe");

    /// <summary>Gets the health probe capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Core.IHealthCheckable Health(SiameseCat.Core.IPlcDriver driver) =>
        driver as SiameseCat.Core.IHealthCheckable ?? throw Missing(driver, "health probe");

    private static NotSupportedException Missing(SiameseCat.Core.IPlcDriver driver, string capability) =>
        new($"Driver '{driver.Descriptor.Id}' does not support '{capability}'. "
            + $"Declared capabilities: {driver.Descriptor.Capabilities}.");
}
