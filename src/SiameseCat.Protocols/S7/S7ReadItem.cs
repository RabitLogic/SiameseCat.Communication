namespace SiameseCat.Protocols.S7;

/// <summary>Describes one item in a multi-variable S7 read request.</summary>
public sealed class S7ReadItem
{
    /// <summary>Creates a read item.</summary>
    /// <param name="address">Address to read.</param>
    /// <param name="length">Number of bytes to read, or 1 for a Bool item.</param>
    public S7ReadItem(S7Address address, int length)
    {
        if (length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be greater than zero.");
        }

        if (address.DataType == S7DataType.Bool && length != 1)
        {
            throw new ArgumentException("Bool items must have a length of 1.", nameof(length));
        }

        Address = address;
        Length = length;
    }

    /// <summary>Gets the address to read.</summary>
    public S7Address Address { get; }

    /// <summary>Gets the number of bytes, or 1 for a Bool item.</summary>
    public int Length { get; }
}
