using System.Diagnostics;
using SiameseCat.Communication;
using SiameseCat.Communication.Drivers;
using SiameseCat.Communication.Server.Modbus;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.Drivers;

/// <summary>
/// Step 3 of the driver track: why batching matters.
/// </summary>
/// <remarks>
/// The sample counts requests on the slave side, which is the only honest measure: the driver may
/// merge, split or chunk a batch internally, but the wire traffic is what the PLC has to answer.
/// </remarks>
public sealed class DriverBatchSample : ISample
{
    /// <inheritdoc/>
    public string Id => "drivers/batch";

    /// <inheritdoc/>
    public string Title => "Drivers: batch reads and writes";

    /// <inheritdoc/>
    public string Description =>
        "Read 8 registers one by one, then as one batch, and compare the number of Modbus requests the slave received.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run drivers/batch";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        await using var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        await slave.StartAsync(cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < 8; i++)
        {
            slave.Data.TryWriteHoldingRegisters(i, new[] { (ushort)(100 + i) });
        }

        var registry = DriverRegistryExtensions.CreateDefaultRegistry();
        await using var driver = registry.Create("modbus", new DriverConfiguration
        {
            Transport = TransportKind.Tcp,
            Host = "127.0.0.1",
            Port = slave.Port,
            Timeout = TimeSpan.FromSeconds(2),
        });
        await driver.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var reader = DriverAccess.Reader(driver);
        var writer = DriverAccess.Writer(driver);
        var batchReader = DriverAccess.BatchReader(driver);
        var batchWriter = DriverAccess.BatchWriter(driver);

        var addresses = Enumerable.Range(0, 8)
            .Select(index => driver.AddressParser.Parse($"HR{index}"))
            .ToArray();

        SampleConsole.Step("1. Eight reads, one request each");
        var before = slave.Statistics.TotalRequests;
        var stopwatch = Stopwatch.StartNew();
        foreach (var address in addresses)
        {
            await reader.ReadAsync(address, cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        var perItemRequests = slave.Statistics.TotalRequests - before;
        SampleConsole.Value("Slave requests", perItemRequests);
        SampleConsole.Value("Elapsed", $"{stopwatch.Elapsed.TotalMilliseconds:0.##} ms");

        SampleConsole.Step("2. The same eight values as one batch");
        before = slave.Statistics.TotalRequests;
        stopwatch.Restart();
        var batch = await batchReader.ReadBatchAsync(addresses, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        var batchRequests = slave.Statistics.TotalRequests - before;
        SampleConsole.Value("Slave requests", batchRequests);
        SampleConsole.Value("Elapsed", $"{stopwatch.Elapsed.TotalMilliseconds:0.##} ms");
        SampleConsole.Value("Values", string.Join(", ", batch.Select(value => value.AsUInt16())));

        SampleConsole.Check(batchRequests < perItemRequests, "batching reduced the number of Modbus requests");
        SampleConsole.Check(
            batch.Select(value => value.AsUInt16()).SequenceEqual(Enumerable.Range(100, 8).Select(value => (ushort)value)),
            "the batch returned the same values in request order");

        SampleConsole.Step("3. Batch writes");
        before = slave.Statistics.TotalRequests;
        foreach (var index in Enumerable.Range(20, 4))
        {
            await writer.WriteAsync(driver.AddressParser.Parse($"HR{index}"), PlcValue.FromUInt16((ushort)index), cancellationToken)
                .ConfigureAwait(false);
        }

        var perItemWrites = slave.Statistics.TotalRequests - before;

        before = slave.Statistics.TotalRequests;
        await batchWriter.WriteBatchAsync(
                Enumerable.Range(24, 4)
                    .Select(index => new PlcWriteRequest(
                        driver.AddressParser.Parse($"HR{index}"),
                        PlcValue.FromUInt16((ushort)index)))
                    .ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        var batchWrites = slave.Statistics.TotalRequests - before;

        SampleConsole.Value("Writes one by one", perItemWrites);
        SampleConsole.Value("Writes as one batch", batchWrites);
        SampleConsole.Check(batchWrites < perItemWrites, "contiguous writes collapse into a single FC16 request");

        SampleConsole.Step("4. How the driver decides");
        SampleConsole.Detail("Modbus: contiguous registers/coils are merged, then split at the protocol limits");
        SampleConsole.Detail("         (2000 coils, 125 registers per read, 1968 coils / 123 registers per write).");
        SampleConsole.Detail("S7:     items are packed until the negotiated PDU size is reached, then chunked.");
        SampleConsole.Detail("Gaps are never filled silently: the driver issues one request per contiguous run.");
        SampleConsole.Note("Next: 'run drivers/subscription' to poll a set of tags with one request per cycle.");
    }
}
