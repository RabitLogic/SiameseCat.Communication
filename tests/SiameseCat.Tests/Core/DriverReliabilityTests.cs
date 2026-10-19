using SiameseCat.Communication;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Core;

public class RetryBackoffTests
{
    [Fact]
    public void GetRetryDelay_BacksOffExponentiallyAndCaps()
    {
        var configuration = new DriverConfiguration
        {
            RetryDelay = TimeSpan.FromMilliseconds(100),
            MaxRetryDelay = TimeSpan.FromMilliseconds(250),
            RetryJitter = false,
        };

        Assert.Equal(TimeSpan.FromMilliseconds(100), configuration.GetRetryDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(200), configuration.GetRetryDelay(2));
        Assert.Equal(TimeSpan.FromMilliseconds(250), configuration.GetRetryDelay(3));
        Assert.Equal(TimeSpan.FromMilliseconds(250), configuration.GetRetryDelay(10));
    }

    [Fact]
    public void GetRetryDelay_AppliesJitterWithinHalfToFullRange()
    {
        var configuration = new DriverConfiguration
        {
            RetryDelay = TimeSpan.FromMilliseconds(200),
            RetryJitter = true,
        };

        for (var i = 0; i < 50; i++)
        {
            var delay = configuration.GetRetryDelay(1);

            Assert.InRange(delay.TotalMilliseconds, 100, 200);
        }
    }

    [Fact]
    public void GetRetryDelay_IsZeroWithoutBaseDelay()
    {
        var configuration = new DriverConfiguration { RetryDelay = TimeSpan.Zero };

        Assert.Equal(TimeSpan.Zero, configuration.GetRetryDelay(3));
    }

    [Fact]
    public void Validate_RejectsKeepAliveWithoutProbeAddress()
    {
        var configuration = new DriverConfiguration
        {
            KeepAliveInterval = TimeSpan.FromSeconds(1),
        };

        Assert.Throws<ArgumentException>(configuration.Validate);
    }

    [Fact]
    public void Validate_RejectsZeroKeepAliveThreshold()
    {
        var configuration = new DriverConfiguration
        {
            ProbeAddress = "R0",
            KeepAliveInterval = TimeSpan.FromSeconds(1),
            KeepAliveFailureThreshold = 0,
        };

        Assert.Throws<ArgumentOutOfRangeException>(configuration.Validate);
    }
}

public class DriverReliabilityTests
{
    [Fact]
    public async Task ConnectAsync_RetriesTransientFailures()
    {
        var driver = new MockDriver(new DriverConfiguration
        {
            RetryCount = 2,
            RetryDelay = TimeSpan.FromMilliseconds(1),
            RetryJitter = false,
        });
        driver.ConnectFailuresRemaining = 1;

        await driver.ConnectAsync();

        Assert.Equal(2, driver.ConnectCount);
        Assert.Equal(DriverState.Connected, driver.State);

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_StopsAfterExhaustingRetries()
    {
        var driver = new MockDriver(new DriverConfiguration
        {
            RetryCount = 1,
            RetryDelay = TimeSpan.FromMilliseconds(1),
            RetryJitter = false,
        });
        driver.ConnectFailuresRemaining = 5;

        await Assert.ThrowsAsync<CommunicationTimeoutException>(() => driver.ConnectAsync());

        Assert.Equal(2, driver.ConnectCount);
        Assert.Equal(DriverState.Faulted, driver.State);

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task PingAsync_ReturnsTrueWhenProbeReadSucceeds()
    {
        var logger = new RecordingLogger();
        var driver = new MockDriver(
            new DriverConfiguration
            {
                ProbeAddress = "R0",
                Timeout = TimeSpan.FromSeconds(1),
            },
            logger);
        await driver.ConnectAsync();

        Assert.True(await driver.PingAsync());

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task PingAsync_ReturnsFalseWhenProbeFails()
    {
        var logger = new RecordingLogger();
        var driver = new MockDriver(
            new DriverConfiguration
            {
                ProbeAddress = "R0",
                Timeout = TimeSpan.FromSeconds(1),
            },
            logger);
        await driver.ConnectAsync();
        driver.TransientFailuresRemaining = 1;

        Assert.False(await driver.PingAsync());

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task PingAsync_RequiresProbeAddress()
    {
        var driver = new MockDriver(new DriverConfiguration());
        await driver.ConnectAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.PingAsync());

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task KeepAlive_ReconnectsAfterConsecutiveFailures()
    {
        var driver = new MockDriver(new DriverConfiguration
        {
            ProbeAddress = "R0",
            KeepAliveInterval = TimeSpan.FromMilliseconds(20),
            KeepAliveFailureThreshold = 2,
            Timeout = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.FromMilliseconds(1),
            RetryJitter = false,
        });

        await driver.ConnectAsync();
        Assert.Equal(1, driver.ConnectCount);

        driver.TransientFailuresRemaining = 2;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (driver.ConnectCount < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(2, driver.ConnectCount);
        Assert.Equal(DriverState.Connected, driver.State);

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task ReadAsync_UsesPerCallTimeoutWhenShorterThanConfiguration()
    {
        var driver = new MockDriver(new DriverConfiguration
        {
            Timeout = TimeSpan.FromSeconds(5),
            RetryDelay = TimeSpan.FromMilliseconds(1),
            RetryJitter = false,
        });
        await driver.ConnectAsync();
        driver.ReadDelay = TimeSpan.FromMilliseconds(200);

        await Assert.ThrowsAsync<CommunicationTimeoutException>(
            () => driver.ReadAsync(driver.AddressParser.Parse("R0"), TimeSpan.FromMilliseconds(20)));

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task ReadAsync_PerCallTimeoutOverridesShortConfiguration()
    {
        var driver = new MockDriver(new DriverConfiguration
        {
            Timeout = TimeSpan.FromMilliseconds(20),
            RetryDelay = TimeSpan.FromMilliseconds(1),
            RetryJitter = false,
        });
        await driver.ConnectAsync();
        driver.ReadDelay = TimeSpan.FromMilliseconds(80);

        var value = await driver.ReadAsync(
            driver.AddressParser.Parse("R0"),
            TimeSpan.FromSeconds(2));

        Assert.Equal(PlcValue.Null, value);

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task ReadAsync_RejectsNonPositiveTimeout()
    {
        var driver = new MockDriver(new DriverConfiguration());
        await driver.ConnectAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => driver.ReadAsync(driver.AddressParser.Parse("R0"), TimeSpan.Zero));

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task DisconnectAsync_StopsKeepAlive()
    {
        var driver = new MockDriver(new DriverConfiguration
        {
            ProbeAddress = "R0",
            KeepAliveInterval = TimeSpan.FromMilliseconds(10),
            KeepAliveFailureThreshold = 1,
            Timeout = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.FromMilliseconds(1),
            RetryJitter = false,
        });

        await driver.ConnectAsync();
        await driver.DisconnectAsync();

        driver.TransientFailuresRemaining = 100;
        await Task.Delay(120);

        Assert.Equal(1, driver.ConnectCount);
        Assert.Equal(DriverState.Disconnected, driver.State);

        await driver.DisposeAsync();
    }
}
