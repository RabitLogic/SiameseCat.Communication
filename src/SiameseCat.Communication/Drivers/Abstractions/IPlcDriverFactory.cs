namespace SiameseCat.Communication;

/// <summary>Creates drivers of one protocol.</summary>
public interface IPlcDriverFactory
{
    /// <summary>Gets the descriptor of the drivers created by this factory.</summary>
    DriverDescriptor Descriptor { get; }

    /// <summary>Creates a driver.</summary>
    /// <param name="configuration">Connection and retry settings.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="metrics">Optional metrics collector; a new instance is created when omitted.</param>
    IPlcDriver Create(
        DriverConfiguration configuration,
        IPlcLogger? logger = null,
        PlcMetrics? metrics = null);
}
