using System.Net;
using Microsoft.Extensions.Logging;
using SiameseCat.Communication.Tcp;

namespace SiameseCat.Communication.Simulation.Tcp;

/// <summary>
/// One client connection accepted by <see cref="CustomTcpSimulator"/>. Read and write frames; a single
/// handler owns the connection and operations on it are not thread-safe.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The adapter only wraps the socket stream; the server owns and closes the client.")]
public sealed class CustomTcpConnection
{
    private readonly StreamTransport _transport;
    private readonly TcpFrameConnection _connection;
    private readonly CustomTcpSimulatorStatistics _statistics;

    internal CustomTcpConnection(
        Stream stream,
        EndPoint? remoteEndPoint,
        ITcpFrameCodec codec,
        CustomTcpSimulatorStatistics statistics,
        ILogger? logger,
        bool logFrames)
    {
        _transport = new StreamTransport(stream);
        _connection = new TcpFrameConnection(
            _transport,
            codec,
            logger,
            logFrames,
            remoteEndPoint?.ToString() ?? "client");
        _statistics = statistics;
        RemoteEndPoint = remoteEndPoint;
    }

    /// <summary>Gets the remote endpoint of the client, when the socket reports one.</summary>
    public EndPoint? RemoteEndPoint { get; }

    /// <summary>Reads the next frame.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The payload without framing, or <see langword="null"/> when the client closed the connection.</returns>
    public async ValueTask<byte[]?> ReadFrameAsync(CancellationToken cancellationToken = default)
    {
        var frame = await _connection.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        if (frame is not null)
        {
            _statistics.RecordFrameReceived(frame.Length);
        }

        return frame;
    }

    /// <summary>Sends one frame.</summary>
    /// <param name="payload">Payload without framing; the server codec adds it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        var frame = _connection.Codec.Encode(payload.Span);
        await _connection.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        _statistics.RecordFrameSent(frame.Length);
    }
}
