namespace SiameseCat.Communication;

/// <summary>Base contract implemented by every device driver.</summary>
public interface IDeviceDriver : IAsyncDisposable
{
    /// <summary>Gets the descriptor that identifies the driver and its capabilities.</summary>
    DriverDescriptor Descriptor { get; }

    /// <summary>Gets the parser for protocol-native addresses.</summary>
    IDeviceAddressParser AddressParser { get; }

    /// <summary>Gets the current lifecycle state.</summary>
    DriverState State { get; }

    /// <summary>Gets the metrics collected by the driver.</summary>
    CommunicationMetrics Metrics { get; }

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    event EventHandler<DriverStateChangedEventArgs>? StateChanged;

    /// <summary>Opens the connection. Calling it while connected is a no-op.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the connection. The driver can be connected again afterwards.</summary>
    Task DisconnectAsync();
}
