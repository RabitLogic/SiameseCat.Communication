namespace SiameseCat.Communication.Server.Modbus;

public sealed partial class ModbusDataStore
{

    private readonly Dictionary<ushort, FileRecordArea> _files = new();
    private readonly object _fileSync = new();

    private sealed class FileRecordArea
    {
        public FileRecordArea(ushort[] data, int recordLength)
        {
            Data = data;
            RecordLength = recordLength;
        }

        public ushort[] Data { get; }

        public int RecordLength { get; }
    }

    /// <summary>
    /// Defines a file served by Read File Record (FC14) and Write File Record (FC15). Every record in a
    /// file has the same length, as required by the Modbus specification.
    /// </summary>
    /// <param name="fileNumber">File number, 1 to 65535.</param>
    /// <param name="recordLength">Registers per record, 1 to 125.</param>
    /// <param name="recordCount">Number of records in the file.</param>
    public void DefineFile(ushort fileNumber, int recordLength, int recordCount)
    {
        if (fileNumber == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileNumber), fileNumber, "File numbers start at 1.");
        }

        if (recordLength is < 1 or > 125)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recordLength), recordLength, "A record holds between 1 and 125 registers.");
        }

        if (recordCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(recordCount), recordCount, "A file needs at least one record.");
        }

        var total = (long)recordLength * recordCount;
        if (total > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recordCount), recordCount, "The file is too large to keep in memory.");
        }

        lock (_fileSync)
        {
            _files[fileNumber] = new FileRecordArea(new ushort[total], recordLength);
        }
    }

    /// <summary>Gets the file numbers that were defined with <see cref="DefineFile"/>.</summary>
    /// <returns>A copy of the file number list.</returns>
    public ushort[] GetFileNumbers()
    {
        lock (_fileSync)
        {
            var numbers = new ushort[_files.Count];
            _files.Keys.CopyTo(numbers, 0);
            return numbers;
        }
    }

    /// <summary>Reads one record of a defined file.</summary>
    /// <param name="fileNumber">File number.</param>
    /// <param name="recordNumber">Record number inside the file.</param>
    /// <param name="destination">Receives the register values.</param>
    /// <returns><see langword="false"/> when the file, the record or the length is out of range.</returns>
    public bool TryReadFileRecord(ushort fileNumber, ushort recordNumber, Span<ushort> destination)
    {
        lock (_fileSync)
        {
            if (!_files.TryGetValue(fileNumber, out var area)
                || destination.Length > area.RecordLength)
            {
                return false;
            }

            var offset = (long)recordNumber * area.RecordLength;
            if (offset + destination.Length > area.Data.Length)
            {
                return false;
            }

            area.Data.AsSpan((int)offset, destination.Length).CopyTo(destination);
            return true;
        }
    }

    /// <summary>Reads one record of a defined file.</summary>
    /// <param name="fileNumber">File number.</param>
    /// <param name="recordNumber">Record number inside the file.</param>
    /// <param name="recordLength">Number of registers to read.</param>
    /// <returns>The register values, or <see langword="null"/> when the file, the record or the length is out of range.</returns>
    public ushort[]? ReadFileRecord(ushort fileNumber, ushort recordNumber, int recordLength)
    {
        if (recordLength is < 1 or > 125)
        {
            return null;
        }

        var values = new ushort[recordLength];
        return TryReadFileRecord(fileNumber, recordNumber, values) ? values : null;
    }

    /// <summary>Writes one record of a defined file.</summary>
    /// <param name="fileNumber">File number.</param>
    /// <param name="recordNumber">Record number inside the file.</param>
    /// <param name="values">Registers to write; at most one record length.</param>
    /// <returns><see langword="false"/> when the file, the record or the length is out of range.</returns>
    public bool TryWriteFileRecord(ushort fileNumber, ushort recordNumber, ReadOnlySpan<ushort> values)
    {
        lock (_fileSync)
        {
            if (!CanWriteFileRecordUnlocked(fileNumber, recordNumber, values.Length, out var area, out var offset))
            {
                return false;
            }

            values.CopyTo(area!.Data.AsSpan(offset, values.Length));
            return true;
        }
    }

    internal bool CanWriteFileRecord(ushort fileNumber, ushort recordNumber, int length)
    {
        lock (_fileSync)
        {
            return CanWriteFileRecordUnlocked(fileNumber, recordNumber, length, out _, out _);
        }
    }

    internal bool TryCopyFileRecord(
        ushort fileNumber,
        ushort recordNumber,
        int length,
        Span<byte> destination)
    {
        lock (_fileSync)
        {
            if (!CanWriteFileRecordUnlocked(fileNumber, recordNumber, length, out var area, out var offset))
            {
                return false;
            }

            for (var i = 0; i < length; i++)
            {
                var value = area!.Data[offset + i];
                destination[i * 2] = (byte)(value >> 8);
                destination[(i * 2) + 1] = (byte)value;
            }

            return true;
        }
    }

    private bool CanWriteFileRecordUnlocked(
        ushort fileNumber,
        ushort recordNumber,
        int length,
        out FileRecordArea? area,
        out int offset)
    {
        area = null;
        offset = 0;

        if (!_files.TryGetValue(fileNumber, out var found) || length > found.RecordLength)
        {
            return false;
        }

        var start = (long)recordNumber * found.RecordLength;
        if (start + length > found.Data.Length)
        {
            return false;
        }

        area = found;
        offset = (int)start;
        return true;
    }

}
