using SiameseCat.Communication;
using SiameseCat.Communication.Modbus;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Modbus;

public class ModbusClientExtendedTests
{
    [Fact]
    public async Task ReadDeviceIdentification_ParsesBasicObjects()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(
            1,
            1,
            0x2B, 0x0E, 0x01, 0x01, 0x00, 0x00, 0x03,
            0x00, 0x0A, (byte)'S', (byte)'i', (byte)'a', (byte)'m', (byte)'e', (byte)'s', (byte)'e', (byte)'C', (byte)'a', (byte)'t',
            0x01, 0x03, (byte)'A', (byte)'B', (byte)'C',
            0x02, 0x03, (byte)'1', (byte)'.', (byte)'0'));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        var identification = await client.ReadDeviceIdentificationAsync();

        Assert.Equal("SiameseCat", identification.VendorName);
        Assert.Equal("ABC", identification.ProductCode);
        Assert.Equal("1.0", identification.MajorMinorRevision);
        Assert.Equal((byte)0x01, identification.ConformityLevel);
        Assert.False(identification.MoreFollows);
        Assert.Equal(3, identification.Objects.Count);
        Assert.Equal(
            new byte[] { 0x2B, 0x0E, 0x01, 0x00 },
            transport.Writes[0].AsSpan(7).ToArray());
    }

    [Fact]
    public async Task ReadDeviceIdentification_FollowsContinuationPages()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(
            1,
            1,
            0x2B, 0x0E, 0x02, 0x02, 0xFF, 0x04, 0x02,
            0x03, 0x04, (byte)'h', (byte)'t', (byte)'t', (byte)'p',
            0x04, 0x03, (byte)'D', (byte)'E', (byte)'V'));
        transport.EnqueueResponse(Mbap(
            2,
            1,
            0x2B, 0x0E, 0x02, 0x02, 0x00, 0x00, 0x01,
            0x05, 0x03, (byte)'M', (byte)'1', (byte)'0'));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        var identification = await client.ReadDeviceIdentificationAsync(1, ModbusDeviceIdCode.Regular);

        Assert.Equal("http", identification.GetObjectText(0x03));
        Assert.Equal("DEV", identification.GetObjectText(0x04));
        Assert.Equal("M10", identification.GetObjectText(0x05));
        Assert.False(identification.MoreFollows);
        Assert.Equal(2, transport.Writes.Count);
        Assert.Equal(
            new byte[] { 0x2B, 0x0E, 0x02, 0x00 },
            transport.Writes[0].AsSpan(7).ToArray());
        Assert.Equal(
            new byte[] { 0x2B, 0x0E, 0x02, 0x04 },
            transport.Writes[1].AsSpan(7).ToArray());
    }

    [Fact]
    public async Task ReadDeviceIdentification_StopsWhenContinuationIsNotRequested()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(
            1,
            1,
            0x2B, 0x0E, 0x03, 0x03, 0xFF, 0x80, 0x01,
            0x00, 0x03, (byte)'A', (byte)'B', (byte)'C'));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        var identification = await client.ReadDeviceIdentificationAsync(
            1, ModbusDeviceIdCode.Extended, 0, followMore: false);

        Assert.True(identification.MoreFollows);
        Assert.Equal((byte)0x80, identification.NextObjectId);
        Assert.Single(transport.Writes);
    }

    [Fact]
    public async Task ReadDeviceIdentification_IndividualReturnsSingleObject()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(
            1,
            1,
            0x2B, 0x0E, 0x04, 0x83, 0x00, 0x00, 0x01,
            0x05, 0x03, (byte)'M', (byte)'1', (byte)'0'));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        var identification = await client.ReadDeviceIdentificationAsync(1, ModbusDeviceIdCode.Individual, 0x05);

        Assert.Equal("M10", identification.GetObjectText(0x05));
        Assert.Equal((byte)0x83, identification.ConformityLevel);
        Assert.Single(transport.Writes);
    }

    [Fact]
    public async Task ReadDeviceIdentification_TruncatedResponseThrows()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(1, 1, 0x2B, 0x0E, 0x01, 0x01, 0x00, 0x00, 0x01));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        await Assert.ThrowsAsync<ProtocolException>(() => client.ReadDeviceIdentificationAsync());
    }

    [Fact]
    public async Task ReadDeviceIdentification_ExceptionResponseThrows()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(1, 1, 0xAB, 0x02));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        var exception = await Assert.ThrowsAsync<ModbusException>(() => client.ReadDeviceIdentificationAsync());

        Assert.Equal(2, exception.ExceptionCode);
    }

    [Fact]
    public async Task ReadDeviceIdentification_UndefinedAccessTypeIsRejected()
    {
        var transport = new MockTransport();
        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.ReadDeviceIdentificationAsync(1, (ModbusDeviceIdCode)0x09));
    }

    [Fact]
    public async Task ReadFileRecords_BuildsRequestAndParsesRecords()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(
            1,
            1,
            0x14, 0x0C,
            0x05, 0x06, 0x12, 0x34, 0x56, 0x78,
            0x05, 0x06, 0x9A, 0xBC, 0xDE, 0xF0));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        var records = await client.ReadFileRecordsAsync(new[]
        {
            new ModbusFileRecordRequest(4, 0, 2),
            new ModbusFileRecordRequest(4, 1, 2),
        });

        Assert.Equal(new ushort[] { 0x1234, 0x5678 }, records[0]);
        Assert.Equal(new ushort[] { 0x9ABC, 0xDEF0 }, records[1]);
        Assert.Equal(
            new byte[]
            {
                0x14, 0x0E,
                0x06, 0x00, 0x04, 0x00, 0x00, 0x00, 0x02,
                0x06, 0x00, 0x04, 0x00, 0x01, 0x00, 0x02,
            },
            transport.Writes[0].AsSpan(7).ToArray());
    }

    [Fact]
    public async Task ReadFileRecords_ShortRecordThrows()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(1, 1, 0x14, 0x04, 0x03, 0x06, 0x12, 0x34));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        await Assert.ThrowsAsync<ProtocolException>(
            () => client.ReadFileRecordsAsync(new[] { new ModbusFileRecordRequest(4, 0, 2) }));
    }

    [Fact]
    public async Task ReadFileRecords_InvalidArgumentsAreRejected()
    {
        var transport = new MockTransport();
        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.ReadFileRecordsAsync(Array.Empty<ModbusFileRecordRequest>()));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.ReadFileRecordsAsync(new[] { new ModbusFileRecordRequest(4, 0, 0) }));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.ReadFileRecordsAsync(new[] { new ModbusFileRecordRequest(0, 0, 1) }));

        var many = new ModbusFileRecordRequest[36];
        for (var i = 0; i < many.Length; i++)
        {
            many[i] = new ModbusFileRecordRequest(4, (ushort)i, 1);
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ReadFileRecordsAsync(many));
    }

    [Fact]
    public async Task WriteFileRecords_SendsRequestAndAcceptsEcho()
    {
        var transport = new MockTransport();
        byte[] pdu = { 0x15, 0x0B, 0x06, 0x00, 0x04, 0x00, 0x00, 0x00, 0x02, 0x12, 0x34, 0x56, 0x78 };
        transport.EnqueueResponse(Mbap(1, 1, pdu));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        await client.WriteFileRecordsAsync(new[]
        {
            new ModbusFileRecord(4, 0, new ushort[] { 0x1234, 0x5678 }),
        });

        Assert.Equal(pdu, transport.Writes[0].AsSpan(7).ToArray());
    }

    [Fact]
    public async Task WriteFileRecords_ResponseThatDoesNotEchoThrows()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(
            1,
            1,
            0x15, 0x0B, 0x06, 0x00, 0x04, 0x00, 0x00, 0x00, 0x02, 0x12, 0x34, 0x56, 0x79));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        await Assert.ThrowsAsync<ProtocolException>(() => client.WriteFileRecordsAsync(new[]
        {
            new ModbusFileRecord(4, 0, new ushort[] { 0x1234, 0x5678 }),
        }));
    }

    [Fact]
    public async Task WriteFileRecords_BroadcastDoesNotWaitForResponse()
    {
        var transport = new MockTransport();

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        await client.WriteFileRecordsAsync(
            0,
            new[] { new ModbusFileRecord(4, 0, new ushort[] { 1 }) });

        Assert.Single(transport.Writes);
        Assert.True(client.IsConnected);
    }

    private static ModbusTcpOptions CreateOptions() => new()
    {
        UnitId = 1,
        Timeout = TimeSpan.FromSeconds(1),
        RetryDelay = TimeSpan.Zero,
    };

    private static byte[] Mbap(ushort transactionId, byte unitId, params byte[] pdu)
    {
        var frame = new byte[7 + pdu.Length];
        frame[0] = (byte)(transactionId >> 8);
        frame[1] = (byte)transactionId;
        frame[2] = 0;
        frame[3] = 0;
        var length = (ushort)(pdu.Length + 1);
        frame[4] = (byte)(length >> 8);
        frame[5] = (byte)length;
        frame[6] = unitId;
        pdu.CopyTo(frame, 7);
        return frame;
    }
}
