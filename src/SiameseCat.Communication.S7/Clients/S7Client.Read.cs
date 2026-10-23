using System.Buffers.Binary;

using SiameseCat.Communication;
using SiameseCat.Communication.Exceptions;

namespace SiameseCat.Communication.S7;

public sealed partial class S7Client
{
    /// <summary>Reads raw bytes starting at <paramref name="address"/>.</summary>
    /// <param name="address">Start address inside a DB, M, I or Q area.</param>
    /// <param name="count">Number of bytes to read. Large reads are split according to the negotiated PDU size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bytes returned by the device.</returns>
    /// <exception cref="ArgumentException">The address is a bit address; use <see cref="ReadBoolAsync"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The requested range exceeds the S7 address space.</exception>
    public async Task<byte[]> ReadBytesAsync(
        S7Address address,
        int count,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNegative(count, nameof(count));
        if (count == 0)
        {
            return Array.Empty<byte>();
        }

        if (address.DataType == S7DataType.Bool)
        {
            throw new ArgumentException("Use ReadBoolAsync for bit addresses.", nameof(address));
        }

        ValidateRange(address, count);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();

            var result = new byte[count];
            var offset = 0;
            var maxChunk = GetReadChunkSize();

            while (offset < count)
            {
                var chunk = Math.Min(maxChunk, count - offset);
                var chunkAddress = new S7Address(
                    address.Area, address.DbNumber, address.ByteOffset + offset, address.DataType);
                var data = await ReadVarSingleAsync(chunkAddress, chunk, bit: false, cancellationToken)
                    .ConfigureAwait(false);

                if (data.Length < chunk)
                {
                    throw new ProtocolException(
                        $"The device returned {data.Length} bytes but {chunk} were requested.");
                }

                Array.Copy(data, 0, result, offset, chunk);
                offset += chunk;
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads a single bit.</summary>
    /// <param name="address">Bit address such as <c>DB1.DBX0.0</c> or <c>M0.0</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">The address is not a bit address.</exception>
    public async Task<bool> ReadBoolAsync(S7Address address, CancellationToken cancellationToken = default)
    {
        if (address.DataType != S7DataType.Bool)
        {
            throw new ArgumentException("The address must be a Bool address such as DB1.DBX0.0 or M0.0.", nameof(address));
        }

        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();
            var data = await ReadVarSingleAsync(address, 1, bit: true, cancellationToken).ConfigureAwait(false);
            return data.Length > 0 && (data[0] & 0x01) != 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads one byte.</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<byte> ReadByteAsync(S7Address address, CancellationToken cancellationToken = default) =>
        (await ReadBytesAsync(address, 1, cancellationToken).ConfigureAwait(false))[0];

    /// <summary>Reads a 16-bit unsigned integer (big-endian on the wire).</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ushort> ReadUInt16Async(S7Address address, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(address, 2, cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }

    /// <summary>Reads a 16-bit signed integer (big-endian on the wire).</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<short> ReadInt16Async(S7Address address, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(address, 2, cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt16BigEndian(bytes);
    }

    /// <summary>Reads a 32-bit unsigned integer (big-endian on the wire).</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<uint> ReadUInt32Async(S7Address address, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(address, 4, cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    /// <summary>Reads a 32-bit signed integer (big-endian on the wire).</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<int> ReadInt32Async(S7Address address, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(address, 4, cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32BigEndian(bytes);
    }

    /// <summary>Reads an IEEE 754 single-precision value.</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<float> ReadSingleAsync(S7Address address, CancellationToken cancellationToken = default)
    {
        var bits = await ReadInt32Async(address, cancellationToken).ConfigureAwait(false);
        return BitConverter.Int32BitsToSingle(bits);
    }

    /// <summary>Alias of <see cref="ReadSingleAsync"/> for callers that use the S7 name.</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<float> ReadFloatAsync(S7Address address, CancellationToken cancellationToken = default) =>
        ReadSingleAsync(address, cancellationToken);

    /// <summary>
    /// Reads an S7 STRING. The storage starts with a maximum-length byte and a current-length byte,
    /// followed by <paramref name="maxLength"/> characters.
    /// </summary>
    /// <param name="address">Address of the maximum-length byte.</param>
    /// <param name="maxLength">Declared maximum character count of the STRING.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is outside 0..254.</exception>
    public async Task<string> ReadStringAsync(
        S7Address address,
        int maxLength,
        CancellationToken cancellationToken = default)
    {
        if (maxLength is < 0 or > 254)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "S7 string length must be between 0 and 254.");
        }

        var bytes = await ReadBytesAsync(address, maxLength + 2, cancellationToken).ConfigureAwait(false);
        var currentLength = bytes[1];
        if (currentLength > maxLength || currentLength > bytes.Length - 2)
        {
            throw new ProtocolException($"The device returned an invalid S7 string length ({currentLength}).");
        }

        return _options.StringEncoding.GetString(bytes, 2, currentLength);
    }

    /// <summary>Reads several variables in a single S7 request.</summary>
    /// <param name="items">Items to read. The request must fit in the negotiated PDU size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One payload per item, in the same order as <paramref name="items"/>.</returns>
    /// <exception cref="ArgumentException">The request does not fit in the negotiated PDU size.</exception>
    public async Task<byte[][]> ReadMultipleAsync(
        IReadOnlyList<S7ReadItem> items,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(items, nameof(items));
        if (items.Count == 0)
        {
            return Array.Empty<byte[]>();
        }

        foreach (var item in items)
        {
            Guard.NotNull(item, nameof(items));
        }

        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();

            var parameter = BuildMultiReadParameter(items);
            if (parameter.Length + 12 > _pduSize)
            {
                throw new ArgumentException(
                    $"The request needs {parameter.Length} parameter bytes but the negotiated PDU size is {_pduSize}.",
                    nameof(items));
            }

            var response = await ExecuteJobAsync(
                parameter, ReadOnlyMemory<byte>.Empty, isWrite: false, cancellationToken)
                .ConfigureAwait(false);
            ValidateReadParameter(response.Parameter, items.Count);
            var raw = ParseReadData(response.Data, items.Count);
            var result = new byte[items.Count][];

            for (var i = 0; i < items.Count; i++)
            {
                var expected = items[i].Address.DataType == S7DataType.Bool ? 1 : items[i].Length;
                if (raw[i].Length < expected)
                {
                    throw new ProtocolException(
                        $"The device returned {raw[i].Length} bytes for item {i} but {expected} were requested.");
                }

                result[i] = raw[i].AsSpan(0, expected).ToArray();
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }
}
