namespace SiameseCat.Server;

/// <summary>Thread-safe counters for an <see cref="S7Server"/>.</summary>
public sealed class S7ServerStatistics
{
    private long _connections;
    private long _activeConnections;
    private long _requests;
    private long _setupRequests;
    private long _readRequests;
    private long _writeRequests;
    private long _itemsRead;
    private long _itemsWritten;
    private long _itemErrors;
    private long _framesDiscarded;

    /// <summary>Gets how many ISO-on-TCP connections were accepted.</summary>
    public long TotalConnections => Interlocked.Read(ref _connections);

    /// <summary>Gets how many connections are open right now.</summary>
    public long ActiveConnections => Interlocked.Read(ref _activeConnections);

    /// <summary>Gets how many S7 job PDUs were handled.</summary>
    public long TotalRequests => Interlocked.Read(ref _requests);

    /// <summary>Gets how many Setup Communication requests were handled.</summary>
    public long SetupRequests => Interlocked.Read(ref _setupRequests);

    /// <summary>Gets how many read-variable requests were handled.</summary>
    public long ReadRequests => Interlocked.Read(ref _readRequests);

    /// <summary>Gets how many write-variable requests were handled.</summary>
    public long WriteRequests => Interlocked.Read(ref _writeRequests);

    /// <summary>Gets how many variables were read.</summary>
    public long ItemsRead => Interlocked.Read(ref _itemsRead);

    /// <summary>Gets how many variables were written.</summary>
    public long ItemsWritten => Interlocked.Read(ref _itemsWritten);

    /// <summary>Gets how many item-level errors were returned.</summary>
    public long ItemErrors => Interlocked.Read(ref _itemErrors);

    /// <summary>Gets how many frames were dropped (bad TPKT length, unknown COTP or S7 framing).</summary>
    public long FramesDiscarded => Interlocked.Read(ref _framesDiscarded);

    /// <summary>Clears every counter.</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _connections, 0);
        Interlocked.Exchange(ref _activeConnections, 0);
        Interlocked.Exchange(ref _requests, 0);
        Interlocked.Exchange(ref _setupRequests, 0);
        Interlocked.Exchange(ref _readRequests, 0);
        Interlocked.Exchange(ref _writeRequests, 0);
        Interlocked.Exchange(ref _itemsRead, 0);
        Interlocked.Exchange(ref _itemsWritten, 0);
        Interlocked.Exchange(ref _itemErrors, 0);
        Interlocked.Exchange(ref _framesDiscarded, 0);
    }

    internal void RecordConnection()
    {
        Interlocked.Increment(ref _connections);
        Interlocked.Increment(ref _activeConnections);
    }

    internal void RecordDisconnection() => Interlocked.Decrement(ref _activeConnections);

    internal void RecordRequest() => Interlocked.Increment(ref _requests);

    internal void RecordSetup() => Interlocked.Increment(ref _setupRequests);

    internal void RecordRead(int items)
    {
        Interlocked.Increment(ref _readRequests);
        Interlocked.Add(ref _itemsRead, items);
    }

    internal void RecordWrite(int items)
    {
        Interlocked.Increment(ref _writeRequests);
        Interlocked.Add(ref _itemsWritten, items);
    }

    internal void RecordItemError() => Interlocked.Increment(ref _itemErrors);

    internal void RecordDiscardedFrame() => Interlocked.Increment(ref _framesDiscarded);
}
