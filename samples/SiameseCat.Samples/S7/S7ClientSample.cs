using SiameseCat.Communication.S7;
using SiameseCat.Communication.Server.S7;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.S7;

/// <summary>
/// Step 1 of the S7 track: the typed <see cref="S7Client"/> against an S7 CPU, or against the
/// in-process <see cref="S7Server"/> when no <c>--host</c> is supplied.
/// </summary>
public sealed class S7ClientSample : ISample
{
    /// <inheritdoc/>
    public string Id => "s7/client";

    /// <inheritdoc/>
    public string Title => "S7: typed client (ISO-on-TCP)";

    /// <inheritdoc/>
    public string Description =>
        "Connect with rack/slot, read and write BOOL, BYTE, WORD, DINT, REAL and STRING, then read several variables in one PDU.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run s7/client [--host 192.168.0.10] [--port 102] [--rack 0] [--slot 1]";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        var simulated = !arguments.Has("host");
        var host = arguments.Get("host", "127.0.0.1");
        var port = arguments.GetInt("port", 102);
        var rack = arguments.GetInt("rack", 0);
        var slot = arguments.GetInt("slot", 1);

        await using var simulator = simulated ? await StartSimulatorAsync(cancellationToken).ConfigureAwait(false) : null;
        if (simulated)
        {
            port = simulator!.Port;
            SampleConsole.Note($"No --host supplied, using the in-process S7 slave on 127.0.0.1:{port}.");
        }

        SampleConsole.Step("1. Create and connect");
        var options = new S7Options
        {
            Host = host,
            Port = port,
            Rack = rack,
            Slot = slot,
            Timeout = TimeSpan.FromSeconds(3),
        };
        await using var client = new S7Client(options);
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Connected", client.IsConnected);
        SampleConsole.Value("Negotiated PDU", $"{client.PduSize} bytes");

        SampleConsole.Step("2. Write and read back a mix of types");
        await client.WriteBoolAsync(S7Address.Parse("DB1.DBX0.0"), true, cancellationToken).ConfigureAwait(false);
        await client.WriteUInt16Async(S7Address.Parse("DB1.DBW2"), 1234, cancellationToken).ConfigureAwait(false);
        await client.WriteInt32Async(S7Address.Parse("DB1.DBD4"), -123456, cancellationToken).ConfigureAwait(false);
        await client.WriteSingleAsync(S7Address.Parse("DB1.DBD8"), 12.5f, cancellationToken).ConfigureAwait(false);
        await client.WriteStringAsync(S7Address.Parse("DB1.DBB12"), "SiameseCat", 20, cancellationToken).ConfigureAwait(false);

        var flag = await client.ReadBoolAsync(S7Address.Parse("DB1.DBX0.0"), cancellationToken).ConfigureAwait(false);
        var word = await client.ReadUInt16Async(S7Address.Parse("DB1.DBW2"), cancellationToken).ConfigureAwait(false);
        var dint = await client.ReadInt32Async(S7Address.Parse("DB1.DBD4"), cancellationToken).ConfigureAwait(false);
        var real = await client.ReadSingleAsync(S7Address.Parse("DB1.DBD8"), cancellationToken).ConfigureAwait(false);
        var text = await client.ReadStringAsync(S7Address.Parse("DB1.DBB12"), 20, cancellationToken).ConfigureAwait(false);

        SampleConsole.Table(
            new[] { "Address", "Type", "Value" },
            new[]
            {
                new[] { "DB1.DBX0.0", "BOOL", flag.ToString() },
                new[] { "DB1.DBW2", "WORD", word.ToString() },
                new[] { "DB1.DBD4", "DINT", dint.ToString() },
                new[] { "DB1.DBD8", "REAL", real.ToString("0.###") },
                new[] { "DB1.DBB12", "STRING[20]", text },
            });

        if (simulated)
        {
            SampleConsole.Check(
                flag && word == 1234 && dint == -123456 && Math.Abs(real - 12.5f) < 0.001f && text == "SiameseCat",
                "every type survived the round trip");
        }

        SampleConsole.Step("3. Read several variables in one request");
        var items = new[]
        {
            new S7ReadItem(S7Address.Parse("DB1.DBW2"), 2),
            new S7ReadItem(S7Address.Parse("DB1.DBD4"), 4),
            new S7ReadItem(S7Address.Parse("DB1.DBD8"), 4),
        };
        var results = await client.ReadMultipleAsync(items, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Items", results.Length);
        SampleConsole.Value("Payload bytes", string.Join(" | ", results.Select(bytes => Convert.ToHexString(bytes))));
        SampleConsole.Note("One request/response pair instead of three: this is what the driver layer does for batch reads.");

        SampleConsole.Step("4. MERKER and process image areas");
        await client.WriteByteAsync(S7Address.Parse("MB10"), 0x5A, cancellationToken).ConfigureAwait(false);
        var marker = await client.ReadByteAsync(S7Address.Parse("MB10"), cancellationToken).ConfigureAwait(false);
        var input = await client.ReadByteAsync(S7Address.Parse("IB0"), cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("MB10", $"0x{marker:X2}");
        SampleConsole.Value("IB0", $"0x{input:X2}");

        await client.DisconnectAsync().ConfigureAwait(false);
        SampleConsole.Note("Next: 'run s7/driver' to use Siemens tag addresses (DB1.DBW2, M0.0) through the driver layer.");
    }

    private static async Task<S7Server> StartSimulatorAsync(CancellationToken cancellationToken)
    {
        var server = new S7Server(new S7ServerOptions { Port = 0 });
        await server.StartAsync(cancellationToken).ConfigureAwait(false);
        server.Data.TryEnsureDataBlock(1, 64);
        return server;
    }
}
