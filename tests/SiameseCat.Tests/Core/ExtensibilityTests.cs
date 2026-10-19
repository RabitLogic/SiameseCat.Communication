using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Core;

public class DriverCapabilityTests
{
    [Fact]
    public void Descriptor_DefaultsToAllCapabilities()
    {
        var descriptor = new DriverDescriptor("x", "X", "test", TransportKind.Tcp);

        Assert.Equal(DriverCapabilities.All, descriptor.Capabilities);
    }

    [Fact]
    public async Task ReadOnlyDriver_RejectsWrites()
    {
        await using var driver = new MockDriver(capabilities: DriverCapabilities.Read | DriverCapabilities.BatchRead);
        await driver.ConnectAsync();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => driver.WriteAsync(driver.AddressParser.Parse("R1"), PlcValue.FromUInt16(1)));

        Assert.Contains("Write", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteOnlyDriver_RejectsReads()
    {
        await using var driver = new MockDriver(capabilities: DriverCapabilities.Write);
        await driver.ConnectAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => driver.ReadAsync(driver.AddressParser.Parse("R1")));
    }

    [Fact]
    public async Task Driver_WithoutSubscribeCapability_RejectsSubscriptions()
    {
        await using var driver = new MockDriver(capabilities: DriverCapabilities.Read);
        await driver.ConnectAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => driver.SubscribeAsync(
                driver.AddressParser.Parse("R0"),
                TimeSpan.FromMilliseconds(10),
                _ => Task.CompletedTask));
    }

    [Fact]
    public async Task Driver_WithoutHealthCapability_RejectsPing()
    {
        await using var driver = new MockDriver(
            new DriverConfiguration { ProbeAddress = "R0" },
            capabilities: DriverCapabilities.Read);
        await driver.ConnectAsync();

        await Assert.ThrowsAsync<NotSupportedException>(() => driver.PingAsync());
    }

    [Fact]
    public void KeepAlive_RequiresHealthCapability()
    {
        var configuration = new DriverConfiguration
        {
            KeepAliveInterval = TimeSpan.FromSeconds(1),
            ProbeAddress = "R0",
        };

        Assert.Throws<InvalidOperationException>(
            () => new MockDriver(configuration, capabilities: DriverCapabilities.Read));
    }
}

public class NativeSubscriptionTests
{
    [Fact]
    public async Task DoSubscribeAsync_IsUsedInsteadOfPolling()
    {
        var handle = new TrackingAsyncDisposable();
        await using var driver = new MockDriver();
        driver.SubscribeHandler = (_, _, _, _) => Task.FromResult<IAsyncDisposable?>(handle);
        await driver.ConnectAsync();

        var subscription = await driver.SubscribeAsync(
            driver.AddressParser.Parse("R0"),
            TimeSpan.FromMilliseconds(10),
            _ => Task.CompletedTask);
        await Task.Delay(60);

        Assert.Equal(0, driver.ReadCount);

        await subscription.DisposeAsync();
        Assert.True(handle.Disposed);
    }

    [Fact]
    public async Task SubscribeAsync_FallsBackToPollingWhenNoHookIsProvided()
    {
        await using var driver = new MockDriver();
        await driver.ConnectAsync();

        var subscription = await driver.SubscribeAsync(
            driver.AddressParser.Parse("R0"),
            TimeSpan.FromMilliseconds(10),
            _ => Task.CompletedTask);
        await Task.Delay(60);

        Assert.True(driver.ReadCount > 0);

        await subscription.DisposeAsync();
    }

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

public class SharedTransportTests
{
    [Fact]
    public void SharedTransportFactory_ReusesOneTransport()
    {
        var transport = new MockTransport();
        var factory = new SharedTransportFactory(() => transport);
        var configuration = new DriverConfiguration();

        Assert.Same(transport, factory.Create(configuration));
        Assert.Same(transport, factory.Create(configuration));
    }

    [Fact]
    public async Task SharedTransportFactory_DisposesTransportOnce()
    {
        var transport = new MockTransport();
        var factory = new SharedTransportFactory(() => transport);
        _ = factory.Create(new DriverConfiguration());

        await factory.DisposeAsync();
        await factory.DisposeAsync();

        Assert.True(transport.Disposed);
        Assert.Throws<ObjectDisposedException>(() => factory.Create(new DriverConfiguration()));
    }

    [Fact]
    public void CustomTransport_WithoutFactory_IsRejected()
    {
        var configuration = new DriverConfiguration { Transport = TransportKind.Custom };

        Assert.Throws<ArgumentException>(configuration.Validate);
    }

    [Fact]
    public async Task ModbusRtu_OverInjectedTransport_SharesOneLink()
    {
        var transport = new MockTransport();
        transport.ResponseFactory = request => request.ToArray();
        await using var factory = new SharedTransportFactory(() => transport);

        var first = CreateRtuClient(factory, unitId: 1);
        var second = CreateRtuClient(factory, unitId: 2);
        await using var firstClient = first;
        await using var secondClient = second;

        await firstClient.ConnectAsync();
        await secondClient.ConnectAsync();
        await firstClient.WriteSingleRegisterAsync(1, 0, 11);
        await secondClient.WriteSingleRegisterAsync(2, 0, 22);

        Assert.Equal(2, transport.Writes.Count);
        Assert.Equal(1, transport.Writes[0][0]);
        Assert.Equal(2, transport.Writes[1][0]);
    }

    [Fact]
    public void Modbus_CustomTransport_RequiresKnownFraming()
    {
        var configuration = new DriverConfiguration
        {
            Transport = TransportKind.Custom,
            TransportFactory = new SharedTransportFactory(() => new MockTransport()),
        };
        configuration.Options["framing"] = "ascii";

        Assert.Throws<ArgumentException>(() => ModbusClientFactory.Create(configuration, 1));
    }

    [Fact]
    public async Task Drivers_SharingRequestLock_DoNotRunRequestsConcurrently()
    {
        var sharedLock = new RequestLock();
        var probe = new ConcurrencyProbe();

        await using var first = new MockDriver(new DriverConfiguration { RequestLock = sharedLock });
        await using var second = new MockDriver(new DriverConfiguration { RequestLock = sharedLock });
        first.Probe = probe;
        second.Probe = probe;
        first.ReadDelay = TimeSpan.FromMilliseconds(60);
        second.ReadDelay = TimeSpan.FromMilliseconds(60);

        await first.ConnectAsync();
        await second.ConnectAsync();

        await Task.WhenAll(
            first.ReadAsync(first.AddressParser.Parse("R0")),
            second.ReadAsync(second.AddressParser.Parse("R0")));

        Assert.Equal(1, probe.MaxActive);
    }

    private static ModbusRtuClient CreateRtuClient(ITransportFactory factory, byte unitId)
    {
        var configuration = new DriverConfiguration
        {
            Transport = TransportKind.Custom,
            TransportFactory = factory,
            RequestLock = ((SharedTransportFactory)factory).RequestLock,
        };
        configuration.Options["unitId"] = unitId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        configuration.Options["framing"] = "rtu";

        return (ModbusRtuClient)ModbusClientFactory.Create(configuration, unitId);
    }
}

public class FrameLoggingTests
{
    [Fact]
    public async Task LoggingTransport_WritesHexDumps()
    {
        var inner = new MockTransport();
        inner.EnqueueResponse(0x01, 0x02);
        var logger = new RecordingLogger();
        await using var transport = new LoggingTransport(inner, logger, "test");

        await transport.WriteAsync(new byte[] { 0xAA, 0x01 });
        var buffer = new byte[2];
        var read = await transport.ReadAsync(buffer);

        Assert.Equal(2, read);
        Assert.True(logger.Contains(PlcLogLevel.Trace, "test TX AA 01"));
        Assert.True(logger.Contains(PlcLogLevel.Trace, "test RX 01 02"));
    }

    [Fact]
    public async Task ModbusTcpClient_LogFrames_ReportsTrafficThroughTheLogger()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.Mbap(1, 1, 0x03, 0x02, 0x00, 0x2A));
        var logger = new RecordingLogger();
        await using var client = new ModbusTcpClient(transport, new ModbusTcpOptions
        {
            LogFrames = true,
            Logger = logger,
            Timeout = TimeSpan.FromSeconds(1),
        });

        await client.ConnectAsync();
        var values = await client.ReadHoldingRegistersAsync(1, 0, 1);

        Assert.Equal(42, values[0]);
        Assert.True(logger.Contains(PlcLogLevel.Trace, "TX"));
        Assert.True(logger.Contains(PlcLogLevel.Trace, "RX"));
    }

    [Fact]
    public async Task S7Client_LogFrames_ReportsTrafficThroughTheLogger()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(ProtocolFrames.CrConfirm());
        transport.EnqueueResponse(ProtocolFrames.SetupAck(1));
        var logger = new RecordingLogger();
        await using var client = new SiameseCat.Protocols.S7.S7Client(transport, new SiameseCat.Protocols.S7.S7Options
        {
            LogFrames = true,
            Logger = logger,
        });

        await client.ConnectAsync();

        Assert.True(logger.Contains(PlcLogLevel.Trace, "TX"));
        Assert.True(logger.Contains(PlcLogLevel.Trace, "RX"));
    }
}
