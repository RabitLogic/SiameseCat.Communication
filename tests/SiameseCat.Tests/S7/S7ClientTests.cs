using SiameseCat.Communication;
using SiameseCat.Communication.S7;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.S7;

public class S7ClientTests
{
    [Fact]
    public async Task ConnectAndReadUInt16_UsesExpectedFrames()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(CrConfirm());
        transport.EnqueueResponse(SetupAck(1));
        transport.EnqueueResponse(BuildS7Response(
            2,
            new byte[] { 0x04, 0x01 },
            DataItem(new byte[] { 0x00, 0x0A })));

        await using var client = new S7Client(transport, CreateOptions());
        await client.ConnectAsync();

        var value = await client.ReadUInt16Async(S7Address.Parse("DB1.DBW0"));

        Assert.Equal((ushort)10, value);
        Assert.Equal(960, client.PduSize);
        Assert.Equal(3, transport.Writes.Count);

        var setup = transport.Writes[1];
        Assert.Equal(25, setup.Length);
        Assert.Equal(0xF0, setup[17]);

        var read = transport.Writes[2];
        Assert.Equal(31, read.Length);
        Assert.Equal(0x04, read[17]);
        Assert.Equal(0x01, read[18]);
        Assert.Equal(0x02, read[22]);
        Assert.Equal(0x00, read[25]);
        Assert.Equal(0x01, read[26]);
        Assert.Equal(0x84, read[27]);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00 }, read[28..31]);
    }

    [Fact]
    public async Task WriteUInt16_SendsBigEndianPayload()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(CrConfirm());
        transport.EnqueueResponse(SetupAck(1));
        transport.EnqueueResponse(BuildS7Response(
            2,
            new byte[] { 0x05, 0x01 },
            new byte[] { 0xFF, 0x04, 0x00, 0x10 }));

        await using var client = new S7Client(transport, CreateOptions());
        await client.ConnectAsync();

        await client.WriteUInt16Async(S7Address.Parse("DB1.DBW0"), 0x1234);

        var write = transport.Writes[2];
        Assert.Equal(37, write.Length);
        Assert.Equal(0x05, write[17]);
        Assert.Equal(0x02, write[22]);
        Assert.Equal(0x84, write[27]);
        Assert.Equal(new byte[] { 0x00, 0x04, 0x00, 0x10, 0x12, 0x34 }, write[31..37]);
    }

    [Fact]
    public async Task ReadBool_UsesBitTransportSize()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(CrConfirm());
        transport.EnqueueResponse(SetupAck(1));
        transport.EnqueueResponse(BuildS7Response(
            2,
            new byte[] { 0x04, 0x01 },
            new byte[] { 0xFF, 0x03, 0x00, 0x01, 0x01 }));

        await using var client = new S7Client(transport, CreateOptions());
        await client.ConnectAsync();

        var value = await client.ReadBoolAsync(S7Address.Parse("DB1.DBX0.0"));

        Assert.True(value);
        var read = transport.Writes[2];
        Assert.Equal(0x01, read[22]);
        Assert.Equal(0x01, read[24]);
        Assert.Equal(0x84, read[27]);
    }

    [Fact]
    public async Task ReadMultiple_ReturnsAllItems()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(CrConfirm());
        transport.EnqueueResponse(SetupAck(1));

        var data = new byte[] { 0xFF, 0x04, 0x00, 0x10, 0x00, 0x0A }
            .Concat(new byte[] { 0xFF, 0x04, 0x00, 0x20, 0x12, 0x34, 0x56, 0x78 })
            .ToArray();
        transport.EnqueueResponse(BuildS7Response(2, new byte[] { 0x04, 0x02 }, data));

        await using var client = new S7Client(transport, CreateOptions());
        await client.ConnectAsync();

        var result = await client.ReadMultipleAsync(new[]
        {
            new S7ReadItem(S7Address.Parse("DB1.DBW0"), 2),
            new S7ReadItem(S7Address.Parse("DB1.DBD2"), 4),
        });

        Assert.Equal(new byte[] { 0x00, 0x0A }, result[0]);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, result[1]);
        Assert.Equal(0x02, transport.Writes[2][18]);
    }

    [Fact]
    public async Task ReadMultiple_ThrowsItemException()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(CrConfirm());
        transport.EnqueueResponse(SetupAck(1));
        transport.EnqueueResponse(BuildS7Response(
            2,
            new byte[] { 0x04, 0x01 },
            new byte[] { 0x0A, 0x04, 0x00, 0x10 }));

        await using var client = new S7Client(transport, CreateOptions());
        await client.ConnectAsync();

        var exception = await Assert.ThrowsAsync<S7ItemException>(
            () => client.ReadBytesAsync(S7Address.Parse("DB1.DBW0"), 2));

        Assert.Equal(0x0A, exception.ReturnCode);
        Assert.Contains("DB1.DBW0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Object does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LargeRead_IsSplitAcrossPduChunks()
    {
        var expected = Enumerable.Range(0, 300).Select(i => (byte)(i % 256)).ToArray();

        var transport = new MockTransport();
        transport.EnqueueResponse(CrConfirm());
        transport.EnqueueResponse(SetupAck(1, pduSize: 240));
        transport.EnqueueResponse(BuildS7Response(2, new byte[] { 0x04, 0x01 }, DataItem(expected[..208])));
        transport.EnqueueResponse(BuildS7Response(3, new byte[] { 0x04, 0x01 }, DataItem(expected[208..])));

        await using var client = new S7Client(transport, CreateOptions());
        await client.ConnectAsync();

        var actual = await client.ReadBytesAsync(S7Address.Parse("DB1.DBB0"), 300);

        Assert.Equal(240, client.PduSize);
        Assert.Equal(expected, actual);
        Assert.Equal(4, transport.Writes.Count);

        var secondRead = transport.Writes[3];
        Assert.Equal(0x00, secondRead[23]);
        Assert.Equal(0x5C, secondRead[24]);
        Assert.Equal(0x06, secondRead[29]);
        Assert.Equal(0x80, secondRead[30]);
    }

    [Fact]
    public async Task ConnectTimeout_ThrowsCommunicationTimeoutException()
    {
        var transport = new MockTransport { BlockReads = true };
        var options = CreateOptions();
        options.ConnectTimeout = TimeSpan.FromMilliseconds(50);

        await using var client = new S7Client(transport, options);

        await Assert.ThrowsAsync<CommunicationTimeoutException>(() => client.ConnectAsync());
        Assert.False(transport.IsOpen);
    }

    private static S7Options CreateOptions() => new()
    {
        Host = "test-host",
        ConnectTimeout = TimeSpan.FromSeconds(1),
        Timeout = TimeSpan.FromSeconds(1),
    };

    private static byte[] CrConfirm() =>
        Tpkt(0x11, 0xD0, 0x00, 0x01, 0x00, 0x00, 0x00);

    private static byte[] SetupAck(ushort pduReference, int pduSize = 960)
    {
        var parameter = new byte[]
        {
            0xF0, 0x00, 0x00, 0x00, 0x00, 0x01,
            (byte)(pduSize >> 8), (byte)pduSize,
        };

        return BuildS7Response(pduReference, parameter, Array.Empty<byte>());
    }

    private static byte[] BuildS7Response(ushort pduReference, byte[] parameter, byte[] data)
    {
        var payload = new byte[3 + 12 + parameter.Length + data.Length];
        payload[0] = 0x02;
        payload[1] = 0xF0;
        payload[2] = 0x80;
        payload[3] = 0x32;
        payload[4] = 0x03;
        payload[5] = 0x00;
        payload[6] = 0x00;
        payload[7] = (byte)(pduReference >> 8);
        payload[8] = (byte)pduReference;
        payload[9] = (byte)(parameter.Length >> 8);
        payload[10] = (byte)parameter.Length;
        payload[11] = (byte)(data.Length >> 8);
        payload[12] = (byte)data.Length;
        payload[13] = 0x00;
        payload[14] = 0x00;
        parameter.CopyTo(payload, 15);
        data.CopyTo(payload, 15 + parameter.Length);
        return Tpkt(payload);
    }

    private static byte[] DataItem(byte[] data)
    {
        var item = new byte[4 + data.Length];
        item[0] = 0xFF;
        item[1] = 0x04;
        var bitLength = data.Length * 8;
        item[2] = (byte)(bitLength >> 8);
        item[3] = (byte)bitLength;
        data.CopyTo(item, 4);
        return item;
    }

    private static byte[] Tpkt(params byte[] payload)
    {
        var frame = new byte[4 + payload.Length];
        frame[0] = 0x03;
        frame[1] = 0x00;
        frame[2] = (byte)(frame.Length >> 8);
        frame[3] = (byte)frame.Length;
        payload.CopyTo(frame, 4);
        return frame;
    }
}
