using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SiameseCat.Communication;
using SiameseCat.Communication.Hosting;
using SiameseCat.Communication.Server.Modbus;
using SiameseCat.Communication.Server.S7;

namespace SiameseCat.Tests.Integration;

public class HealthCheckTests
{
    private static readonly string[] ReadyTags = new[] { "ready" };
    [Fact]
    public async Task DriverCheck_IsHealthy_WhenTheDeviceAnswers()
    {
        await using var slave = await StartModbusSlaveAsync();

        var provider = BuildProvider(services => services
            .AddHealthChecks()
            .AddSiameseCatDriverCheck("modbus", configuration =>
            {
                configuration.Transport = TransportKind.Tcp;
                configuration.Host = "127.0.0.1";
                configuration.Port = slave.Port;
                configuration.Timeout = TimeSpan.FromSeconds(2);
                configuration.ProbeAddress = "HR0";
            }, tags: ReadyTags));

        await using (provider.ConfigureAwait(false))
        {
            var service = provider.GetRequiredService<HealthCheckService>();
            var report = await service.CheckHealthAsync();

            var entry = Assert.Single(report.Entries);
            Assert.Equal("siamese:modbus", entry.Key);
            Assert.Equal(HealthStatus.Healthy, entry.Value.Status);
            Assert.Contains("modbus", entry.Value.Description, StringComparison.Ordinal);
            Assert.Equal("modbus", entry.Value.Data["driver"]);
            Assert.True(Convert.ToInt64(entry.Value.Data["requests"], System.Globalization.CultureInfo.InvariantCulture) >= 1);
            Assert.Contains("ready", entry.Value.Tags);
        }
    }

    [Fact]
    public async Task DriverCheck_BecomesUnhealthy_WhenTheDeviceStopsAnswering()
    {
        var slave = await StartModbusSlaveAsync();

        var provider = BuildProvider(services => services
            .AddHealthChecks()
            .AddSiameseCatDriverCheck("modbus", configuration =>
            {
                configuration.Transport = TransportKind.Tcp;
                configuration.Host = "127.0.0.1";
                configuration.Port = slave.Port;
                configuration.Timeout = TimeSpan.FromMilliseconds(500);
                configuration.ProbeAddress = "HR0";
            }));

        await using (provider.ConfigureAwait(false))
        {
            var service = provider.GetRequiredService<HealthCheckService>();

            Assert.Equal(HealthStatus.Healthy, (await service.CheckHealthAsync()).Status);

            await slave.DisposeAsync();
            var report = await service.CheckHealthAsync();

            var entry = Assert.Single(report.Entries);
            Assert.Equal(HealthStatus.Unhealthy, entry.Value.Status);
            Assert.Equal(DriverState.Faulted.ToString(), entry.Value.Data["state"]);
        }
    }

    [Fact]
    public async Task DriverCheck_IsUnhealthy_WhenTheDriverCannotConnect()
    {
        var provider = BuildProvider(services => services
            .AddHealthChecks()
            .AddSiameseCatDriverCheck("modbus", configuration =>
            {
                configuration.Transport = TransportKind.Tcp;
                configuration.Host = "127.0.0.1";
                configuration.Port = FindClosedPort();
                configuration.ConnectTimeout = TimeSpan.FromMilliseconds(200);
                configuration.Timeout = TimeSpan.FromMilliseconds(200);
                configuration.ProbeAddress = "HR0";
            }));

        await using (provider.ConfigureAwait(false))
        {
            var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

            var entry = Assert.Single(report.Entries);
            Assert.Equal(HealthStatus.Unhealthy, entry.Value.Status);
            Assert.NotNull(entry.Value.Exception);
        }
    }

    [Fact]
    public async Task DriverCheck_IsDegraded_WhenNoProbeAddressIsConfigured()
    {
        await using var slave = await StartModbusSlaveAsync();

        var provider = BuildProvider(services => services
            .AddHealthChecks()
            .AddSiameseCatDriverCheck("modbus", configuration =>
            {
                configuration.Transport = TransportKind.Tcp;
                configuration.Host = "127.0.0.1";
                configuration.Port = slave.Port;
            }));

        await using (provider.ConfigureAwait(false))
        {
            var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

            var entry = Assert.Single(report.Entries);
            Assert.Equal(HealthStatus.Degraded, entry.Value.Status);
            Assert.Contains("ProbeAddress", entry.Value.Description, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DriverCheck_CanMonitorAnApplicationOwnedDriver()
    {
        await using var slave = await StartModbusSlaveAsync();

        var provider = BuildProvider(services =>
        {
            services.AddSiameseCatDriver("modbus", configuration =>
            {
                configuration.Transport = TransportKind.Tcp;
                configuration.Host = "127.0.0.1";
                configuration.Port = slave.Port;
                configuration.Timeout = TimeSpan.FromSeconds(2);
                configuration.ProbeAddress = "HR0";
            });
            services.AddHealthChecks().AddSiameseCatDriverCheck(
                "plc-main",
                sp => sp.GetRequiredKeyedService<IPlcDriver>("modbus"));
        });

        await using (provider.ConfigureAwait(false))
        {
            var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

            var entry = Assert.Single(report.Entries);
            Assert.Equal("plc-main", entry.Key);
            Assert.Equal(HealthStatus.Healthy, entry.Value.Status);
        }
    }

    [Fact]
    public async Task DriverCheck_ReportsTheS7Driver()
    {
        await using var slave = new S7Server(new S7ServerOptions { Port = 0 });
        await slave.StartAsync();
        slave.Data.TryEnsureDataBlock(1, 32);

        var provider = BuildProvider(services => services
            .AddHealthChecks()
            .AddSiameseCatDriverCheck("siemens", configuration =>
            {
                configuration.Transport = TransportKind.Tcp;
                configuration.Host = "127.0.0.1";
                configuration.Port = slave.Port;
                configuration.Timeout = TimeSpan.FromSeconds(2);
                configuration.ProbeAddress = "DB1.DBW0";
            }, name: "plc-s7"));

        await using (provider.ConfigureAwait(false))
        {
            var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

            var entry = Assert.Single(report.Entries);
            Assert.Equal("plc-s7", entry.Key);
            Assert.Equal(HealthStatus.Healthy, entry.Value.Status);
            Assert.Equal("siemens", entry.Value.Data["driver"]);
        }
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSiameseCat();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static async Task<ModbusTcpServer> StartModbusSlaveAsync()
    {
        var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        slave.Data.TryWriteHoldingRegisters(0, new ushort[] { 1, 2, 3 });
        await slave.StartAsync();
        return slave;
    }

    private static int FindClosedPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
