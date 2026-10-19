using SiameseCat.Protocols.S7;

namespace SiameseCat.Server.S7;

/// <summary>
/// Byte-oriented memory of an S7 slave: the M, I and Q areas plus data blocks created on demand.
/// All members are thread-safe and may be called while a master is connected.
/// </summary>
public sealed class S7DataStore
{
    private readonly object _sync = new();
    private readonly byte[] _merkers;
    private readonly byte[] _inputs;
    private readonly byte[] _outputs;
    private readonly Dictionary<int, byte[]> _dataBlocks = new();
    private readonly int _defaultDataBlockSize;
    private readonly bool _autoCreateDataBlocks;

    /// <summary>Creates a store with the given area sizes.</summary>
    /// <param name="memorySize">Size of the M area in bytes.</param>
    /// <param name="inputSize">Size of the I area in bytes.</param>
    /// <param name="outputSize">Size of the Q area in bytes.</param>
    /// <param name="defaultDataBlockSize">Size used when a data block is created on demand.</param>
    /// <param name="autoCreateDataBlocks">Whether data blocks referenced by a master are created on first use.</param>
    public S7DataStore(
        int memorySize = 512,
        int inputSize = 512,
        int outputSize = 512,
        int defaultDataBlockSize = 1024,
        bool autoCreateDataBlocks = true)
    {
        _merkers = new byte[ServerBufferSizes.Validate(memorySize, nameof(memorySize))];
        _inputs = new byte[ServerBufferSizes.Validate(inputSize, nameof(inputSize))];
        _outputs = new byte[ServerBufferSizes.Validate(outputSize, nameof(outputSize))];
        _defaultDataBlockSize = ServerBufferSizes.Validate(defaultDataBlockSize, nameof(defaultDataBlockSize));
        _autoCreateDataBlocks = autoCreateDataBlocks;
    }

    /// <summary>Raised after bytes were written, including through an S7 write request.</summary>
    public event EventHandler<S7WriteEventArgs>? Written;

    /// <summary>Gets the size of the M area in bytes.</summary>
    public int MemorySize => _merkers.Length;

    /// <summary>Gets the size of the I area in bytes.</summary>
    public int InputSize => _inputs.Length;

    /// <summary>Gets the size of the Q area in bytes.</summary>
    public int OutputSize => _outputs.Length;

    /// <summary>Gets the data block numbers that currently exist.</summary>
    public IReadOnlyCollection<int> DataBlockNumbers
    {
        get
        {
            lock (_sync)
            {
                return _dataBlocks.Keys.OrderBy(number => number).ToArray();
            }
        }
    }

    /// <summary>Creates a data block when it does not exist yet.</summary>
    /// <param name="number">Data block number, 1..65535.</param>
    /// <param name="size">Size in bytes.</param>
    /// <returns><see langword="true"/> when the block exists afterwards.</returns>
    public bool TryEnsureDataBlock(int number, int size)
    {
        if (number is < 1 or > 65535 || size is < 1 or > 65536)
        {
            return false;
        }

        lock (_sync)
        {
            if (_dataBlocks.ContainsKey(number))
            {
                return true;
            }

            _dataBlocks[number] = new byte[size];
            return true;
        }
    }

    /// <summary>Gets whether a data block exists.</summary>
    /// <param name="number">Data block number.</param>
    public bool ContainsDataBlock(int number)
    {
        lock (_sync)
        {
            return _dataBlocks.ContainsKey(number);
        }
    }

    /// <summary>Reads bytes from an area.</summary>
    /// <param name="area">Memory area.</param>
    /// <param name="dbNumber">Data block number (ignored for M/I/Q areas).</param>
    /// <param name="start">First byte offset.</param>
    /// <param name="count">Number of bytes.</param>
    /// <returns>The bytes, or <see langword="null"/> when the area or range does not exist.</returns>
    public byte[]? ReadBytes(S7Area area, int dbNumber, int start, int count)
    {
        lock (_sync)
        {
            var buffer = Resolve(area, dbNumber);
            if (buffer is null || !Fits(start, count, buffer.Length))
            {
                return null;
            }

            var values = new byte[count];
            Array.Copy(buffer, start, values, 0, count);
            return values;
        }
    }

    /// <summary>Reads bytes into a caller-provided buffer.</summary>
    /// <param name="area">Memory area.</param>
    /// <param name="dbNumber">Data block number (ignored for M/I/Q areas).</param>
    /// <param name="start">First byte offset.</param>
    /// <param name="destination">Destination buffer; its length is the byte count.</param>
    /// <returns><see langword="false"/> when the area or range does not exist.</returns>
    internal bool TryCopyBytes(S7Area area, int dbNumber, int start, Span<byte> destination)
    {
        lock (_sync)
        {
            var buffer = Resolve(area, dbNumber);
            if (buffer is null || !Fits(start, destination.Length, buffer.Length))
            {
                return false;
            }

            buffer.AsSpan(start, destination.Length).CopyTo(destination);
            return true;
        }
    }

    /// <summary>Writes bytes into an area.</summary>
    /// <param name="area">Memory area.</param>
    /// <param name="dbNumber">Data block number (ignored for M/I/Q areas).</param>
    /// <param name="start">First byte offset.</param>
    /// <param name="values">Bytes to write.</param>
    /// <returns><see langword="false"/> when the area or range does not exist.</returns>
    public bool TryWriteBytes(S7Area area, int dbNumber, int start, ReadOnlySpan<byte> values)
    {
        S7WriteEventArgs? args = null;
        var handler = Written;
        lock (_sync)
        {
            var buffer = Resolve(area, dbNumber);
            if (buffer is null || !Fits(start, values.Length, buffer.Length))
            {
                return false;
            }

            if (values.Length > 0)
            {
                if (handler is null)
                {
                    values.CopyTo(buffer.AsSpan(start, values.Length));
                }
                else
                {
                    var written = values.ToArray();
                    var previous = new byte[written.Length];
                    Array.Copy(buffer, start, previous, 0, written.Length);
                    written.CopyTo(buffer, start);
                    args = new S7WriteEventArgs(area, dbNumber, start, written, previous);
                }
            }
        }

        if (args is not null)
        {
            handler!.Invoke(this, args);
        }

        return true;
    }

    /// <summary>Reads a single bit.</summary>
    /// <param name="area">Memory area.</param>
    /// <param name="dbNumber">Data block number (ignored for M/I/Q areas).</param>
    /// <param name="bitAddress">Bit address (byte offset × 8 + bit).</param>
    /// <param name="value">The bit value.</param>
    /// <returns><see langword="false"/> when the bit address is outside the area.</returns>
    public bool TryReadBit(S7Area area, int dbNumber, int bitAddress, out bool value)
    {
        value = false;
        lock (_sync)
        {
            var buffer = Resolve(area, dbNumber);
            if (buffer is null || bitAddress < 0 || bitAddress >= buffer.Length * 8)
            {
                return false;
            }

            value = (buffer[bitAddress / 8] & (1 << (bitAddress % 8))) != 0;
            return true;
        }
    }

    /// <summary>Writes a single bit.</summary>
    /// <param name="area">Memory area.</param>
    /// <param name="dbNumber">Data block number (ignored for M/I/Q areas).</param>
    /// <param name="bitAddress">Bit address (byte offset × 8 + bit).</param>
    /// <param name="value">Value to write.</param>
    /// <returns><see langword="false"/> when the bit address is outside the area.</returns>
    public bool TryWriteBit(S7Area area, int dbNumber, int bitAddress, bool value)
    {
        S7WriteEventArgs? args = null;
        var handler = Written;
        lock (_sync)
        {
            var buffer = Resolve(area, dbNumber);
            if (buffer is null || bitAddress < 0 || bitAddress >= buffer.Length * 8)
            {
                return false;
            }

            var index = bitAddress / 8;
            var mask = (byte)(1 << (bitAddress % 8));
            var previous = buffer[index];
            buffer[index] = value ? (byte)(previous | mask) : (byte)(previous & ~mask);
            if (handler is not null)
            {
                args = new S7WriteEventArgs(
                    area,
                    dbNumber,
                    index,
                    new[] { buffer[index] },
                    new[] { previous });
            }
        }

        if (args is not null)
        {
            handler!.Invoke(this, args);
        }

        return true;
    }

    /// <summary>Clears every area and every data block.</summary>
    public void Reset()
    {
        lock (_sync)
        {
            Array.Clear(_merkers, 0, _merkers.Length);
            Array.Clear(_inputs, 0, _inputs.Length);
            Array.Clear(_outputs, 0, _outputs.Length);

            foreach (var block in _dataBlocks.Values)
            {
                Array.Clear(block, 0, block.Length);
            }
        }
    }

    private byte[]? Resolve(S7Area area, int dbNumber)
    {
        switch (area)
        {
            case S7Area.Inputs:
                return _inputs;

            case S7Area.Outputs:
                return _outputs;

            case S7Area.Merkers:
                return _merkers;

            case S7Area.DataBlock:
                if (_dataBlocks.TryGetValue(dbNumber, out var block))
                {
                    return block;
                }

                if (!_autoCreateDataBlocks || dbNumber is < 1 or > 65535)
                {
                    return null;
                }

                block = new byte[_defaultDataBlockSize];
                _dataBlocks[dbNumber] = block;
                return block;

            default:
                return null;
        }
    }

    private static bool Fits(int start, int count, int length) =>
        start >= 0 && count >= 0 && (long)start + count <= length;
}
