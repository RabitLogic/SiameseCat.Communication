namespace SiameseCat.Communication;

/// <summary>Protocol-neutral value exchanged with a device.</summary>
public readonly struct DeviceValue : IEquatable<DeviceValue>
{
    private readonly bool _bool;
    private readonly long _integer;
    private readonly double _real;
    private readonly object? _reference;
    private readonly DeviceValueQuality _quality;
    private readonly DateTimeOffset _timestamp;

    private DeviceValue(
        DeviceDataType dataType,
        bool boolValue = false,
        long integer = 0,
        double real = 0,
        object? reference = null,
        DeviceValueQuality quality = DeviceValueQuality.Good,
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
    public DeviceDataType DataType { get; }

    /// <summary>Gets the quality reported with the value. Defaults to <see cref="DeviceValueQuality.Good"/>.</summary>
    public DeviceValueQuality Quality => _quality;

    /// <summary>
    /// Gets the moment the value was sampled, or <see langword="null"/> when the driver does not stamp
    /// values. Enable <see cref="DriverConfiguration.StampReadTimestamps"/> to stamp every successful read.
    /// </summary>
    public DateTimeOffset? Timestamp => _timestamp == default ? null : _timestamp;

    /// <summary>Gets whether <see cref="Quality"/> is <see cref="DeviceValueQuality.Good"/>.</summary>
    public bool IsGood => _quality == DeviceValueQuality.Good;

    /// <summary>Returns a copy with a different quality.</summary>
    /// <param name="quality">Quality to attach.</param>
    public DeviceValue WithQuality(DeviceValueQuality quality) =>
        new(DataType, _bool, _integer, _real, _reference, quality, _timestamp);

    /// <summary>Returns a copy with a sampling timestamp.</summary>
    /// <param name="timestamp">Sampling moment; usually <see cref="DateTimeOffset.UtcNow"/>.</param>
    public DeviceValue WithTimestamp(DateTimeOffset timestamp) =>
        new(DataType, _bool, _integer, _real, _reference, _quality, timestamp);

    /// <summary>Creates a <see cref="DeviceDataType.Null"/> value.</summary>
    public static DeviceValue Null => default;

    /// <summary>Creates a boolean value.</summary>
    /// <param name="value">Value.</param>
    public static DeviceValue FromBool(bool value) => new(DeviceDataType.Bool, boolValue: value);

    /// <summary>Creates an 8-bit unsigned value.</summary>
    /// <param name="value">Value.</param>
    public static DeviceValue FromByte(byte value) => new(DeviceDataType.Byte, integer: value);

    /// <summary>Creates a 16-bit signed value.</summary>
    /// <param name="value">Value.</param>
    public static DeviceValue FromInt16(short value) => new(DeviceDataType.Int16, integer: value);

    /// <summary>Creates a 16-bit unsigned value.</summary>
    /// <param name="value">Value.</param>
    public static DeviceValue FromUInt16(ushort value) => new(DeviceDataType.UInt16, integer: value);

    /// <summary>Creates a 32-bit signed value.</summary>
    /// <param name="value">Value.</param>
    public static DeviceValue FromInt32(int value) => new(DeviceDataType.Int32, integer: value);

    /// <summary>Creates a 32-bit unsigned value.</summary>
    /// <param name="value">Value.</param>
    public static DeviceValue FromUInt32(uint value) => new(DeviceDataType.UInt32, integer: value);

    /// <summary>Creates a 64-bit signed value.</summary>
    /// <param name="value">Value.</param>
    public static DeviceValue FromInt64(long value) => new(DeviceDataType.Int64, integer: value);

    /// <summary>Creates a 64-bit unsigned value.</summary>
    /// <param name="value">Value.</param>
    public static DeviceValue FromUInt64(ulong value) => new(DeviceDataType.UInt64, integer: unchecked((long)value));

    /// <summary>Creates a single-precision value.</summary>
    /// <param name="value">Value.</param>
    public static DeviceValue FromFloat(float value) => new(DeviceDataType.Float, real: value);

    /// <summary>Creates a double-precision value.</summary>
    /// <param name="value">Value.</param>
    public static DeviceValue FromDouble(double value) => new(DeviceDataType.Double, real: value);

    /// <summary>Creates a text value.</summary>
    /// <param name="value">Value.</param>
    public static DeviceValue FromString(string value) =>
        new(DeviceDataType.String, reference: value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a raw byte value.</summary>
    /// <param name="value">Value.</param>
    public static DeviceValue FromBytes(ReadOnlySpan<byte> value) => new(DeviceDataType.Raw, reference: value.ToArray());

    /// <summary>Creates an array of 16-bit signed integers.</summary>
    /// <param name="values">Element values.</param>
    public static DeviceValue FromInt16Array(ReadOnlySpan<short> values) =>
        new(DeviceDataType.Int16, reference: values.ToArray());

    /// <summary>Creates an array of 16-bit unsigned integers.</summary>
    /// <param name="values">Element values.</param>
    public static DeviceValue FromUInt16Array(ReadOnlySpan<ushort> values) =>
        new(DeviceDataType.UInt16, reference: values.ToArray());

    /// <summary>Creates an array of 32-bit signed integers.</summary>
    /// <param name="values">Element values.</param>
    public static DeviceValue FromInt32Array(ReadOnlySpan<int> values) =>
        new(DeviceDataType.Int32, reference: values.ToArray());

    /// <summary>Creates an array of 32-bit unsigned integers.</summary>
    /// <param name="values">Element values.</param>
    public static DeviceValue FromUInt32Array(ReadOnlySpan<uint> values) =>
        new(DeviceDataType.UInt32, reference: values.ToArray());

    /// <summary>Creates an array of 64-bit signed integers.</summary>
    /// <param name="values">Element values.</param>
    public static DeviceValue FromInt64Array(ReadOnlySpan<long> values) =>
        new(DeviceDataType.Int64, reference: values.ToArray());

    /// <summary>Creates an array of 64-bit unsigned integers.</summary>
    /// <param name="values">Element values.</param>
    public static DeviceValue FromUInt64Array(ReadOnlySpan<ulong> values) =>
        new(DeviceDataType.UInt64, reference: values.ToArray());

    /// <summary>Creates an array of single-precision numbers.</summary>
    /// <param name="values">Element values.</param>
    public static DeviceValue FromFloatArray(ReadOnlySpan<float> values) =>
        new(DeviceDataType.Float, reference: values.ToArray());

    /// <summary>Creates an array of double-precision numbers.</summary>
    /// <param name="values">Element values.</param>
    public static DeviceValue FromDoubleArray(ReadOnlySpan<double> values) =>
        new(DeviceDataType.Double, reference: values.ToArray());

    /// <summary>Gets whether the value holds an array of elements.</summary>
    public bool IsArray => DataType is not (DeviceDataType.Null or DeviceDataType.String or DeviceDataType.Raw)
        && _reference is Array;

    /// <summary>Gets the element count: array length, string length, byte count, or 1 for scalars.</summary>
    public int Length => DataType switch
    {
        DeviceDataType.Null => 0,
        DeviceDataType.String => ((string)_reference!).Length,
        DeviceDataType.Raw => ((byte[])_reference!).Length,
        _ => _reference is Array array ? array.Length : 1,
    };

    /// <summary>Gets an array value as 16-bit signed integers.</summary>
    public short[] AsInt16Array() => CloneArray<short>(DeviceDataType.Int16);

    /// <summary>Gets an array value as 16-bit unsigned integers.</summary>
    public ushort[] AsUInt16Array() => CloneArray<ushort>(DeviceDataType.UInt16);

    /// <summary>Gets an array value as 32-bit signed integers.</summary>
    public int[] AsInt32Array() => CloneArray<int>(DeviceDataType.Int32);

    /// <summary>Gets an array value as 32-bit unsigned integers.</summary>
    public uint[] AsUInt32Array() => CloneArray<uint>(DeviceDataType.UInt32);

    /// <summary>Gets an array value as 64-bit signed integers.</summary>
    public long[] AsInt64Array() => CloneArray<long>(DeviceDataType.Int64);

    /// <summary>Gets an array value as 64-bit unsigned integers.</summary>
    public ulong[] AsUInt64Array() => CloneArray<ulong>(DeviceDataType.UInt64);

    /// <summary>Gets an array value as single-precision numbers.</summary>
    public float[] AsFloatArray() => CloneArray<float>(DeviceDataType.Float);

    /// <summary>Gets an array value as double-precision numbers.</summary>
    public double[] AsDoubleArray() => CloneArray<double>(DeviceDataType.Double);

    private T[] CloneArray<T>(DeviceDataType expected)
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
            DeviceDataType.Bool => _bool,
            DeviceDataType.String or DeviceDataType.Raw or DeviceDataType.Null
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
            DeviceDataType.Raw when _reference is byte[] { Length: 1 } bytes => bytes[0],
            DeviceDataType.String or DeviceDataType.Raw or DeviceDataType.Null
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
            DeviceDataType.Bool => _bool ? 1 : 0,
            DeviceDataType.Float or DeviceDataType.Double => checked((long)_real),
            DeviceDataType.String or DeviceDataType.Raw or DeviceDataType.Null
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
            DeviceDataType.Bool => _bool ? 1 : 0,
            DeviceDataType.Float or DeviceDataType.Double => _real,
            DeviceDataType.String or DeviceDataType.Raw or DeviceDataType.Null
                => throw new InvalidCastException($"Cannot read a {DataType} value as Double."),
            _ => _integer,
        };
    }

    /// <summary>Gets the text value.</summary>
    public string AsString() => DataType switch
    {
        DeviceDataType.String => (string)_reference!,
        _ => throw new InvalidCastException($"Cannot read a {DataType} value as String."),
    };

    /// <summary>Gets the raw bytes.</summary>
    public byte[] AsBytes()
    {
        ThrowIfArray();

        return DataType switch
        {
            DeviceDataType.Raw => (byte[])((byte[])_reference!).Clone(),
            DeviceDataType.Null => Array.Empty<byte>(),
            _ => throw new InvalidCastException($"Cannot read a {DataType} value as Raw bytes."),
        };
    }

    /// <summary>Returns the boxed underlying value. Array values are cloned.</summary>
    public object? ToObject() => DataType switch
    {
        DeviceDataType.Null => null,
        DeviceDataType.Bool => _bool,
        DeviceDataType.Byte => (byte)_integer,
        DeviceDataType.Int16 => IsArray ? ((short[])_reference!).Clone() : (short)_integer,
        DeviceDataType.UInt16 => IsArray ? ((ushort[])_reference!).Clone() : (ushort)_integer,
        DeviceDataType.Int32 => IsArray ? ((int[])_reference!).Clone() : (int)_integer,
        DeviceDataType.UInt32 => IsArray ? ((uint[])_reference!).Clone() : (uint)_integer,
        DeviceDataType.Int64 => IsArray ? ((long[])_reference!).Clone() : _integer,
        DeviceDataType.UInt64 => IsArray ? ((ulong[])_reference!).Clone() : unchecked((ulong)_integer),
        DeviceDataType.Float => IsArray ? ((float[])_reference!).Clone() : (float)_real,
        DeviceDataType.Double => IsArray ? ((double[])_reference!).Clone() : _real,
        DeviceDataType.String => _reference,
        DeviceDataType.Raw => ((byte[])_reference!).Clone(),
        _ => null,
    };

    /// <inheritdoc/>
    /// <remarks>
    /// Quality and timestamp are metadata: two values with the same data type and payload compare equal
    /// even when their quality or timestamp differ, so change detection keeps working on polled reads.
    /// Arrays compare element by element.
    /// </remarks>
    public bool Equals(DeviceValue other)
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
            DeviceDataType.Null => true,
            DeviceDataType.Bool => _bool == other._bool,
            DeviceDataType.Float or DeviceDataType.Double => _real.Equals(other._real),
            DeviceDataType.String => string.Equals((string?)_reference, (string?)other._reference, StringComparison.Ordinal),
            DeviceDataType.Raw => ((byte[])_reference!).AsSpan().SequenceEqual((byte[])other._reference!),
            _ => _integer == other._integer,
        };
    }

    private bool EqualsArray(DeviceValue other)
    {
        if (other._reference is not Array rightArray || rightArray.Length != Length)
        {
            return false;
        }

        return DataType switch
        {
            DeviceDataType.Int16 => ((short[])_reference!).AsSpan().SequenceEqual((short[])rightArray),
            DeviceDataType.UInt16 => ((ushort[])_reference!).AsSpan().SequenceEqual((ushort[])rightArray),
            DeviceDataType.Int32 => ((int[])_reference!).AsSpan().SequenceEqual((int[])rightArray),
            DeviceDataType.UInt32 => ((uint[])_reference!).AsSpan().SequenceEqual((uint[])rightArray),
            DeviceDataType.Int64 => ((long[])_reference!).AsSpan().SequenceEqual((long[])rightArray),
            DeviceDataType.UInt64 => ((ulong[])_reference!).AsSpan().SequenceEqual((ulong[])rightArray),
            DeviceDataType.Float => ((float[])_reference!).AsSpan().SequenceEqual((float[])rightArray),
            DeviceDataType.Double => ((double[])_reference!).AsSpan().SequenceEqual((double[])rightArray),
            _ => false,
        };
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DeviceValue other && Equals(other);

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
            DeviceDataType.Null => 0,
            DeviceDataType.Bool => _bool.GetHashCode(),
            DeviceDataType.Float or DeviceDataType.Double => _real.GetHashCode(),
            DeviceDataType.String => StringComparer.Ordinal.GetHashCode((string)_reference!),
            DeviceDataType.Raw => ((byte[])_reference!).Length,
            _ => _integer.GetHashCode(),
        };
    }

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator ==(DeviceValue left, DeviceValue right) => left.Equals(right);

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator !=(DeviceValue left, DeviceValue right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => IsArray
        ? $"{DataType}[{Length}]"
        : DataType switch
        {
            DeviceDataType.Null => "Null",
            DeviceDataType.String => $"String:\"{_reference}\"",
            DeviceDataType.Raw => $"Raw:{((byte[])_reference!).Length}B",
            _ => $"{DataType}:{ToObject()}",
        };
}
