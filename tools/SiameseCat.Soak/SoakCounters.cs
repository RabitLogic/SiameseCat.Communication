using System.Collections.Concurrent;

namespace SiameseCat.Soak;

/// <summary>Thread-safe counters shared by every worker.</summary>
internal sealed class SoakCounters
{
    private readonly ConcurrentDictionary<string, long> _errorKinds = new(StringComparer.Ordinal);
    private long _attempts;
    private long _successes;
    private long _failures;
    private long _reconnects;
    private long _lastSuccessTicks;

    /// <summary>Gets the number of finished cycles, successful or not.</summary>
    public long Attempts => Interlocked.Read(ref _attempts);

    /// <summary>Gets the number of successful cycles.</summary>
    public long Successes => Interlocked.Read(ref _successes);

    /// <summary>Gets the number of failed cycles.</summary>
    public long Failures => Interlocked.Read(ref _failures);

    /// <summary>Gets how often a worker had to reconnect a faulted driver.</summary>
    public long Reconnects => Interlocked.Read(ref _reconnects);

    /// <summary>Gets the success rate, or 1 when nothing ran yet.</summary>
    public double SuccessRate => Attempts == 0 ? 1.0 : Successes / (double)Attempts;

    /// <summary>Gets the UTC time of the last successful cycle.</summary>
    public DateTimeOffset? LastSuccess
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSuccessTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>Gets the failures grouped by exception type.</summary>
    public IReadOnlyDictionary<string, long> ErrorKinds => _errorKinds;

    /// <summary>Records a successful cycle.</summary>
    public void RecordSuccess()
    {
        Interlocked.Increment(ref _attempts);
        Interlocked.Increment(ref _successes);
        Interlocked.Exchange(ref _lastSuccessTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    /// <summary>Records a failed cycle.</summary>
    /// <param name="error">Exception that ended the cycle.</param>
    public void RecordFailure(Exception error)
    {
        Interlocked.Increment(ref _attempts);
        Interlocked.Increment(ref _failures);
        _errorKinds.AddOrUpdate(error.GetType().Name, 1, static (_, count) => count + 1);
    }

    /// <summary>Records a reconnect attempt after a worker found its driver faulted.</summary>
    public void RecordReconnect() => Interlocked.Increment(ref _reconnects);
}
