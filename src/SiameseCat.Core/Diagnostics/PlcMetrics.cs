using System.Diagnostics;

namespace SiameseCat.Core;

/// <summary>Kind of operation a metric belongs to.</summary>
public enum PlcOperation
{
    /// <summary>Single read.</summary>
    Read,

    /// <summary>Single write.</summary>
    Write,

    /// <summary>Batch read.</summary>
    ReadBatch,

    /// <summary>Batch write.</summary>
    WriteBatch,

    /// <summary>Atomic read-write exchange (Modbus function 23).</summary>
    ReadWrite,

    /// <summary>Health probe.</summary>
    Ping,
}

/// <summary>Thread-safe counters describing driver activity.</summary>
/// <remarks>
/// Latency percentiles come from a 64-bucket logarithmic histogram (four buckets per octave, so every
/// bucket is at most ~19 % wide and the last one covers ~55 s), which keeps recording allocation-free.
/// </remarks>
public sealed class PlcMetrics
{
    private const int BucketCount = 64;
    private const int OperationCount = 6;

    private readonly long[] _latencyBuckets = new long[BucketCount];
    private readonly long[] _requests = new long[OperationCount];
    private readonly long[] _failures = new long[OperationCount];

    private long _failureTotal;
    private long _timeouts;
    private long _connectionClosed;
    private long _protocol;
    private long _other;
    private long _retries;
    private long _bytesRead;
    private long _bytesWritten;
    private long _latencyTicks;
    private long _maxLatencyTicks;

    /// <summary>Records a request attempt.</summary>
    /// <param name="operation">Operation kind.</param>
    public void RecordRequest(PlcOperation operation) =>
        Interlocked.Increment(ref _requests[(int)operation]);

    /// <summary>Records a failed attempt and classifies the error.</summary>
    /// <param name="operation">Operation kind.</param>
    /// <param name="error">Error that caused the failure, when available.</param>
    public void RecordFailure(PlcOperation operation, Exception? error = null)
    {
        Interlocked.Increment(ref _failures[(int)operation]);
        Interlocked.Increment(ref _failureTotal);

        switch (error)
        {
            case CommunicationTimeoutException:
                Interlocked.Increment(ref _timeouts);
                break;
            case ConnectionClosedException:
                Interlocked.Increment(ref _connectionClosed);
                break;
            case ProtocolException:
                Interlocked.Increment(ref _protocol);
                break;
            default:
                Interlocked.Increment(ref _other);
                break;
        }
    }

    /// <summary>Records a retry.</summary>
    public void RecordRetry() => Interlocked.Increment(ref _retries);

    /// <summary>Records payload bytes read.</summary>
    /// <param name="bytes">Byte count.</param>
    public void RecordBytesRead(int bytes) => Interlocked.Add(ref _bytesRead, bytes);

    /// <summary>Records payload bytes written.</summary>
    /// <param name="bytes">Byte count.</param>
    public void RecordBytesWritten(int bytes) => Interlocked.Add(ref _bytesWritten, bytes);

    /// <summary>Records the latency of a successful attempt.</summary>
    /// <param name="latency">Elapsed time.</param>
    public void RecordLatency(TimeSpan latency)
    {
        var ticks = (long)(latency.TotalMilliseconds * Stopwatch.Frequency / 1000.0);
        Interlocked.Add(ref _latencyTicks, ticks);
        UpdateMax(ref _maxLatencyTicks, ticks);
        Interlocked.Increment(ref _latencyBuckets[GetBucket(latency.TotalMilliseconds)]);
    }

    /// <summary>Takes a consistent snapshot of the counters.</summary>
    public PlcMetricsSnapshot Snapshot()
    {
        var requests = Sum(_requests);
        var ticks = Interlocked.Read(ref _latencyTicks);
        var averageMs = requests == 0 ? 0 : ticks * 1000.0 / Stopwatch.Frequency / requests;
        var maxMs = Interlocked.Read(ref _maxLatencyTicks) * 1000.0 / Stopwatch.Frequency;

        return new PlcMetricsSnapshot(
            requests,
            Interlocked.Read(ref _failureTotal),
            Interlocked.Read(ref _retries),
            Interlocked.Read(ref _bytesRead),
            Interlocked.Read(ref _bytesWritten),
            averageMs,
            Math.Min(Percentile(0.50, requests), maxMs),
            Math.Min(Percentile(0.95, requests), maxMs),
            maxMs,
            new PlcOperationCounts(
                Interlocked.Read(ref _requests[(int)PlcOperation.Read]),
                Interlocked.Read(ref _requests[(int)PlcOperation.Write]),
                Interlocked.Read(ref _requests[(int)PlcOperation.ReadBatch]),
                Interlocked.Read(ref _requests[(int)PlcOperation.WriteBatch]),
                Interlocked.Read(ref _requests[(int)PlcOperation.ReadWrite]),
                Interlocked.Read(ref _requests[(int)PlcOperation.Ping])),
            new PlcFailureCounts(
                Interlocked.Read(ref _timeouts),
                Interlocked.Read(ref _connectionClosed),
                Interlocked.Read(ref _protocol),
                Interlocked.Read(ref _other)));
    }

    private static int GetBucket(double milliseconds)
    {
        if (milliseconds <= 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(Math.Log(milliseconds, 2) * 4);
        return Math.Min(Math.Max(index, 0), BucketCount - 1);
    }

    private double Percentile(double percentile, long total)
    {
        if (total == 0)
        {
            return 0;
        }

        var target = (long)Math.Ceiling(total * percentile);
        long cumulative = 0;
        for (var i = 0; i < BucketCount; i++)
        {
            cumulative += Interlocked.Read(ref _latencyBuckets[i]);
            if (cumulative >= target)
            {
                return Math.Pow(2, i / 4.0);
            }
        }

        return Math.Pow(2, (BucketCount - 1) / 4.0);
    }

    private static long Sum(long[] values)
    {
        long total = 0;
        for (var i = 0; i < values.Length; i++)
        {
            total += Interlocked.Read(ref values[i]);
        }

        return total;
    }

    private static void UpdateMax(ref long target, long value)
    {
        var current = Interlocked.Read(ref target);
        while (value > current)
        {
            var previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }
}

/// <summary>Per-operation request counts.</summary>
/// <param name="Read">Single reads.</param>
/// <param name="Write">Single writes.</param>
/// <param name="ReadBatch">Batch reads.</param>
/// <param name="WriteBatch">Batch writes.</param>
/// <param name="ReadWrite">Atomic read-write exchanges (Modbus function 23).</param>
/// <param name="Ping">Health probes.</param>
public readonly record struct PlcOperationCounts(
    long Read,
    long Write,
    long ReadBatch,
    long WriteBatch,
    long ReadWrite,
    long Ping);

/// <summary>Failure counts grouped by error kind.</summary>
/// <param name="Timeouts">Request timeouts.</param>
/// <param name="ConnectionClosed">Connections lost or refused.</param>
/// <param name="Protocol">Protocol or device errors.</param>
/// <param name="Other">Any other failure.</param>
public readonly record struct PlcFailureCounts(
    long Timeouts,
    long ConnectionClosed,
    long Protocol,
    long Other);

/// <summary>Immutable snapshot of <see cref="PlcMetrics"/>.</summary>
/// <param name="Requests">Number of request attempts.</param>
/// <param name="Failures">Number of failed attempts.</param>
/// <param name="Retries">Number of retries.</param>
/// <param name="BytesRead">Payload bytes read.</param>
/// <param name="BytesWritten">Payload bytes written.</param>
/// <param name="AverageLatencyMs">Average latency of successful attempts in milliseconds.</param>
/// <param name="P50LatencyMs">Approximate 50th percentile latency in milliseconds.</param>
/// <param name="P95LatencyMs">Approximate 95th percentile latency in milliseconds.</param>
/// <param name="MaxLatencyMs">Maximum observed latency in milliseconds.</param>
/// <param name="Operations">Per-operation request counts.</param>
/// <param name="FailureKinds">Failure counts grouped by error kind.</param>
public readonly record struct PlcMetricsSnapshot(
    long Requests,
    long Failures,
    long Retries,
    long BytesRead,
    long BytesWritten,
    double AverageLatencyMs,
    double P50LatencyMs,
    double P95LatencyMs,
    double MaxLatencyMs,
    PlcOperationCounts Operations,
    PlcFailureCounts FailureKinds)
{
    /// <inheritdoc/>
    public override string ToString() =>
        $"requests={Requests} (read={Operations.Read}, write={Operations.Write}, "
        + $"readBatch={Operations.ReadBatch}, writeBatch={Operations.WriteBatch}, "
        + $"readWrite={Operations.ReadWrite}, ping={Operations.Ping}), "
        + $"failures={Failures} (timeout={FailureKinds.Timeouts}, closed={FailureKinds.ConnectionClosed}, "
        + $"protocol={FailureKinds.Protocol}, other={FailureKinds.Other}), retries={Retries}, "
        + $"read={BytesRead}B, written={BytesWritten}B, "
        + $"avg={AverageLatencyMs:F1}ms, p50={P50LatencyMs:F1}ms, p95={P95LatencyMs:F1}ms, max={MaxLatencyMs:F1}ms";
}
