namespace SiameseCat.Core;

/// <summary>Capability: reading several addresses in one request when the protocol allows it.</summary>
public interface IBatchReadable
{
    /// <summary>Reads several values.</summary>
    /// <param name="addresses">Addresses to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One value per address, in the same order.</returns>
    Task<PlcValue[]> ReadBatchAsync(
        IReadOnlyList<PlcAddress> addresses,
        CancellationToken cancellationToken = default);

    /// <summary>Reads several values with a per-call timeout that overrides <see cref="DriverConfiguration.Timeout"/>.</summary>
    /// <param name="addresses">Addresses to read.</param>
    /// <param name="timeout">Timeout for this call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One value per address, in the same order.</returns>
    Task<PlcValue[]> ReadBatchAsync(
        IReadOnlyList<PlcAddress> addresses,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
