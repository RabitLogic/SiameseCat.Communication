using System.Text;
using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Samples.Infrastructure;
using SiameseCat.Server.Modbus;
using SiameseCat.Transport;

namespace SiameseCat.Samples.Transports;

/// <summary>
/// Step 1 of the transport track: raw sockets and the diagnostic function codes a real device answers.
/// </summary>
/// <remarks>
/// Diagnostic frames (FC07/FC0B/FC11/FC16) have no typed client API, so this sample builds them by hand
/// on top of <see cref="TcpTransport"/>. The same approach works for vendor-specific frames.
/// </remarks>
public sealed class RawTcpTransportSample : ISample
{
    /// <inheritdoc/>
    public string Id => "transports/raw-tcp";

    /// <inheritdoc/>
    public string Title => "Transports: raw TCP frames";

    /// <inheritdoc/>
    public string Description =>
        "Send hand-built MBAP frames for FC17 Report Server ID, FC07 Read Exception Status and FC0B Comm Event Counter.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run transports/raw-tcp";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        await using var slave = new ModbusTcpServer(new ModbusServerOptions
        {
            Port = 0,
            ServerIdentifier = "SiameseCat",
            RunIndicator = true,
        });
        await slave.StartAsync(cancellationToken).ConfigureAwait(false);

        await using var transport = new TcpTransport(new TcpTransportOptions
        {
            Host = "127.0.0.1",
            Port = slave.Port,
        });
        await transport.OpenAsync(cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Connected", transport.IsOpen);

        SampleConsole.Step("1. FC17 Report Server ID");
        var reportIdResponse = await RawFrameExchange
            .RequestAsync(transport, RawFrameExchange.Tcp(1, 1, 0x11), cancellationToken)
            .ConfigureAwait(false);
        SampleConsole.Value("Response", RawFrameExchange.Hex(reportIdResponse));
        if (reportIdResponse.Length > 9)
        {
            var length = reportIdResponse[8];
            var identifier = Encoding.ASCII.GetString(reportIdResponse, 9, length - 1);
            var runIndicator = reportIdResponse[8 + length];
            SampleConsole.Value("Identifier", identifier);
            SampleConsole.Value("Run indicator", runIndicator == 0xFF ? "ON" : "OFF");
            SampleConsole.Check(identifier == "SiameseCat", "the slave reported the configured identifier");
        }

        SampleConsole.Step("2. FC07 Read Exception Status");
        var statusResponse = await RawFrameExchange
            .RequestAsync(transport, RawFrameExchange.Tcp(2, 1, 0x07), cancellationToken)
            .ConfigureAwait(false);
        SampleConsole.Value("Response", RawFrameExchange.Hex(statusResponse));
        SampleConsole.Check(statusResponse.Length >= 9, "the exception status is one byte behind the MBAP header");

        SampleConsole.Step("3. FC0B Get Comm Event Counter");
        var counterResponse = await RawFrameExchange
            .RequestAsync(transport, RawFrameExchange.Tcp(3, 1, 0x0B), cancellationToken)
            .ConfigureAwait(false);
        SampleConsole.Value("Response", RawFrameExchange.Hex(counterResponse));
        SampleConsole.Check(counterResponse.Length >= 12, "the event counter is a 16-bit value in the response");

        SampleConsole.Step("4. An unsupported function code returns exception 0x01");
        var unknownResponse = await RawFrameExchange
            .RequestAsync(transport, RawFrameExchange.Tcp(4, 1, 0x41), cancellationToken)
            .ConfigureAwait(false);
        SampleConsole.Value("Response", RawFrameExchange.Hex(unknownResponse));
        SampleConsole.Check(
            unknownResponse.Length >= 9 && unknownResponse[7] == 0xC1 && unknownResponse[8] == 0x01,
            "unknown functions answer with function code | 0x80 and exception 0x01");

        SampleConsole.Step("5. A truncated frame is dropped without a reply");
        var truncatedResponse = await RawFrameExchange
            .RequestAsync(transport, new byte[] { 0x00, 0x05, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00 }, cancellationToken)
            .ConfigureAwait(false);
        SampleConsole.Value("Response", RawFrameExchange.Hex(truncatedResponse));
        SampleConsole.Value("Frames discarded", slave.Statistics.FramesDiscarded);
        SampleConsole.Check(truncatedResponse.Length == 0, "a length field that never completes is discarded");

        SampleConsole.Step("6. Statistics after the raw traffic");
        SampleConsole.Value("Frames received", slave.Statistics.FramesReceived);
        SampleConsole.Value("Requests", slave.Statistics.TotalRequests);
        SampleConsole.Value("Exceptions", slave.Statistics.TotalExceptions);
        SampleConsole.Value("FC17 requests", slave.Statistics.GetRequests(0x11));

        await transport.CloseAsync().ConfigureAwait(false);
        SampleConsole.Note("Next: 'run transports/custom' injects a custom ITransport under a Modbus client.");
    }
}
