using SiameseCat.Communication.Exceptions;
using SiameseCat.Communication.Transport;
using SiameseCat.Communication.Transport.Tcp;

namespace SiameseCat.Communication.Modbus;

/// <summary>
/// MBAP framing for <see cref="ModbusTcpClient"/>, built on the shared TCP frame reader. A frame is
/// returned as the complete MBAP header plus body so the client can validate the transaction id,
/// protocol id and unit id before it parses the PDU.
/// </summary>
/// <remarks>
/// This codec only understands the Modbus Application Protocol header; it is unrelated to the generic
/// framing presets in <see cref="TcpFrameCodec"/>. The Modbus RTU side has no codec because its framing
/// is derived from the function code and the CRC.
/// </remarks>
public sealed class MbapFrameCodec : ITcpFrameCodec
{
    internal const int HeaderLength = 6;
    internal const int MaxBodyLength = 254;
    internal const int MinBodyLength = 2;

    /// <inheritdoc/>
    /// <remarks>The payload is expected to be a complete MBAP frame.</remarks>
    public byte[] Encode(ReadOnlySpan<byte> payload) => payload.ToArray();

    /// <inheritdoc/>
    public ITcpFrameReader CreateReader() => new Reader();

    private sealed class Reader : ITcpFrameReader
    {
        public async ValueTask<byte[]?> ReadFrameAsync(
            ITransport transport,
            CancellationToken cancellationToken = default)
        {
            Guard.NotNull(transport, nameof(transport));

            var prefix = new byte[HeaderLength];
            if (!await ReadExactlyAsync(transport, prefix, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var length = (prefix[4] << 8) | prefix[5];
            if (length is < MinBodyLength or > MaxBodyLength)
            {
                throw new ProtocolException($"The Modbus TCP frame has invalid length {length}.");
            }

            var frame = new byte[HeaderLength + length];
            prefix.CopyTo(frame, 0);

            await ReadExactlyAsync(transport, frame.AsMemory(HeaderLength, length), cancellationToken)
                .ConfigureAwait(false);

            return frame;
        }

        private static async Task<bool> ReadExactlyAsync(
            ITransport transport,
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await transport.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    if (offset == 0)
                    {
                        return false;
                    }

                    throw new ConnectionClosedException(
                        "The connection was closed in the middle of a Modbus TCP frame.");
                }

                offset += read;
            }

            return true;
        }
    }
}
