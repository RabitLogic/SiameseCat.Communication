namespace SiameseCat.Communication;

/// <summary>Capability: single-address writes.</summary>
public interface IWritable
{
    /// <summary>Writes one value.</summary>
    /// <param name="address">Address to write.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(DeviceAddress address, DeviceValue value, CancellationToken cancellationToken = default);

    /// <summary>Writes one value with a per-call timeout that overrides <see cref="DriverConfiguration.Timeout"/>.</summary>
    /// <param name="address">Address to write.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="timeout">Timeout for this call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(
        DeviceAddress address,
        DeviceValue value,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
