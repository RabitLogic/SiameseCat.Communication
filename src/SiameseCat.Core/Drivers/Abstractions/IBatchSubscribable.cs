namespace SiameseCat.Core;

/// <summary>
/// Capability: change notifications for several addresses that share one polling cycle.
/// Implementations usually read the whole set with <see cref="IBatchReadable"/> instead of
/// issuing one request per address.
/// </summary>
public interface IBatchSubscribable
{
    /// <summary>Subscribes to changes of several addresses at once.</summary>
    /// <param name="addresses">Addresses to watch.</param>
    /// <param name="interval">Polling interval.</param>
    /// <param name="onChanged">
    /// Callback invoked with one value per address (same order) when at least one value changed.
    /// It is invoked once for the initial read.
    /// </param>
    /// <param name="cancellationToken">Cancellation token that also ends the subscription.</param>
    /// <returns>A handle that stops the subscription when disposed.</returns>
    Task<IAsyncDisposable> SubscribeBatchAsync(
        IReadOnlyList<PlcAddress> addresses,
        TimeSpan interval,
        Func<IReadOnlyList<PlcValue>, Task> onChanged,
        CancellationToken cancellationToken = default);
}
