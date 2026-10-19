namespace SiameseCat.Core;

/// <summary>Protocol-neutral value exchanged with a PLC.</summary>
public readonly struct PlcValue : IEquatable<PlcValue>
{
    private readonly bool _bool;
    private readonly long _integer;
    private readonly double _real;
    private readonly object? _reference;

    private PlcValue(PlcDataType dataType, bool boolValue = false, long integer = 0, double real = 0, object? reference = null)
    {
        DataType = dataType;
        _bool = boolValue;
        _integer = integer;
        _real = real;
        _reference = reference;
    }

    /// <summary>Gets the data type of the stored value.</summary>
    public PlcDataType DataType { get; }

    /// <summary>Creates a <see cref="PlcDataType.Null"/> value.</summary>
    public static PlcValue Null => default;

    /// <summary>Creates a boolean value.</summary>
    /// <param name="value">Value.</param>
    public static PlcValue FromBool(bool value) => new(PlcDataType.Bool, boolValue: value);

    /// <summary>Creates an 8-bit unsigned value.</summary>
    /// <param name="value">Value.</param>
    public static PlcValue FromByte(byte value) => new(PlcDataType.Byte, integer: value);

    /// <summary>Creates a 16-bit signed value.</summary>
    /// <param name="value">Value.</param>
    public static PlcValue FromInt16(short value) => new(PlcDataType.Int16, integer: value);

    /// <summary>Creates a 16-bit unsigned value.</summary>
    /// <param name="value">Value.</param>
    public static PlcValue FromUInt16(ushort value) => new(PlcDataType.UInt16, integer: value);

    /// <summary>Creates a 32-bit signed value.</summary>
    /// <param name="value">Value.</param>
    public static PlcValue FromInt32(int value) => new(PlcDataType.Int32, integer: value);

    /// <summary>Creates a 32-bit unsigned value.</summary>
    /// <param name="value">Value.</param>
    public static PlcValue FromUInt32(uint value) => new(PlcDataType.UInt32, integer: value);

    /// <summary>Creates a 64-bit signed value.</summary>
    /// <param name="value">Value.</param>
    public static PlcValue FromInt64(long value) => new(PlcDataType.Int64, integer: value);

    /// <summary>Creates a 64-bit unsigned value.</summary>
    /// <param name="value">Value.</param>
    public static PlcValue FromUInt64(ulong value) => new(PlcDataType.UInt64, integer: unchecked((long)value));

    /// <summary>Creates a single-precision value.</summary>
    /// <param name="value">Value.</param>
    public static PlcValue FromFloat(float value) => new(PlcDataType.Float, real: value);

    /// <summary>Creates a double-precision value.</summary>
    /// <param name="value">Value.</param>
    public static PlcValue FromDouble(double value) => new(PlcDataType.Double, real: value);

    /// <summary>Creates a text value.</summary>
    /// <param name="value">Value.</param>
    public static PlcValue FromString(string value) =>
        new(PlcDataType.String, reference: value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a raw byte value.</summary>
    /// <param name="value">Value.</param>
    public static PlcValue FromBytes(ReadOnlySpan<byte> value) => new(PlcDataType.Raw, reference: value.ToArray());

    /// <summary>Gets the value as a boolean. Numeric values are converted from non-zero.</summary>
    public bool AsBool() => DataType switch
    {
        PlcDataType.Bool => _bool,
        PlcDataType.String or PlcDataType.Raw or PlcDataType.Null
            => throw new InvalidCastException($"Cannot read a {DataType} value as Bool."),
        _ => _integer != 0,
    };

    /// <summary>Gets the value as an 8-bit unsigned integer.</summary>
    public byte AsByte() => DataType switch
    {
        PlcDataType.Raw when _reference is byte[] { Length: 1 } bytes => bytes[0],
        PlcDataType.String or PlcDataType.Raw or PlcDataType.Null
            => throw new InvalidCastException($"Cannot read a {DataType} value as Byte."),
        _ => checked((byte)AsInt64()),
    };

    /// <summary>Gets the value as a 16-bit signed integer.</summary>
    public short AsInt16() => checked((short)AsInt64());

    /// <summary>Gets the value as a 16-bit unsigned integer.</summary>
    public ushort AsUInt16() => checked((ushort)AsInt64());

    /// <summary>Gets the value as a 32-bit signed integer.</summary>
    public int AsInt32() => checked((int)AsInt64());

    /// <summary>Gets the value as a 32-bit unsigned integer.</summary>
    public uint AsUInt32() => checked((uint)AsInt64());

    /// <summary>Gets the value as a 64-bit signed integer.</summary>
    public long AsInt64() => DataType switch
    {
        PlcDataType.Bool => _bool ? 1 : 0,
        PlcDataType.Float or PlcDataType.Double => checked((long)_real),
        PlcDataType.String or PlcDataType.Raw or PlcDataType.Null
            => throw new InvalidCastException($"Cannot read a {DataType} value as Int64."),
        _ => _integer,
    };

    /// <summary>Gets the value as a 64-bit unsigned integer.</summary>
    public ulong AsUInt64() => unchecked((ulong)AsInt64());

    /// <summary>Gets the value as a single-precision number.</summary>
    public float AsFloat() => (float)AsDouble();

    /// <summary>Gets the value as a double-precision number.</summary>
    public double AsDouble() => DataType switch
    {
        PlcDataType.Bool => _bool ? 1 : 0,
        PlcDataType.Float or PlcDataType.Double => _real,
        PlcDataType.String or PlcDataType.Raw or PlcDataType.Null
            => throw new InvalidCastException($"Cannot read a {DataType} value as Double."),
        _ => _integer,
    };

    /// <summary>Gets the text value.</summary>
    public string AsString() => DataType switch
    {
        PlcDataType.String => (string)_reference!,
        _ => throw new InvalidCastException($"Cannot read a {DataType} value as String."),
    };

    /// <summary>Gets the raw bytes.</summary>
    public byte[] AsBytes() => DataType switch
    {
        PlcDataType.Raw => (byte[])((byte[])_reference!).Clone(),
        PlcDataType.Null => Array.Empty<byte>(),
        _ => throw new InvalidCastException($"Cannot read a {DataType} value as Raw bytes."),
    };

    /// <summary>Returns the boxed underlying value.</summary>
    public object? ToObject() => DataType switch
    {
        PlcDataType.Null => null,
        PlcDataType.Bool => _bool,
        PlcDataType.Byte => (byte)_integer,
        PlcDataType.Int16 => (short)_integer,
        PlcDataType.UInt16 => (ushort)_integer,
        PlcDataType.Int32 => (int)_integer,
        PlcDataType.UInt32 => (uint)_integer,
        PlcDataType.Int64 => _integer,
        PlcDataType.UInt64 => unchecked((ulong)_integer),
        PlcDataType.Float => (float)_real,
        PlcDataType.Double => _real,
        PlcDataType.String => _reference,
        PlcDataType.Raw => ((byte[])_reference!).Clone(),
        _ => null,
    };

    /// <inheritdoc/>
    public bool Equals(PlcValue other)
    {
        if (DataType != other.DataType)
        {
            return false;
        }

        return DataType switch
        {
            PlcDataType.Null => true,
            PlcDataType.Bool => _bool == other._bool,
            PlcDataType.Float or PlcDataType.Double => _real.Equals(other._real),
            PlcDataType.String => string.Equals((string?)_reference, (string?)other._reference, StringComparison.Ordinal),
            PlcDataType.Raw => ((byte[])_reference!).AsSpan().SequenceEqual((byte[])other._reference!),
            _ => _integer == other._integer,
        };
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PlcValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => DataType switch
    {
        PlcDataType.Null => 0,
        PlcDataType.Bool => _bool.GetHashCode(),
        PlcDataType.Float or PlcDataType.Double => _real.GetHashCode(),
        PlcDataType.String => StringComparer.Ordinal.GetHashCode((string)_reference!),
        PlcDataType.Raw => ((byte[])_reference!).Length,
        _ => _integer.GetHashCode(),
    };

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator ==(PlcValue left, PlcValue right) => left.Equals(right);

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator !=(PlcValue left, PlcValue right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => DataType switch
    {
        PlcDataType.Null => "Null",
        PlcDataType.String => $"String:\"{_reference}\"",
        PlcDataType.Raw => $"Raw:{((byte[])_reference!).Length}B",
        _ => $"{DataType}:{ToObject()}",
    };
}
