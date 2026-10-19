namespace SiameseCat.Server.S7;

/// <summary>Common contract of the in-process S7 slave.</summary>
public interface IS7Server : IAsyncDisposable
{
    /// <summary>Gets the memory areas and data blocks that the slave exposes.</summary>
    S7DataStore Data { get; }

    /// <summary>Gets the request and frame counters of the slave.</summary>
    S7ServerStatistics Statistics { get; }

    /// <summary>Gets whether the slave is currently accepting connections.</summary>
    bool IsRunning { get; }

    /// <summary>Starts listening for ISO-on-TCP connections.</summary>
    /// <param name="cancellationToken">Cancels the accept loop.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops listening and closes every connection.</summary>
    Task StopAsync();
}
