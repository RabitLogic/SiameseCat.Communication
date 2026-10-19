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

    /// <summary>Encodes a value as big-endian bytes.</summary>
    /// <param name="value">Value to encode. Strings are not supported.</param>
    /// <exception cref="NotSupportedException">The value is a string.</exception>
    public static byte[] ToBytes(PlcValue value)
    {
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

    /// <summary>Decodes a big-endian value.</summary>
    /// <param name="dataType">Target data type.</param>
    /// <param name="bytes">Source bytes; at least the size required by <paramref name="dataType"/>.</param>
    public static PlcValue FromBytes(PlcDataType dataType, ReadOnlySpan<byte> bytes)
    {
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
}
