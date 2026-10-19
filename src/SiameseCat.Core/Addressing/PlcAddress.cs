namespace SiameseCat.Core;

/// <summary>
/// Protocol-neutral address produced by an <see cref="IPlcAddressParser"/>.
/// </summary>
/// <remarks>
/// <para><see cref="Region"/> is a protocol-defined key such as <c>holding</c> (Modbus) or <c>DB1</c> (S7).</para>
/// <para><see cref="Offset"/> is expressed in the smallest addressable unit of the region (registers for Modbus, bytes for S7).</para>
/// </remarks>
public readonly record struct PlcAddress
{
    /// <summary>Creates an address.</summary>
    /// <param name="region">Protocol-defined region key.</param>
    /// <param name="offset">Offset inside the region.</param>
    /// <param name="dataType">Access width.</param>
    /// <param name="length">Element count, character count for strings or byte count for raw data.</param>
    /// <param name="bitOffset">Bit offset inside the byte, only valid for <see cref="PlcDataType.Bool"/>.</param>
    /// <param name="symbol">
    /// Optional protocol symbol, for example an OPC UA node id, a CIP tag name or an ADS symbol.
    /// Symbolic protocols keep their namespace in <paramref name="region"/> and may leave
    /// <paramref name="offset"/> at 0.
    /// </param>
    public PlcAddress(
        string region,
        int offset,
        PlcDataType dataType,
        int length = 1,
        byte? bitOffset = null,
        string? symbol = null)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            throw new ArgumentException("Region must be specified.", nameof(region));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset cannot be negative.");
        }

        if (dataType == PlcDataType.Null)
        {
            throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "A concrete data type is required.");
        }

        if (length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be at least 1.");
        }

        if (dataType.IsSingleElement() && length != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                length,
                $"{dataType} describes a single element; use String or Raw for arrays.");
        }

        if (bitOffset is not null)
        {
            if (dataType != PlcDataType.Bool)
            {
                throw new ArgumentException("A bit offset is only valid for Bool addresses.", nameof(bitOffset));
            }

            if (bitOffset > 7)
            {
                throw new ArgumentOutOfRangeException(nameof(bitOffset), bitOffset, "Bit offset must be between 0 and 7.");
            }
        }

        Region = region;
        Offset = offset;
        DataType = dataType;
        Length = length;
        BitOffset = bitOffset;
        Symbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol;
    }

    /// <summary>Creates a symbolic address for protocols that address by name instead of by offset.</summary>
    /// <param name="region">Protocol-defined namespace key, for example <c>ns=2</c>.</param>
    /// <param name="symbol">Symbol or node id, for example <c>s=Temperature</c>.</param>
    /// <param name="dataType">Access width.</param>
    /// <param name="length">Element count for strings and raw data.</param>
    public static PlcAddress FromSymbol(
        string region,
        string symbol,
        PlcDataType dataType,
        int length = 1)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol must be specified.", nameof(symbol));
        }

        return new PlcAddress(region, 0, dataType, length, bitOffset: null, symbol);
    }

    /// <summary>Gets the protocol-defined region key.</summary>
    public string Region { get; }

    /// <summary>Gets the offset inside the region.</summary>
    public int Offset { get; }

    /// <summary>Gets the access width.</summary>
    public PlcDataType DataType { get; }

    /// <summary>Gets the element count, character count or byte count.</summary>
    public int Length { get; }

    /// <summary>Gets the bit offset inside the byte for Bool addresses.</summary>
    public byte? BitOffset { get; }

    /// <summary>Gets the optional protocol symbol when the protocol addresses by name instead of by offset.</summary>
    public string? Symbol { get; }

    /// <summary>Gets the payload size in bytes.</summary>
    public int ByteSize => DataType switch
    {
        PlcDataType.String or PlcDataType.Raw => Length,
        _ => DataType.GetByteSize() * Length,
    };

    /// <summary>Returns a copy with a different data type.</summary>
    /// <param name="dataType">New data type.</param>
    public PlcAddress WithDataType(PlcDataType dataType) => new(Region, Offset, dataType, Length, BitOffset, Symbol);

    /// <summary>Returns a copy with a different length.</summary>
    /// <param name="length">New length.</param>
    public PlcAddress WithLength(int length) => new(Region, Offset, DataType, length, BitOffset, Symbol);

    /// <summary>Returns a copy with a symbol. Protocols that address by name can keep it next to the offset.</summary>
    /// <param name="symbol">Symbol or node id.</param>
    public PlcAddress WithSymbol(string symbol) => new(Region, Offset, DataType, Length, BitOffset, symbol);

    /// <summary>Returns the canonical text form, for example <c>holding:10[UInt16]</c> or <c>ns=2:s=Temp[Double]</c>.</summary>
    public override string ToString() => (Symbol, BitOffset) switch
    {
        (not null, _) => $"{Region}:{Symbol}[{DataType}]",
        (null, null) => $"{Region}:{Offset}[{DataType}]",
        _ => $"{Region}:{Offset}.{BitOffset}[{DataType}]",
    };
}
