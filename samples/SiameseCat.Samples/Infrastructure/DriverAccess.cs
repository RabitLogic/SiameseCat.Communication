namespace SiameseCat.Samples.Infrastructure;

/// <summary>
/// Resolves a capability interface from an <see cref="SiameseCat.Communication.IPlcDriver"/>.
/// </summary>
/// <remarks>
/// The driver contract is deliberately split per capability (<c>IReadable</c>, <c>IWritable</c>,
/// <c>IBatchReadable</c>, ...), and <see cref="SiameseCat.Communication.DriverDescriptor.Capabilities"/> says which
/// ones a driver declares. These helpers turn a missing capability into a readable exception instead of
/// an <see cref="InvalidCastException"/>.
/// </remarks>
public static class DriverAccess
{
    /// <summary>Gets the single-address read capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.IReadable Reader(SiameseCat.Communication.IPlcDriver driver) =>
        driver as SiameseCat.Communication.IReadable ?? throw Missing(driver, "read");

    /// <summary>Gets the single-address write capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.IWritable Writer(SiameseCat.Communication.IPlcDriver driver) =>
        driver as SiameseCat.Communication.IWritable ?? throw Missing(driver, "write");

    /// <summary>Gets the batch read capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.IBatchReadable BatchReader(SiameseCat.Communication.IPlcDriver driver) =>
        driver as SiameseCat.Communication.IBatchReadable ?? throw Missing(driver, "batch read");

    /// <summary>Gets the batch write capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.IBatchWritable BatchWriter(SiameseCat.Communication.IPlcDriver driver) =>
        driver as SiameseCat.Communication.IBatchWritable ?? throw Missing(driver, "batch write");

    /// <summary>Gets the single-address subscription capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.ISubscribable Subscriber(SiameseCat.Communication.IPlcDriver driver) =>
        driver as SiameseCat.Communication.ISubscribable ?? throw Missing(driver, "subscribe");

    /// <summary>Gets the grouped subscription capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.IBatchSubscribable BatchSubscriber(SiameseCat.Communication.IPlcDriver driver) =>
        driver as SiameseCat.Communication.IBatchSubscribable ?? throw Missing(driver, "batch subscribe");

    /// <summary>Gets the health probe capability.</summary>
    /// <param name="driver">Driver instance.</param>
    public static SiameseCat.Communication.IHealthCheckable Health(SiameseCat.Communication.IPlcDriver driver) =>
        driver as SiameseCat.Communication.IHealthCheckable ?? throw Missing(driver, "health probe");

    private static NotSupportedException Missing(SiameseCat.Communication.IPlcDriver driver, string capability) =>
        new($"Driver '{driver.Descriptor.Id}' does not support '{capability}'. "
            + $"Declared capabilities: {driver.Descriptor.Capabilities}.");
}
