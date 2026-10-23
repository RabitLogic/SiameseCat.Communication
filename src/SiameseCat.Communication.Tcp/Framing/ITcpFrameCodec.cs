using SiameseCat.Communication.Exceptions;
using SiameseCat.Communication.Transport;

namespace SiameseCat.Communication.Transport.Tcp;

/// <summary>
/// Turns payloads into wire frames and back. Implement it to support a framing scheme the built-in
/// <see cref="TcpFrameCodec"/> does not cover, for example a checksummed or escaped frame.
/// </summary>
/// <remarks>A codec is shared by every connection, so implementations must be stateless.</remarks>
public interface ITcpFrameCodec
{
    /// <summary>Encodes one payload into the bytes written to the wire.</summary>
    /// <param name="payload">Protocol payload without framing.</param>
    byte[] Encode(ReadOnlySpan<byte> payload);

    /// <summary>
    /// Creates a reader with its own read-ahead buffer. Create one reader per client or connection;
    /// readers are not thread-safe.
    /// </summary>
    ITcpFrameReader CreateReader();
}

/// <summary>Reads complete frames from one connection.</summary>
public interface ITcpFrameReader
{
    /// <summary>Reads exactly one frame.</summary>
    /// <param name="transport">Open transport that carries the byte stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The frame payload, or <see langword="null"/> when the peer closed the connection cleanly.</returns>
    /// <exception cref="ConnectionClosedException">The peer closed the connection in the middle of a frame.</exception>
    /// <exception cref="ProtocolException">The frame violates the configured limits.</exception>
    ValueTask<byte[]?> ReadFrameAsync(ITransport transport, CancellationToken cancellationToken = default);
}
