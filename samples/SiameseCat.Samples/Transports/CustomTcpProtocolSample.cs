using System.Globalization;
using System.Text;

using SiameseCat.Communication.Simulation.Tcp;
using SiameseCat.Communication.Transport.Tcp;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.Transports;

/// <summary>
/// Step 3 of the transport track: a complete custom TCP protocol with shared framing.
/// </summary>
/// <remarks>
/// The wire format is deliberately tiny: ASCII requests (<c>READ tag</c>, <c>WRITE tag value</c>) with
/// CR LF framing. The server, the client and the framing presets all live in
/// <c>SiameseCat.Communication.Tcp</c>; nothing here depends on the device driver layer.
/// </remarks>
public sealed class CustomTcpProtocolSample : ISample
{
    /// <inheritdoc/>
    public string Id => "transports/custom-protocol";

    /// <inheritdoc/>
    public string Title => "Transports: custom TCP protocol";

    /// <inheritdoc/>
    public string Description =>
        "Serve a line based protocol with CustomTcpSimulator, talk to it with CustomTcpClient and compare the framing presets.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run transports/custom-protocol [--port 5000]";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        var tags = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["temperature"] = 21,
            ["pressure"] = 100,
        };

        var framing = new TcpFrameOptions { Mode = TcpFrameMode.Delimiter };
        var codec = new TcpFrameCodec(framing);

        await using var server = new CustomTcpSimulator(
            (connection, token) => ServeAsync(connection, tags, token),
            new CustomTcpSimulatorOptions
            {
                Port = arguments.GetInt("port", 0),
                Codec = codec,
            });

        await server.StartAsync(cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Endpoint", $"127.0.0.1:{server.Port}");

        SampleConsole.Step("1. Request/response with CustomTcpClient");
        await using (var client = new CustomTcpClient(new CustomTcpClientOptions
        {
            Tcp = { Host = "127.0.0.1", Port = server.Port },
            Framing = framing,
            Timeout = TimeSpan.FromSeconds(2),
        }))
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            var answer = await client.RequestAsync(Encoding.ASCII.GetBytes("READ temperature"), cancellationToken)
                .ConfigureAwait(false);
            SampleConsole.Value("READ temperature", Encoding.ASCII.GetString(answer));

            await client.SendAsync(Encoding.ASCII.GetBytes("WRITE temperature 30"), cancellationToken)
                .ConfigureAwait(false);
            answer = await client.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            SampleConsole.Value("WRITE temperature 30", answer is null ? "(closed)" : Encoding.ASCII.GetString(answer));

            SampleConsole.Check(tags["temperature"] == 30, "the server applied the write");
            await client.DisconnectAsync().ConfigureAwait(false);
        }

        SampleConsole.Step("2. Another framing, unchanged protocol");
        var binary = new TcpFrameOptions
        {
            Mode = TcpFrameMode.LengthPrefix,
            PrefixLength = 4,
            MaxFrameLength = 4096,
        };

        await using (var binaryServer = new CustomTcpSimulator(
            async (connection, token) =>
            {
                var request = await connection.ReadFrameAsync(token).ConfigureAwait(false);
                if (request is not null)
                {
                    await connection.SendAsync(request, token).ConfigureAwait(false);
                }
            },
            new CustomTcpSimulatorOptions { Port = 0, Framing = binary }))
        {
            await binaryServer.StartAsync(cancellationToken).ConfigureAwait(false);

            await using var client = new CustomTcpClient(new CustomTcpClientOptions
            {
                Tcp = { Host = "127.0.0.1", Port = binaryServer.Port },
                Framing = binary,
                Timeout = TimeSpan.FromSeconds(2),
            });

            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var request = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var answer = await client.RequestAsync(request, cancellationToken).ConfigureAwait(false);
            SampleConsole.Value("Length-prefixed echo", Convert.ToHexString(answer));
            SampleConsole.Check(answer.AsSpan().SequenceEqual(request), "the length-prefixed frame round-tripped");
        }

        SampleConsole.Step("3. Statistics");
        SampleConsole.Value("Connections", server.Statistics.ConnectionsAccepted);
        SampleConsole.Value("Frames received", server.Statistics.FramesReceived);
        SampleConsole.Value("Frames sent", server.Statistics.FramesSent);
        SampleConsole.Value("Handler faults", server.Statistics.HandlerFaults);

        SampleConsole.Note("See MbapFrameCodec for the same core used by Modbus TCP with MBAP framing.");
    }

    private static async Task ServeAsync(
        CustomTcpConnection connection,
        IDictionary<string, ushort> tags,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var request = await connection.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                return;
            }

            var parts = Encoding.ASCII.GetString(request).Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var response = Handle(parts, tags);
            await connection.SendAsync(Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Handle(string[] parts, IDictionary<string, ushort> tags)
    {
        if (parts.Length < 2)
        {
            return "ERR bad request";
        }

        if (parts[0].Equals("READ", StringComparison.OrdinalIgnoreCase) && parts.Length == 2)
        {
            return tags.TryGetValue(parts[1], out var value) ? $"VALUE {value}" : "ERR unknown tag";
        }

        if (parts[0].Equals("WRITE", StringComparison.OrdinalIgnoreCase) && parts.Length == 3)
        {
            if (!tags.ContainsKey(parts[1]))
            {
                return "ERR unknown tag";
            }

            if (!ushort.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return "ERR bad value";
            }

            tags[parts[1]] = value;
            return "OK";
        }

        return "ERR bad request";
    }
}
