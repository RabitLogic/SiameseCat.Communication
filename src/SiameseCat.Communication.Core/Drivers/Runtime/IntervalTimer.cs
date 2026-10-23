namespace SiameseCat.Communication;

/// <summary>
/// Minimal periodic timer with the same usage pattern as <c>System.Threading.PeriodicTimer</c>,
/// which is not available on .NET Standard 2.0.
/// </summary>
internal sealed class IntervalTimer : IDisposable
{
    private readonly TimeSpan _interval;
    private long _nextTickTicks;

    public IntervalTimer(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be greater than zero.");
        }

        _interval = interval;
        _nextTickTicks = DateTime.UtcNow.Ticks + interval.Ticks;
    }

    public async ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTime.UtcNow.Ticks;
        var next = Interlocked.Read(ref _nextTickTicks);

        if (next > now)
        {
            var delay = TimeSpan.FromTicks(next - now);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        // Skip missed ticks so a slow consumer does not accumulate a backlog.
        var scheduled = Interlocked.Read(ref _nextTickTicks);
        var current = DateTime.UtcNow.Ticks;
        var nextTick = scheduled + _interval.Ticks;
        Interlocked.Exchange(ref _nextTickTicks, nextTick > current ? nextTick : current + _interval.Ticks);

        return true;
    }

    public void Dispose()
    {
    }
}
