using SiameseCat.Protocols.S7;
using SiameseCat.Server;

namespace SiameseCat.Tests.Server;

public class S7ServerTests
{
    [Fact]
    public async Task Connect_NegotiatesTheConfiguredPduSize()
    {
        await using var server = await StartServerAsync();
        await using var client = await ConnectAsync(server);

        Assert.True(client.IsConnected);
        Assert.Equal(960, client.PduSize);
        Assert.Equal(1, server.Statistics.TotalConnections);
    }

    [Fact]
    public async Task WriteAndReadFloat_RoundTripsThroughTheStore()
    {
        await using var server = await StartServerAsync();
        await using var client = await ConnectAsync(server);
        var address = S7Address.Parse("DB1.DBD0");

        await client.WriteSingleAsync(address, 12.5f);
        var value = await client.ReadSingleAsync(address);

        Assert.Equal(12.5f, value);
        Assert.Equal(4, server.Data.ReadBytes(S7Area.DataBlock, 1, 0, 4)!.Length);
        Assert.True(server.Data.ContainsDataBlock(1));
    }

    [Fact]
    public async Task BoolRoundTrip_UsesBitAddressing()
    {
        await using var server = await StartServerAsync();
        await using var client = await ConnectAsync(server);
        var address = S7Address.Parse("M0.3");

        await client.WriteBoolAsync(address, true);

        Assert.True(await client.ReadBoolAsync(address));
        Assert.True(server.Data.TryReadBit(S7Area.Merkers, 0, 3, out var bit));
        Assert.True(bit);
    }

    [Fact]
    public async Task StringRoundTrip_UsesTheS7Header()
    {
        await using var server = await StartServerAsync();
        await using var client = await ConnectAsync(server);
        var address = S7Address.Parse("DB2.DBB0");

        await client.WriteStringAsync(address, "HELLO", maxLength: 10);
        var text = await client.ReadStringAsync(address, maxLength: 10);

        Assert.Equal("HELLO", text);

        var raw = server.Data.ReadBytes(S7Area.DataBlock, 2, 0, 12)!;
        Assert.Equal(10, raw[0]);
        Assert.Equal(5, raw[1]);
    }

    [Fact]
    public async Task WriteBits_CrossesByteBoundaries()
    {
        await using var server = await StartServerAsync();
        await using var client = await ConnectAsync(server);
        var start = S7Address.Parse("M10.6");

        await client.WriteBitsAsync(start, new[] { true, true, false, true });

        Assert.True(await client.ReadBoolAsync(S7Address.Parse("M10.6")));
        Assert.True(await client.ReadBoolAsync(S7Address.Parse("M10.7")));
        Assert.False(await client.ReadBoolAsync(S7Address.Parse("M11.0")));
        Assert.True(await client.ReadBoolAsync(S7Address.Parse("M11.1")));
    }

    [Fact]
    public async Task MultipleReadWrite_UsesPerItemResponses()
    {
        await using var server = await StartServerAsync();
        await using var client = await ConnectAsync(server);

        await client.WriteMultipleAsync(new[]
        {
            new S7WriteItem(S7Address.Parse("DB3.DBW0"), new byte[] { 0x12, 0x34 }),
            new S7WriteItem(S7Address.Parse("DB3.DBD4"), BitConverter.GetBytes(3.5f)),
        });

        var values = await client.ReadMultipleAsync(new[]
        {
            new S7ReadItem(S7Address.Parse("DB3.DBW0"), 2),
            new S7ReadItem(S7Address.Parse("DB3.DBD4"), 4),
        });

        Assert.Equal(new byte[] { 0x12, 0x34 }, values[0]);
        Assert.Equal(3.5f, BitConverter.ToSingle(values[1], 0));
        Assert.Equal(2, server.Statistics.ItemsWritten);
        Assert.Equal(2, server.Statistics.ItemsRead);
    }

    [Fact]
    public async Task ReadOutsideTheArea_ThrowsItemException()
    {
        await using var server = await StartServerAsync();
        await using var client = await ConnectAsync(server);

        var exception = await Assert.ThrowsAsync<S7ItemException>(
            () => client.ReadUInt16Async(S7Address.Parse("MW10000")));

        Assert.Equal(0x05, exception.ReturnCode);
        Assert.Equal(1, server.Statistics.ItemErrors);
    }

    [Fact]
    public async Task UnknownDataBlock_ReturnsObjectNotExistWhenAutoCreateIsDisabled()
    {
        await using var server = await StartServerAsync(options => options.AutoCreateDataBlocks = false);
        await using var client = await ConnectAsync(server);

        var exception = await Assert.ThrowsAsync<S7ItemException>(
            () => client.ReadUInt16Async(S7Address.Parse("DB9.DBW0")));

        Assert.Equal(0x0A, exception.ReturnCode);
    }

    [Fact]
    public async Task DataStoreEvents_ReportClientWrites()
    {
        await using var server = await StartServerAsync();
        S7WriteEventArgs? captured = null;
        server.Data.Written += (_, args) => captured = args;

        await using var client = await ConnectAsync(server);
        await client.WriteUInt16Async(S7Address.Parse("DB4.DBW2"), 0x1234);

        Assert.NotNull(captured);
        Assert.Equal(S7Area.DataBlock, captured!.Area);
        Assert.Equal(4, captured.DbNumber);
        Assert.Equal(2, captured.Start);
        Assert.Equal(new byte[] { 0x12, 0x34 }, captured.Values);
        Assert.Equal(new byte[] { 0x00, 0x00 }, captured.PreviousValues);
    }

    [Fact]
    public async Task Statistics_TrackTraffic()
    {
        await using var server = await StartServerAsync();
        await using var client = await ConnectAsync(server);

        await client.WriteUInt16Async(S7Address.Parse("MW2"), 42);
        await client.ReadUInt16Async(S7Address.Parse("MW2"));

        Assert.Equal(1, server.Statistics.SetupRequests);
        Assert.Equal(1, server.Statistics.WriteRequests);
        Assert.Equal(1, server.Statistics.ReadRequests);
        Assert.Equal(3, server.Statistics.TotalRequests);
        Assert.Equal(1, server.Statistics.ItemsWritten);
        Assert.Equal(1, server.Statistics.ItemsRead);
        Assert.Equal(0, server.Statistics.FramesDiscarded);
    }

    [Fact]
    public async Task ServerStopsCleanly_AndResetsTheStore()
    {
        await using var server = await StartServerAsync();
        await using (var client = await ConnectAsync(server))
        {
            await client.WriteUInt16Async(S7Address.Parse("MW0"), 0xABCD);
        }

        await server.StopAsync();
        Assert.False(server.IsRunning);

        server.Data.Reset();
        Assert.Equal(new byte[] { 0x00, 0x00 }, server.Data.ReadBytes(S7Area.Merkers, 0, 0, 2));
    }

    [Fact]
    public void InvalidOptions_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new S7ServerOptions { Port = 70000 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new S7ServerOptions { PduSize = 100 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new S7ServerOptions { MemorySize = 0 }.Validate());
    }

    private static async Task<S7Server> StartServerAsync(Action<S7ServerOptions>? configure = null)
    {
        var options = new S7ServerOptions { Port = 0, DefaultDataBlockSize = 256 };
        configure?.Invoke(options);
        var server = new S7Server(options);
        await server.StartAsync();
        return server;
    }

    private static async Task<S7Client> ConnectAsync(S7Server server)
    {
        var client = new S7Client(new S7Options
        {
            Host = "127.0.0.1",
            Port = server.Port,
            Timeout = TimeSpan.FromSeconds(2),
            ConnectTimeout = TimeSpan.FromSeconds(2),
        });

        await client.ConnectAsync();
        return client;
    }
}
