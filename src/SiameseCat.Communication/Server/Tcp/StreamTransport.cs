using System.Buffers;

namespace SiameseCat.Communication.Server.Tcp;

/// <summary>
/// Adapts the network stream of an accepted socket to <see cref="ITransport"/> so the shared frame
/// codecs can read from it.
/// </summary>
internal sealed class StreamTransport : ITransport
{
    private readonly Stream _stream;

    public StreamTransport(Stream stream) => _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    public bool IsOpen { get; private set; } = true;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        IsOpen = false;
        return Task.CompletedTask;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        return _stream.WriteAsync(buffer, cancellationToken);
#else
        return new ValueTask(_stream.WriteAsync(buffer.ToArray(), 0, buffer.Length, cancellationToken));
#endif
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        return _stream.ReadAsync(buffer, cancellationToken);
#else
        return ReadLegacyAsync(buffer, cancellationToken);
#endif
    }

    public void DiscardInput()
    {
        // The accepted socket is owned by the server connection; nothing is buffered here.
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

#if !NET8_0_OR_GREATER
    private async ValueTask<int> ReadLegacyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = await _stream.ReadAsync(rented, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            rented.AsSpan(0, read).CopyTo(buffer.Span);
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
#endif
}
