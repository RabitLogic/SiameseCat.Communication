namespace SiameseCat.Communication.Drivers;

/// <summary>
/// Serializes protocol requests. Drivers use their own lock by default; assign a shared instance to
/// <see cref="DriverConfiguration.RequestLock"/> when several drivers use the same physical link.
/// </summary>
public sealed class RequestLock : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Waits until the caller has exclusive access.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WaitAsync(CancellationToken cancellationToken = default) => _gate.WaitAsync(cancellationToken);

    /// <summary>Releases exclusive access. Every successful <see cref="WaitAsync"/> needs one call.</summary>
    public void Release() => _gate.Release();

    /// <inheritdoc/>
    public void Dispose() => _gate.Dispose();
}
