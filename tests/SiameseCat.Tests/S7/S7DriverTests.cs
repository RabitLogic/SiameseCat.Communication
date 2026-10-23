using SiameseCat.Communication;
using SiameseCat.Communication.S7;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.S7;

public class S7AddressParserTests
{
    private readonly S7AddressParser _parser = new();

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

        Assert.Equal(native, S7AddressParser.ToS7Address(neutral));
    }

    [Fact]
    public void Parse_RejectsInvalidText()
    {
        Assert.ThrowsAny<Exception>(() => _parser.Parse("not-an-address"));
    }
}

public class S7DriverTests
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
    public async Task ReadUInt16Array_ReturnsElements()
    {
        var transport = CreateConnectedTransport();
        transport.EnqueueResponse(ProtocolFrames.S7Response(
            2,
            new byte[] { 0x04, 0x01 },
            ProtocolFrames.DataItem(new byte[] { 0x00, 0x0A, 0x00, 0x14 })));
        await using var driver = await CreateConnectedDriverAsync(transport);

        var value = await driver.ReadAsync(
            driver.AddressParser.Parse("DB1.DBW0").WithLength(2));

        Assert.True(value.IsArray);
        Assert.Equal(new ushort[] { 10, 20 }, value.AsUInt16Array());
    }

    [Fact]
    public async Task WriteUInt16Array_SendsAllElements()
    {
        var transport = CreateConnectedTransport();
        transport.EnqueueResponse(ProtocolFrames.S7Response(
            2,
            new byte[] { 0x05, 0x01 },
            new byte[] { 0xFF }));
        await using var driver = await CreateConnectedDriverAsync(transport);

        await driver.WriteAsync(
            driver.AddressParser.Parse("DB1.DBW0").WithLength(2),
            PlcValue.FromUInt16Array(new ushort[] { 0x1234, 0x5678 }));

        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, transport.Writes[2][35..39]);
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
        var factory = new S7DriverFactory();
        await using var driver = factory.Create(new DriverConfiguration { Transport = TransportKind.Tcp, Host = "127.0.0.1" });

        Assert.Equal("s7", driver.Descriptor.Id);
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
    public async Task WriteBatch_WithOnlyString_UsesOneRequest()
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
        Assert.Equal(new byte[] { 4, 2, (byte)'A', (byte)'B' }, transport.Writes[2][35..39]);
    }

    [Fact]
    public async Task WriteBatch_SendsStringsInTheMultiWriteRequest()
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
            new PlcWriteRequest(
                driver.AddressParser.Parse("DB3.DBB0", PlcDataType.String).WithLength(4),
                PlcValue.FromString("AB")),
        });

        Assert.Equal(3, transport.Writes.Count);
        var frame = transport.Writes[2];
        Assert.Contains("AB", System.Text.Encoding.Latin1.GetString(frame), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadBatch_DecodesStringsInTheMultiReadResponse()
    {
        var transport = CreateConnectedTransport();
        var data = ProtocolFrames.DataItem(new byte[] { 0x00, 0x2A })
            .Concat(ProtocolFrames.DataItem(new byte[] { 4, 2, (byte)'A', (byte)'B', 0, 0 }))
            .ToArray();
        transport.EnqueueResponse(ProtocolFrames.S7Response(2, new byte[] { 0x04, 0x02 }, data));
        await using var driver = await CreateConnectedDriverAsync(transport);

        var values = await driver.ReadBatchAsync(new[]
        {
            driver.AddressParser.Parse("DB1.DBW0"),
            driver.AddressParser.Parse("DB3.DBB0", PlcDataType.String).WithLength(4),
        });

        Assert.Equal(PlcValue.FromUInt16(42), values[0]);
        Assert.Equal("AB", values[1].AsString());
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

    private static async Task<S7Driver> CreateConnectedDriverAsync(MockTransport transport)
    {
        var client = new S7Client(transport, new S7Options
        {
            Host = "test-host",
            Timeout = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.Zero,
        });

        var driver = new S7Driver(client, new DriverConfiguration
        {
            Timeout = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.Zero,
        });

        await driver.ConnectAsync();
        return driver;
    }
}
