using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Modbus;

public class ModbusAddressParserTests
{
    private readonly ModbusAddressParser _parser = new();

    [Theory]
    [InlineData("40001", ModbusAddressParser.HoldingRegisterRegion, 0)]
    [InlineData("40010", ModbusAddressParser.HoldingRegisterRegion, 9)]
    [InlineData("30001", ModbusAddressParser.InputRegisterRegion, 0)]
    [InlineData("10001", ModbusAddressParser.DiscreteInputRegion, 0)]
    [InlineData("00001", ModbusAddressParser.CoilRegion, 0)]
    [InlineData("09999", ModbusAddressParser.CoilRegion, 9998)]
    [InlineData("400001", ModbusAddressParser.HoldingRegisterRegion, 0)]
    [InlineData("465536", ModbusAddressParser.HoldingRegisterRegion, 65535)]
    [InlineData("HR10", ModbusAddressParser.HoldingRegisterRegion, 10)]
    [InlineData("hr:10", ModbusAddressParser.HoldingRegisterRegion, 10)]
    [InlineData("IR0", ModbusAddressParser.InputRegisterRegion, 0)]
    [InlineData("C5", ModbusAddressParser.CoilRegion, 5)]
    [InlineData("DI7", ModbusAddressParser.DiscreteInputRegion, 7)]
    [InlineData("holding:12", ModbusAddressParser.HoldingRegisterRegion, 12)]
    [InlineData("10", ModbusAddressParser.HoldingRegisterRegion, 10)]
    public void Parse_ReturnsExpectedRegionAndOffset(string text, string region, int offset)
    {
        var address = _parser.Parse(text);

        Assert.Equal(region, address.Region);
        Assert.Equal(offset, address.Offset);
    }

    [Fact]
    public void Parse_UsesRegionDefaultDataType()
    {
        Assert.Equal(PlcDataType.Bool, _parser.Parse("00001").DataType);
        Assert.Equal(PlcDataType.Bool, _parser.Parse("10001").DataType);
        Assert.Equal(PlcDataType.UInt16, _parser.Parse("40001").DataType);
        Assert.Equal(PlcDataType.UInt16, _parser.Parse("30001").DataType);
    }

    [Fact]
    public void Parse_OverridesDataType()
    {
        var address = _parser.Parse("40001", PlcDataType.Float);

        Assert.Equal(PlcDataType.Float, address.DataType);
        Assert.Equal(ModbusAddressParser.HoldingRegisterRegion, address.Region);
    }

    [Theory]
    [InlineData("50001")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("HR-1")]
    public void Parse_RejectsInvalidAddress(string text)
    {
        Assert.ThrowsAny<Exception>(() => _parser.Parse(text));
    }
}

public class ModbusDriverTests
{
    [Fact]
    public async Task ReadHoldingRegister_ReturnsUInt16()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x03, 0x02, 0x00, 0x2A));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var value = await driver.ReadAsync(driver.AddressParser.Parse("40001"));

        Assert.Equal(PlcValue.FromUInt16(42), value);
    }

    [Fact]
    public async Task ReadCoil_ReturnsBool()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x01, 0x01, 0x01));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var value = await driver.ReadAsync(driver.AddressParser.Parse("00001"));

        Assert.Equal(PlcValue.FromBool(true), value);
    }

    [Fact]
    public async Task ReadFloat_UsesTwoRegisters()
    {
        var transport = new MockTransport();
        var bytes = PlcValueCodec.ToBytes(PlcValue.FromFloat(1.5f));
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x03, 0x04, bytes[0], bytes[1], bytes[2], bytes[3]));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var value = await driver.ReadAsync(driver.AddressParser.Parse("HR10", PlcDataType.Float));

        Assert.Equal(1.5f, value.AsFloat());
        Assert.Equal(new byte[] { 0x00, 0x0A, 0x00, 0x02 }, transport.Writes[0][8..12]);
    }

    [Fact]
    public async Task ReadString_DecodesRegisters()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x03, 0x04, 0x41, 0x42, 0x00, 0x00));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var value = await driver.ReadAsync(
            driver.AddressParser.Parse("HR20", PlcDataType.String).WithLength(4));

        Assert.Equal("AB", value.AsString());
    }

    [Fact]
    public async Task ReadRaw_ReturnsBytes()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x03, 0x04, 0x01, 0x02, 0x03, 0x00));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var value = await driver.ReadAsync(
            driver.AddressParser.Parse("HR30", PlcDataType.Raw).WithLength(3));

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, value.AsBytes());
    }

    [Fact]
    public async Task WriteHoldingRegister_SendsFunctionCode06()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x06, 0x00, 0x01, 0x00, 0x07));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        await driver.WriteAsync(driver.AddressParser.Parse("40002"), PlcValue.FromUInt16(7));

        Assert.Equal(new byte[] { 0x06, 0x00, 0x01, 0x00, 0x07 }, transport.Writes[0][7..]);
    }

    [Fact]
    public async Task WriteCoil_SendsFunctionCode05()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x05, 0x00, 0x03, 0xFF, 0x00));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        await driver.WriteAsync(driver.AddressParser.Parse("C3"), PlcValue.FromBool(true));

        Assert.Equal(new byte[] { 0x05, 0x00, 0x03, 0xFF, 0x00 }, transport.Writes[0][7..]);
    }

    [Fact]
    public async Task WriteToInputRegister_IsRejected()
    {
        var transport = new MockTransport();
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => driver.WriteAsync(driver.AddressParser.Parse("30001"), PlcValue.FromUInt16(1)));
    }

    [Fact]
    public async Task ReadBatch_MergesContiguousRegistersIntoOneRequest()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x03, 0x04, 0x00, 0x0A, 0x00, 0x0B));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var values = await driver.ReadBatchAsync(new[]
        {
            driver.AddressParser.Parse("HR0"),
            driver.AddressParser.Parse("HR1"),
        });

        Assert.Equal(new[] { PlcValue.FromUInt16(10), PlcValue.FromUInt16(11) }, values);
        Assert.Single(transport.Writes);
    }

    [Fact]
    public async Task ReadBatch_KeepsRequestOrderAcrossRegions()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x01, 0x01, 0x01));
        transport.EnqueueResponse(ProtocolFrames.Mbap(2, 1, 0x03, 0x02, 0x00, 0x05));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var values = await driver.ReadBatchAsync(new[]
        {
            driver.AddressParser.Parse("C0"),
            driver.AddressParser.Parse("HR0"),
        });

        Assert.Equal(PlcValue.FromBool(true), values[0]);
        Assert.Equal(PlcValue.FromUInt16(5), values[1]);
    }

    [Fact]
    public async Task Factory_CreatesTcpDriverByDefault()
    {
        var factory = new ModbusDriverFactory();
        await using var driver = (ModbusDriver)factory.Create(new DriverConfiguration
        {
            Host = "192.168.0.10",
            Transport = TransportKind.Tcp,
        });

        Assert.Equal("modbus", driver.Descriptor.Id);
        Assert.Equal(502, driver.Descriptor.DefaultPort);
        Assert.Equal(ModbusAddressParser.HoldingRegisterRegion, driver.AddressParser.Parse("40001").Region);
    }

    [Fact]
    public async Task Factory_CreatesRtuDriverForSerialTransport()
    {
        var factory = new ModbusDriverFactory();
        await using var driver = (ModbusDriver)factory.Create(new DriverConfiguration
        {
            Transport = TransportKind.Serial,
            Serial = { PortName = "COM_TEST", BaudRate = 19200 },
        });

        Assert.Equal(TransportKind.Tcp | TransportKind.Serial, driver.Descriptor.Transports);
    }

    [Fact]
    public async Task WriteBatch_MergesContiguousRegistersIntoOneRequest()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x10, 0x00, 0x00, 0x00, 0x03));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        await driver.WriteBatchAsync(new[]
        {
            new PlcWriteRequest(driver.AddressParser.Parse("HR0"), PlcValue.FromUInt16(10)),
            new PlcWriteRequest(driver.AddressParser.Parse("HR1"), PlcValue.FromUInt16(11)),
            new PlcWriteRequest(driver.AddressParser.Parse("HR2"), PlcValue.FromUInt16(12)),
        });

        Assert.Single(transport.Writes);
        Assert.Equal(
            new byte[] { 0x10, 0x00, 0x00, 0x00, 0x03, 0x06, 0x00, 0x0A, 0x00, 0x0B, 0x00, 0x0C },
            transport.Writes[0][7..]);
    }

    [Fact]
    public async Task WriteBatch_SplitsNonContiguousRegisters()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x06, 0x00, 0x00, 0x00, 0x01));
        transport.EnqueueResponse(ProtocolFrames.Mbap(2, 1, 0x06, 0x00, 0x05, 0x00, 0x02));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        await driver.WriteBatchAsync(new[]
        {
            new PlcWriteRequest(driver.AddressParser.Parse("HR0"), PlcValue.FromUInt16(1)),
            new PlcWriteRequest(driver.AddressParser.Parse("HR5"), PlcValue.FromUInt16(2)),
        });

        Assert.Equal(2, transport.Writes.Count);
        Assert.Equal(new byte[] { 0x06, 0x00, 0x05, 0x00, 0x02 }, transport.Writes[1][7..]);
    }

    [Fact]
    public async Task WriteBatch_MergesContiguousCoils()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x0F, 0x00, 0x00, 0x00, 0x03));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        await driver.WriteBatchAsync(new[]
        {
            new PlcWriteRequest(driver.AddressParser.Parse("C0"), PlcValue.FromBool(true)),
            new PlcWriteRequest(driver.AddressParser.Parse("C1"), PlcValue.FromBool(false)),
            new PlcWriteRequest(driver.AddressParser.Parse("C2"), PlcValue.FromBool(true)),
        });

        Assert.Single(transport.Writes);
        Assert.Equal(
            new byte[] { 0x0F, 0x00, 0x00, 0x00, 0x03, 0x01, 0x05 },
            transport.Writes[0][7..]);
    }

    [Fact]
    public async Task WriteBatch_RejectsReadOnlyRegion()
    {
        var transport = new MockTransport();
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => driver.WriteBatchAsync(new[]
            {
                new PlcWriteRequest(driver.AddressParser.Parse("IR0"), PlcValue.FromUInt16(1)),
            }));
    }

    [Fact]
    public void Factory_WithBroadcastUnitId_CreatesBroadcastDriver()
    {
        var configuration = new DriverConfiguration { Transport = TransportKind.Tcp };
        configuration.Options["unitId"] = "0";

        var driver = Assert.IsType<ModbusDriver>(new ModbusDriverFactory().Create(configuration));

        Assert.True(driver.IsBroadcast);
    }

    [Fact]
    public async Task BroadcastWrite_SendsUnitIdZeroWithoutWaitingForResponse()
    {
        var transport = new MockTransport();
        await using var driver = CreateDriver(transport, unitId: 0);
        await driver.ConnectAsync();

        await driver.WriteAsync(driver.AddressParser.Parse("HR0"), PlcValue.FromUInt16(5));

        Assert.True(driver.IsBroadcast);
        Assert.Single(transport.Writes);
        Assert.Equal(0, transport.Writes[0][6]);
        Assert.Equal(new byte[] { 0x06, 0x00, 0x00, 0x00, 0x05 }, transport.Writes[0][7..]);
    }

    [Fact]
    public async Task BroadcastRead_IsRejected()
    {
        var transport = new MockTransport();
        await using var driver = CreateDriver(transport, unitId: 0);
        await driver.ConnectAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => driver.ReadAsync(driver.AddressParser.Parse("HR0")));
    }

    [Fact]
    public async Task ReadWriteRegisters_UsesFunctionCode23()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x17, 0x04, 0x00, 0x0A, 0x00, 0x0B));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var registers = await driver.ReadWriteRegistersAsync(
            driver.AddressParser.Parse("HR10"),
            new ushort[] { 1, 2 },
            driver.AddressParser.Parse("HR0"),
            readQuantity: 2);

        Assert.Equal(new ushort[] { 10, 11 }, registers);
        Assert.Equal(
            new byte[]
            {
                0x17,
                0x00, 0x00, 0x00, 0x02,
                0x00, 0x0A, 0x00, 0x02, 0x04,
                0x00, 0x01, 0x00, 0x02,
            },
            transport.Writes[0][7..]);
        Assert.Equal(1, driver.Metrics.Snapshot().Operations.ReadWrite);
    }

    [Fact]
    public async Task ReadWriteRegisters_RejectsNonHoldingRegion()
    {
        var transport = new MockTransport();
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => driver.ReadWriteRegistersAsync(
                driver.AddressParser.Parse("C0"),
                new ushort[] { 1 },
                driver.AddressParser.Parse("HR0"),
                readQuantity: 1));
    }

    [Fact]
    public async Task ReadNumericArray_ReturnsElements()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(
            1, 1, 0x03, 0x06, 0x00, 0x0A, 0x00, 0x14, 0x00, 0x1E));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var value = await driver.ReadAsync(
            driver.AddressParser.Parse("HR0", PlcDataType.UInt16).WithLength(3));

        Assert.True(value.IsArray);
        Assert.Equal(new ushort[] { 10, 20, 30 }, value.AsUInt16Array());
    }

    [Fact]
    public async Task WriteNumericArray_UsesMultipleRegisterRequest()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x10, 0x00, 0x00, 0x00, 0x02));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        await driver.WriteAsync(
            driver.AddressParser.Parse("HR0", PlcDataType.UInt16).WithLength(2),
            PlcValue.FromUInt16Array(new ushort[] { 0x1234, 0x5678 }));

        var frame = transport.Writes[0];
        Assert.Equal(0x10, frame[7]);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, frame[13..17]);
    }

    [Fact]
    public async Task WriteArray_WithWrongElementCount_IsRejected()
    {
        var transport = new MockTransport();
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => driver.WriteAsync(
                driver.AddressParser.Parse("HR0", PlcDataType.UInt16).WithLength(2),
                PlcValue.FromUInt16Array(new ushort[] { 1, 2, 3 })));
    }

    private static ModbusDriver CreateDriver(MockTransport transport, byte unitId = 1)
    {
        var client = new ModbusTcpClient(transport, new ModbusTcpOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.Zero,
        });

        return new ModbusDriver(client, new DriverConfiguration
        {
            Timeout = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.Zero,
        }, unitId);
    }
}
