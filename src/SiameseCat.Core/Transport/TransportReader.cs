namespace SiameseCat.Core;

/// <summary>
/// Adds a small read-ahead buffer in front of an <see cref="ITransport"/> so bytes that were read
/// while checking for a request echo are not lost.
/// </summary>
public sealed class TransportReader
{
    private readonly ITransport _transport;
    private byte[] _pending = [];
    private int _pendingOffset;

    /// <summary>Creates a reader over <paramref name="transport"/>.</summary>
    public TransportReader(ITransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes, including any prepended bytes.</summary>
    public async Task<byte[]> ReadExactlyAsync(int count, CancellationToken cancellationToken)
    {
        if (count == 0)
        {
            return Array.Empty<byte>();
        }

        var result = new byte[count];
        var written = 0;

        var available = _pending.Length - _pendingOffset;
        if (available > 0)
        {
            var copy = Math.Min(count, available);
            Array.Copy(_pending, _pendingOffset, result, 0, copy);
            _pendingOffset += copy;
            written = copy;

            if (_pendingOffset >= _pending.Length)
            {
                _pending = Array.Empty<byte>();
                _pendingOffset = 0;
            }
        }

        while (written < count)
        {
            var read = await _transport.ReadAsync(result.AsMemory(written), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                throw new ConnectionClosedException("The connection was closed while waiting for a response.");
            }

            written += read;
        }

        return result;
    }

    /// <summary>Pushes bytes back so they are returned by the next read.</summary>
    public void Prepend(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        var remaining = _pending.Length - _pendingOffset;
        var merged = new byte[data.Length + remaining];
        data.CopyTo(merged);
        if (remaining > 0)
        {
            Array.Copy(_pending, _pendingOffset, merged, data.Length, remaining);
        }

        _pending = merged;
        _pendingOffset = 0;
    }
}
