namespace SiameseCat.Communication.Simulation.Modbus;

/// <summary>Common contract of the in-process Modbus slaves.</summary>
public interface IModbusSimulator : IAsyncDisposable
{
    /// <summary>Gets the register and coil storage that the slave exposes.</summary>
    ModbusDataStore Data { get; }

    /// <summary>Gets the request and frame counters of the slave.</summary>
    ModbusSimulatorStatistics Statistics { get; }

    /// <summary>Gets whether the slave is currently accepting requests.</summary>
    bool IsRunning { get; }

    /// <summary>Starts listening for requests.</summary>
    /// <param name="cancellationToken">Cancels the accept or frame loop.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops listening and releases the transport.</summary>
    Task StopAsync();
}
