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
    public async Task MaskWriteRegister_FrameLengthIsInferred()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Rtu(0x01, 0x16, 0x00, 0x00, 0x00, 0xF2, 0x00, 0x25));
        await using var server = new ModbusRtuServer(transport, new ModbusRtuServerOptions());
        server.Data.TryWriteHoldingRegisters(0, new ushort[] { 0x0012 });

        await server.StartAsync();
        await WaitForAsync(() => transport.Writes.Count == 1);

        Assert.Equal((ushort)0x0017, server.Data[0]);
        Assert.Equal(
            ProtocolFrames.Rtu(0x01, 0x16, 0x00, 0x00, 0x00, 0xF2, 0x00, 0x25),
            transport.Writes[0]);
    }

    [Fact]
    public async Task ReportServerId_And_ExceptionStatus_AreAnswered()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Rtu(0x01, 0x07));
        transport.EnqueueResponse(ProtocolFrames.Rtu(0x01, 0x11));
        await using var server = new ModbusRtuServer(transport, new ModbusRtuServerOptions
        {
            ServerIdentifier = "RTU-1",
            RunIndicator = true,
        });
        server.Data.ExceptionStatus = 0x01;

        await server.StartAsync();
        await WaitForAsync(() => transport.Writes.Count == 2);

        Assert.Equal(ProtocolFrames.Rtu(0x01, 0x07, 0x01), transport.Writes[0]);
        Assert.Equal(
            ProtocolFrames.Rtu(0x01, 0x11, 0x06, (byte)'R', (byte)'T', (byte)'U', (byte)'-', (byte)'1', 0xFF),
            transport.Writes[1]);
    }

    [Fact]
    public async Task BadCrc_IncrementsDiscardedFrameCounter()
    {
        var transport = new MockTransport();
        var frame = ProtocolFrames.Rtu(0x01, 0x03, 0x00, 0x00, 0x00, 0x01);
        frame[frame.Length - 1] ^= 0xFF;
        transport.EnqueueResponse(frame);
        await using var server = new ModbusRtuServer(transport, new ModbusRtuServerOptions());

        await server.StartAsync();
        await WaitForAsync(() => !server.IsRunning);

        Assert.Equal(1, server.Statistics.FramesDiscarded);
        Assert.Equal(0, server.Statistics.FramesReceived);
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
