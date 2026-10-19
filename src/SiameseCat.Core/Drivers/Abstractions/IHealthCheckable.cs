namespace SiameseCat.Core;

/// <summary>Capability: checking that the link to the device is still usable.</summary>
/// <remarks>
/// <see cref="PingAsync"/> performs a side-effect-free read of <see cref="DriverConfiguration.ProbeAddress"/>
/// and reports success instead of throwing, so it can be used by monitoring and by the keep-alive loop.
/// </remarks>
public interface IHealthCheckable
{
    /// <summary>Checks whether the device answers. Never throws for communication failures.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the probe read succeeded.</returns>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}
