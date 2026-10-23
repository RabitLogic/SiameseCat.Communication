namespace SiameseCat.Communication;

/// <summary>Creates the transport that a driver talks through.</summary>
/// <remarks>
/// Set <see cref="DriverConfiguration.TransportFactory"/> to reuse an existing link, to tunnel the
/// protocol through a custom channel, or to share one physical port between several drivers
/// (combine it with <see cref="DriverConfiguration.RequestLock"/> so requests do not interleave).
/// </remarks>
public interface ITransportFactory
{
    /// <summary>Returns the transport for a driver.</summary>
    /// <param name="configuration">Configuration of the driver that is being created.</param>
    ITransport Create(DriverConfiguration configuration);
}
