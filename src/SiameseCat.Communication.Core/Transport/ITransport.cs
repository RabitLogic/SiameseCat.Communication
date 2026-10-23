namespace SiameseCat.Communication;

/// <summary>
/// Byte-oriented transport used by the protocol clients. A transport is not thread-safe;
/// clients serialize access to it.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>Gets whether the transport is currently open.</summary>
    bool IsOpen { get; }

    /// <summary>Opens the underlying connection.</summary>
    Task OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the underlying connection.</summary>
    Task CloseAsync();

    /// <summary>Writes all bytes to the connection.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads at least one byte. Returns the number of bytes read, or 0 when the peer closed the connection.
    /// </summary>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>Discards any bytes that are already buffered by the transport.</summary>
    void DiscardInput();
}
