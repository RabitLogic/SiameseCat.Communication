namespace SiameseCat.Core;

/// <summary>Protocol-neutral value exchanged with a PLC.</summary>
public readonly struct PlcValue : IEquatable<PlcValue>
{
    private readonly bool _bool;
    private readonly long _integer;
    private readonly double _real;
    private readonly object? _reference;
    private readonly PlcValueQuality _quality;
    private readonly DateTimeOffset _timestamp;

    private PlcValue(
        PlcDataType dataType,
        bool boolValue = false,
        long integer = 0,
        double real = 0,
        object? reference = null,
        PlcValueQuality quality = PlcValueQuality.Good,
        DateTimeOffset timestamp = default)
    {
        DataType = dataType;
        _bool = boolValue;
        _integer = integer;
        _real = real;
        _reference = reference;
        _quality = quality;
        _timestamp = timestamp;
    }

    /// <summary>Gets the data type of the stored value.</summary>
    public PlcDataType DataType { get; }

    /// <summary>Gets the quality reported with the value. Defaults to <see cref="PlcValueQuality.Good"/>.</summary>
    public PlcValueQuality Quality => _quality;

    /// <summary>
    /// Gets the moment the value was sampled, or <see langword="null"/> when the driver does not stamp
    /// values. Enable <see cref="DriverConfiguration.StampReadTimestamps"/> to stamp every successful read.
    /// </summary>
    public DateTimeOffset? Timestamp => _timestamp == default ? null : _timestamp;

    /// <summary>Gets whether <see cref="Quality"/> is <see cref="PlcValueQuality.Good"/>.</summary>
    public bool IsGood => _quality == PlcValueQuality.Good;

    /// <summary>Returns a copy with a different quality.</summary>
    /// <param name="quality">Quality to attach.</param>
    public PlcValue WithQuality(PlcValueQuality quality) =>
        new(DataType, _bool, _integer, _real, _reference, quality, _timestamp);

    /// <summary>Returns a copy with a sampling timestamp.</summary>
    /// <param name="timestamp">Sampling moment; usually <see cref="DateTimeOffset.UtcNow"/>.</param>
    public PlcValue WithTimestamp(DateTimeOffset timestamp) =>
        new(DataType, _bool, _integer, _real, _reference, _quality, timestamp);

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

    /// <summary>Creates an array of 16-bit signed integers.</summary>
    /// <param name="values">Element values.</param>
    public static PlcValue FromInt16Array(ReadOnlySpan<short> values) =>
        new(PlcDataType.Int16, reference: values.ToArray());

    /// <summary>Creates an array of 16-bit unsigned integers.</summary>
    /// <param name="values">Element values.</param>
    public static PlcValue FromUInt16Array(ReadOnlySpan<ushort> values) =>
        new(PlcDataType.UInt16, reference: values.ToArray());

    /// <summary>Creates an array of 32-bit signed integers.</summary>
    /// <param name="values">Element values.</param>
    public static PlcValue FromInt32Array(ReadOnlySpan<int> values) =>
        new(PlcDataType.Int32, reference: values.ToArray());

    /// <summary>Creates an array of 32-bit unsigned integers.</summary>
    /// <param name="values">Element values.</param>
    public static PlcValue FromUInt32Array(ReadOnlySpan<uint> values) =>
        new(PlcDataType.UInt32, reference: values.ToArray());

    /// <summary>Creates an array of 64-bit signed integers.</summary>
    /// <param name="values">Element values.</param>
    public static PlcValue FromInt64Array(ReadOnlySpan<long> values) =>
        new(PlcDataType.Int64, reference: values.ToArray());

    /// <summary>Creates an array of 64-bit unsigned integers.</summary>
    /// <param name="values">Element values.</param>
    public static PlcValue FromUInt64Array(ReadOnlySpan<ulong> values) =>
        new(PlcDataType.UInt64, reference: values.ToArray());

    /// <summary>Creates an array of single-precision numbers.</summary>
    /// <param name="values">Element values.</param>
    public static PlcValue FromFloatArray(ReadOnlySpan<float> values) =>
        new(PlcDataType.Float, reference: values.ToArray());

    /// <summary>Creates an array of double-precision numbers.</summary>
    /// <param name="values">Element values.</param>
    public static PlcValue FromDoubleArray(ReadOnlySpan<double> values) =>
        new(PlcDataType.Double, reference: values.ToArray());

    /// <summary>Gets whether the value holds an array of elements.</summary>
    public bool IsArray => DataType is not (PlcDataType.Null or PlcDataType.String or PlcDataType.Raw)
        && _reference is Array;

    /// <summary>Gets the element count: array length, string length, byte count, or 1 for scalars.</summary>
    public int Length => DataType switch
    {
        PlcDataType.Null => 0,
        PlcDataType.String => ((string)_reference!).Length,
        PlcDataType.Raw => ((byte[])_reference!).Length,
        _ => _reference is Array array ? array.Length : 1,
    };

    /// <summary>Gets an array value as 16-bit signed integers.</summary>
    public short[] AsInt16Array() => CloneArray<short>(PlcDataType.Int16);

    /// <summary>Gets an array value as 16-bit unsigned integers.</summary>
    public ushort[] AsUInt16Array() => CloneArray<ushort>(PlcDataType.UInt16);

    /// <summary>Gets an array value as 32-bit signed integers.</summary>
    public int[] AsInt32Array() => CloneArray<int>(PlcDataType.Int32);

    /// <summary>Gets an array value as 32-bit unsigned integers.</summary>
    public uint[] AsUInt32Array() => CloneArray<uint>(PlcDataType.UInt32);

    /// <summary>Gets an array value as 64-bit signed integers.</summary>
    public long[] AsInt64Array() => CloneArray<long>(PlcDataType.Int64);

    /// <summary>Gets an array value as 64-bit unsigned integers.</summary>
    public ulong[] AsUInt64Array() => CloneArray<ulong>(PlcDataType.UInt64);

    /// <summary>Gets an array value as single-precision numbers.</summary>
    public float[] AsFloatArray() => CloneArray<float>(PlcDataType.Float);

    /// <summary>Gets an array value as double-precision numbers.</summary>
    public double[] AsDoubleArray() => CloneArray<double>(PlcDataType.Double);

    private T[] CloneArray<T>(PlcDataType expected)
    {
        if (DataType != expected || _reference is not T[] values)
        {
            throw new InvalidCastException($"Cannot read a {DataType} value as {expected}[{Length}].");
        }

        return (T[])values.Clone();
    }

    private void ThrowIfArray()
    {
        if (IsArray)
        {
            throw new InvalidCastException(
                $"The value is an array of {Length} {DataType} elements; use As{DataType}Array().");
        }
    }

    /// <summary>Gets the value as a boolean. Numeric values are converted from non-zero.</summary>
    public bool AsBool()
    {
        ThrowIfArray();

        return DataType switch
        {
            PlcDataType.Bool => _bool,
            PlcDataType.String or PlcDataType.Raw or PlcDataType.Null
                => throw new InvalidCastException($"Cannot read a {DataType} value as Bool."),
            _ => _integer != 0,
        };
    }

    /// <summary>Gets the value as an 8-bit unsigned integer.</summary>
    public byte AsByte()
    {
        ThrowIfArray();

        return DataType switch
        {
            PlcDataType.Raw when _reference is byte[] { Length: 1 } bytes => bytes[0],
            PlcDataType.String or PlcDataType.Raw or PlcDataType.Null
                => throw new InvalidCastException($"Cannot read a {DataType} value as Byte."),
            _ => checked((byte)AsInt64()),
        };
    }

    /// <summary>Gets the value as a 16-bit signed integer.</summary>
    public short AsInt16() => checked((short)AsInt64());

    /// <summary>Gets the value as a 16-bit unsigned integer.</summary>
    public ushort AsUInt16() => checked((ushort)AsInt64());

    /// <summary>Gets the value as a 32-bit signed integer.</summary>
    public int AsInt32() => checked((int)AsInt64());

    /// <summary>Gets the value as a 32-bit unsigned integer.</summary>
    public uint AsUInt32() => checked((uint)AsInt64());

    /// <summary>Gets the value as a 64-bit signed integer.</summary>
    public long AsInt64()
    {
        ThrowIfArray();

        return DataType switch
        {
            PlcDataType.Bool => _bool ? 1 : 0,
            PlcDataType.Float or PlcDataType.Double => checked((long)_real),
            PlcDataType.String or PlcDataType.Raw or PlcDataType.Null
                => throw new InvalidCastException($"Cannot read a {DataType} value as Int64."),
            _ => _integer,
        };
    }

    /// <summary>Gets the value as a 64-bit unsigned integer.</summary>
    public ulong AsUInt64() => unchecked((ulong)AsInt64());

    /// <summary>Gets the value as a single-precision number.</summary>
    public float AsFloat() => (float)AsDouble();

    /// <summary>Gets the value as a double-precision number.</summary>
    public double AsDouble()
    {
        ThrowIfArray();

        return DataType switch
        {
            PlcDataType.Bool => _bool ? 1 : 0,
            PlcDataType.Float or PlcDataType.Double => _real,
            PlcDataType.String or PlcDataType.Raw or PlcDataType.Null
                => throw new InvalidCastException($"Cannot read a {DataType} value as Double."),
            _ => _integer,
        };
    }

    /// <summary>Gets the text value.</summary>
    public string AsString() => DataType switch
    {
        PlcDataType.String => (string)_reference!,
        _ => throw new InvalidCastException($"Cannot read a {DataType} value as String."),
    };

    /// <summary>Gets the raw bytes.</summary>
    public byte[] AsBytes()
    {
        ThrowIfArray();

        return DataType switch
        {
            PlcDataType.Raw => (byte[])((byte[])_reference!).Clone(),
            PlcDataType.Null => Array.Empty<byte>(),
            _ => throw new InvalidCastException($"Cannot read a {DataType} value as Raw bytes."),
        };
    }

    /// <summary>Returns the boxed underlying value. Array values are cloned.</summary>
    public object? ToObject() => DataType switch
    {
        PlcDataType.Null => null,
        PlcDataType.Bool => _bool,
        PlcDataType.Byte => (byte)_integer,
        PlcDataType.Int16 => IsArray ? ((short[])_reference!).Clone() : (short)_integer,
        PlcDataType.UInt16 => IsArray ? ((ushort[])_reference!).Clone() : (ushort)_integer,
        PlcDataType.Int32 => IsArray ? ((int[])_reference!).Clone() : (int)_integer,
        PlcDataType.UInt32 => IsArray ? ((uint[])_reference!).Clone() : (uint)_integer,
        PlcDataType.Int64 => IsArray ? ((long[])_reference!).Clone() : _integer,
        PlcDataType.UInt64 => IsArray ? ((ulong[])_reference!).Clone() : unchecked((ulong)_integer),
        PlcDataType.Float => IsArray ? ((float[])_reference!).Clone() : (float)_real,
        PlcDataType.Double => IsArray ? ((double[])_reference!).Clone() : _real,
        PlcDataType.String => _reference,
        PlcDataType.Raw => ((byte[])_reference!).Clone(),
        _ => null,
    };

    /// <inheritdoc/>
    /// <remarks>
    /// Quality and timestamp are metadata: two values with the same data type and payload compare equal
    /// even when their quality or timestamp differ, so change detection keeps working on polled reads.
    /// Arrays compare element by element.
    /// </remarks>
    public bool Equals(PlcValue other)
    {
        if (DataType != other.DataType)
        {
            return false;
        }

        if (IsArray)
        {
            return EqualsArray(other);
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

    private bool EqualsArray(PlcValue other)
    {
        if (other._reference is not Array rightArray || rightArray.Length != Length)
        {
            return false;
        }

        return DataType switch
        {
            PlcDataType.Int16 => ((short[])_reference!).AsSpan().SequenceEqual((short[])rightArray),
            PlcDataType.UInt16 => ((ushort[])_reference!).AsSpan().SequenceEqual((ushort[])rightArray),
            PlcDataType.Int32 => ((int[])_reference!).AsSpan().SequenceEqual((int[])rightArray),
            PlcDataType.UInt32 => ((uint[])_reference!).AsSpan().SequenceEqual((uint[])rightArray),
            PlcDataType.Int64 => ((long[])_reference!).AsSpan().SequenceEqual((long[])rightArray),
            PlcDataType.UInt64 => ((ulong[])_reference!).AsSpan().SequenceEqual((ulong[])rightArray),
            PlcDataType.Float => ((float[])_reference!).AsSpan().SequenceEqual((float[])rightArray),
            PlcDataType.Double => ((double[])_reference!).AsSpan().SequenceEqual((double[])rightArray),
            _ => false,
        };
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PlcValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (IsArray)
        {
            unchecked
            {
                return ((int)DataType * 397) ^ Length;
            }
        }

        return DataType switch
        {
            PlcDataType.Null => 0,
            PlcDataType.Bool => _bool.GetHashCode(),
            PlcDataType.Float or PlcDataType.Double => _real.GetHashCode(),
            PlcDataType.String => StringComparer.Ordinal.GetHashCode((string)_reference!),
            PlcDataType.Raw => ((byte[])_reference!).Length,
            _ => _integer.GetHashCode(),
        };
    }

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator ==(PlcValue left, PlcValue right) => left.Equals(right);

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator !=(PlcValue left, PlcValue right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => IsArray
        ? $"{DataType}[{Length}]"
        : DataType switch
        {
            PlcDataType.Null => "Null",
            PlcDataType.String => $"String:\"{_reference}\"",
            PlcDataType.Raw => $"Raw:{((byte[])_reference!).Length}B",
            _ => $"{DataType}:{ToObject()}",
        };
}
