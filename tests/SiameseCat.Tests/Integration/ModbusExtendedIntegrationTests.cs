using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.Server.Modbus;

namespace SiameseCat.Tests.Integration;

public class ModbusExtendedIntegrationTests
{
    [Fact]
    public async Task DeviceIdentification_RoundTripsThroughTheBuiltInSlave()
    {
        var options = new ModbusServerOptions { Port = 0 };
        options.DeviceIdentification[0x04] = "Test bench";
        await using var server = new ModbusTcpServer(options);
        await server.StartAsync();

        await using var client = await ConnectAsync(server);

        var identification = await client.ReadDeviceIdentificationAsync(1, ModbusDeviceIdCode.Regular);

        Assert.Equal("SiameseCat", identification.VendorName);
        Assert.Equal("SiameseCat.Communication", identification.ProductCode);
        Assert.Equal("Test bench", identification.ProductName);
        Assert.False(identification.MoreFollows);
    }

    [Fact]
    public async Task FileRecords_RoundTripThroughTheBuiltInSlave()
    {
        var options = new ModbusServerOptions { Port = 0 };
        await using var server = new ModbusTcpServer(options);
        server.Data.DefineFile(4, 3, 4);
        await server.StartAsync();

        await using var client = await ConnectAsync(server);

        await client.WriteFileRecordsAsync(new[]
        {
            new ModbusFileRecord(4, 1, new ushort[] { 1, 2, 3 }),
            new ModbusFileRecord(4, 3, new ushort[] { 9, 8, 7 }),
        });

        Assert.Equal(new ushort[] { 1, 2, 3 }, server.Data.ReadFileRecord(4, 1, 3));
        Assert.Equal(new ushort[] { 9, 8, 7 }, server.Data.ReadFileRecord(4, 3, 3));

        var records = await client.ReadFileRecordsAsync(new[]
        {
            new ModbusFileRecordRequest(4, 1, 3),
            new ModbusFileRecordRequest(4, 3, 2),
        });

        Assert.Equal(new ushort[] { 1, 2, 3 }, records[0]);
        Assert.Equal(new ushort[] { 9, 8 }, records[1]);
    }

    private static async Task<ModbusTcpClient> ConnectAsync(ModbusTcpServer server)
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
}
