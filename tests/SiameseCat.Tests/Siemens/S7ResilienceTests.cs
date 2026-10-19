using SiameseCat.Communication;
using SiameseCat.Communication.S7;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Siemens;

public class S7ResilienceTests
{
    [Fact]
    public async Task Read_RetriesAfterTimeout_AndReconnects()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.CrConfirm());
        transport.EnqueueResponse(ProtocolFrames.SetupAck(1));
        transport.EnqueueResponse(ProtocolFrames.CrConfirm());
        transport.EnqueueResponse(ProtocolFrames.SetupAck(1));
        transport.EnqueueResponse(ProtocolFrames.S7Response(
            2,
            new byte[] { 0x04, 0x01 },
            ProtocolFrames.DataItem(new byte[] { 0x00, 0x2A })));

        var options = CreateOptions();
        options.Timeout = TimeSpan.FromMilliseconds(50);
        options.RetryCount = 1;

        await using var client = new S7Client(transport, options);
        await client.ConnectAsync();

        transport.TimeoutFailuresRemaining = 1;

        var value = await client.ReadUInt16Async(S7Address.Parse("DB1.DBW0"));

        Assert.Equal((ushort)42, value);
        Assert.Equal(2, transport.OpenCount);
        Assert.Equal(2, transport.CloseCount);
    }

    [Fact]
    public async Task Write_IsNotRetriedByDefault()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.CrConfirm());
        transport.EnqueueResponse(ProtocolFrames.SetupAck(1));

        var options = CreateOptions();
        options.Timeout = TimeSpan.FromMilliseconds(50);
        options.RetryCount = 1;

        await using var client = new S7Client(transport, options);
        await client.ConnectAsync();

        transport.TimeoutFailuresRemaining = 1;

        await Assert.ThrowsAsync<CommunicationTimeoutException>(
            () => client.WriteUInt16Async(S7Address.Parse("DB1.DBW0"), 1));

        Assert.Equal(3, transport.Writes.Count);
        Assert.Equal(1, transport.OpenCount);
    }

    [Fact]
    public async Task WriteMultiple_SendsAllItemsInOneRequest()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.CrConfirm());
        transport.EnqueueResponse(ProtocolFrames.SetupAck(1));
        transport.EnqueueResponse(ProtocolFrames.S7Response(
            2,
            new byte[] { 0x05, 0x02 },
            new byte[] { 0xFF, 0xFF }));

        await using var client = new S7Client(transport, CreateOptions());
        await client.ConnectAsync();

        await client.WriteMultipleAsync(new[]
        {
            new S7WriteItem(S7Address.Parse("DB1.DBW0"), new byte[] { 0x12, 0x34 }),
            new S7WriteItem(S7Address.Parse("DB1.DBX4.0"), new byte[] { 0x01 }),
        });

        var request = transport.Writes[2];
        Assert.Equal(17 + 26 + 12, request.Length);

        var parameter = request.AsSpan(17);
        Assert.Equal(0x05, parameter[0]);
        Assert.Equal(0x02, parameter[1]);

        var data = request.AsSpan(17 + 2 + 24);
        Assert.Equal(new byte[] { 0x00, 0x04, 0x00, 0x10, 0x12, 0x34 }, data[..6].ToArray());
        Assert.Equal(new byte[] { 0x00, 0x03, 0x00, 0x01, 0x01, 0x00 }, data[6..12].ToArray());
    }

    [Fact]
    public async Task DisposedClient_ThrowsObjectDisposedException()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.CrConfirm());
        transport.EnqueueResponse(ProtocolFrames.SetupAck(1));

        var client = new S7Client(transport, CreateOptions());
        await client.ConnectAsync();
        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.ReadBytesAsync(S7Address.Parse("DB1.DBB0"), 1));
    }

    [Fact]
    public async Task ReadBeyondAddressSpace_Throws()
    {
        var transport = new MockTransport();
        await using var client = new S7Client(transport, CreateOptions());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.ReadBytesAsync(S7Address.Parse("DB1.DBB2097150"), 10));
    }

    private static S7Options CreateOptions() => new()
    {
        Host = "test-host",
        Timeout = TimeSpan.FromSeconds(1),
        RetryDelay = TimeSpan.Zero,
    };
}
