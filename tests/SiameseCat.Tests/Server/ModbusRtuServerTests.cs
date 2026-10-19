using SiameseCat.Server;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Server;

public class ModbusRtuServerTests
{
    [Fact]
    public async Task ReadHoldingRegisters_RespondsWithValues()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Rtu(0x01, 0x03, 0x00, 0x00, 0x00, 0x02));
        await using var server = new ModbusRtuServer(transport, new ModbusRtuServerOptions());
        server.Data.TryWriteHoldingRegisters(0, new ushort[] { 10, 20 });

        await server.StartAsync();
        await WaitForAsync(() => transport.Writes.Count == 1);

        Assert.Equal(
            ProtocolFrames.Rtu(0x01, 0x03, 0x04, 0x00, 0x0A, 0x00, 0x14),
            transport.Writes[0]);
    }

    [Fact]
    public async Task WriteSingleRegister_RespondsWithEchoAndUpdatesStore()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Rtu(0x01, 0x06, 0x00, 0x05, 0x04, 0xD2));
        await using var server = new ModbusRtuServer(transport, new ModbusRtuServerOptions());

        await server.StartAsync();
        await WaitForAsync(() => transport.Writes.Count == 1);

        Assert.Equal((ushort)1234, server.Data[5]);
        Assert.Equal(
            ProtocolFrames.Rtu(0x01, 0x06, 0x00, 0x05, 0x04, 0xD2),
            transport.Writes[0]);
    }

    [Fact]
    public async Task FramesForOtherStations_AreIgnored()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Rtu(0x09, 0x03, 0x00, 0x00, 0x00, 0x01));
        await using var server = new ModbusRtuServer(transport, new ModbusRtuServerOptions { UnitId = 1 });

        await server.StartAsync();
        await WaitForAsync(() => !server.IsRunning);

        Assert.Empty(transport.Writes);
    }

    [Fact]
    public async Task FramesWithBadCrc_AreIgnored()
    {
        var transport = new MockTransport();
        var frame = ProtocolFrames.Rtu(0x01, 0x03, 0x00, 0x00, 0x00, 0x01);
        frame[frame.Length - 1] ^= 0xFF;
        transport.EnqueueResponse(frame);
        await using var server = new ModbusRtuServer(transport, new ModbusRtuServerOptions());

        await server.StartAsync();
        await WaitForAsync(() => !server.IsRunning);

        Assert.Empty(transport.Writes);
    }

    [Fact]
    public async Task BroadcastWrite_IsAppliedWithoutResponse()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Rtu(0x00, 0x06, 0x00, 0x03, 0x04, 0xD2));
        await using var server = new ModbusRtuServer(transport, new ModbusRtuServerOptions());

        await server.StartAsync();
        await WaitForAsync(() => server.Data[3] == 1234);

        Assert.Empty(transport.Writes);
    }

    [Fact]
    public async Task WriteMultipleRegisters_UsesTheSharedPduProcessor()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Rtu(
            0x01, 0x10, 0x00, 0x00, 0x00, 0x02, 0x04, 0x00, 0x0A, 0x00, 0x14));
        await using var server = new ModbusRtuServer(transport, new ModbusRtuServerOptions());

        await server.StartAsync();
        await WaitForAsync(() => transport.Writes.Count == 1);

        Assert.Equal(new ushort[] { 10, 20 }, server.Data.ReadHoldingRegisters(0, 2));
        Assert.Equal(ProtocolFrames.Rtu(0x01, 0x10, 0x00, 0x00, 0x00, 0x02), transport.Writes[0]);
    }

    [Fact]
    public void Options_RejectInvalidStationAddress()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModbusRtuServerOptions { UnitId = 0 }.Validate());
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }
    }
}
