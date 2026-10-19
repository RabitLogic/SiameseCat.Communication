namespace SiameseCat.Core;

/// <summary>Capability: change notifications, implemented by polling or native subscriptions.</summary>
public interface ISubscribable
{
    /// <summary>Subscribes to value changes.</summary>
    /// <param name="address">Address to poll.</param>
    /// <param name="interval">Polling interval.</param>
    /// <param name="onChanged">Callback invoked when the value changes.</param>
    /// <param name="cancellationToken">Cancellation token that also ends the subscription.</param>
    /// <returns>A handle that stops the subscription when disposed.</returns>
    Task<IAsyncDisposable> SubscribeAsync(
        PlcAddress address,
        TimeSpan interval,
        Func<PlcValue, Task> onChanged,
        CancellationToken cancellationToken = default);
}
