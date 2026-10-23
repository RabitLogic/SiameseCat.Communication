using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SiameseCat.Communication;
using SiameseCat.Communication.Hosting;
using SiameseCat.Communication.Simulation.Modbus;
using SiameseCat.Communication.Simulation.S7;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.Drivers;

/// <summary>
/// Step 6 of the driver track: dependency injection and host lifecycle.
/// </summary>
/// <remarks>
/// <c>AddSiameseCat()</c> registers the driver registry and an <see cref="ICommunicationLogger"/> that writes
/// through <see cref="ILoggerFactory"/>. <c>AddSiameseCatDriver(id, ...)</c> registers a keyed transient
/// driver, and <c>AddModbusSimulator</c>/<c>AddS7Simulator</c> register in-process slaves that start and stop
/// with the host.
/// </remarks>
public sealed class DriverHostingSample : ISample
{
    /// <inheritdoc/>
    public string Id => "drivers/hosting";

    /// <inheritdoc/>
    public string Title => "Drivers: dependency injection and host lifecycle";

    /// <inheritdoc/>
    public string Description =>
        "Register drivers, logging and in-process slaves in an IServiceProvider, start them like a host does and resolve a driver by key.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run drivers/hosting";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        var modbusPort = 0;

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new SampleLoggerProvider());
        });

        SampleConsole.Step("1. Compose the container");
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSiameseCat();
        services.AddModbusSimulator(new ModbusSimulatorOptions { Port = 0, ServerIdentifier = "SampleSlave" });
        services.AddS7Simulator(new S7SimulatorOptions { Port = 0 });
        services.AddSiameseCatDriver("modbus", configuration =>
        {
            configuration.Transport = TransportKind.Tcp;
            configuration.Host = "127.0.0.1";
            configuration.Port = modbusPort;
            configuration.Timeout = TimeSpan.FromSeconds(2);
            configuration.ProbeAddress = "HR0";
        });

        await using var provider = services.BuildServiceProvider();
        SampleConsole.Value("ICommunicationLogger", provider.GetRequiredService<ICommunicationLogger>().GetType().Name);
        SampleConsole.Value("Registry size", provider.GetRequiredService<DriverRegistry>().Descriptors.Count());

        SampleConsole.Step("2. Start the hosted services, exactly like a generic host");
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        foreach (var service in hostedServices)
        {
            await service.StartAsync(cancellationToken).ConfigureAwait(false);
            SampleConsole.Detail($"started {service.GetType().Name}");
        }

        var modbusSlave = provider.GetRequiredService<ModbusTcpSimulator>();
        modbusPort = modbusSlave.Port;
        modbusSlave.Data.TryWriteHoldingRegisters(0, new ushort[] { 7, 8, 9 });
        SampleConsole.Value("Modbus slave port", modbusSlave.Port);
        SampleConsole.Value("S7 slave port", provider.GetRequiredService<S7Simulator>().Port);
        SampleConsole.Value("IModbusSimulator count", provider.GetServices<IModbusSimulator>().Count());
        SampleConsole.Value("IS7Simulator count", provider.GetServices<IS7Simulator>().Count());

        SampleConsole.Step("3. Resolve the driver by key and use it");
        await using (var driver = provider.GetRequiredKeyedService<IDeviceDriver>("modbus"))
        {
            await driver.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var reader = DriverAccess.Reader(driver);
            var health = DriverAccess.Health(driver);
            var value = await reader.ReadAsync(driver.AddressParser.Parse("HR1"), cancellationToken).ConfigureAwait(false);
            SampleConsole.Value("HR1", value.AsUInt16());
            SampleConsole.Value("Ping", await health.PingAsync(cancellationToken).ConfigureAwait(false));
            SampleConsole.Value("Metrics", driver.Metrics.Snapshot().Requests.ToString());
            await driver.DisconnectAsync().ConfigureAwait(false);
        }

        SampleConsole.Step("4. Stop the host");
        for (var i = hostedServices.Length - 1; i >= 0; i--)
        {
            await hostedServices[i].StopAsync(cancellationToken).ConfigureAwait(false);
            SampleConsole.Detail($"stopped {hostedServices[i].GetType().Name}");
        }

        SampleConsole.Value("Slave running", modbusSlave.IsRunning);
        SampleConsole.Value("Frames received", modbusSlave.Statistics.FramesReceived);
        SampleConsole.Check(!modbusSlave.IsRunning, "the slave stopped with the host");

        SampleConsole.Note("Next: 'run simulation/modbus-tcp' to build your own slave without the DI helpers.");
    }

    /// <summary>Forwards <see cref="ILogger"/> output to the console with the SiameseCat prefix.</summary>
    private sealed class SampleLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new SampleLogger(categoryName);

        public void Dispose()
        {
        }

        private sealed class SampleLogger(string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                Console.WriteLine($"   [log:{logLevel}] {category}: {formatter(state, exception)}");
            }
        }
    }
}
