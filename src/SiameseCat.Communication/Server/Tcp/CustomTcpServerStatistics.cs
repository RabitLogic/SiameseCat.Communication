namespace SiameseCat.Communication.Server.Tcp;

/// <summary>Counters collected by <see cref="CustomTcpServer"/>.</summary>
public sealed class CustomTcpServerStatistics
{
    private long _connectionsAccepted;
    private long _framesReceived;
    private long _framesSent;
    private long _bytesReceived;
    private long _bytesSent;
    private long _handlerFaults;

    /// <summary>Gets how many connections were accepted since the server was created.</summary>
    public long ConnectionsAccepted => Interlocked.Read(ref _connectionsAccepted);

    /// <summary>Gets how many frames the server read from clients.</summary>
    public long FramesReceived => Interlocked.Read(ref _framesReceived);

    /// <summary>Gets how many frames the server sent to clients.</summary>
    public long FramesSent => Interlocked.Read(ref _framesSent);

    /// <summary>Gets how many bytes the server read, framing included.</summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <summary>Gets how many bytes the server wrote, framing included.</summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>Gets how many connection handlers failed with an unexpected exception.</summary>
    public long HandlerFaults => Interlocked.Read(ref _handlerFaults);

    internal void RecordConnectionAccepted() => Interlocked.Increment(ref _connectionsAccepted);

    internal void RecordFrameReceived(int bytes)
    {
        Interlocked.Increment(ref _framesReceived);
        Interlocked.Add(ref _bytesReceived, bytes);
    }

    internal void RecordFrameSent(int bytes)
    {
        Interlocked.Increment(ref _framesSent);
        Interlocked.Add(ref _bytesSent, bytes);
    }

    internal void RecordHandlerFault() => Interlocked.Increment(ref _handlerFaults);
}
