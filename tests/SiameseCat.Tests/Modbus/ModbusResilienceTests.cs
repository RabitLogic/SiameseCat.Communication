using SiameseCat.Communication;
using SiameseCat.Communication.Exceptions;
using SiameseCat.Communication.Modbus;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Modbus;

public class ModbusResilienceTests
{
    [Fact]
    public async Task Read_RetriesAndReconnects_AfterTimeout()
    {
        var transport = new MockTransport { TimeoutFailuresRemaining = 1 };
        transport.EnqueueResponse(ProtocolFrames.Rtu(0x01, 0x03, 0x02, 0x00, 0x2A));

        var options = CreateOptions();
        options.Timeout = TimeSpan.FromMilliseconds(50);
        options.RetryCount = 1;

        await using var client = new ModbusRtuClient(transport, options);
        await client.ConnectAsync();

        var registers = await client.ReadHoldingRegistersAsync(0, 1);

        Assert.Equal(42, registers[0]);
        Assert.Equal(2, transport.Writes.Count);
        Assert.Equal(2, transport.OpenCount);
    }

    [Fact]
    public async Task Write_IsNotRetriedByDefault()
    {
        var transport = new MockTransport { TimeoutFailuresRemaining = 1 };

        var options = CreateOptions();
        options.Timeout = TimeSpan.FromMilliseconds(50);
        options.RetryCount = 1;

        await using var client = new ModbusRtuClient(transport, options);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<CommunicationTimeoutException>(
            () => client.WriteSingleRegisterAsync(0, 1));

        Assert.Single(transport.Writes);
        Assert.Equal(1, transport.OpenCount);
    }

    [Fact]
    public async Task Write_IsRetriedWhenRetryWritesIsEnabled()
    {
        var transport = new MockTransport { TimeoutFailuresRemaining = 1 };
        transport.EnqueueResponse(ProtocolFrames.Rtu(0x01, 0x06, 0x00, 0x00, 0x00, 0x01));

        var options = CreateOptions();
        options.Timeout = TimeSpan.FromMilliseconds(50);
        options.RetryCount = 1;
        options.RetryWrites = true;

        await using var client = new ModbusRtuClient(transport, options);
        await client.ConnectAsync();

        await client.WriteSingleRegisterAsync(0, 1);

        Assert.Equal(2, transport.Writes.Count);
        Assert.Equal(2, transport.OpenCount);
    }

    [Fact]
    public async Task TcpTimeout_ClosesConnectionSoNextRequestFailsFast()
    {
        var transport = new MockTransport { TimeoutFailuresRemaining = 1 };

        var options = new ModbusTcpOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50),
            RetryDelay = TimeSpan.Zero,
        };

        await using var client = new ModbusTcpClient(transport, options);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<CommunicationTimeoutException>(
            () => client.ReadHoldingRegistersAsync(0, 1));

        Assert.Equal(1, transport.CloseCount);
        Assert.False(transport.IsOpen);

        await Assert.ThrowsAsync<CommunicationException>(
            () => client.ReadHoldingRegistersAsync(0, 1));
    }

    [Fact]
    public async Task DisposedClient_ThrowsObjectDisposedException()
    {
        var transport = new MockTransport();
        var client = new ModbusRtuClient(transport, CreateOptions());
        await client.ConnectAsync();
        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.ReadHoldingRegistersAsync(0, 1));
    }

    private static ModbusRtuOptions CreateOptions() => new()
    {
        UnitId = 1,
        Timeout = TimeSpan.FromSeconds(1),
        RetryDelay = TimeSpan.Zero,
    };
}
