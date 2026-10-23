using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SiameseCat.Communication.Diagnostics;
using SiameseCat.Communication.Hosting;
using SiameseCat.Communication.Simulation.Modbus;
using SiameseCat.Communication.Simulation.S7;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Simulation;

public class ModbusSimulatorHostingTests
{
    [Fact]
    public async Task AddModbusServer_RegistersAndStartsTheSlave()
    {
        var services = new ServiceCollection();
        services.AddModbusSimulator(options =>
        {
            options.Port = 0;
            options.HoldingRegisters = 32;
        });

        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<IModbusSimulator>();

        Assert.IsType<ModbusTcpSimulator>(server);
        Assert.False(server.IsRunning);
        Assert.Equal(32, server.Data.HoldingRegisterCount);

        await provider.GetRequiredService<IHostedService>().StartAsync(CancellationToken.None);
        Assert.True(server.IsRunning);
        Assert.True(((ModbusTcpSimulator)server).Port > 0);

        await provider.GetRequiredService<IHostedService>().StopAsync(CancellationToken.None);
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task AddS7Server_RegistersAndStartsTheS7Slave()
    {
        var services = new ServiceCollection();
        services.AddS7Simulator(options => options.Port = 0);

        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<IS7Simulator>();
        Assert.IsType<S7Simulator>(server);
        Assert.False(server.IsRunning);

        await provider.GetRequiredService<IHostedService>().StartAsync(CancellationToken.None);
        Assert.True(server.IsRunning);

        server.Data.TryWriteBytes(SiameseCat.Communication.S7.S7Area.Merkers, 0, 0, new byte[] { 0x12, 0x34 });

        await using var client = new SiameseCat.Communication.S7.S7Client(new SiameseCat.Communication.S7.S7Options
        {
            Host = "127.0.0.1",
            Port = ((S7Simulator)server).Port,
            Timeout = TimeSpan.FromSeconds(2),
            ConnectTimeout = TimeSpan.FromSeconds(2),
        });

        await client.ConnectAsync();
        var value = await client.ReadUInt16Async(SiameseCat.Communication.S7.S7Address.Parse("MW0"));
        Assert.Equal((ushort)0x1234, value);

        await provider.GetRequiredService<IHostedService>().StopAsync(CancellationToken.None);
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task AddModbusServer_RegistersOneHostedService()
    {
        var services = new ServiceCollection();
        services.AddModbusSimulator(options => options.Port = 0);
        services.AddModbusSimulator(options => options.Port = 0);

        await using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IHostedService>());
        Assert.Equal(2, provider.GetServices<IModbusSimulator>().Count());
    }

    [Fact]
    public async Task AddModbusRtuServer_RegistersTheRtuSlave()
    {
        var services = new ServiceCollection();
        services.AddModbusRtuSimulator(options =>
        {
            options.UnitId = 9;
            options.Serial.PortName = "COM3";
        });

        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<IModbusSimulator>();

        Assert.IsType<ModbusRtuSimulator>(server);
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task AddModbusServer_UsesTheRegisteredCommunicationLogger()
    {
        var logger = new RecordingLogger();
        var services = new ServiceCollection();
        services.AddSingleton<SiameseCat.Communication.Diagnostics.ICommunicationLogger>(logger);
        services.AddModbusSimulator(options => options.Port = 0);

        await using var provider = services.BuildServiceProvider();
        var server = (ModbusTcpSimulator)provider.GetRequiredService<IModbusSimulator>();

        await provider.GetRequiredService<IHostedService>().StartAsync(CancellationToken.None);
        await using var client = new SiameseCat.Communication.Modbus.ModbusTcpClient(
            new SiameseCat.Communication.Modbus.ModbusTcpOptions
            {
                UnitId = 1,
                Timeout = TimeSpan.FromSeconds(2),
                Tcp = { Host = "127.0.0.1", Port = server.Port },
            });

        await client.ConnectAsync();
        await client.ReadHoldingRegistersAsync(1, 0, 1);

        Assert.True(logger.Contains(SiameseCat.Communication.Diagnostics.CommunicationLogLevel.Debug, "client connected"));
    }
}
