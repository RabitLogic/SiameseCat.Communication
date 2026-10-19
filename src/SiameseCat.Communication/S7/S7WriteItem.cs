namespace SiameseCat.Communication.S7;

/// <summary>Describes one item in a multi-variable S7 write request.</summary>
public sealed class S7WriteItem
{
    /// <summary>Creates a write item.</summary>
    /// <param name="address">Target address.</param>
    /// <param name="data">
    /// Payload to write. Bool items require exactly one byte containing 0 or 1; other items require
    /// at least one byte.
    /// </param>
    public S7WriteItem(S7Address address, ReadOnlyMemory<byte> data)
    {
        if (address.DataType == S7DataType.Bool)
        {
            if (data.Length != 1)
            {
                throw new ArgumentException("Bool items must have a payload of exactly one byte.", nameof(data));
            }

            if (data.Span[0] > 1)
            {
                throw new ArgumentException("Bool items must contain 0 or 1.", nameof(data));
            }
        }
        else if (data.IsEmpty)
        {
            throw new ArgumentException("The payload must contain at least one byte.", nameof(data));
        }

        Address = address;
        Data = data.ToArray();
    }

    /// <summary>Gets the target address.</summary>
    public S7Address Address { get; }

    /// <summary>Gets the payload bytes. Bool items contain a single 0 or 1 byte.</summary>
    public ReadOnlyMemory<byte> Data { get; }
}
