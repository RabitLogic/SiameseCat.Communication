using System.Text;
using Microsoft.Extensions.Logging;

namespace SiameseCat.Communication.Tcp;

/// <summary>
/// Writes and reads whole frames over a transport with a <see cref="ITcpFrameCodec"/>. It contains no
/// protocol knowledge, so custom TCP protocols and Modbus TCP share it; timeouts, retries and
/// serialization stay with the caller that owns the connection.
/// </summary>
/// <remarks>Instances are not thread-safe; serialize calls or use one instance per connection.</remarks>
public sealed class TcpFrameConnection
{
    private static readonly Action<ILogger, string, string, Exception?> LogTx =
        LoggerMessage.Define<string, string>(LogLevel.Trace, new EventId(1, "TcpTx"), "{Name} TX {Frame}");

    private static readonly Action<ILogger, string, string, Exception?> LogRx =
        LoggerMessage.Define<string, string>(LogLevel.Trace, new EventId(2, "TcpRx"), "{Name} RX {Frame}");

    private readonly ITransport _transport;
    private readonly ILogger? _logger;
    private readonly bool _logFrames;
    private readonly string _name;
    private ITcpFrameReader _reader;

    /// <summary>Creates the connection wrapper.</summary>
    /// <param name="transport">Transport that carries the byte stream; the caller owns its lifetime.</param>
    /// <param name="codec">Codec that turns payloads into frames and back.</param>
    /// <param name="logger">Optional logger for frame dumps.</param>
    /// <param name="logFrames">Whether every frame is logged at trace level.</param>
    /// <param name="name">Short label used in log entries.</param>
    public TcpFrameConnection(
        ITransport transport,
        ITcpFrameCodec codec,
        ILogger? logger = null,
        bool logFrames = false,
        string? name = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _logger = logger;
        _logFrames = logFrames;
        _name = string.IsNullOrWhiteSpace(name) ? "tcp" : name!;
        _reader = Codec.CreateReader();
    }

    /// <summary>Gets the framing codec used by this connection.</summary>
    public ITcpFrameCodec Codec { get; }

    /// <summary>Gets whether the underlying transport is open.</summary>
    public bool IsOpen => _transport.IsOpen;

    /// <summary>Discards the read-ahead buffer, for example after a reconnect.</summary>
    public void ResetReader() => _reader = Codec.CreateReader();

    /// <summary>Encodes a payload and writes the resulting frame.</summary>
    /// <param name="payload">Protocol payload without framing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default) =>
        WriteAsync(Codec.Encode(payload.Span), cancellationToken);

    /// <summary>Writes a frame that the caller has already encoded.</summary>
    /// <param name="frame">Complete wire frame.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (_logFrames && _logger is not null && _logger.IsEnabled(LogLevel.Trace))
        {
            LogTx(_logger, _name, ToHex(frame.Span), null);
        }

        await _transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads exactly one frame.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The frame payload, or <see langword="null"/> when the peer closed the connection.</returns>
    public async ValueTask<byte[]?> ReadFrameAsync(CancellationToken cancellationToken = default)
    {
        var frame = await _reader.ReadFrameAsync(_transport, cancellationToken).ConfigureAwait(false);

        if (frame is not null && _logFrames && _logger is not null && _logger.IsEnabled(LogLevel.Trace))
        {
            LogRx(_logger, _name, ToHex(frame), null);
        }

        return frame;
    }

    /// <summary>Writes a frame and reads the next frame. Timeouts and retries are the caller's responsibility.</summary>
    /// <param name="frame">Complete wire frame.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response payload, or an exception when the peer closes the connection.</returns>
    public async Task<byte[]> ExchangeAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default)
    {
        await WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        return await ReadFrameAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ConnectionClosedException("The peer closed the connection before answering.");
    }

    /// <summary>Discards bytes that the transport has already buffered.</summary>
    public void DiscardInput() => _transport.DiscardInput();

    private void EnsureConnected()
    {
        if (!_transport.IsOpen)
        {
            throw new ConnectionClosedException("The connection is not open.");
        }
    }

    private static string ToHex(ReadOnlySpan<byte> data)
    {
        var text = new StringBuilder(data.Length * 3);
        foreach (var value in data)
        {
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }
}
