using SiameseCat.Communication.S7;
using SiameseCat.Communication.Server.S7;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.Server;

/// <summary>
/// Step 3 of the slave track: an S7 (ISO-on-TCP) slave that speaks S7comm.
/// </summary>
/// <remarks>
/// Point S7.Net Plus, Snap7 or a real engineering tool at this endpoint; the sample uses the library's
/// own <see cref="S7Client"/> so it also runs without hardware.
/// </remarks>
public sealed class S7ServerSample : ISample
{
    /// <inheritdoc/>
    public string Id => "server/s7";

    /// <inheritdoc/>
    public string Title => "Slave: S7 server (ISO-on-TCP)";

    /// <inheritdoc/>
    public string Description =>
        "Serve MERKER, inputs, outputs and data blocks, react to writes from an external master and read the S7 counters.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run server/s7 [--port 1102]";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        var port = arguments.GetInt("port", 0);
        var options = new S7ServerOptions
        {
            Host = "127.0.0.1",
            Port = port,
            MemorySize = 256,
            InputSize = 256,
            OutputSize = 256,
            DefaultDataBlockSize = 512,
            PduSize = 480,
        };
        await using var slave = new S7Server(options);

        SampleConsole.Step("1. Prepare the data blocks and hook writes");
        slave.Data.TryEnsureDataBlock(1, 128);
        slave.Data.Written += (_, e) =>
            SampleConsole.Detail($"master wrote {e.Area} DB{e.DbNumber} at {e.Start} ({e.Count} bytes)");

        SampleConsole.Step("2. Start listening");
        await slave.StartAsync(cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Endpoint", $"127.0.0.1:{slave.Port}");
        SampleConsole.Value("Running", slave.IsRunning);
        SampleConsole.Value("Data blocks", string.Join(", ", slave.Data.DataBlockNumbers));
        SampleConsole.Value("Offered PDU", options.PduSize);

        SampleConsole.Step("3. Talk to it with the S7 client");
        await using var client = new S7Client(new S7Options
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            Rack = 0,
            Slot = 1,
            Timeout = TimeSpan.FromSeconds(2),
        });
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("PDU from the handshake", client.PduSize);

        await client.WriteUInt16Async(S7Address.Parse("DB1.DBW0"), 4321, cancellationToken).ConfigureAwait(false);
        await client.WriteStringAsync(S7Address.Parse("DB1.DBB10"), "simulated", 20, cancellationToken)
            .ConfigureAwait(false);
        var word = await client.ReadUInt16Async(S7Address.Parse("DB1.DBW0"), cancellationToken).ConfigureAwait(false);
        var text = await client.ReadStringAsync(S7Address.Parse("DB1.DBB10"), 20, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("DB1.DBW0", word);
        SampleConsole.Value("DB1.DBB10", text);
        SampleConsole.Check(word == 4321 && text == "simulated", "the S7 slave round-tripped the values");

        SampleConsole.Step("4. Update the image from the application side");
        slave.Data.TryWriteBytes(S7Area.Merkers, 0, 10, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        var merker = await client.ReadBytesAsync(S7Address.Parse("MB10"), 4, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("MB10..13", Convert.ToHexString(merker));
        SampleConsole.Check(merker[0] == 0xDE && merker[3] == 0xEF, "the master sees what the application wrote");

        SampleConsole.Step("5. Statistics");
        SampleConsole.Value("Connections", slave.Statistics.TotalConnections);
        SampleConsole.Value("Active connections", slave.Statistics.ActiveConnections);
        SampleConsole.Value("Setup requests", slave.Statistics.SetupRequests);
        SampleConsole.Value("Read requests", slave.Statistics.ReadRequests);
        SampleConsole.Value("Write requests", slave.Statistics.WriteRequests);
        SampleConsole.Value("Items read", slave.Statistics.ItemsRead);
        SampleConsole.Value("Items written", slave.Statistics.ItemsWritten);
        SampleConsole.Value("Item errors", slave.Statistics.ItemErrors);

        await client.DisconnectAsync().ConfigureAwait(false);
        await slave.StopAsync().ConfigureAwait(false);
        SampleConsole.Value("Running", slave.IsRunning);
        SampleConsole.Note("S7.Net Plus needs an explicit rack/slot and port; both are accepted by this slave.");
        SampleConsole.Note("See docs/comparison.md for a measured comparison against S7.Net Plus.");
    }
}
