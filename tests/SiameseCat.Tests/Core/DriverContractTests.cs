using SiameseCat.Communication;
using SiameseCat.Communication.Drivers;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Core;

/// <summary>Phase 0: validates the driver contract with the in-memory mock driver.</summary>
public class DriverContractTests
{
    [Fact]
    public async Task Connect_TransitionsThroughConnectingToConnected()
    {
        await using var driver = new MockDriver();
        var states = new List<DriverState>();
        driver.StateChanged += (_, args) => states.Add(args.NewState);

        Assert.Equal(DriverState.Disconnected, driver.State);

        await driver.ConnectAsync();

        Assert.Equal(DriverState.Connected, driver.State);
        Assert.Equal(new[] { DriverState.Connecting, DriverState.Connected }, states);
        Assert.Equal(1, driver.ConnectCount);
    }

    [Fact]
    public async Task Connect_IsIdempotent()
    {
        await using var driver = new MockDriver();

        await driver.ConnectAsync();
        await driver.ConnectAsync();

        Assert.Equal(1, driver.ConnectCount);
    }

    [Fact]
    public async Task ReadBeforeConnect_Throws()
    {
        await using var driver = new MockDriver();
        var address = driver.AddressParser.Parse("R0");

        var exception = await Assert.ThrowsAsync<CommunicationException>(
            () => driver.ReadAsync(address));

        Assert.Contains("ConnectAsync", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsValue()
    {
        await using var driver = new MockDriver();
        await driver.ConnectAsync();
        var address = driver.AddressParser.Parse("R5");

        await driver.WriteAsync(address, DeviceValue.FromUInt16(1234));
        var value = await driver.ReadAsync(address);

        Assert.Equal(DeviceValue.FromUInt16(1234), value);
    }

    [Fact]
    public async Task ReadBatch_ReturnsValuesInRequestOrder()
    {
        await using var driver = new MockDriver();
        await driver.ConnectAsync();

        var first = driver.AddressParser.Parse("R1");
        var second = driver.AddressParser.Parse("R2");
        var third = driver.AddressParser.Parse("R3");
        await driver.WriteAsync(first, DeviceValue.FromUInt16(1));
        await driver.WriteAsync(second, DeviceValue.FromUInt16(2));
        await driver.WriteAsync(third, DeviceValue.FromUInt16(3));

        var values = await driver.ReadBatchAsync(new[] { third, first, second });

        Assert.Equal(
            new[] { DeviceValue.FromUInt16(3), DeviceValue.FromUInt16(1), DeviceValue.FromUInt16(2) },
            values);
    }

    [Fact]
    public async Task TransientFailure_TriggersRetryAndReconnect()
    {
        var configuration = new DriverConfiguration { RetryCount = 2, RetryDelay = TimeSpan.Zero };
        await using var driver = new MockDriver(configuration);
        await driver.ConnectAsync();

        var address = driver.AddressParser.Parse("R0");
        await driver.WriteAsync(address, DeviceValue.FromUInt16(9));
        driver.TransientFailuresRemaining = 1;

        var value = await driver.ReadAsync(address);

        Assert.Equal(DeviceValue.FromUInt16(9), value);
        Assert.Equal(2, driver.ConnectCount);
        Assert.Equal(1, driver.DisconnectCount);
        Assert.Equal(1, driver.Metrics.Snapshot().Retries);
    }

    [Fact]
    public async Task Write_IsNotRetriedByDefault()
    {
        var configuration = new DriverConfiguration { RetryCount = 2, RetryDelay = TimeSpan.Zero };
        await using var driver = new MockDriver(configuration);
        await driver.ConnectAsync();

        driver.TransientFailuresRemaining = 1;

        await Assert.ThrowsAsync<CommunicationTimeoutException>(
            () => driver.WriteAsync(driver.AddressParser.Parse("R0"), DeviceValue.FromUInt16(1)));

        Assert.Equal(1, driver.ConnectCount);
    }

    [Fact]
    public async Task Write_IsRetriedWhenRetryWritesIsEnabled()
    {
        var configuration = new DriverConfiguration
        {
            RetryCount = 1,
            RetryWrites = true,
            RetryDelay = TimeSpan.Zero,
        };

        await using var driver = new MockDriver(configuration);
        await driver.ConnectAsync();
        driver.TransientFailuresRemaining = 1;

        await driver.WriteAsync(driver.AddressParser.Parse("R0"), DeviceValue.FromUInt16(1));

        Assert.Equal(2, driver.ConnectCount);
    }

    [Fact]
    public async Task Metrics_CountRequestsFailuresAndLatency()
    {
        await using var driver = new MockDriver();
        await driver.ConnectAsync();

        await driver.WriteAsync(driver.AddressParser.Parse("R0"), DeviceValue.FromUInt16(1));
        await driver.ReadAsync(driver.AddressParser.Parse("R0"));

        var snapshot = driver.Metrics.Snapshot();

        Assert.Equal(2, snapshot.Requests);
        Assert.Equal(0, snapshot.Failures);
        Assert.Equal(2, snapshot.BytesRead);
        Assert.Equal(2, snapshot.BytesWritten);
        Assert.True(snapshot.AverageLatencyMs >= 0);
    }

    [Fact]
    public async Task Subscribe_NotifiesOnValueChange()
    {
        await using var driver = new MockDriver();
        await driver.ConnectAsync();
        var address = driver.AddressParser.Parse("R0");
        await driver.WriteAsync(address, DeviceValue.FromUInt16(1));

        var changes = new List<DeviceValue>();
        var subscription = await driver.SubscribeAsync(
            address,
            TimeSpan.FromMilliseconds(20),
            value =>
            {
                lock (changes)
                {
                    changes.Add(value);
                }

                return Task.CompletedTask;
            });

        await Task.Delay(100);
        await driver.WriteAsync(address, DeviceValue.FromUInt16(2));
        await Task.Delay(150);
        await subscription.DisposeAsync();

        lock (changes)
        {
            Assert.Contains(DeviceValue.FromUInt16(1), changes);
            Assert.Contains(DeviceValue.FromUInt16(2), changes);
        }
    }

    [Fact]
    public async Task Dispose_DisconnectsAndRejectsFurtherCalls()
    {
        var driver = new MockDriver();
        await driver.ConnectAsync();

        await driver.DisposeAsync();
        await driver.DisposeAsync();

        Assert.Equal(DriverState.Disconnected, driver.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => driver.ReadAsync(driver.AddressParser.Parse("R0")));
    }

    [Fact]
    public async Task ReadTagAsync_UsesTagDataType()
    {
        await using var driver = new MockDriver();
        await driver.ConnectAsync();
        var tag = new DeviceTag("speed", "R7", DeviceDataType.Int16, scale: 0.5);

        await driver.WriteAsync(driver.AddressParser.Parse("R7", DeviceDataType.Int16), DeviceValue.FromInt16(100));
        var value = await driver.ReadTagAsync(tag);

        Assert.Equal((short)100, value.AsInt16());
        Assert.Equal(50, tag.ToEngineering(value.AsDouble()), 6);
    }

    [Fact]
    public async Task ReadTagsAsync_ReadsAllTags()
    {
        await using var driver = new MockDriver();
        await driver.ConnectAsync();
        await driver.WriteAsync(driver.AddressParser.Parse("R0"), DeviceValue.FromUInt16(1));
        await driver.WriteAsync(driver.AddressParser.Parse("R1"), DeviceValue.FromUInt16(2));

        var values = await driver.ReadTagsAsync(new[]
        {
            new DeviceTag("a", "R0", DeviceDataType.UInt16),
            new DeviceTag("b", "R1", DeviceDataType.UInt16),
        });

        Assert.Equal(new[] { DeviceValue.FromUInt16(1), DeviceValue.FromUInt16(2) }, values);
    }
}

public class DriverRegistryTests
{
    [Fact]
    public void Create_ReturnsRegisteredDriver()
    {
        var registry = new DriverRegistry().Register(new ModbusDriverFactoryForTests());

        Assert.True(registry.Contains("mock-driver"));
        var driver = registry.Create("mock-driver", new DriverConfiguration());

        Assert.IsType<MockDriver>(driver);
    }

    [Fact]
    public void Create_UnknownDriver_ListsKnownDrivers()
    {
        var registry = new DriverRegistry().Register(new ModbusDriverFactoryForTests());

        var exception = Assert.Throws<KeyNotFoundException>(
            () => registry.Create("missing", new DriverConfiguration()));

        Assert.Contains("mock-driver", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_ReplacesExistingFactory()
    {
        var registry = new DriverRegistry()
            .Register(new ModbusDriverFactoryForTests())
            .Register(new ModbusDriverFactoryForTests());

        Assert.Single(registry.Factories);
    }

    [Fact]
    public void CreateDefaultRegistry_ContainsBuiltInDrivers()
    {
        var registry = DriverRegistryExtensions.CreateDefaultRegistry();

        Assert.True(registry.Contains("modbus"));
        Assert.True(registry.Contains("s7"));
        Assert.False(registry.Contains("inovance"));
        Assert.Equal(2, registry.Descriptors.Count());
    }

    private sealed class ModbusDriverFactoryForTests : IDeviceDriverFactory
    {
        public DriverDescriptor Descriptor { get; } = new(
            "mock-driver", "Mock", "Test driver", TransportKind.Tcp);

        public IDeviceDriver Create(
            DriverConfiguration configuration,
            ICommunicationLogger? logger = null,
            CommunicationMetrics? metrics = null) => new MockDriver(configuration, logger, metrics);
    }
}
