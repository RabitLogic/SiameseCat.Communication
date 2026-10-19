namespace SiameseCat.Server.Modbus;

/// <summary>
/// Thread-safe request and frame counters for a Modbus slave, grouped by function code.
/// </summary>
public sealed class ModbusServerStatistics
{
    private readonly long[] _requests = new long[256];
    private readonly long[] _exceptions = new long[256];
    private long _totalRequests;
    private long _totalExceptions;
    private long _framesReceived;
    private long _framesDiscarded;

    /// <summary>Gets the number of request PDUs that were handled.</summary>
    public long TotalRequests => Interlocked.Read(ref _totalRequests);

    /// <summary>Gets the number of request PDUs that were answered with an exception.</summary>
    public long TotalExceptions => Interlocked.Read(ref _totalExceptions);

    /// <summary>Gets the number of complete frames that were received.</summary>
    public long FramesReceived => Interlocked.Read(ref _framesReceived);

    /// <summary>Gets the number of frames that were dropped because they were truncated or malformed.</summary>
    public long FramesDiscarded => Interlocked.Read(ref _framesDiscarded);

    /// <summary>Gets how many requests were handled for a function code.</summary>
    /// <param name="functionCode">Modbus function code.</param>
    public long GetRequests(byte functionCode) => Interlocked.Read(ref _requests[functionCode]);

    /// <summary>Gets how many exception responses were sent for a function code.</summary>
    /// <param name="functionCode">Modbus function code.</param>
    public long GetExceptions(byte functionCode) => Interlocked.Read(ref _exceptions[functionCode]);

    /// <summary>Clears every counter.</summary>
    public void Reset()
    {
        Array.Clear(_requests, 0, _requests.Length);
        Array.Clear(_exceptions, 0, _exceptions.Length);
        Interlocked.Exchange(ref _totalRequests, 0);
        Interlocked.Exchange(ref _totalExceptions, 0);
        Interlocked.Exchange(ref _framesReceived, 0);
        Interlocked.Exchange(ref _framesDiscarded, 0);
    }

    internal void RecordRequest(byte functionCode)
    {
        Interlocked.Increment(ref _requests[functionCode]);
        Interlocked.Increment(ref _totalRequests);
    }

    internal void RecordException(byte functionCode)
    {
        Interlocked.Increment(ref _exceptions[functionCode]);
        Interlocked.Increment(ref _totalExceptions);
    }

    internal void RecordFrame() => Interlocked.Increment(ref _framesReceived);

    internal void RecordDiscardedFrame() => Interlocked.Increment(ref _framesDiscarded);
}
