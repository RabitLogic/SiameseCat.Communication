using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Samples.Infrastructure;
using SiameseCat.Server.Modbus;
using SiameseCat.Transport;

namespace SiameseCat.Samples.Modbus;

/// <summary>
/// Step 1 of the Modbus track: open a TCP connection, read holding registers, write a register and
/// read it back. Without <c>--host</c> the sample starts an in-process slave so it also runs offline.
/// </summary>
public sealed class ModbusTcpSample : ISample
{
    /// <inheritdoc/>
    public string Id => "modbus/tcp";

    /// <inheritdoc/>
    public string Title => "Modbus TCP: connect, read, write";

    /// <inheritdoc/>
    public string Description =>
        "Smallest useful program: connect a ModbusTcpClient, read FC03 holding registers, write one register with FC06 and read it back.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run modbus/tcp [--host 192.168.0.10] [--port 502] [--unit 1]";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        var simulated = !arguments.Has("host");
        var host = arguments.Get("host", "127.0.0.1");
        var port = arguments.GetInt("port", 502);
        var unitId = arguments.GetInt("unit", 1);

        await using var simulator = simulated ? await StartSimulatorAsync(cancellationToken).ConfigureAwait(false) : null;
        if (simulated)
        {
            port = simulator!.Port;
            SampleConsole.Note($"No --host supplied, using the in-process slave on 127.0.0.1:{port}.");
        }

        SampleConsole.Step("1. Create the client");
        var options = new ModbusTcpOptions
        {
            UnitId = (byte)unitId,
            Timeout = TimeSpan.FromSeconds(2),
            Tcp = new TcpTransportOptions { Host = host, Port = port },
        };
        SampleConsole.Value("Unit id", options.UnitId);
        SampleConsole.Value("Endpoint", $"{options.Tcp.Host}:{options.Tcp.Port}");

        await using var client = new ModbusTcpClient(options);

        SampleConsole.Step("2. Connect");
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Connected", client.IsConnected);

        SampleConsole.Step("3. Read 4 holding registers (FC03)");
        var registers = await client.ReadHoldingRegistersAsync(0, 4, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("HR0..HR3", string.Join(", ", registers));

        SampleConsole.Step("4. Write one register (FC06) and read it back");
        await client.WriteSingleRegisterAsync(2, 4242, cancellationToken).ConfigureAwait(false);
        var afterWrite = await client.ReadHoldingRegistersAsync(2, 1, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("HR2", afterWrite[0]);

        if (simulated)
        {
            SampleConsole.Check(afterWrite[0] == 4242, "FC06 wrote 4242 and FC03 read the same value back");
        }

        SampleConsole.Step("5. Write several registers (FC16) and read the block");
        await client.WriteMultipleRegistersAsync(10, new ushort[] { 1, 2, 3 }, cancellationToken).ConfigureAwait(false);
        var block = await client.ReadHoldingRegistersAsync(10, 3, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("HR10..HR12", string.Join(", ", block));

        if (simulated)
        {
            SampleConsole.Check(block.SequenceEqual(new ushort[] { 1, 2, 3 }), "FC16 wrote the whole block in one request");
        }

        SampleConsole.Step("6. Disconnect");
        await client.DisconnectAsync().ConfigureAwait(false);
        SampleConsole.Value("Connected", client.IsConnected);
        SampleConsole.Note("Next: 'run modbus/rtu' for the serial framing, 'run modbus/function-codes' for every function code.");
    }

    private static async Task<ModbusTcpServer> StartSimulatorAsync(CancellationToken cancellationToken)
    {
        var server = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        await server.StartAsync(cancellationToken).ConfigureAwait(false);
        server.Data.TryWriteHoldingRegisters(0, new ushort[] { 100, 200, 300, 400 });
        server.Data.TryWriteCoils(0, new[] { true, false, true, true });
        return server;
    }
}
