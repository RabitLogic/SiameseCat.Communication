using System.Diagnostics;
using SiameseCat.Core;
using SiameseCat.Drivers;
using SiameseCat.Samples.Infrastructure;
using SiameseCat.Server.Modbus;

namespace SiameseCat.Samples.Drivers;

/// <summary>
/// Step 5 of the driver track: retries, keep-alive and failure isolation.
/// </summary>
/// <remarks>
/// Every behaviour here is configured on <see cref="DriverConfiguration"/>; there is no hidden state in
/// the driver, so the same settings describe the same policy for Modbus, S7 and brand drivers.
/// </remarks>
public sealed class DriverResilienceSample : ISample
{
    /// <inheritdoc/>
    public string Id => "drivers/resilience";

    /// <inheritdoc/>
    public string Title => "Drivers: retries, keep-alive and failures";

    /// <inheritdoc/>
    public string Description =>
        "Watch retry timing against an unreachable device, then stop the slave and see how ping, metrics and state react.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run drivers/resilience";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        var registry = DriverRegistryExtensions.CreateDefaultRegistry();

        SampleConsole.Step("1. Retries against an unreachable device");
        var closedPort = FindClosedPort();
        await using (var unreachable = registry.Create("modbus", new DriverConfiguration
        {
            Transport = TransportKind.Tcp,
            Host = "127.0.0.1",
            Port = closedPort,
            ConnectTimeout = TimeSpan.FromMilliseconds(300),
            Timeout = TimeSpan.FromMilliseconds(300),
            RetryCount = 2,
            RetryDelay = TimeSpan.FromMilliseconds(100),
        }))
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await unreachable.ConnectAsync(cancellationToken).ConfigureAwait(false);
                SampleConsole.Warn("Connecting to a closed port succeeded, which was not expected.");
            }
            catch (CommunicationException ex)
            {
                stopwatch.Stop();
                SampleConsole.Value("Exception", ex.GetType().Name);
                SampleConsole.Value("Elapsed", $"{stopwatch.Elapsed.TotalMilliseconds:0} ms");
                SampleConsole.Value("Retries recorded", unreachable.Metrics.Snapshot().Retries);
                SampleConsole.Check(
                    stopwatch.Elapsed >= TimeSpan.FromMilliseconds(200),
                    "the configured retries delay the failure instead of failing on the first attempt");
            }
        }

        SampleConsole.Step("2. A healthy session keeps counters");
        var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        await slave.StartAsync(cancellationToken).ConfigureAwait(false);
        slave.Data.TryWriteHoldingRegisters(0, new ushort[] { 1 });

        var configuration = new DriverConfiguration
        {
            Transport = TransportKind.Tcp,
            Host = "127.0.0.1",
            Port = slave.Port,
            Timeout = TimeSpan.FromMilliseconds(500),
            RetryCount = 1,
            RetryDelay = TimeSpan.FromMilliseconds(50),
            ProbeAddress = "HR0",
            KeepAliveInterval = TimeSpan.FromMilliseconds(100),
            KeepAliveFailureThreshold = 2,
        };

        await using var driver = registry.Create("modbus", configuration);
        driver.StateChanged += (_, e) => SampleConsole.Detail($"state {e.OldState} -> {e.NewState}");
        await driver.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var reader = DriverAccess.Reader(driver);
        var health = DriverAccess.Health(driver);

        _ = await reader.ReadAsync(driver.AddressParser.Parse("HR0"), cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Ping", await health.PingAsync(cancellationToken).ConfigureAwait(false));
        SampleConsole.Value("State", driver.State);

        SampleConsole.Step("3. The device disappears");
        await slave.DisposeAsync().ConfigureAwait(false);
        SampleConsole.Detail("Slave stopped; the keep-alive loop keeps probing in the background.");

        try
        {
            await reader.ReadAsync(driver.AddressParser.Parse("HR0"), cancellationToken).ConfigureAwait(false);
            SampleConsole.Warn("Reading from a stopped slave succeeded, which was not expected.");
        }
        catch (CommunicationException ex)
        {
            SampleConsole.Value("Read exception", ex.GetType().Name);
        }

        var ping = await health.PingAsync(cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Ping after the failure", ping);
        SampleConsole.Check(!ping, "PingAsync reports the outage instead of throwing");

        await WaitForStateAsync(driver, DriverState.Faulted, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("State", driver.State);

        var snapshot = driver.Metrics.Snapshot();
        SampleConsole.Value("Requests", snapshot.Requests);
        SampleConsole.Value("Failures", snapshot.Failures);
        SampleConsole.Value("Failure kinds", snapshot.FailureKinds);
        SampleConsole.Value("Timeouts", snapshot.FailureKinds.Timeouts);
        SampleConsole.Check(snapshot.Failures > 0, "failures are counted by kind, not just as an error message");

        SampleConsole.Step("4. Recovery policy");
        SampleConsole.Detail("A faulted driver accepts ConnectAsync again once the device answers.");
        SampleConsole.Detail("KeepAliveInterval = 0 disables the background probe; the default is off.");
        SampleConsole.Detail("Wire an IPlcLogger to see every retry and state transition on the log.");
        SampleConsole.Note("Next: 'run drivers/hosting' to register drivers and slaves in dependency injection.");
    }

    private static async Task WaitForStateAsync(IPlcDriver driver, DriverState state, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (driver.State != state && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
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
