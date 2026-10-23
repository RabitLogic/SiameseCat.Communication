namespace SiameseCat.Communication;

/// <summary>Capability: writing several addresses in one request when the protocol allows it.</summary>
public interface IBatchWritable
{
    /// <summary>Writes several values.</summary>
    /// <param name="requests">Address/value pairs to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteBatchAsync(
        IReadOnlyList<DeviceWriteRequest> requests,
        CancellationToken cancellationToken = default);

    /// <summary>Writes several values with a per-call timeout that overrides <see cref="DriverConfiguration.Timeout"/>.</summary>
    /// <param name="requests">Address/value pairs to write.</param>
    /// <param name="timeout">Timeout for this call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteBatchAsync(
        IReadOnlyList<DeviceWriteRequest> requests,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
