namespace SiameseCat.Communication.Drivers;

/// <summary>Capability: single-address reads.</summary>
public interface IReadable
{
    /// <summary>Reads one value.</summary>
    /// <param name="address">Address to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DeviceValue> ReadAsync(DeviceAddress address, CancellationToken cancellationToken = default);

    /// <summary>Reads one value with a per-call timeout that overrides <see cref="CommunicationOptions.Timeout"/>.</summary>
    /// <param name="address">Address to read.</param>
    /// <param name="timeout">Timeout for this call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DeviceValue> ReadAsync(DeviceAddress address, TimeSpan timeout, CancellationToken cancellationToken = default);
}
