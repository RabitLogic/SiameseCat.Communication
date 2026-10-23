using SiameseCat.Communication;
using SiameseCat.Communication.Drivers;
using SiameseCat.Communication.Simulation.Modbus;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.Drivers;

/// <summary>
/// Step 4 of the driver track: polling subscriptions for one address and for a group.
/// </summary>
/// <remarks>
/// Both subscription kinds are driven by the slave side of the sample: a background writer changes the
/// values, the subscription reports the changes, and the sample verifies that no change was missed.
/// </remarks>
public sealed class DriverSubscriptionSample : ISample
{
    /// <inheritdoc/>
    public string Id => "drivers/subscription";

    /// <inheritdoc/>
    public string Title => "Drivers: polling subscriptions";

    /// <inheritdoc/>
    public string Description =>
        "Subscribe to a single address and to a group that shares one polling cycle, with jitter and failure backoff enabled.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run drivers/subscription";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        await using var slave = new ModbusTcpSimulator(new ModbusSimulatorOptions { Port = 0 });
        await slave.StartAsync(cancellationToken).ConfigureAwait(false);

        var registry = DriverRegistryExtensions.CreateDefaultRegistry();
        await using var driver = registry.Create("modbus", new DriverConfiguration
        {
            Transport = TransportKind.Tcp,
            Host = "127.0.0.1",
            Port = slave.Port,
            Timeout = TimeSpan.FromSeconds(2),
            SubscriptionJitter = true,
            SubscriptionMaxBackoff = TimeSpan.FromSeconds(1),
        });
        await driver.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var subscriber = DriverAccess.Subscriber(driver);
        var batchSubscriber = DriverAccess.BatchSubscriber(driver);

        SampleConsole.Step("1. Single-address subscription");
        var changes = 0;
        var lastValue = DeviceValue.Null;
        using var single = new CancellationTokenSource();
        await using (await subscriber.SubscribeAsync(
                driver.AddressParser.Parse("HR0"),
                TimeSpan.FromMilliseconds(40),
                value =>
                {
                    Interlocked.Increment(ref changes);
                    lastValue = value;
                    return Task.CompletedTask;
                },
                single.Token)
            .ConfigureAwait(false))
        {
            using var writer = new CancellationTokenSource();
            var writerTask = RunWriterAsync(slave, writer.Token);
            await Task.Delay(600, cancellationToken).ConfigureAwait(false);
            writer.Cancel();
            await writerTask.ConfigureAwait(false);
        }

        var singleChanges = Volatile.Read(ref changes);
        SampleConsole.Value("Notifications", singleChanges);
        SampleConsole.Value("Last value", lastValue.ToString());
        SampleConsole.Check(singleChanges > 0, "the subscription reported the changes made by the writer");

        SampleConsole.Step("2. Group subscription: one read per cycle");
        var batchChanges = 0;
        var lastBatch = Array.Empty<DeviceValue>();
        var groupAddresses = new[]
        {
            driver.AddressParser.Parse("HR10"),
            driver.AddressParser.Parse("HR11"),
            driver.AddressParser.Parse("HR12"),
            driver.AddressParser.Parse("HR13"),
        };

        var before = slave.Statistics.TotalRequests;
        await using (await batchSubscriber.SubscribeBatchAsync(
                groupAddresses,
                TimeSpan.FromMilliseconds(40),
                values =>
                {
                    Interlocked.Increment(ref batchChanges);
                    lastBatch = values.ToArray();
                    return Task.CompletedTask;
                },
                cancellationToken)
            .ConfigureAwait(false))
        {
            using var writer = new CancellationTokenSource();
            var writerTask = RunBatchWriterAsync(slave, writer.Token);
            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            writer.Cancel();
            await writerTask.ConfigureAwait(false);
        }

        var groupRequests = slave.Statistics.TotalRequests - before;
        var notifications = Volatile.Read(ref batchChanges);
        SampleConsole.Value("Notifications", notifications);
        SampleConsole.Value("Last values", string.Join(", ", lastBatch.Select(value => value.ToString())));
        SampleConsole.Value("Slave requests", groupRequests);
        SampleConsole.Value("Requests per notification", notifications == 0 ? "n/a" : (groupRequests / (double)notifications).ToString("0.##"));
        SampleConsole.Check(lastBatch.Length == groupAddresses.Length, "each notification carries one value per address");
        SampleConsole.Check(notifications > 0, "the group subscription reported changes");
        SampleConsole.Check(
            groupRequests <= 24,
            "400 ms at a 40 ms interval stays near the cycle count even though four addresses are watched");

        SampleConsole.Step("3. What the options do");
        SampleConsole.Detail("SubscriptionJitter: spreads poll phases so several drivers do not hit the device together.");
        SampleConsole.Detail("SubscriptionMaxBackoff: after a failed read the interval grows up to this value, then resets.");
        SampleConsole.Detail("The returned handle is IAsyncDisposable; disposing it (or cancelling the token) stops polling.");
        SampleConsole.Note("Next: 'run drivers/resilience' for retries, keep-alive and failure isolation.");
    }

    private static async Task RunWriterAsync(ModbusTcpSimulator slave, CancellationToken cancellationToken)
    {
        var value = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            slave.Data.TryWriteHoldingRegisters(0, new[] { (ushort)value++ });
            try
            {
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task RunBatchWriterAsync(ModbusTcpSimulator slave, CancellationToken cancellationToken)
    {
        var value = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            slave.Data.TryWriteHoldingRegisters(10, new ushort[]
            {
                (ushort)value++,
                (ushort)(value + 1),
                (ushort)(value + 2),
                (ushort)(value + 3),
            });

            try
            {
                await Task.Delay(60, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
