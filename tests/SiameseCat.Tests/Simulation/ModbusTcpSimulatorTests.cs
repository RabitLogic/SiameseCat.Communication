using System.Net.Sockets;

using SiameseCat.Communication;
using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.Simulation.Modbus;
using SiameseCat.Communication.Simulation.S7;

namespace SiameseCat.Tests.Simulation;

public class ModbusTcpSimulatorTests
{
    private static readonly ushort[] Preloaded = { 10, 20, 30 };
    private static readonly bool[] CoilPattern = { true, false, true };

    [Fact]
    public async Task ReadHoldingRegisters_ReturnsPreloadedValues()
    {
        await using var server = await StartServerAsync();
        server.Data.TryWriteHoldingRegisters(0, Preloaded);
        await using var client = await ConnectAsync(server);

        var values = await client.ReadHoldingRegistersAsync(1, 0, 3);

        Assert.Equal(new ushort[] { 10, 20, 30 }, values);
    }

    [Fact]
    public async Task WriteSingleRegister_UpdatesStore()
    {
        await using var server = await StartServerAsync();
        await using var client = await ConnectAsync(server);

        await client.WriteSingleRegisterAsync(1, 5, 1234);

        Assert.Equal((ushort)1234, server.Data[5]);
    }

    [Fact]
    public async Task WriteAndReadCoils_RoundTrip()
    {
        await using var server = await StartServerAsync();
        await using var client = await ConnectAsync(server);

        await client.WriteSingleCoilAsync(1, 3, true);
        await client.WriteMultipleCoilsAsync(1, 10, CoilPattern);

        Assert.True(server.Data.ReadCoils(3, 1)![0]);
        Assert.Equal(CoilPattern, server.Data.ReadCoils(10, 3));

        var readBack = await client.ReadCoilsAsync(1, 10, 3);
        Assert.Equal(CoilPattern, readBack);
    }

    [Fact]
    public async Task WriteMultipleRegisters_UpdatesStore()
    {
        await using var server = await StartServerAsync();
        await using var client = await ConnectAsync(server);

        await client.WriteMultipleRegistersAsync(1, 20, new ushort[] { 7, 8, 9 });

        Assert.Equal(new ushort[] { 7, 8, 9 }, server.Data.ReadHoldingRegisters(20, 3));
    }

    [Fact]
    public async Task ReadWriteRegisters_WritesThenReads()
    {
        await using var server = await StartServerAsync();
        server.Data.TryWriteHoldingRegisters(0, new ushort[] { 1, 2, 3 });
        await using var client = await ConnectAsync(server);

        var values = await client.ReadWriteMultipleRegistersAsync(
            1,
            readStartAddress: 0,
            readQuantity: 2,
            writeStartAddress: 10,
            writeValues: new ushort[] { 100, 200 });

        Assert.Equal(new ushort[] { 1, 2 }, values);
        Assert.Equal(new ushort[] { 100, 200 }, server.Data.ReadHoldingRegisters(10, 2));
    }

    [Fact]
    public async Task OutOfRangeAddress_ReturnsIllegalDataAddress()
    {
        await using var server = await StartServerAsync(options => options.HoldingRegisters = 4);
        await using var client = await ConnectAsync(server);

        var exception = await Assert.ThrowsAsync<ModbusException>(
            () => client.ReadHoldingRegistersAsync(1, 10, 2));

        Assert.Equal(0x02, exception.ExceptionCode);
    }

    [Fact]
    public async Task InvalidQuantity_ReturnsIllegalDataValue()
    {
        await using var server = await StartServerAsync();

        var response = await SendRawPduAsync(server, unitId: 1, new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00 });

        Assert.Equal(0x83, response[0]);
        Assert.Equal(0x03, response[1]);
    }

    [Fact]
    public async Task BroadcastWrite_IsAppliedWithoutResponse()
    {
        await using var server = await StartServerAsync();
        await using var client = await ConnectAsync(server);
        client.DefaultUnitId = 0;

        await client.WriteSingleRegisterAsync(0, 3, 555);

        Assert.True(await WaitForAsync(() => server.Data[3] == 555), "Broadcast write was not applied.");
    }

    [Fact]
    public async Task MultipleClients_AreServedConcurrently()
    {
        await using var server = await StartServerAsync();
        server.Data.TryWriteHoldingRegisters(0, new ushort[] { 42 });

        await using var first = await ConnectAsync(server);
        await using var second = await ConnectAsync(server);

        var results = await Task.WhenAll(
            first.ReadHoldingRegistersAsync(1, 0, 1),
            second.ReadHoldingRegistersAsync(2, 0, 1));

        Assert.Equal((ushort)42, results[0][0]);
        Assert.Equal((ushort)42, results[1][0]);
    }

    [Fact]
    public async Task UnsupportedFunction_ReturnsIllegalFunction()
    {
        await using var server = await StartServerAsync();

        var response = await SendRawPduAsync(server, unitId: 1, new byte[] { 0x41 });

        Assert.Equal(0xC1, response[0]);
        Assert.Equal(0x01, response[1]);
    }

    [Fact]
    public async Task Dispose_StopsListening()
    {
        var server = await StartServerAsync();

        await server.DisposeAsync();

        Assert.False(server.IsRunning);
    }

    [Fact]
    public void Options_RejectInvalidPort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModbusSimulatorOptions { Port = 70000 }.Validate());
    }

    [Fact]
    public void DataStore_RejectsOutOfRangeWrites()
    {
        var store = new ModbusDataStore(holdingRegisters: 4);

        Assert.False(store.TryWriteHoldingRegisters(3, new ushort[] { 1, 2 }));
        Assert.Null(store.ReadHoldingRegisters(4, 1));
    }

    private static async Task<ModbusTcpSimulator> StartServerAsync(Action<ModbusSimulatorOptions>? configure = null)
    {
        var options = new ModbusSimulatorOptions { Port = 0, Host = "127.0.0.1" };
        configure?.Invoke(options);

        var server = new ModbusTcpSimulator(options);
        await server.StartAsync();
        return server;
    }

    private static async Task<ModbusTcpClient> ConnectAsync(ModbusTcpSimulator server)
    {
        var client = new ModbusTcpClient(new ModbusTcpOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromSeconds(2),
            RetryDelay = TimeSpan.Zero,
            Tcp = { Host = "127.0.0.1", Port = server.Port },
        });

        await client.ConnectAsync();
        return client;
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout = default)
    {
        var limit = timeout == default ? TimeSpan.FromSeconds(2) : timeout;
        var deadline = DateTime.UtcNow + limit;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }

    private static async Task<byte[]> SendRawPduAsync(ModbusTcpSimulator server, byte unitId, byte[] pdu)
    {
        using var socket = new TcpClient();
        await socket.ConnectAsync("127.0.0.1", server.Port);
        var stream = socket.GetStream();

        var frame = new byte[7 + pdu.Length];
        frame[1] = 0x01;
        frame[4] = (byte)((pdu.Length + 1) >> 8);
        frame[5] = (byte)(pdu.Length + 1);
        frame[6] = unitId;
        pdu.CopyTo(frame, 7);

        await stream.WriteAsync(frame);

        var header = new byte[7];
        await ReadExactlyAsync(stream, header, header.Length);
        var length = (header[4] << 8) | header[5];
        var response = new byte[length - 1];
        await ReadExactlyAsync(stream, response, response.Length);
        return response;
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
            if (read <= 0)
            {
                throw new IOException("The server closed the connection.");
            }

            offset += read;
        }
    }
}
