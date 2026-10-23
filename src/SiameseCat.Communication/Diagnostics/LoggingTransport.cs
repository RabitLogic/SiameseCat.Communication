using System.Text;

namespace SiameseCat.Communication;

/// <summary>
/// Decorates a transport and writes every frame to an <see cref="ICommunicationLogger"/> at
/// <see cref="CommunicationLogLevel.Trace"/>, which makes protocol problems visible in production logs.
/// </summary>
public sealed class LoggingTransport : ITransport
{
    private readonly ITransport _inner;
    private readonly ICommunicationLogger _logger;
    private readonly string _name;

    /// <summary>Creates the decorator.</summary>
    /// <param name="inner">Transport that performs the actual I/O.</param>
    /// <param name="logger">Logger that receives the frame dumps.</param>
    /// <param name="name">Short label used in the log entries, for example <c>modbus/1</c>.</param>
    public LoggingTransport(ITransport inner, ICommunicationLogger logger, string? name = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _name = string.IsNullOrWhiteSpace(name) ? "transport" : name!;
    }

    /// <inheritdoc/>
    public bool IsOpen => _inner.IsOpen;

    /// <inheritdoc/>
    public Task OpenAsync(CancellationToken cancellationToken = default) => _inner.OpenAsync(cancellationToken);

    /// <inheritdoc/>
    public Task CloseAsync() => _inner.CloseAsync();

    /// <inheritdoc/>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length > 0)
        {
            _logger.Log(CommunicationLogLevel.Trace, $"{_name} TX {ToHex(buffer.Span)}");
        }

        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0)
        {
            _logger.Log(CommunicationLogLevel.Trace, $"{_name} RX {ToHex(buffer.Span[..read])}");
        }

        return read;
    }

    /// <inheritdoc/>
    public void DiscardInput() => _inner.DiscardInput();

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    private static string ToHex(ReadOnlySpan<byte> data)
    {
        var hex = new StringBuilder(data.Length * 3);
        foreach (var value in data)
        {
            if (hex.Length > 0)
            {
                hex.Append(' ');
            }

            hex.Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }
}
