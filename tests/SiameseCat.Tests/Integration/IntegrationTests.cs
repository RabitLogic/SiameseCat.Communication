using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiameseCat.Communication;
using SiameseCat.Communication.Hosting;

namespace SiameseCat.Tests.Integration;

public class IntegrationTests
{
    [Fact]
    public void AddSiameseCat_RegistersRegistryWithBuiltInDrivers()
    {
        var provider = new ServiceCollection().AddSiameseCat().BuildServiceProvider();

        var registry = provider.GetRequiredService<DriverRegistry>();

        Assert.True(registry.Contains("modbus"));
        Assert.True(registry.Contains("s7"));
        Assert.False(registry.Contains("inovance"));
    }

    [Fact]
    public void AddSiameseCat_UsesNullLoggerWhenLoggingIsNotRegistered()
    {
        var provider = new ServiceCollection().AddSiameseCat().BuildServiceProvider();

        Assert.Same(NullPlcLogger.Instance, provider.GetRequiredService<IPlcLogger>());
    }

    [Fact]
    public void AddSiameseCatDriver_ResolvesKeyedDriver()
    {
        var provider = new ServiceCollection()
            .AddSiameseCatDriver("modbus", configuration =>
            {
                configuration.Transport = TransportKind.Tcp;
                configuration.Host = "192.168.0.10";
            })
            .BuildServiceProvider();

        var driver = provider.GetRequiredKeyedService<IPlcDriver>("modbus");

        Assert.Equal("modbus", driver.Descriptor.Id);
        Assert.Equal(502, driver.Descriptor.DefaultPort);
    }

    [Fact]
    public void MicrosoftLoggerAdapter_MapsLevels()
    {
        Assert.Equal(LogLevel.Trace, MicrosoftLoggerAdapter.ToLogLevel(PlcLogLevel.Trace));
        Assert.Equal(LogLevel.Debug, MicrosoftLoggerAdapter.ToLogLevel(PlcLogLevel.Debug));
        Assert.Equal(LogLevel.Information, MicrosoftLoggerAdapter.ToLogLevel(PlcLogLevel.Information));
        Assert.Equal(LogLevel.Warning, MicrosoftLoggerAdapter.ToLogLevel(PlcLogLevel.Warning));
        Assert.Equal(LogLevel.Error, MicrosoftLoggerAdapter.ToLogLevel(PlcLogLevel.Error));
    }

    [Fact]
    public void MicrosoftLoggerAdapter_ForwardsMessages()
    {
        var logger = new RecordingLoggerWrapper();
        var adapter = new MicrosoftLoggerAdapter(logger);

        adapter.Log(PlcLogLevel.Warning, "connection lost", new InvalidOperationException("boom"));

        Assert.True(logger.SawWarning);
        Assert.Contains("connection lost", logger.LastMessage, StringComparison.Ordinal);
    }

    private sealed class RecordingLoggerWrapper : ILogger
    {
        public bool SawWarning { get; private set; }

        public string LastMessage { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            SawWarning |= logLevel == LogLevel.Warning;
            LastMessage = formatter(state, exception);
        }
    }
}
