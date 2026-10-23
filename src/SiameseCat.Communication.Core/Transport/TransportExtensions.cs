using SiameseCat.Communication.Exceptions;

namespace SiameseCat.Communication.Transport;

/// <summary>Helpers for reading a fixed number of bytes from an <see cref="ITransport"/>.</summary>
public static class TransportExtensions
{
    /// <summary>Reads exactly <paramref name="count"/> bytes or throws when the connection closes.</summary>
    public static async Task<byte[]> ReadExactlyAsync(
        this ITransport transport,
        int count,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(transport, nameof(transport));
        Guard.NotNegative(count, nameof(count));

        var buffer = new byte[count];
        await transport.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }

    /// <summary>Fills <paramref name="buffer"/> completely or throws when the connection closes.</summary>
    public static async Task ReadExactlyAsync(
        this ITransport transport,
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(transport, nameof(transport));

        var written = 0;
        while (written < buffer.Length)
        {
            var read = await transport.ReadAsync(buffer[written..], cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                throw new ConnectionClosedException("The connection was closed while waiting for data.");
            }

            written += read;
        }
    }
}
