using SiameseCat.Communication;
using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.Server.Modbus;
using SiameseCat.Communication.Transport;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.Transports;

/// <summary>
/// Step 2 of the transport track: injecting a custom link under the protocol layer.
/// </summary>
/// <remarks>
/// Every client accepts an <see cref="ITransport"/>, and every driver accepts an
/// <see cref="ITransportFactory"/>. That is the extension point for RS-485 bridges, radio modems,
/// TLS tunnels and mock links in tests. The sample counts the bytes on both directions to show that
/// the protocol layer really runs on top of the injected link.
/// </remarks>
public sealed class CustomTransportSample : ISample
{
    /// <inheritdoc/>
    public string Id => "transports/custom";

    /// <inheritdoc/>
    public string Title => "Transports: custom ITransport and frame logging";

    /// <inheritdoc/>
    public string Description =>
        "Wrap a TCP socket in a recording transport, run Modbus through it, and log every frame with LogFrames.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run transports/custom";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        await using var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        await slave.StartAsync(cancellationToken).ConfigureAwait(false);
        slave.Data.TryWriteHoldingRegisters(0, new ushort[] { 0x0102, 0x0304 });

        SampleConsole.Step("1. Client on top of a recording transport");
        var recorder = new RecordingTransport(new TcpTransport(new TcpTransportOptions
        {
            Host = "127.0.0.1",
            Port = slave.Port,
        }));

        await using (var client = new ModbusTcpClient(recorder, new ModbusTcpOptions { UnitId = 1, Timeout = TimeSpan.FromSeconds(2) }))
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var registers = await client.ReadHoldingRegistersAsync(0, 2, cancellationToken).ConfigureAwait(false);
            SampleConsole.Value("Values", string.Join(", ", registers.Select(value => $"0x{value:X4}")));
            await client.DisconnectAsync().ConfigureAwait(false);
        }

        foreach (var frame in recorder.Frames)
        {
            SampleConsole.Value(frame.Direction, frame.Hex);
        }

        SampleConsole.Check(recorder.BytesSent >= 12 && recorder.BytesReceived > 0, "the injected transport carried the exchange");
        SampleConsole.Check(
            recorder.Frames.Count >= 2 && recorder.Frames[0].Direction == "->",
            "the recording proves the request/response framing");

        SampleConsole.Step("2. Log every frame on the driver level");
        var logger = new ConsoleCommunicationLogger();
        var registry = new DriverRegistry()
            .Register(new ModbusDriverFactory());
        await using (var driver = registry.Create(
            "modbus",
            new DriverConfiguration
            {
                Transport = TransportKind.Tcp,
                Host = "127.0.0.1",
                Port = slave.Port,
                Timeout = TimeSpan.FromSeconds(2),
                LogFrames = true,
            },
            logger))
        {
            await driver.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await DriverAccess.Reader(driver).ReadAsync(driver.AddressParser.Parse("HR0"), cancellationToken).ConfigureAwait(false);
            await driver.DisconnectAsync().ConfigureAwait(false);
        }

        SampleConsole.Value("Logged frames", logger.FrameCount);

        SampleConsole.Step("3. Where the injection points are");
        SampleConsole.Detail("Client constructors: new ModbusTcpClient(ITransport, options), new S7Client(ITransport, options).");
        SampleConsole.Detail("Driver level:      DriverConfiguration.TransportFactory = myFactory;");
        SampleConsole.Detail("Shared port:       SharedTransportFactory + DriverConfiguration.RequestLock.");
        SampleConsole.Detail("Diagnostics:       LoggingTransport decorator or DriverConfiguration.LogFrames = true.");
        SampleConsole.Note("Done with the transports track; 'run list' shows every sample again.");
    }

    /// <summary>Transport decorator that records the frames of both directions.</summary>
    private sealed class RecordingTransport(ITransport inner) : ITransport
    {
        private readonly List<(string Direction, string Hex)> _frames = new();

        public List<(string Direction, string Hex)> Frames => _frames;

        public int BytesSent { get; private set; }

        public int BytesReceived { get; private set; }

        public bool IsOpen => inner.IsOpen;

        public Task OpenAsync(CancellationToken cancellationToken = default) => inner.OpenAsync(cancellationToken);

        public Task CloseAsync() => inner.CloseAsync();

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            BytesSent += buffer.Length;
            _frames.Add(("->", RawFrameExchange.Hex(buffer.Span)));
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                BytesReceived += read;
                _frames.Add(("<-", RawFrameExchange.Hex(buffer.Span[..read])));
            }

            return read;
        }

        public void DiscardInput() => inner.DiscardInput();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    /// <summary>Minimal <see cref="ICommunicationLogger"/> that prints frames to the console.</summary>
    private sealed class ConsoleCommunicationLogger : ICommunicationLogger
    {
        public int FrameCount { get; private set; }

        public void Log(CommunicationLogLevel level, string message, Exception? exception = null)
        {
            if (message.Contains(" TX ", StringComparison.Ordinal) || message.Contains(" RX ", StringComparison.Ordinal))
            {
                FrameCount++;
            }

            Console.WriteLine($"   [log:{level}] {message}");
            if (exception is not null)
            {
                Console.WriteLine($"   [log:{level}] {exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}
