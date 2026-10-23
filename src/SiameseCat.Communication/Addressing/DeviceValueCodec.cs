using System.Buffers.Binary;

namespace SiameseCat.Communication;

/// <summary>Converts between <see cref="DeviceValue"/> and its big-endian byte representation.</summary>
/// <remarks>
/// Modbus registers and S7 variables are both transmitted big-endian, so a single codec can be
/// shared by both protocol drivers. Text is not handled here because each protocol encodes
/// strings with its own layout and encoding.
/// </remarks>
public static class DeviceValueCodec
{
    /// <summary>Gets the byte size required to store <paramref name="address"/>.</summary>
    /// <param name="address">Address that describes the value.</param>
    public static int GetByteSize(DeviceAddress address) => address.ByteSize;

    /// <summary>Gets the byte size required to store a value of <paramref name="dataType"/>.</summary>
    /// <param name="dataType">Data type.</param>
    /// <param name="length">Element count, character count or byte count.</param>
    public static int GetByteSize(DeviceDataType dataType, int length = 1)
    {
        var elementSize = dataType.GetByteSize();
        return elementSize == 0 ? length : elementSize * length;
    }

    /// <summary>Encodes a value as big-endian bytes. Array values are encoded element by element.</summary>
    /// <param name="value">Value to encode. Strings are not supported.</param>
    /// <exception cref="NotSupportedException">The value is a string.</exception>
    public static byte[] ToBytes(DeviceValue value)
    {
        if (value.IsArray)
        {
            return ToArrayBytes(value);
        }

        switch (value.DataType)
        {
            case DeviceDataType.Null:
                return Array.Empty<byte>();

            case DeviceDataType.Bool:
                return new[] { value.AsBool() ? (byte)1 : (byte)0 };

            case DeviceDataType.Byte:
                return new[] { value.AsByte() };

            case DeviceDataType.Int16:
            case DeviceDataType.UInt16:
                {
                    var bytes = new byte[2];
                    BinaryPrimitives.WriteUInt16BigEndian(bytes, value.AsUInt16());
                    return bytes;
                }

            case DeviceDataType.Int32:
            case DeviceDataType.UInt32:
                {
                    var bytes = new byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(bytes, value.AsUInt32());
                    return bytes;
                }

            case DeviceDataType.Int64:
            case DeviceDataType.UInt64:
                {
                    var bytes = new byte[8];
                    BinaryPrimitives.WriteUInt64BigEndian(bytes, value.AsUInt64());
                    return bytes;
                }

            case DeviceDataType.Float:
                {
                    var bytes = new byte[4];
                    BinaryPrimitives.WriteInt32BigEndian(bytes, BitConverter.SingleToInt32Bits(value.AsFloat()));
                    return bytes;
                }

            case DeviceDataType.Double:
                {
                    var bytes = new byte[8];
                    BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(value.AsDouble()));
                    return bytes;
                }

            case DeviceDataType.Raw:
                return value.AsBytes();

            case DeviceDataType.String:
                throw new NotSupportedException(
                    "Strings are encoded by the protocol driver because their layout differs per protocol.");

            default:
                throw new NotSupportedException($"Unsupported data type {value.DataType}.");
        }
    }

    /// <summary>Decodes a big-endian value, optionally as an array of elements.</summary>
    /// <param name="dataType">Target data type.</param>
    /// <param name="bytes">Source bytes; at least the size required by the elements.</param>
    /// <param name="count">Element count; 1 decodes a scalar. Ignored for <see cref="DeviceDataType.Raw"/> and strings.</param>
    public static DeviceValue FromBytes(DeviceDataType dataType, ReadOnlySpan<byte> bytes, int count = 1)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be at least 1.");
        }

        if (count > 1)
        {
            return FromArrayBytes(dataType, bytes, count);
        }

        var required = GetByteSize(dataType);
        if (dataType is not (DeviceDataType.Raw or DeviceDataType.Null) && dataType is not DeviceDataType.String && bytes.Length < required)
        {
            throw new ArgumentException(
                $"A {dataType} value needs {required} bytes but only {bytes.Length} were provided.", nameof(bytes));
        }

        return dataType switch
        {
            DeviceDataType.Null => DeviceValue.Null,
            DeviceDataType.Bool => DeviceValue.FromBool(bytes[0] != 0),
            DeviceDataType.Byte => DeviceValue.FromByte(bytes[0]),
            DeviceDataType.Int16 => DeviceValue.FromInt16(BinaryPrimitives.ReadInt16BigEndian(bytes)),
            DeviceDataType.UInt16 => DeviceValue.FromUInt16(BinaryPrimitives.ReadUInt16BigEndian(bytes)),
            DeviceDataType.Int32 => DeviceValue.FromInt32(BinaryPrimitives.ReadInt32BigEndian(bytes)),
            DeviceDataType.UInt32 => DeviceValue.FromUInt32(BinaryPrimitives.ReadUInt32BigEndian(bytes)),
            DeviceDataType.Int64 => DeviceValue.FromInt64(BinaryPrimitives.ReadInt64BigEndian(bytes)),
            DeviceDataType.UInt64 => DeviceValue.FromUInt64(BinaryPrimitives.ReadUInt64BigEndian(bytes)),
            DeviceDataType.Float => DeviceValue.FromFloat(BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32BigEndian(bytes))),
            DeviceDataType.Double => DeviceValue.FromDouble(BitConverter.Int64BitsToDouble(
                BinaryPrimitives.ReadInt64BigEndian(bytes))),
            DeviceDataType.Raw => DeviceValue.FromBytes(bytes),
            DeviceDataType.String => throw new NotSupportedException(
                "Strings are decoded by the protocol driver because their layout differs per protocol."),
            _ => throw new NotSupportedException($"Unsupported data type {dataType}."),
        };
    }

    private static byte[] ToArrayBytes(DeviceValue value)
    {
        switch (value.DataType)
        {
            case DeviceDataType.Int16:
                {
                    var values = value.AsInt16Array();
                    var bytes = new byte[values.Length * 2];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(i * 2), values[i]);
                    }

                    return bytes;
                }

            case DeviceDataType.UInt16:
                {
                    var values = value.AsUInt16Array();
                    var bytes = new byte[values.Length * 2];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2), values[i]);
                    }

                    return bytes;
                }

            case DeviceDataType.Int32:
                {
                    var values = value.AsInt32Array();
                    var bytes = new byte[values.Length * 4];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(i * 4), values[i]);
                    }

                    return bytes;
                }

            case DeviceDataType.UInt32:
                {
                    var values = value.AsUInt32Array();
                    var bytes = new byte[values.Length * 4];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), values[i]);
                    }

                    return bytes;
                }

            case DeviceDataType.Int64:
                {
                    var values = value.AsInt64Array();
                    var bytes = new byte[values.Length * 8];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(i * 8), values[i]);
                    }

                    return bytes;
                }

            case DeviceDataType.UInt64:
                {
                    var values = value.AsUInt64Array();
                    var bytes = new byte[values.Length * 8];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(i * 8), values[i]);
                    }

                    return bytes;
                }

            case DeviceDataType.Float:
                {
                    var values = value.AsFloatArray();
                    var bytes = new byte[values.Length * 4];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteInt32BigEndian(
                            bytes.AsSpan(i * 4),
                            BitConverter.SingleToInt32Bits(values[i]));
                    }

                    return bytes;
                }

            case DeviceDataType.Double:
                {
                    var values = value.AsDoubleArray();
                    var bytes = new byte[values.Length * 8];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteInt64BigEndian(
                            bytes.AsSpan(i * 8),
                            BitConverter.DoubleToInt64Bits(values[i]));
                    }

                    return bytes;
                }

            default:
                throw new NotSupportedException($"Arrays of {value.DataType} are not supported by the codec.");
        }
    }

    private static DeviceValue FromArrayBytes(DeviceDataType dataType, ReadOnlySpan<byte> bytes, int count)
    {
        if (dataType is DeviceDataType.Raw or DeviceDataType.String or DeviceDataType.Null)
        {
            return FromBytes(dataType, bytes);
        }

        var stride = GetByteSize(dataType);
        if (bytes.Length < stride * count)
        {
            throw new ArgumentException(
                $"{count} {dataType} elements need {stride * count} bytes but only {bytes.Length} were provided.",
                nameof(bytes));
        }

        switch (dataType)
        {
            case DeviceDataType.Int16:
                {
                    var values = new short[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(i * stride));
                    }

                    return DeviceValue.FromInt16Array(values);
                }

            case DeviceDataType.UInt16:
                {
                    var values = new ushort[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(i * stride));
                    }

                    return DeviceValue.FromUInt16Array(values);
                }

            case DeviceDataType.Int32:
                {
                    var values = new int[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(i * stride));
                    }

                    return DeviceValue.FromInt32Array(values);
                }

            case DeviceDataType.UInt32:
                {
                    var values = new uint[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(i * stride));
                    }

                    return DeviceValue.FromUInt32Array(values);
                }

            case DeviceDataType.Int64:
                {
                    var values = new long[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(i * stride));
                    }

                    return DeviceValue.FromInt64Array(values);
                }

            case DeviceDataType.UInt64:
                {
                    var values = new ulong[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(i * stride));
                    }

                    return DeviceValue.FromUInt64Array(values);
                }

            case DeviceDataType.Float:
                {
                    var values = new float[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BitConverter.Int32BitsToSingle(
                            BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(i * stride)));
                    }

                    return DeviceValue.FromFloatArray(values);
                }

            case DeviceDataType.Double:
                {
                    var values = new double[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BitConverter.Int64BitsToDouble(
                            BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(i * stride)));
                    }

                    return DeviceValue.FromDoubleArray(values);
                }

            default:
                throw new NotSupportedException($"Arrays of {dataType} are not supported by the codec.");
        }
    }
}
