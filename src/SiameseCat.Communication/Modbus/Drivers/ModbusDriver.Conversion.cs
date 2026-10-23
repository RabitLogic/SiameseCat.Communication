using SiameseCat.Communication;

namespace SiameseCat.Communication.Modbus;

public partial class ModbusDriver
{
    private static void EnsureBool(DeviceAddress address)
    {
        if (address.DataType != DeviceDataType.Bool)
        {
            throw new NotSupportedException(
                $"Modbus region '{address.Region}' only supports Bool values.");
        }
    }

    private static int GetRegisterCount(DeviceAddress address)
    {
        if (address.DataType is DeviceDataType.String or DeviceDataType.Raw)
        {
            return (address.Length + 1) / 2;
        }

        var elementRegisters = address.DataType switch
        {
            DeviceDataType.Byte or DeviceDataType.Int16 or DeviceDataType.UInt16 => 1,
            DeviceDataType.Int32 or DeviceDataType.UInt32 or DeviceDataType.Float => 2,
            DeviceDataType.Int64 or DeviceDataType.UInt64 or DeviceDataType.Double => 4,
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

    private DeviceValue FromRegisters(DeviceAddress address, ReadOnlySpan<ushort> registers)
    {
        if (address.DataType == DeviceDataType.Bool)
        {
            throw new NotSupportedException(
                $"Region '{address.Region}' cannot be accessed as Bool; use coils or discrete inputs.");
        }

        if (address.Length > 1 && address.DataType is not (DeviceDataType.String or DeviceDataType.Raw))
        {
            return FromRegisterArray(address, registers);
        }

        switch (address.DataType)
        {
            case DeviceDataType.Byte:
                return DeviceValue.FromByte((byte)registers[0]);

            case DeviceDataType.Int16:
                return DeviceValue.FromInt16(ModbusRegisterConverter.ReadInt16(registers, 0));

            case DeviceDataType.UInt16:
                return DeviceValue.FromUInt16(ModbusRegisterConverter.ReadUInt16(registers, 0));

            case DeviceDataType.Int32:
                return DeviceValue.FromInt32(ModbusRegisterConverter.ReadInt32(registers, 0));

            case DeviceDataType.UInt32:
                return DeviceValue.FromUInt32(ModbusRegisterConverter.ReadUInt32(registers, 0));

            case DeviceDataType.Int64:
                return DeviceValue.FromInt64(ModbusRegisterConverter.ReadInt64(registers, 0));

            case DeviceDataType.UInt64:
                return DeviceValue.FromUInt64(ModbusRegisterConverter.ReadUInt64(registers, 0));

            case DeviceDataType.Float:
                return DeviceValue.FromFloat(ModbusRegisterConverter.ReadSingle(registers, 0));

            case DeviceDataType.Double:
                return DeviceValue.FromDouble(ModbusRegisterConverter.ReadDouble(registers, 0));

            case DeviceDataType.String:
                return DeviceValue.FromString(ModbusRegisterConverter.ReadString(
                    registers,
                    0,
                    registers.Length,
                    _stringEncoding,
                    _swapBytes));

            case DeviceDataType.Raw:
                {
                    var bytes = ModbusRegisterConverter.ReadBytes(registers, 0, registers.Length, _swapBytes);
                    return DeviceValue.FromBytes(bytes.Length > address.Length ? bytes.AsSpan(0, address.Length) : bytes);
                }

            default:
                throw new NotSupportedException($"Unsupported data type {address.DataType}.");
        }
    }

    private ushort[] ToRegisters(DeviceAddress address, DeviceValue value)
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
            case DeviceDataType.Byte:
                registers[0] = value.AsByte();
                break;
            case DeviceDataType.Int16:
                ModbusRegisterConverter.WriteInt16(registers, 0, value.AsInt16());
                break;
            case DeviceDataType.UInt16:
                ModbusRegisterConverter.WriteUInt16(registers, 0, value.AsUInt16());
                break;
            case DeviceDataType.Int32:
                ModbusRegisterConverter.WriteInt32(registers, 0, value.AsInt32());
                break;
            case DeviceDataType.UInt32:
                ModbusRegisterConverter.WriteUInt32(registers, 0, value.AsUInt32());
                break;
            case DeviceDataType.Int64:
                ModbusRegisterConverter.WriteInt64(registers, 0, value.AsInt64());
                break;
            case DeviceDataType.UInt64:
                ModbusRegisterConverter.WriteUInt64(registers, 0, value.AsUInt64());
                break;
            case DeviceDataType.Float:
                ModbusRegisterConverter.WriteSingle(registers, 0, value.AsFloat());
                break;
            case DeviceDataType.Double:
                ModbusRegisterConverter.WriteDouble(registers, 0, value.AsDouble());
                break;
            case DeviceDataType.String:
                ModbusRegisterConverter.WriteString(
                    registers, 0, count, value.AsString(), _stringEncoding, _swapBytes);
                break;
            case DeviceDataType.Raw:
                ModbusRegisterConverter.WriteBytes(registers, 0, count, value.AsBytes(), _swapBytes);
                break;
            default:
                throw new NotSupportedException($"Unsupported data type {address.DataType}.");
        }

        return registers;
    }

    private static DeviceValue FromRegisterArray(DeviceAddress address, ReadOnlySpan<ushort> registers)
    {
        var count = address.Length;

        switch (address.DataType)
        {
            case DeviceDataType.Byte:
                {
                    var values = new byte[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = (byte)registers[i];
                    }

                    return DeviceValue.FromBytes(values);
                }

            case DeviceDataType.Int16:
                {
                    var values = new short[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadInt16(registers, i);
                    }

                    return DeviceValue.FromInt16Array(values);
                }

            case DeviceDataType.UInt16:
                {
                    var values = new ushort[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadUInt16(registers, i);
                    }

                    return DeviceValue.FromUInt16Array(values);
                }

            case DeviceDataType.Int32:
                {
                    var values = new int[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadInt32(registers, i * 2);
                    }

                    return DeviceValue.FromInt32Array(values);
                }

            case DeviceDataType.UInt32:
                {
                    var values = new uint[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadUInt32(registers, i * 2);
                    }

                    return DeviceValue.FromUInt32Array(values);
                }

            case DeviceDataType.Int64:
                {
                    var values = new long[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadInt64(registers, i * 4);
                    }

                    return DeviceValue.FromInt64Array(values);
                }

            case DeviceDataType.UInt64:
                {
                    var values = new ulong[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadUInt64(registers, i * 4);
                    }

                    return DeviceValue.FromUInt64Array(values);
                }

            case DeviceDataType.Float:
                {
                    var values = new float[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadSingle(registers, i * 2);
                    }

                    return DeviceValue.FromFloatArray(values);
                }

            case DeviceDataType.Double:
                {
                    var values = new double[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = ModbusRegisterConverter.ReadDouble(registers, i * 4);
                    }

                    return DeviceValue.FromDoubleArray(values);
                }

            default:
                throw new NotSupportedException($"Unsupported array data type {address.DataType}.");
        }
    }

    private static void WriteArrayRegisters(DeviceAddress address, DeviceValue value, ushort[] registers)
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
            case DeviceDataType.Int16:
                {
                    var values = value.AsInt16Array();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteInt16(registers, i, values[i]);
                    }

                    break;
                }

            case DeviceDataType.UInt16:
                {
                    var values = value.AsUInt16Array();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteUInt16(registers, i, values[i]);
                    }

                    break;
                }

            case DeviceDataType.Int32:
                {
                    var values = value.AsInt32Array();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteInt32(registers, i * 2, values[i]);
                    }

                    break;
                }

            case DeviceDataType.UInt32:
                {
                    var values = value.AsUInt32Array();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteUInt32(registers, i * 2, values[i]);
                    }

                    break;
                }

            case DeviceDataType.Int64:
                {
                    var values = value.AsInt64Array();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteInt64(registers, i * 4, values[i]);
                    }

                    break;
                }

            case DeviceDataType.UInt64:
                {
                    var values = value.AsUInt64Array();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteUInt64(registers, i * 4, values[i]);
                    }

                    break;
                }

            case DeviceDataType.Float:
                {
                    var values = value.AsFloatArray();
                    for (var i = 0; i < values.Length; i++)
                    {
                        ModbusRegisterConverter.WriteSingle(registers, i * 2, values[i]);
                    }

                    break;
                }

            case DeviceDataType.Double:
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
