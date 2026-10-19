using SiameseCat.Core;
using SiameseCat.Drivers.Inovance;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Inovance;

public class InovanceAddressParserTests
{
    private readonly InovanceAddressParser _parser = new();

    [Theory]
    [InlineData("D100", ModbusAddressParser.HoldingRegisterRegion, 100, PlcDataType.UInt16)]
    [InlineData("d0", ModbusAddressParser.HoldingRegisterRegion, 0, PlcDataType.UInt16)]
    [InlineData("M10", ModbusAddressParser.CoilRegion, 10, PlcDataType.Bool)]
    [InlineData("X17", ModbusAddressParser.DiscreteInputRegion, 15, PlcDataType.Bool)]
    [InlineData("Y10", ModbusAddressParser.CoilRegion, 8200, PlcDataType.Bool)]
    [InlineData("y0", ModbusAddressParser.CoilRegion, 8192, PlcDataType.Bool)]
    public void Parse_MapsSoftElements(string text, string region, int offset, PlcDataType dataType)
    {
        var address = _parser.Parse(text);

        Assert.Equal(region, address.Region);
        Assert.Equal(offset, address.Offset);
        Assert.Equal(dataType, address.DataType);
    }

    [Fact]
    public void Parse_OverridesDataType()
    {
        var address = _parser.Parse("D200", PlcDataType.Float);

        Assert.Equal(ModbusAddressParser.HoldingRegisterRegion, address.Region);
        Assert.Equal(200, address.Offset);
        Assert.Equal(PlcDataType.Float, address.DataType);
    }

    [Theory]
    [InlineData("40001", ModbusAddressParser.HoldingRegisterRegion, 0)]
    [InlineData("HR10", ModbusAddressParser.HoldingRegisterRegion, 10)]
    [InlineData("IR2", ModbusAddressParser.InputRegisterRegion, 2)]
    [InlineData("C3", ModbusAddressParser.CoilRegion, 3)]
    public void Parse_AcceptsPlainModbusAddresses(string text, string region, int offset)
    {
        var address = _parser.Parse(text);

        Assert.Equal(region, address.Region);
        Assert.Equal(offset, address.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Z10")]
    [InlineData("D-1")]
    [InlineData("DX1")]
    public void Parse_RejectsInvalidAddress(string text)
    {
        Assert.ThrowsAny<Exception>(() => _parser.Parse(text));
    }

    [Fact]
    public void Mapping_OverridesAreaBases()
    {
        var parser = new InovanceAddressParser(new InovanceMapping
        {
            DBase = 1000,
            MBase = 100,
            XBase = 200,
            YBase = 300,
        });

        Assert.Equal(1001, parser.Parse("D1").Offset);
        Assert.Equal(101, parser.Parse("M1").Offset);
        Assert.Equal(208, parser.Parse("X10").Offset);
        Assert.Equal(308, parser.Parse("Y10").Offset);
    }

    [Fact]
    public void Mapping_ReadsBaseOptions()
    {
        var configuration = new DriverConfiguration();
        configuration.Options["dBase"] = "10";
        configuration.Options["mBase"] = "20";
        configuration.Options["xBase"] = "30";
        configuration.Options["yBase"] = "40";

        var mapping = InovanceMapping.FromConfiguration(configuration);

        Assert.Equal(10, mapping.DBase);
        Assert.Equal(20, mapping.MBase);
        Assert.Equal(30, mapping.XBase);
        Assert.Equal(40, mapping.YBase);
    }

    [Fact]
    public void Mapping_RejectsNegativeBase()
    {
        var configuration = new DriverConfiguration();
        configuration.Options["dBase"] = "-1";

        Assert.Throws<ArgumentOutOfRangeException>(() => InovanceMapping.FromConfiguration(configuration));
    }

    [Fact]
    public void Mapping_RejectsAddressOverflow()
    {
        var parser = new InovanceAddressParser(new InovanceMapping { YBase = 65535 });

        Assert.Throws<FormatException>(() => parser.Parse("Y10"));
    }
}

public class InovanceDriverTests
{
    [Fact]
    public async Task ReadDataRegister_ReturnsUInt16()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x03, 0x02, 0x00, 0x2A));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var value = await driver.ReadAsync(driver.AddressParser.Parse("D100"));

        Assert.Equal(PlcValue.FromUInt16(42), value);
        Assert.Equal(new byte[] { 0x00, 0x64, 0x00, 0x01 }, transport.Writes[0][8..12]);
    }

    [Fact]
    public async Task ReadAuxiliaryRelay_ReadsCoil()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x01, 0x01, 0x01));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var value = await driver.ReadAsync(driver.AddressParser.Parse("M10"));

        Assert.True(value.AsBool());
        Assert.Equal(new byte[] { 0x00, 0x0A, 0x00, 0x01 }, transport.Writes[0][8..12]);
    }

    [Fact]
    public async Task ReadInput_ReadsDiscreteInputWithOctalNumber()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x02, 0x01, 0x01));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var value = await driver.ReadAsync(driver.AddressParser.Parse("X17"));

        Assert.True(value.AsBool());
        Assert.Equal(0x02, transport.Writes[0][7]);
        Assert.Equal(new byte[] { 0x00, 0x0F, 0x00, 0x01 }, transport.Writes[0][8..12]);
    }

    [Fact]
    public async Task WriteOutput_CoilUsesYBase()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x05, 0x20, 0x08, 0xFF, 0x00));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        await driver.WriteAsync(driver.AddressParser.Parse("Y10"), PlcValue.FromBool(true));

        Assert.Equal(new byte[] { 0x05, 0x20, 0x08, 0xFF, 0x00 }, transport.Writes[0][7..]);
    }

    [Fact]
    public async Task ReadBatch_MergesContiguousRegisters()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x03, 0x06, 0x00, 0x01, 0x00, 0x02, 0x00, 0x03));
        await using var driver = CreateDriver(transport);
        await driver.ConnectAsync();

        var values = await driver.ReadBatchAsync(new[]
        {
            driver.AddressParser.Parse("D0"),
            driver.AddressParser.Parse("D1"),
            driver.AddressParser.Parse("D2"),
        });

        Assert.Equal(
            new[] { PlcValue.FromUInt16(1), PlcValue.FromUInt16(2), PlcValue.FromUInt16(3) },
            values);
        Assert.Single(transport.Writes);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x03 }, transport.Writes[0][8..12]);
    }

    [Fact]
    public async Task Factory_CreatesTcpDriver()
    {
        var factory = new InovanceDriverFactory();
        await using var driver = factory.Create(new DriverConfiguration
        {
            Host = "192.168.0.10",
            Transport = TransportKind.Tcp,
        });

        Assert.Equal("inovance", driver.Descriptor.Id);
        Assert.Equal(502, driver.Descriptor.DefaultPort);
        Assert.Equal(ModbusAddressParser.CoilRegion, driver.AddressParser.Parse("M1").Region);
    }

    [Fact]
    public async Task Factory_CreatesRtuDriverForSerialTransport()
    {
        var factory = new InovanceDriverFactory();
        await using var driver = factory.Create(new DriverConfiguration
        {
            Transport = TransportKind.Serial,
            Serial = { PortName = "COM_TEST", BaudRate = 19200 },
        });

        Assert.Equal(TransportKind.Tcp | TransportKind.Serial, driver.Descriptor.Transports);
    }

    private static InovanceDriver CreateDriver(MockTransport transport, InovanceMapping? mapping = null)
    {
        var client = new ModbusTcpClient(transport, new ModbusTcpOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.Zero,
        });

        return new InovanceDriver(
            client,
            new DriverConfiguration
            {
                Timeout = TimeSpan.FromSeconds(1),
                RetryDelay = TimeSpan.Zero,
            },
            unitId: 1,
            mapping);
    }
}
