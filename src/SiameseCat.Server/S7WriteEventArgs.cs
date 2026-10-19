using SiameseCat.Protocols.S7;

namespace SiameseCat.Server;

/// <summary>Describes a write into the S7 data store.</summary>
public sealed class S7WriteEventArgs : EventArgs
{
    /// <summary>Creates the event arguments.</summary>
    /// <param name="area">Memory area that was written.</param>
    /// <param name="dbNumber">Data block number, or 0 for M/I/Q areas.</param>
    /// <param name="start">First byte offset.</param>
    /// <param name="values">Bytes that were written.</param>
    /// <param name="previousValues">Bytes that were stored before the write.</param>
    public S7WriteEventArgs(S7Area area, int dbNumber, int start, byte[] values, byte[] previousValues)
    {
        Area = area;
        DbNumber = dbNumber;
        Start = start;
        Values = values ?? throw new ArgumentNullException(nameof(values));
        PreviousValues = previousValues ?? throw new ArgumentNullException(nameof(previousValues));
    }

    /// <summary>Gets the memory area that was written.</summary>
    public S7Area Area { get; }

    /// <summary>Gets the data block number, or 0 for the M/I/Q areas.</summary>
    public int DbNumber { get; }

    /// <summary>Gets the first byte offset.</summary>
    public int Start { get; }

    /// <summary>Gets the number of bytes that were written.</summary>
    public int Count => Values.Count;

    /// <summary>Gets the bytes that were written.</summary>
    public IReadOnlyList<byte> Values { get; }

    /// <summary>Gets the bytes that were stored before the write.</summary>
    public IReadOnlyList<byte> PreviousValues { get; }
}
