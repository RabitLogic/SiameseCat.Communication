using System.Text;

using SiameseCat.Communication.Modbus;

namespace SiameseCat.Tests.Modbus;

public class ModbusRegisterConverterTests
{
    [Fact]
    public void ReadUInt32_UsesHighWordFirstByDefault()
    {
        var registers = new ushort[] { 0x1234, 0x5678 };

        Assert.Equal(0x12345678u, ModbusRegisterConverter.ReadUInt32(registers, 0));
    }

    [Fact]
    public void ReadUInt32_SupportsLowWordFirst()
    {
        var registers = new ushort[] { 0x5678, 0x1234 };

        Assert.Equal(0x12345678u, ModbusRegisterConverter.ReadUInt32(registers, 0, ModbusWordOrder.LowWordFirst));
    }

    [Fact]
    public void ReadSingle_RoundTrips()
    {
        var registers = new ushort[2];
        ModbusRegisterConverter.WriteSingle(registers, 0, 123.456f);

        Assert.Equal(123.456f, ModbusRegisterConverter.ReadSingle(registers, 0));
    }

    [Fact]
    public void ReadDouble_RoundTripsBothWordOrders()
    {
        var registers = new ushort[4];
        ModbusRegisterConverter.WriteDouble(registers, 0, Math.PI, ModbusWordOrder.LowWordFirst);

        Assert.Equal(Math.PI, ModbusRegisterConverter.ReadDouble(registers, 0, ModbusWordOrder.LowWordFirst));
    }

    [Fact]
    public void ReadString_StopsAtNullTerminator()
    {
        var registers = new ushort[4];
        ModbusRegisterConverter.WriteString(registers, 0, 4, "AB");

        Assert.Equal("AB", ModbusRegisterConverter.ReadString(registers, 0, 4));
    }

    [Fact]
    public void ReadString_CanSwapBytesWithinRegisters()
    {
        var registers = new ushort[2];
        ModbusRegisterConverter.WriteString(registers, 0, 2, "AB", Encoding.ASCII, swapBytes: true);

        Assert.Equal(0x4241, registers[0]);
        Assert.Equal("AB", ModbusRegisterConverter.ReadString(registers, 0, 2, swapBytes: true));
    }

    [Fact]
    public void ReadString_TrimsNullBeforeDecodingMultiByteText()
    {
        var registers = new ushort[4];
        ModbusRegisterConverter.WriteString(registers, 0, 4, "温度", Encoding.UTF8);

        Assert.Equal("温度", ModbusRegisterConverter.ReadString(registers, 0, 4, Encoding.UTF8));
        Assert.Equal(
            "温度",
            ModbusRegisterConverter.ReadString(registers, 0, 4, Encoding.UTF8, trimAtNull: false).TrimEnd('\0'));
    }

    [Fact]
    public void ReadString_RejectsRangeOutsideArray()
    {
        var registers = new ushort[2];

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ModbusRegisterConverter.ReadString(registers, 0, 4));
    }

    [Fact]
    public void ReadUInt64_RoundTripsBothWordOrders()
    {
        var registers = new ushort[4];
        const ulong value = 0x0123456789ABCDEFUL;

        ModbusRegisterConverter.WriteUInt64(registers, 0, value);
        Assert.Equal(value, ModbusRegisterConverter.ReadUInt64(registers, 0));
        Assert.Equal(new ushort[] { 0x0123, 0x4567, 0x89AB, 0xCDEF }, registers);

        ModbusRegisterConverter.WriteUInt64(registers, 0, value, ModbusWordOrder.LowWordFirst);
        Assert.Equal(value, ModbusRegisterConverter.ReadUInt64(registers, 0, ModbusWordOrder.LowWordFirst));
        Assert.Equal(new ushort[] { 0xCDEF, 0x89AB, 0x4567, 0x0123 }, registers);
    }

    [Fact]
    public void ReadInt64_RoundTripsNegativeValues()
    {
        var registers = new ushort[4];

        ModbusRegisterConverter.WriteInt64(registers, 0, -1234567890123L);

        Assert.Equal(-1234567890123L, ModbusRegisterConverter.ReadInt64(registers, 0));
    }

    [Fact]
    public void ReadBytes_ConvertsRegistersBigEndian()
    {
        var registers = new ushort[] { 0x0102, 0x0304 };

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, ModbusRegisterConverter.ReadBytes(registers, 0, 2));
        Assert.Equal(
            new byte[] { 0x02, 0x01, 0x04, 0x03 },
            ModbusRegisterConverter.ReadBytes(registers, 0, 2, swapBytes: true));
    }

    [Fact]
    public void WriteBytes_PadsPartialRegistersWithZero()
    {
        var registers = new ushort[3];

        ModbusRegisterConverter.WriteBytes(registers, 0, 3, new byte[] { 0xAA, 0xBB, 0xCC });

        Assert.Equal(new ushort[] { 0xAABB, 0xCC00, 0x0000 }, registers);
        Assert.Equal(
            new byte[] { 0xAA, 0xBB, 0xCC, 0x00, 0x00, 0x00 },
            ModbusRegisterConverter.ReadBytes(registers, 0, 3));
    }

    [Fact]
    public void WriteBytes_RejectsPayloadLargerThanRange()
    {
        var registers = new ushort[2];

        Assert.Throws<ArgumentException>(
            () => ModbusRegisterConverter.WriteBytes(registers, 0, 1, new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void WriteString_EncodesUtf8AndZeroPads()
    {
        var registers = new ushort[3];

        ModbusRegisterConverter.WriteString(registers, 0, 3, "AB", Encoding.UTF8);

        Assert.Equal(new ushort[] { 0x4142, 0x0000, 0x0000 }, registers);
    }

    [Fact]
    public void ReadInt16_HandlesSignBit()
    {
        var registers = new ushort[] { 0xFFFF };

        Assert.Equal(-1, ModbusRegisterConverter.ReadInt16(registers, 0));
    }
}
