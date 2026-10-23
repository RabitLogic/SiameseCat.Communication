using System.Buffers.Binary;

using SiameseCat.Communication;

namespace SiameseCat.Communication.S7;

public sealed partial class S7Client
{
    /// <summary>Writes raw bytes starting at <paramref name="address"/>.</summary>
    /// <param name="address">Start address inside a DB, M, I or Q area.</param>
    /// <param name="data">Bytes to write. Large payloads are split according to the negotiated PDU size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">The address is a bit address; use <see cref="WriteBoolAsync"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The requested range exceeds the S7 address space.</exception>
    public async Task WriteBytesAsync(
        S7Address address,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        if (address.DataType == S7DataType.Bool)
        {
            throw new ArgumentException("Use WriteBoolAsync for bit addresses.", nameof(address));
        }

        if (data.IsEmpty)
        {
            return;
        }

        ValidateRange(address, data.Length);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();

            var offset = 0;
            var maxChunk = GetWriteChunkSize();

            while (offset < data.Length)
            {
                var chunk = Math.Min(maxChunk, data.Length - offset);
                var chunkAddress = new S7Address(
                    address.Area, address.DbNumber, address.ByteOffset + offset, address.DataType);
                var parameter = BuildVariableParameter(0x05, chunkAddress, chunk, bit: false);
                var dataItem = BuildWriteData(data.Slice(offset, chunk).Span, bit: false);
                var response = await ExecuteJobAsync(parameter, dataItem, isWrite: true, cancellationToken)
                    .ConfigureAwait(false);

                ValidateWriteResponse(response);
                offset += chunk;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Writes a consecutive array of bits starting at <paramref name="startAddress"/>.</summary>
    /// <param name="startAddress">First bit address, for example <c>M0.0</c> or <c>DB1.DBX2.0</c>.</param>
    /// <param name="values">Bits to write; consecutive bits may cross byte boundaries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The array is sent as one multi-variable write request and split only when the negotiated PDU
    /// size forces it.
    /// </remarks>
    /// <exception cref="ArgumentException">The start address is not a bit address.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The range exceeds the S7 address space.</exception>
    public async Task WriteBitsAsync(
        S7Address startAddress,
        IReadOnlyList<bool> values,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(values, nameof(values));
        if (startAddress.DataType != S7DataType.Bool)
        {
            throw new ArgumentException(
                "The start address must be a bit address such as DB1.DBX0.0 or M0.0.",
                nameof(startAddress));
        }

        if (values.Count == 0)
        {
            return;
        }

        ValidateRange(startAddress.AddBits(values.Count - 1), 1);
        ThrowIfDisposed();

        // Each bit item costs 12 parameter bytes, a 4-byte data header and a 2-byte payload slot.
        const int bitItemCost = 18;
        var perRequest = Math.Max(1, (_pduSize - 12) / bitItemCost);

        // WriteMultipleAsync serializes every request, so the driver lock is not taken here.
        for (var offset = 0; offset < values.Count; offset += perRequest)
        {
            var count = Math.Min(perRequest, values.Count - offset);
            var items = new S7WriteItem[count];
            for (var i = 0; i < count; i++)
            {
                items[i] = new S7WriteItem(
                    startAddress.AddBits(offset + i),
                    new[] { values[offset + i] ? (byte)1 : (byte)0 });
            }

            await WriteMultipleAsync(items, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Writes a single bit.</summary>
    /// <param name="address">Bit address such as <c>DB1.DBX0.0</c> or <c>M0.0</c>.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">The address is not a bit address.</exception>
    public async Task WriteBoolAsync(
        S7Address address,
        bool value,
        CancellationToken cancellationToken = default)
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
            var parameter = BuildVariableParameter(0x05, address, 1, bit: true);
            var payload = new[] { value ? (byte)0x01 : (byte)0x00 };
            var response = await ExecuteJobAsync(
                parameter, BuildWriteData(payload, bit: true), isWrite: true, cancellationToken)
                .ConfigureAwait(false);
            ValidateWriteResponse(response);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Writes one byte.</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteByteAsync(
        S7Address address,
        byte value,
        CancellationToken cancellationToken = default) =>
        WriteBytesAsync(address, new[] { value }, cancellationToken);

    /// <summary>Writes a 16-bit unsigned integer (big-endian on the wire).</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteUInt16Async(
        S7Address address,
        ushort value,
        CancellationToken cancellationToken = default)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        await WriteBytesAsync(address, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a 16-bit signed integer (big-endian on the wire).</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteInt16Async(
        S7Address address,
        short value,
        CancellationToken cancellationToken = default)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        await WriteBytesAsync(address, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a 32-bit unsigned integer (big-endian on the wire).</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteUInt32Async(
        S7Address address,
        uint value,
        CancellationToken cancellationToken = default)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        await WriteBytesAsync(address, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a 32-bit signed integer (big-endian on the wire).</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteInt32Async(
        S7Address address,
        int value,
        CancellationToken cancellationToken = default)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        await WriteBytesAsync(address, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes an IEEE 754 single-precision value.</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteSingleAsync(
        S7Address address,
        float value,
        CancellationToken cancellationToken = default) =>
        WriteInt32Async(address, BitConverter.SingleToInt32Bits(value), cancellationToken);

    /// <summary>Alias of <see cref="WriteSingleAsync"/> for callers that use the S7 name.</summary>
    /// <param name="address">Byte, word or dword address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteFloatAsync(
        S7Address address,
        float value,
        CancellationToken cancellationToken = default) =>
        WriteSingleAsync(address, value, cancellationToken);

    /// <summary>Writes an S7 STRING and zero-pads the remaining character storage.</summary>
    /// <param name="address">Address of the maximum-length byte.</param>
    /// <param name="value">Text to write. It is encoded with <see cref="S7Options.StringEncoding"/>.</param>
    /// <param name="maxLength">Declared maximum character count of the STRING.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is outside 0..254.</exception>
    /// <exception cref="ArgumentException">The encoded value does not fit in <paramref name="maxLength"/>.</exception>
    public async Task WriteStringAsync(
        S7Address address,
        string value,
        int maxLength,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(value, nameof(value));
        if (maxLength is < 0 or > 254)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "S7 string length must be between 0 and 254.");
        }

        var encoded = _options.StringEncoding.GetBytes(value);
        if (encoded.Length > maxLength)
        {
            throw new ArgumentException(
                $"The encoded string is {encoded.Length} bytes but the maximum length is {maxLength}.",
                nameof(value));
        }

        var buffer = new byte[maxLength + 2];
        buffer[0] = (byte)maxLength;
        buffer[1] = (byte)encoded.Length;
        encoded.CopyTo(buffer, 2);
        await WriteBytesAsync(address, buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes several variables in a single S7 request.</summary>
    /// <param name="items">Items to write. The request must fit in the negotiated PDU size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">The request does not fit in the negotiated PDU size.</exception>
    public async Task WriteMultipleAsync(
        IReadOnlyList<S7WriteItem> items,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(items, nameof(items));
        if (items.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            Guard.NotNull(item, nameof(items));
            ValidateRange(item.Address, item.Data.Length);
        }

        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConnected();

            var parameter = BuildMultiWriteParameter(items);
            var data = BuildMultiWriteData(items);
            if (parameter.Length + data.Length + 10 > _pduSize)
            {
                throw new ArgumentException(
                    $"The request needs {parameter.Length + data.Length} bytes but the negotiated PDU size is {_pduSize}.",
                    nameof(items));
            }

            var response = await ExecuteJobAsync(parameter, data, isWrite: true, cancellationToken)
                .ConfigureAwait(false);

            ValidateMultiWriteResponse(response, items.Count);
        }
        finally
        {
            _gate.Release();
        }
    }
}
