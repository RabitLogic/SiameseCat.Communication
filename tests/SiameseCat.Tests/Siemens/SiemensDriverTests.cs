using SiameseCat.Core;
using SiameseCat.Drivers.Siemens;
using SiameseCat.Protocols.S7;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Siemens;

public class SiemensAddressParserTests
{
    private readonly SiemensAddressParser _parser = new();

    [Theory]
    [InlineData("DB1.DBX0.0", "DB1", 0, PlcDataType.Bool, 0)]
    [InlineData("DB1.DBX12.7", "DB1", 12, PlcDataType.Bool, 7)]
    [InlineData("DB5.DBB10", "DB5", 10, PlcDataType.Byte, null)]
    [InlineData("DB5.DBW12", "DB5", 12, PlcDataType.UInt16, null)]
    [InlineData("DB5.DBD14", "DB5", 14, PlcDataType.UInt32, null)]
    [InlineData("M0.0", "M", 0, PlcDataType.Bool, 0)]
    [InlineData("MW6", "M", 6, PlcDataType.UInt16, null)]
    [InlineData("I0.1", "I", 0, PlcDataType.Bool, 1)]
    [InlineData("IW4", "I", 4, PlcDataType.UInt16, null)]
    [InlineData("Q0.2", "Q", 0, PlcDataType.Bool, 2)]
    [InlineData("QD8", "Q", 8, PlcDataType.UInt32, null)]
    public void Parse_MapsNativeAddress(
        string text,
        string region,
        int offset,
        PlcDataType dataType,
        int? bitOffset)
    {
        var address = _parser.Parse(text);

        Assert.Equal(region, address.Region);
        Assert.Equal(offset, address.Offset);
        Assert.Equal(dataType, address.DataType);
        Assert.Equal(bitOffset is null ? null : (byte)bitOffset, address.BitOffset);
    }

    [Fact]
    public void Parse_OverridesDataType()
    {
        var address = _parser.Parse("DB1.DBB0", PlcDataType.Int32);

        Assert.Equal(PlcDataType.Int32, address.DataType);
        Assert.Equal(0, address.Offset);
    }

    [Fact]
    public void ToS7Address_RoundTrips()
    {
        var native = S7Address.Parse("DB1.DBX2.3");
        var neutral = _parser.Parse("DB1.DBX2.3");

        Assert.Equal(native, SiemensAddressParser.ToS7Address(neutral));
    }

    [Fact]
    public void Parse_RejectsInvalidText()
    {
        Assert.ThrowsAny<Exception>(() => _parser.Parse("not-an-address"));
    }
}

public class SiemensDriverTests
{
    private static readonly bool[] ThreeBits = { true, false, true };

    private static readonly bool[] OneBit = { true };
    [Fact]
    public async Task ReadUInt16_ReturnsValue()
    {
        var transport = CreateConnectedTransport();
        transport.EnqueueResponse(ProtocolFrames.S7Response(
            2,
            new byte[] { 0x04, 0x01 },
            ProtocolFrames.DataItem(new byte[] { 0x00, 0x2A })));
        await using var driver = await CreateConnectedDriverAsync(transport);

        var value = await driver.ReadAsync(driver.AddressParser.Parse("DB1.DBW0"));

        Assert.Equal(PlcValue.FromUInt16(42), value);
    }

    [Fact]
    public async Task ReadBool_ReturnsValue()
    {
        var transport = CreateConnectedTransport();
        transport.EnqueueResponse(ProtocolFrames.S7Response(
            2,
            new byte[] { 0x04, 0x01 },
            new byte[] { 0xFF, 0x03, 0x00, 0x01, 0x01 }));
        await using var driver = await CreateConnectedDriverAsync(transport);

        var value = await driver.ReadAsync(driver.AddressParser.Parse("M0.0"));

        Assert.True(value.AsBool());
    }

    [Fact]
    public async Task ReadString_UsesS7Layout()
    {
        var transport = CreateConnectedTransport();
        transport.EnqueueResponse(ProtocolFrames.S7Response(
            2,
            new byte[] { 0x04, 0x01 },
            ProtocolFrames.DataItem(new byte[] { 4, 2, (byte)'A', (byte)'B', 0, 0 })));
        await using var driver = await CreateConnectedDriverAsync(transport);

        var value = await driver.ReadAsync(driver.AddressParser.Parse("DB2.DBB0", PlcDataType.String).WithLength(4));

        Assert.Equal("AB", value.AsString());
    }

    [Fact]
    public async Task ReadBatch_UsesSingleMultiVariableRequest()
    {
        var transport = CreateConnectedTransport();
        var data = ProtocolFrames.DataItem(new byte[] { 0x00, 0x0A })
            .Concat(ProtocolFrames.DataItem(new byte[] { 0x00, 0x00, 0x00, 0x2A }))
            .ToArray();
        transport.EnqueueResponse(ProtocolFrames.S7Response(2, new byte[] { 0x04, 0x02 }, data));
        await using var driver = await CreateConnectedDriverAsync(transport);

        var values = await driver.ReadBatchAsync(new[]
        {
            driver.AddressParser.Parse("DB1.DBW0"),
            driver.AddressParser.Parse("DB1.DBD2", PlcDataType.Int32),
        });

        Assert.Equal(PlcValue.FromUInt16(10), values[0]);
        Assert.Equal(PlcValue.FromInt32(42), values[1]);
        Assert.Equal(3, transport.Writes.Count);
    }

    [Fact]
    public async Task WriteUInt16_SendsBigEndianBytes()
    {
        var transport = CreateConnectedTransport();
        transport.EnqueueResponse(ProtocolFrames.S7Response(
            2,
            new byte[] { 0x05, 0x01 },
            new byte[] { 0xFF }));
        await using var driver = await CreateConnectedDriverAsync(transport);

        await driver.WriteAsync(driver.AddressParser.Parse("DB1.DBW0"), PlcValue.FromUInt16(0x1234));

        Assert.Equal(new byte[] { 0x12, 0x34 }, transport.Writes[2][35..37]);
    }

    [Fact]
    public async Task WriteWithWrongSize_IsRejected()
    {
        var transport = CreateConnectedTransport();
        await using var driver = await CreateConnectedDriverAsync(transport);

        await Assert.ThrowsAsync<ArgumentException>(
            () => driver.WriteAsync(driver.AddressParser.Parse("DB1.DBW0"), PlcValue.FromInt32(1)));
    }

    [Fact]
    public async Task Factory_RejectsNonTcpTransport()
    {
        var factory = new SiemensDriverFactory();
        await using var driver = factory.Create(new DriverConfiguration { Transport = TransportKind.Tcp, Host = "127.0.0.1" });

        Assert.Equal("siemens", driver.Descriptor.Id);
        Assert.Equal(102, driver.Descriptor.DefaultPort);

        Assert.Throws<NotSupportedException>(
            () => factory.Create(new DriverConfiguration { Transport = TransportKind.Serial }));
    }

    [Fact]
    public async Task WriteBatch_UsesOneMultiWriteRequest()
    {
        var transport = CreateConnectedTransport();
        transport.EnqueueResponse(ProtocolFrames.S7Response(
            2,
            new byte[] { 0x05, 0x02 },
            new byte[] { 0xFF, 0xFF }));
        await using var driver = await CreateConnectedDriverAsync(transport);

        await driver.WriteBatchAsync(new[]
        {
            new PlcWriteRequest(driver.AddressParser.Parse("DB1.DBW0"), PlcValue.FromUInt16(1)),
            new PlcWriteRequest(driver.AddressParser.Parse("DB1.DBW2"), PlcValue.FromUInt16(2)),
        });

        Assert.Equal(3, transport.Writes.Count);
        Assert.Equal(new byte[] { 0x00, 0x02 }, transport.Writes[2][^2..]);
    }

    [Fact]
    public async Task WriteBatch_FallsBackToSingleWritesForStrings()
    {
        var transport = CreateConnectedTransport();
        transport.EnqueueResponse(ProtocolFrames.S7Response(2, new byte[] { 0x05, 0x01 }, new byte[] { 0xFF }));
        await using var driver = await CreateConnectedDriverAsync(transport);

        await driver.WriteBatchAsync(new[]
        {
            new PlcWriteRequest(
                driver.AddressParser.Parse("DB3.DBB0", PlcDataType.String).WithLength(4),
                PlcValue.FromString("AB")),
        });

        Assert.Equal(3, transport.Writes.Count);
    }

    [Fact]
    public async Task WriteBits_SendsOneMultiWriteRequest()
    {
        var transport = CreateConnectedTransport();
        transport.EnqueueResponse(ProtocolFrames.S7Response(
            2,
            new byte[] { 0x05, 0x03 },
            new byte[] { 0xFF, 0xFF, 0xFF }));
        await using var client = new S7Client(transport, new S7Options
        {
            Host = "test-host",
            Timeout = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.Zero,
        });
        await client.ConnectAsync();

        await client.WriteBitsAsync(S7Address.Parse("M0.0"), ThreeBits);

        Assert.Equal(3, transport.Writes.Count);
        Assert.Equal(new byte[] { 0x00, 0x03, 0x00, 0x01, 0x01, 0x00 }, transport.Writes[2][^6..]);
    }

    [Fact]
    public async Task WriteBits_RejectsNonBoolAddress()
    {
        var transport = CreateConnectedTransport();
        await using var client = new S7Client(transport, new S7Options
        {
            Host = "test-host",
            Timeout = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.Zero,
        });
        await client.ConnectAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.WriteBitsAsync(S7Address.Parse("DB1.DBW0"), OneBit));
    }

    private static MockTransport CreateConnectedTransport()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.CrConfirm());
        transport.EnqueueResponse(ProtocolFrames.SetupAck(1));
        return transport;
    }

    private static async Task<SiemensDriver> CreateConnectedDriverAsync(MockTransport transport)
    {
        var client = new S7Client(transport, new S7Options
        {
            Host = "test-host",
            Timeout = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.Zero,
        });

        var driver = new SiemensDriver(client, new DriverConfiguration
        {
            Timeout = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.Zero,
        });

        await driver.ConnectAsync();
        return driver;
    }
}
