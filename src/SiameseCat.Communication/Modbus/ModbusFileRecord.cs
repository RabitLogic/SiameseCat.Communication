namespace SiameseCat.Communication.Modbus;

/// <summary>One sub-request of a Read File Record request (function code 0x14).</summary>
public readonly struct ModbusFileRecordRequest : IEquatable<ModbusFileRecordRequest>
{
    /// <summary>Creates a sub-request that reads <paramref name="recordLength"/> registers from one record.</summary>
    /// <param name="fileNumber">File number, 1 to 65535.</param>
    /// <param name="recordNumber">Record number inside the file, 0 to 9999.</param>
    /// <param name="recordLength">Number of registers to read, 1 to 125.</param>
    public ModbusFileRecordRequest(ushort fileNumber, ushort recordNumber, ushort recordLength)
    {
        FileNumber = fileNumber;
        RecordNumber = recordNumber;
        RecordLength = recordLength;
    }

    /// <summary>Gets the file number.</summary>
    public ushort FileNumber { get; }

    /// <summary>Gets the record number inside the file.</summary>
    public ushort RecordNumber { get; }

    /// <summary>Gets how many registers the request reads.</summary>
    public ushort RecordLength { get; }

    /// <inheritdoc/>
    public bool Equals(ModbusFileRecordRequest other) =>
        FileNumber == other.FileNumber
        && RecordNumber == other.RecordNumber
        && RecordLength == other.RecordLength;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ModbusFileRecordRequest other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (FileNumber * 397) ^ (RecordNumber * 31) ^ RecordLength;

    /// <summary>Compares two sub-requests.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when both sub-requests are identical.</returns>
    public static bool operator ==(ModbusFileRecordRequest left, ModbusFileRecordRequest right) => left.Equals(right);

    /// <summary>Compares two sub-requests.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when the sub-requests differ.</returns>
    public static bool operator !=(ModbusFileRecordRequest left, ModbusFileRecordRequest right) => !left.Equals(right);
}

/// <summary>One record written by a Write File Record request (function code 0x15).</summary>
public sealed class ModbusFileRecord
{
    /// <summary>Creates a record to write.</summary>
    /// <param name="fileNumber">File number, 1 to 65535.</param>
    /// <param name="recordNumber">Record number inside the file, 0 to 9999.</param>
    /// <param name="data">Registers to write, 1 to 125 values.</param>
    public ModbusFileRecord(ushort fileNumber, ushort recordNumber, ushort[] data)
    {
        FileNumber = fileNumber;
        RecordNumber = recordNumber;
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>Gets the file number.</summary>
    public ushort FileNumber { get; }

    /// <summary>Gets the record number inside the file.</summary>
    public ushort RecordNumber { get; }

    /// <summary>Gets the registers to write.</summary>
    public ushort[] Data { get; }
}
