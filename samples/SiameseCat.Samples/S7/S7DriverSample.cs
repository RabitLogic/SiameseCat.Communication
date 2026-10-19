using SiameseCat.Communication;
using SiameseCat.Communication.Server.S7;
using SiameseCat.Communication.Siemens;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.S7;

/// <summary>
/// Step 2 of the S7 track: the <c>siemens</c> driver, which adds protocol-neutral addresses,
/// scaling-free typed values, batch requests, subscriptions and metrics on top of <c>S7Client</c>.
/// </summary>
public sealed class S7DriverSample : ISample
{
    /// <inheritdoc/>
    public string Id => "s7/driver";

    /// <inheritdoc/>
    public string Title => "S7: the siemens driver (tags, arrays, batch)";

    /// <inheritdoc/>
    public string Description =>
        "Resolve the driver from the registry, parse S7 tag addresses, exchange typed arrays and read several tags in one PDU.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run s7/driver [--host 192.168.0.10] [--port 102] [--rack 0] [--slot 1]";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        var simulated = !arguments.Has("host");
        var host = arguments.Get("host", "127.0.0.1");
        var port = arguments.GetInt("port", 102);

        await using var simulator = simulated ? await StartSimulatorAsync(cancellationToken).ConfigureAwait(false) : null;
        if (simulated)
        {
            port = simulator!.Port;
            SampleConsole.Note($"No --host supplied, using the in-process S7 slave on 127.0.0.1:{port}.");
        }

        SampleConsole.Step("1. Resolve the driver from the registry");
        var registry = new DriverRegistry().Register(new SiemensDriverFactory());
        foreach (var descriptor in registry.Descriptors)
        {
            SampleConsole.Value("Driver", $"{descriptor.Id} ({descriptor.Name})");
            SampleConsole.Value("Transports", descriptor.Transports.ToString());
            SampleConsole.Value("Capabilities", descriptor.Capabilities.ToString());
            SampleConsole.Value("Address examples", string.Join(", ", descriptor.AddressExamples));
        }

        var configuration = new DriverConfiguration
        {
            Transport = TransportKind.Tcp,
            Host = host,
            Port = port,
            Timeout = TimeSpan.FromSeconds(3),
            RetryCount = 1,
            RetryDelay = TimeSpan.FromMilliseconds(100),
            ProbeAddress = "DB1.DBW0",
        };
        configuration.Options["rack"] = arguments.GetInt("rack", 0).ToString();
        configuration.Options["slot"] = arguments.GetInt("slot", 1).ToString();

        await using var driver = registry.Create("siemens", configuration);

        SampleConsole.Step("2. Connect");
        driver.StateChanged += (_, e) => SampleConsole.Detail($"state {e.OldState} -> {e.NewState}");
        await driver.ConnectAsync(cancellationToken).ConfigureAwait(false);

        // The driver contract is split per capability; ask for the ones this sample uses.
        var reader = DriverAccess.Reader(driver);
        var writer = DriverAccess.Writer(driver);
        var batchReader = DriverAccess.BatchReader(driver);
        var batchWriter = DriverAccess.BatchWriter(driver);
        var health = DriverAccess.Health(driver);

        SampleConsole.Value("State", driver.State);
        SampleConsole.Value("Ping", await health.PingAsync(cancellationToken).ConfigureAwait(false));

        SampleConsole.Step("3. Typed addresses instead of raw bytes");
        await writer.WriteAsync(driver.AddressParser.Parse("DB1.DBW2"), PlcValue.FromUInt16(1234), cancellationToken)
            .ConfigureAwait(false);
        await writer.WriteAsync(driver.AddressParser.Parse("M0.1"), PlcValue.FromBool(true), cancellationToken)
            .ConfigureAwait(false);
        await writer.WriteAsync(
                driver.AddressParser.Parse("DB1.DBB12", PlcDataType.String).WithLength(20),
                PlcValue.FromString("SiameseCat"),
                cancellationToken)
            .ConfigureAwait(false);

        var word = await reader.ReadAsync(driver.AddressParser.Parse("DB1.DBW2"), cancellationToken).ConfigureAwait(false);
        var flag = await reader.ReadAsync(driver.AddressParser.Parse("M0.1"), cancellationToken).ConfigureAwait(false);
        var text = await reader.ReadAsync(
                driver.AddressParser.Parse("DB1.DBB12", PlcDataType.String).WithLength(20),
                cancellationToken)
            .ConfigureAwait(false);

        SampleConsole.Value("DB1.DBW2", word.AsUInt16());
        SampleConsole.Value("M0.1", flag.AsBool());
        SampleConsole.Value("DB1.DBB12 STRING[20]", text.AsString());

        SampleConsole.Step("4. Numeric arrays: one PDU for many values");
        var arrayAddress = driver.AddressParser.Parse("DB1.DBW20", PlcDataType.UInt16).WithLength(4);
        await writer.WriteAsync(arrayAddress, PlcValue.FromUInt16Array(new ushort[] { 10, 20, 30, 40 }), cancellationToken)
            .ConfigureAwait(false);
        var array = await reader.ReadAsync(arrayAddress, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("DB1.DBW20..27", string.Join(", ", array.AsUInt16Array()));
        if (simulated)
        {
            SampleConsole.Check(
                array.AsUInt16Array().SequenceEqual(new ushort[] { 10, 20, 30, 40 }),
                "the UInt16[4] array survived the round trip");
        }

        SampleConsole.Step("5. Batch read: three tags, one request");
        var batch = await batchReader.ReadBatchAsync(
                new[]
                {
                    driver.AddressParser.Parse("DB1.DBW2"),
                    driver.AddressParser.Parse("DB1.DBW20"),
                    driver.AddressParser.Parse("MW10"),
                },
                cancellationToken)
            .ConfigureAwait(false);
        SampleConsole.Value("Values", string.Join(", ", batch.Select(value => value.ToString())));

        SampleConsole.Step("6. Batch write");
        await batchWriter.WriteBatchAsync(
                new[]
                {
                    new PlcWriteRequest(driver.AddressParser.Parse("DB1.DBW30"), PlcValue.FromUInt16(111)),
                    new PlcWriteRequest(driver.AddressParser.Parse("DB1.DBW32"), PlcValue.FromUInt16(222)),
                },
                cancellationToken)
            .ConfigureAwait(false);
        var written = await batchReader.ReadBatchAsync(
                new[]
                {
                    driver.AddressParser.Parse("DB1.DBW30"),
                    driver.AddressParser.Parse("DB1.DBW32"),
                },
                cancellationToken)
            .ConfigureAwait(false);
        SampleConsole.Value("DB1.DBW30..33", string.Join(", ", written.Select(value => value.ToString())));

        SampleConsole.Step("7. Metrics");
        var snapshot = driver.Metrics.Snapshot();
        SampleConsole.Value("Requests", snapshot.Requests);
        SampleConsole.Value("Failures", snapshot.Failures);
        SampleConsole.Value("Bytes read", snapshot.BytesRead);
        SampleConsole.Value("Bytes written", snapshot.BytesWritten);
        SampleConsole.Value("Average latency", $"{snapshot.AverageLatencyMs:0.###} ms");
        SampleConsole.Note("Next: 'run drivers/subscription' for polling subscriptions with jitter and backoff.");
    }

    private static async Task<S7Server> StartSimulatorAsync(CancellationToken cancellationToken)
    {
        var server = new S7Server(new S7ServerOptions { Port = 0 });
        await server.StartAsync(cancellationToken).ConfigureAwait(false);
        server.Data.TryEnsureDataBlock(1, 64);
        return server;
    }
}
