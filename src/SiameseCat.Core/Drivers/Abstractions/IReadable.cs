namespace SiameseCat.Core;

/// <summary>Capability: single-address reads.</summary>
public interface IReadable
{
    /// <summary>Reads one value.</summary>
    /// <param name="address">Address to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PlcValue> ReadAsync(PlcAddress address, CancellationToken cancellationToken = default);

    /// <summary>Reads one value with a per-call timeout that overrides <see cref="DriverConfiguration.Timeout"/>.</summary>
    /// <param name="address">Address to read.</param>
    /// <param name="timeout">Timeout for this call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PlcValue> ReadAsync(PlcAddress address, TimeSpan timeout, CancellationToken cancellationToken = default);
}
