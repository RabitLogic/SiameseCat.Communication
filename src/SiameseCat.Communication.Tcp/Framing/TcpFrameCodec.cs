using System.Buffers.Binary;

using SiameseCat.Communication.Exceptions;
using SiameseCat.Communication.Transport;

namespace SiameseCat.Communication.Transport.Tcp;

/// <summary>
/// Built-in framing for custom TCP protocols: raw reads, a length prefix, a trailing delimiter or a
/// fixed frame size. Use <see cref="ITcpFrameCodec"/> directly when the wire format needs a custom
/// encoder.
/// </summary>
public sealed class TcpFrameCodec : ITcpFrameCodec
{
    private const int InitialBufferSize = 512;

    /// <summary>Creates the codec.</summary>
    /// <param name="options">Framing settings.</param>
    public TcpFrameCodec(TcpFrameOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }

    /// <summary>Gets the framing settings used by this codec.</summary>
    public TcpFrameOptions Options { get; }

    /// <inheritdoc/>
    public byte[] Encode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > Options.MaxFrameLength && Options.Mode != TcpFrameMode.Raw)
        {
            throw new ArgumentException(
                $"The payload is {payload.Length} bytes, which exceeds MaxFrameLength ({Options.MaxFrameLength}).",
                nameof(payload));
        }

        switch (Options.Mode)
        {
            case TcpFrameMode.Raw:
                return payload.ToArray();

            case TcpFrameMode.LengthPrefix:
            {
                var length = payload.Length + (Options.LengthIncludesPrefix ? Options.PrefixLength : 0);
                var frame = new byte[Options.PrefixLength + payload.Length];
                WriteLength(frame.AsSpan(0, Options.PrefixLength), length);
                payload.CopyTo(frame.AsSpan(Options.PrefixLength));
                return frame;
            }

            case TcpFrameMode.Delimiter:
            {
                var delimiter = Options.Delimiter;
                var frame = new byte[payload.Length + delimiter.Length];
                payload.CopyTo(frame);
                delimiter.CopyTo(frame, payload.Length);
                return frame;
            }

            case TcpFrameMode.FixedLength:
                if (payload.Length != Options.FixedLength)
                {
                    throw new ArgumentException(
                        $"Fixed-length framing requires a payload of exactly {Options.FixedLength} bytes but got {payload.Length}.",
                        nameof(payload));
                }

                return payload.ToArray();

            default:
                throw new InvalidOperationException($"Unknown framing mode '{Options.Mode}'.");
        }
    }

    /// <inheritdoc/>
    public ITcpFrameReader CreateReader() => new Reader(this);

    private void WriteLength(Span<byte> destination, int length)
    {
        switch (Options.PrefixLength)
        {
            case 1:
                if (length > byte.MaxValue)
                {
                    throw new ArgumentException(
                        $"A 1-byte length field cannot encode {length}; increase PrefixLength.",
                        nameof(length));
                }

                destination[0] = (byte)length;
                break;

            case 2:
                if (length > ushort.MaxValue)
                {
                    throw new ArgumentException(
                        $"A 2-byte length field cannot encode {length}; increase PrefixLength.",
                        nameof(length));
                }

                if (Options.BigEndian)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)length);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)length);
                }

                break;

            default:
                if (Options.BigEndian)
                {
                    BinaryPrimitives.WriteInt32BigEndian(destination, length);
                }
                else
                {
                    BinaryPrimitives.WriteInt32LittleEndian(destination, length);
                }

                break;
        }
    }

    private int ReadLength(ReadOnlySpan<byte> source) => Options.PrefixLength switch
    {
        1 => source[0],
        2 => Options.BigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(source)
            : BinaryPrimitives.ReadUInt16LittleEndian(source),
        _ => Options.BigEndian
            ? BinaryPrimitives.ReadInt32BigEndian(source)
            : BinaryPrimitives.ReadInt32LittleEndian(source),
    };

    private sealed class Reader : ITcpFrameReader
    {
        private readonly TcpFrameCodec _codec;
        private byte[] _buffer = new byte[InitialBufferSize];
        private int _count;

        public Reader(TcpFrameCodec codec) => _codec = codec;

        public async ValueTask<byte[]?> ReadFrameAsync(
            ITransport transport,
            CancellationToken cancellationToken = default)
        {
            Guard.NotNull(transport, nameof(transport));

            var options = _codec.Options;
            switch (options.Mode)
            {
                case TcpFrameMode.Raw:
                    return _count > 0
                        ? TakeBuffered(_count)
                        : await ReadRawAsync(transport, cancellationToken).ConfigureAwait(false);

                case TcpFrameMode.LengthPrefix:
                {
                    var prefix = await ReadExactlyAsync(transport, options.PrefixLength, cancellationToken)
                        .ConfigureAwait(false);
                    if (prefix is null)
                    {
                        return null;
                    }

                    var declared = _codec.ReadLength(prefix);
                    var payloadLength = options.LengthIncludesPrefix ? declared - options.PrefixLength : declared;

                    if (payloadLength < 0 || payloadLength > options.MaxFrameLength)
                    {
                        throw new ProtocolException(
                            $"The peer announced a frame of {payloadLength} bytes, which is outside 0..{options.MaxFrameLength}.");
                    }

                    var payload = await ReadExactlyAsync(transport, payloadLength, cancellationToken)
                        .ConfigureAwait(false);
                    if (payload is null)
                    {
                        throw new ConnectionClosedException("The connection was closed in the middle of a length-prefixed frame.");
                    }

                    return payload;
                }

                case TcpFrameMode.FixedLength:
                {
                    var payload = await ReadExactlyAsync(transport, options.FixedLength, cancellationToken)
                        .ConfigureAwait(false);
                    if (payload is null)
                    {
                        return null;
                    }

                    return payload;
                }

                case TcpFrameMode.Delimiter:
                    return await ReadDelimitedAsync(transport, cancellationToken).ConfigureAwait(false);

                default:
                    throw new InvalidOperationException($"Unknown framing mode '{options.Mode}'.");
            }
        }

        private async ValueTask<byte[]?> ReadRawAsync(ITransport transport, CancellationToken cancellationToken)
        {
            var read = await transport.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                return null;
            }

            _count = read;
            return TakeBuffered(_count);
        }

        private async ValueTask<byte[]?> ReadDelimitedAsync(
            ITransport transport,
            CancellationToken cancellationToken)
        {
            var options = _codec.Options;
            var delimiter = options.Delimiter;
            var maxBuffered = options.MaxFrameLength + delimiter.Length;
            var searchStart = 0;

            while (true)
            {
                var index = IndexOf(_buffer, _count, delimiter, searchStart);
                if (index >= 0)
                {
                    var payloadLength = options.IncludeDelimiter ? index + delimiter.Length : index;
                    if (payloadLength > options.MaxFrameLength)
                    {
                        throw new ProtocolException(
                            $"A delimited frame of {payloadLength} bytes exceeds MaxFrameLength ({options.MaxFrameLength}).");
                    }

                    var frame = new byte[payloadLength];
                    Array.Copy(_buffer, frame, payloadLength);
                    Consume(index + delimiter.Length);
                    return frame;
                }

                searchStart = Math.Max(0, _count - delimiter.Length + 1);

                if (_count >= maxBuffered)
                {
                    throw new ProtocolException(
                        $"The peer sent more than MaxFrameLength ({options.MaxFrameLength}) bytes without the delimiter.");
                }

                if (_count == _buffer.Length)
                {
                    Array.Resize(ref _buffer, Math.Min(_buffer.Length * 2, maxBuffered));
                }

                var read = await transport.ReadAsync(_buffer.AsMemory(_count), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    if (_count == 0)
                    {
                        return null;
                    }

                    throw new ConnectionClosedException(
                        "The connection was closed in the middle of a delimited frame.");
                }

                _count += read;
            }
        }

        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes, consuming read-ahead bytes first.
        /// Returns <see langword="null"/> only when the connection closes before the first byte.
        /// </summary>
        private async ValueTask<byte[]?> ReadExactlyAsync(
            ITransport transport,
            int count,
            CancellationToken cancellationToken)
        {
            if (count == 0)
            {
                return Array.Empty<byte>();
            }

            var result = new byte[count];
            var copied = Math.Min(count, _count);
            if (copied > 0)
            {
                Array.Copy(_buffer, 0, result, 0, copied);
                Consume(copied);
            }

            var offset = copied;
            while (offset < count)
            {
                var read = await transport.ReadAsync(result.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    return offset == 0 ? null : throw new ConnectionClosedException(
                        "The connection was closed in the middle of a frame.");
                }

                offset += read;
            }

            return result;
        }

        private byte[] TakeBuffered(int count)
        {
            var frame = new byte[count];
            Array.Copy(_buffer, frame, count);
            Consume(count);
            return frame;
        }

        private void Consume(int count)
        {
            _count -= count;
            if (_count > 0)
            {
                Array.Copy(_buffer, count, _buffer, 0, _count);
            }
        }

        private static int IndexOf(byte[] buffer, int count, byte[] delimiter, int start)
        {
            var first = delimiter[0];
            var last = count - delimiter.Length;

            for (var i = start; i <= last; i++)
            {
                if (buffer[i] != first)
                {
                    continue;
                }

                var matched = true;
                for (var j = 1; j < delimiter.Length; j++)
                {
                    if (buffer[i + j] != delimiter[j])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
