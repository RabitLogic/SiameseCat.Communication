using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Samples.Infrastructure;
using SiameseCat.Server.Modbus;
using SiameseCat.Transport;

namespace SiameseCat.Samples.Modbus;

/// <summary>
/// Step 2 of the Modbus track: RTU framing (unit id + CRC) over a serial link.
/// </summary>
/// <remarks>
/// Without <c>--com</c> the sample wires an <see cref="ModbusRtuClient"/> to an in-process
/// <see cref="ModbusRtuServer"/> through <see cref="LoopbackTransport"/>, which is exactly how the
/// library's own tests exchange RTU frames. Pass <c>--com COM3</c> to talk to a real device; the only
/// change is which <see cref="ITransport"/> the client is built on.
/// </remarks>
public sealed class ModbusRtuSample : ISample
{
    /// <inheritdoc/>
    public string Id => "modbus/rtu";

    /// <inheritdoc/>
    public string Title => "Modbus RTU: serial framing and CRC";

    /// <inheritdoc/>
    public string Description =>
        "Same PDU logic as Modbus TCP, framed with a unit id and a CRC16. Runs on a loopback cable or a real COM port.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run modbus/rtu [--com COM3] [--baud 9600] [--unit 1]";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        var com = arguments.Get("com", string.Empty);
        var baud = arguments.GetInt("baud", 9600);
        var unitId = (byte)arguments.GetInt("unit", 1);
        var useSerial = com.Length > 0;

        ModbusRtuServer? slave = null;
        ITransport clientTransport;

        if (useSerial)
        {
            SampleConsole.Note($"Using the serial port {com} at {baud} baud.");
            clientTransport = new SerialPortTransport(new SerialPortOptions
            {
                PortName = com,
                BaudRate = baud,
                ReadTimeout = TimeSpan.FromMilliseconds(500),
            });
        }
        else
        {
            var pair = LoopbackTransport.CreatePair();
            clientTransport = pair.Client;
            slave = new ModbusRtuServer(pair.Server, new ModbusRtuServerOptions
            {
                UnitId = unitId,
                Serial = new SerialSettings { ReadTimeout = TimeSpan.FromMilliseconds(25) },
            });
            slave.Data.TryWriteHoldingRegisters(0, new ushort[] { 11, 22, 33, 44 });
            await slave.StartAsync(cancellationToken).ConfigureAwait(false);
            SampleConsole.Note("No --com supplied, so the client drives an in-process RTU slave over an in-memory cable.");
        }

        try
        {
            SampleConsole.Step("1. Create the RTU client");
            var options = new ModbusRtuOptions
            {
                UnitId = unitId,
                Timeout = TimeSpan.FromSeconds(2),
                InterFrameDelay = TimeSpan.FromMilliseconds(2),
                SerialPort = new SerialPortOptions { PortName = com.Length > 0 ? com : "loopback", BaudRate = baud },
            };
            SampleConsole.Value("Unit id", options.UnitId);
            SampleConsole.Value("Serial (hint)", $"{options.SerialPort.PortName} @ {options.SerialPort.BaudRate} 8N1");

            await using var client = new ModbusRtuClient(clientTransport, options);

            SampleConsole.Step("2. Connect and read 4 holding registers");
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var registers = await client.ReadHoldingRegistersAsync(0, 4, cancellationToken).ConfigureAwait(false);
            SampleConsole.Value("HR0..HR3", string.Join(", ", registers));

            SampleConsole.Step("3. Write one register and read it back");
            await client.WriteSingleRegisterAsync(1, 4321, cancellationToken).ConfigureAwait(false);
            var readBack = await client.ReadHoldingRegistersAsync(1, 1, cancellationToken).ConfigureAwait(false);
            SampleConsole.Value("HR1", readBack[0]);

            if (!useSerial)
            {
                SampleConsole.Check(readBack[0] == 4321, "RTU round trip returned the written value");
            }

            SampleConsole.Step("4. Broadcast write (unit id 0, no response expected)");
            await client.WriteSingleRegisterAsync(0, 3, 777, cancellationToken).ConfigureAwait(false);
            SampleConsole.Detail("Broadcast frames are fire-and-forget; RTU never answers them.");
            if (slave is not null)
            {
                SampleConsole.Value("Slave HR3", slave.Data[3]);
                SampleConsole.Check(slave.Data[3] == 777, "the slave applied the broadcast write");
                SampleConsole.Value("Frames discarded", slave.Statistics.FramesDiscarded);
            }

            await client.DisconnectAsync().ConfigureAwait(false);
            SampleConsole.Note("Next: 'run modbus/function-codes' for FC01..FC23, 'run server/modbus-rtu' to build a slave.");
        }
        finally
        {
            if (slave is not null)
            {
                await slave.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
