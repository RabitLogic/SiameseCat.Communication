using System.Buffers.Binary;

namespace SiameseCat.Core;

/// <summary>Converts between <see cref="PlcValue"/> and its big-endian byte representation.</summary>
/// <remarks>
/// Modbus registers and S7 variables are both transmitted big-endian, so a single codec can be
/// shared by both protocol drivers. Text is not handled here because each protocol encodes
/// strings with its own layout and encoding.
/// </remarks>
public static class PlcValueCodec
{
    /// <summary>Gets the byte size required to store <paramref name="address"/>.</summary>
    /// <param name="address">Address that describes the value.</param>
    public static int GetByteSize(PlcAddress address) => address.ByteSize;

    /// <summary>Gets the byte size required to store a value of <paramref name="dataType"/>.</summary>
    /// <param name="dataType">Data type.</param>
    /// <param name="length">Element count, character count or byte count.</param>
    public static int GetByteSize(PlcDataType dataType, int length = 1)
    {
        var elementSize = dataType.GetByteSize();
        return elementSize == 0 ? length : elementSize * length;
    }

    /// <summary>Encodes a value as big-endian bytes. Array values are encoded element by element.</summary>
    /// <param name="value">Value to encode. Strings are not supported.</param>
    /// <exception cref="NotSupportedException">The value is a string.</exception>
    public static byte[] ToBytes(PlcValue value)
    {
        if (value.IsArray)
        {
            return ToArrayBytes(value);
        }

        switch (value.DataType)
        {
            case PlcDataType.Null:
                return Array.Empty<byte>();

            case PlcDataType.Bool:
                return new[] { value.AsBool() ? (byte)1 : (byte)0 };

            case PlcDataType.Byte:
                return new[] { value.AsByte() };

            case PlcDataType.Int16:
            case PlcDataType.UInt16:
                {
                    var bytes = new byte[2];
                    BinaryPrimitives.WriteUInt16BigEndian(bytes, value.AsUInt16());
                    return bytes;
                }

            case PlcDataType.Int32:
            case PlcDataType.UInt32:
                {
                    var bytes = new byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(bytes, value.AsUInt32());
                    return bytes;
                }

            case PlcDataType.Int64:
            case PlcDataType.UInt64:
                {
                    var bytes = new byte[8];
                    BinaryPrimitives.WriteUInt64BigEndian(bytes, value.AsUInt64());
                    return bytes;
                }

            case PlcDataType.Float:
                {
                    var bytes = new byte[4];
                    BinaryPrimitives.WriteInt32BigEndian(bytes, BitConverter.SingleToInt32Bits(value.AsFloat()));
                    return bytes;
                }

            case PlcDataType.Double:
                {
                    var bytes = new byte[8];
                    BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(value.AsDouble()));
                    return bytes;
                }

            case PlcDataType.Raw:
                return value.AsBytes();

            case PlcDataType.String:
                throw new NotSupportedException(
                    "Strings are encoded by the protocol driver because their layout differs per protocol.");

            default:
                throw new NotSupportedException($"Unsupported data type {value.DataType}.");
        }
    }

    /// <summary>Decodes a big-endian value, optionally as an array of elements.</summary>
    /// <param name="dataType">Target data type.</param>
    /// <param name="bytes">Source bytes; at least the size required by the elements.</param>
    /// <param name="count">Element count; 1 decodes a scalar. Ignored for <see cref="PlcDataType.Raw"/> and strings.</param>
    public static PlcValue FromBytes(PlcDataType dataType, ReadOnlySpan<byte> bytes, int count = 1)
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
        if (dataType is not (PlcDataType.Raw or PlcDataType.Null) && dataType is not PlcDataType.String && bytes.Length < required)
        {
            throw new ArgumentException(
                $"A {dataType} value needs {required} bytes but only {bytes.Length} were provided.", nameof(bytes));
        }

        return dataType switch
        {
            PlcDataType.Null => PlcValue.Null,
            PlcDataType.Bool => PlcValue.FromBool(bytes[0] != 0),
            PlcDataType.Byte => PlcValue.FromByte(bytes[0]),
            PlcDataType.Int16 => PlcValue.FromInt16(BinaryPrimitives.ReadInt16BigEndian(bytes)),
            PlcDataType.UInt16 => PlcValue.FromUInt16(BinaryPrimitives.ReadUInt16BigEndian(bytes)),
            PlcDataType.Int32 => PlcValue.FromInt32(BinaryPrimitives.ReadInt32BigEndian(bytes)),
            PlcDataType.UInt32 => PlcValue.FromUInt32(BinaryPrimitives.ReadUInt32BigEndian(bytes)),
            PlcDataType.Int64 => PlcValue.FromInt64(BinaryPrimitives.ReadInt64BigEndian(bytes)),
            PlcDataType.UInt64 => PlcValue.FromUInt64(BinaryPrimitives.ReadUInt64BigEndian(bytes)),
            PlcDataType.Float => PlcValue.FromFloat(BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32BigEndian(bytes))),
            PlcDataType.Double => PlcValue.FromDouble(BitConverter.Int64BitsToDouble(
                BinaryPrimitives.ReadInt64BigEndian(bytes))),
            PlcDataType.Raw => PlcValue.FromBytes(bytes),
            PlcDataType.String => throw new NotSupportedException(
                "Strings are decoded by the protocol driver because their layout differs per protocol."),
            _ => throw new NotSupportedException($"Unsupported data type {dataType}."),
        };
    }

    private static byte[] ToArrayBytes(PlcValue value)
    {
        switch (value.DataType)
        {
            case PlcDataType.Int16:
                {
                    var values = value.AsInt16Array();
                    var bytes = new byte[values.Length * 2];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(i * 2), values[i]);
                    }

                    return bytes;
                }

            case PlcDataType.UInt16:
                {
                    var values = value.AsUInt16Array();
                    var bytes = new byte[values.Length * 2];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2), values[i]);
                    }

                    return bytes;
                }

            case PlcDataType.Int32:
                {
                    var values = value.AsInt32Array();
                    var bytes = new byte[values.Length * 4];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(i * 4), values[i]);
                    }

                    return bytes;
                }

            case PlcDataType.UInt32:
                {
                    var values = value.AsUInt32Array();
                    var bytes = new byte[values.Length * 4];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), values[i]);
                    }

                    return bytes;
                }

            case PlcDataType.Int64:
                {
                    var values = value.AsInt64Array();
                    var bytes = new byte[values.Length * 8];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(i * 8), values[i]);
                    }

                    return bytes;
                }

            case PlcDataType.UInt64:
                {
                    var values = value.AsUInt64Array();
                    var bytes = new byte[values.Length * 8];
                    for (var i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(i * 8), values[i]);
                    }

                    return bytes;
                }

            case PlcDataType.Float:
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

            case PlcDataType.Double:
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

    private static PlcValue FromArrayBytes(PlcDataType dataType, ReadOnlySpan<byte> bytes, int count)
    {
        if (dataType is PlcDataType.Raw or PlcDataType.String or PlcDataType.Null)
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
            case PlcDataType.Int16:
                {
                    var values = new short[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(i * stride));
                    }

                    return PlcValue.FromInt16Array(values);
                }

            case PlcDataType.UInt16:
                {
                    var values = new ushort[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(i * stride));
                    }

                    return PlcValue.FromUInt16Array(values);
                }

            case PlcDataType.Int32:
                {
                    var values = new int[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(i * stride));
                    }

                    return PlcValue.FromInt32Array(values);
                }

            case PlcDataType.UInt32:
                {
                    var values = new uint[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(i * stride));
                    }

                    return PlcValue.FromUInt32Array(values);
                }

            case PlcDataType.Int64:
                {
                    var values = new long[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(i * stride));
                    }

                    return PlcValue.FromInt64Array(values);
                }

            case PlcDataType.UInt64:
                {
                    var values = new ulong[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(i * stride));
                    }

                    return PlcValue.FromUInt64Array(values);
                }

            case PlcDataType.Float:
                {
                    var values = new float[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BitConverter.Int32BitsToSingle(
                            BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(i * stride)));
                    }

                    return PlcValue.FromFloatArray(values);
                }

            case PlcDataType.Double:
                {
                    var values = new double[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = BitConverter.Int64BitsToDouble(
                            BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(i * stride)));
                    }

                    return PlcValue.FromDoubleArray(values);
                }

            default:
                throw new NotSupportedException($"Arrays of {dataType} are not supported by the codec.");
        }
    }
}
