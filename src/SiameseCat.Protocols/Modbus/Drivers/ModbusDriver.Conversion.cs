using SiameseCat.Core;

namespace SiameseCat.Protocols.Modbus;

public partial class ModbusDriver
{
    private static void EnsureBool(PlcAddress address)
    {
        if (address.DataType != PlcDataType.Bool)
        {
            throw new NotSupportedException(
                $"Modbus region '{address.Region}' only supports Bool values.");
        }
    }

    private static int GetRegisterCount(PlcAddress address)
    {
        if (address.DataType is PlcDataType.String or PlcDataType.Raw)
        {
            return (address.Length + 1) / 2;
        }

        var elementRegisters = address.DataType switch
        {
            PlcDataType.Byte or PlcDataType.Int16 or PlcDataType.UInt16 => 1,
            PlcDataType.Int32 or PlcDataType.UInt32 or PlcDataType.Float => 2,
            PlcDataType.Int64 or PlcDataType.UInt64 or PlcDataType.Double => 4,
            _ => throw new NotSupportedException($"Unsupported data type {address.DataType}."),
        };

        return elementRegisters * address.Length;
    }

    private static ushort ToUShort(int value)
    {
        if (value is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The Modbus offset must be between 0 and 65535.");
        }

        return (ushort)value;
    }

    private PlcValue FromRegisters(PlcAddress address, ReadOnlySpan<ushort> registers)
    {
        if (address.DataType == PlcDataType.Bool)
        {
            throw new NotSupportedException(
                $"Region '{address.Region}' cannot be accessed as Bool; use coils or discrete inputs.");
        }

        if (address.Length > 1 && address.DataType is not (PlcDataType.String or PlcDataType.Raw))
        {
            return FromRegisterArray(address, registers);
        }

        switch (address.DataType)
        {
            case PlcDataType.Byte:
                return PlcValue.FromByte((byte)registers[0]);

            case PlcDataType.Int16:
                return PlcValue.FromInt16(ModbusRegisterConverter.ReadInt16(registers, 0));

            case PlcDataType.UInt16:
                return PlcValue.FromUInt16(ModbusRegisterConverter.ReadUInt16(registers, 0));

            case PlcDataType.Int32:
                return PlcValue.FromInt32(ModbusRegisterConverter.ReadInt32(registers, 0));

            case PlcDataType.UInt32:
                return PlcValue.FromUInt32(ModbusRegisterConverter.ReadUInt32(registers, 0));

            case PlcDataType.Int64:
                return PlcValue.FromInt64(ModbusRegisterConverter.ReadInt64(registers, 0));

            case PlcDataType.UInt64:
                return PlcValue.FromUInt64(ModbusRegisterConverter.ReadUInt64(registers, 0));

            case PlcDataType.Float:
                return PlcValue.FromFloat(ModbusRegisterConverter.ReadSingle(registers, 0));

            case PlcDataType.Double:
                return PlcValue.FromDouble(ModbusRegisterConverter.ReadDouble(registers, 0));

            case PlcDataType.String:
                return PlcValue.FromString(ModbusRegisterConverter.ReadString(
                    registers,
                    0,
                    registers.Length,
                    _stringEncoding,
                    _swapBytes));

            case PlcDataType.Raw:
                {
                    var bytes = ModbusRegisterConverter.ReadBytes(registers, 0, registers.Length, _swapBytes);
                    return PlcValue.FromBytes(bytes.Length > address.Length ? bytes.AsSpan(0, address.Length) : bytes);
                }

            default:
                throw new NotSupportedException($"Unsupported data type {address.DataType}.");
        }
    }

    private ushort[] ToRegisters(PlcAddress address, PlcValue value)
    {
        var count = GetRegisterCount(address);
        var registers = new ushort[count];

        if (value.IsArray)
        {
            WriteArrayRegisters(address, value, registers);
            return registers;
        }

        switch (address.DataType)
        {
            case PlcDataType.Byte:
                registers[0] = value.AsByte();
                break;
            case PlcDataType.Int16:
                ModbusRegisterConverter.WriteInt16(registers, 0, value.AsInt16());
                break;
            case PlcDataType.UInt16:
                ModbusRegisterConverter.WriteUInt16(registers, 0, value.AsUInt16());
                break;
            case PlcDataType.Int32:
                ModbusRegisterConverter.WriteInt32(registers, 0, value.AsInt32());
                break;
            case PlcDataType.UInt32:
                ModbusRegisterConverter.WriteUInt32(registers, 0, value.AsUInt32());
                break;
            case PlcDataType.Int64:
                ModbusRegisterConverter.WriteInt64(registers, 0, value.AsInt64());
                break;
            case PlcDataType.UInt64:
                ModbusRegisterConverter.WriteUInt64(registers, 0, value.AsUInt64());
                break;
            case PlcDataType.Float:
                ModbusRegisterConverter.WriteSingle(registers, 0, value.AsFloat());
                break;
            case PlcDataType.Double:
                ModbusRegisterConverter.WriteDouble(registers, 0, value.AsDouble());
                break;
            case PlcDataType.String:
                ModbusRegisterConverter.WriteString(
                    registers, 0, count, value.AsString(), _stringEncoding, _swapBytes);
                break;
            case PlcDataType.Raw:
                ModbusRegisterConverter.WriteBytes(registers, 0, count, value.AsBytes(), _swapBytes);
                break;
            default:
                throw new NotSupportedException($"Unsupported data type {address.DataType}.");
        }

        return registers;
    }

    private static PlcValue FromRegisterArray(PlcAddress address, ReadOnlySpan<ushort> registers)
    {
        var count = address.Length;

        switch (address.DataType)
        {
            case PlcDataType.Byte:
                {
                    var values = new byte[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = (byte)registers[i];
                    }

                    return PlcValue.FromBytes(values);
                }

            case PlcDataType.Int16:
                {
                    var values = new short[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadInt16(registers, i);
                    }

                    return PlcValue.FromInt16Array(values);
                }

            case PlcDataType.UInt16:
                {
                    var values = new ushort[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadUInt16(registers, i);
                    }

                    return PlcValue.FromUInt16Array(values);
                }

            case PlcDataType.Int32:
                {
                    var values = new int[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadInt32(registers, i * 2);
                    }

                    return PlcValue.FromInt32Array(values);
                }

            case PlcDataType.UInt32:
                {
                    var values = new uint[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadUInt32(registers, i * 2);
                    }

                    return PlcValue.FromUInt32Array(values);
                }

            case PlcDataType.Int64:
                {
                    var values = new long[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadInt64(registers, i * 4);
                    }

                    return PlcValue.FromInt64Array(values);
                }

            case PlcDataType.UInt64:
                {
                    var values = new ulong[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadUInt64(registers, i * 4);
                    }

                    return PlcValue.FromUInt64Array(values);
                }

            case PlcDataType.Float:
                {
                    var values = new float[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadSingle(registers, i * 2);
                    }

                    return PlcValue.FromFloatArray(values);
                }

            case PlcDataType.Double:
                {
                    var values = new double[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadDouble(registers, i * 4);
                    }

                    return PlcValue.FromDoubleArray(values);
                }

            default:
                throw new NotSupportedException($"Unsupported array data type {address.DataType}.");
        }
    }

    private static void WriteArrayRegisters(PlcAddress address, PlcValue value, ushort[] registers)
    {
        if (value.Length != address.Length)
        {
            throw new ArgumentException(
                $"The value has {value.Length} elements but the address expects {address.Length}.",
                nameof(value));
        }

        if (value.DataType != address.DataType)
        {
            throw new ArgumentException(
                $"The value is {value.DataType} but the address expects {address.DataType}.",
                nameof(value));
        }

        switch (address.DataType)
        {
            case PlcDataType.Int16:
                {
                    var values = value.AsInt16Array();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteInt16(registers, i, values[i]);
                    }

                    break;
                }

            case PlcDataType.UInt16:
                {
                    var values = value.AsUInt16Array();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteUInt16(registers, i, values[i]);
                    }

                    break;
                }

            case PlcDataType.Int32:
                {
                    var values = value.AsInt32Array();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteInt32(registers, i * 2, values[i]);
                    }

                    break;
                }

            case PlcDataType.UInt32:
                {
                    var values = value.AsUInt32Array();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteUInt32(registers, i * 2, values[i]);
                    }

                    break;
                }

            case PlcDataType.Int64:
                {
                    var values = value.AsInt64Array();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteInt64(registers, i * 4, values[i]);
                    }

                    break;
                }

            case PlcDataType.UInt64:
                {
                    var values = value.AsUInt64Array();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteUInt64(registers, i * 4, values[i]);
                    }

                    break;
                }

            case PlcDataType.Float:
                {
                    var values = value.AsFloatArray();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteSingle(registers, i * 2, values[i]);
                    }

                    break;
                }

            case PlcDataType.Double:
                {
                    var values = value.AsDoubleArray();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteDouble(registers, i * 4, values[i]);
                    }

                    break;
                }

            default:
                throw new NotSupportedException($"Unsupported array data type {address.DataType}.");
        }
    }
}
