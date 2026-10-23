using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.Simulation.Modbus;
using SiameseCat.Communication.Transport;
using SiameseCat.Communication.Transport.Tcp;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.Modbus;

/// <summary>
/// Step 3 of the Modbus track: every function code the client exposes, exercised in one session.
/// </summary>
public sealed class ModbusFunctionCodesSample : ISample
{
    /// <inheritdoc/>
    public string Id => "modbus/function-codes";

    /// <inheritdoc/>
    public string Title => "Modbus: the full function code set";

    /// <inheritdoc/>
    public string Description =>
        "FC01/02 read bits, FC03/04 read registers, FC05/06 write single, FC15/16 write multiple, FC20/21 file records, FC23 read+write, FC43 device identification.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run modbus/function-codes [--host 192.168.0.10] [--port 502] [--unit 1]";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        var simulated = !arguments.Has("host");
        var host = arguments.Get("host", "127.0.0.1");
        var port = arguments.GetInt("port", 502);
        var unitId = (byte)arguments.GetInt("unit", 1);

        await using var simulator = simulated ? await StartSimulatorAsync(cancellationToken).ConfigureAwait(false) : null;
        if (simulated)
        {
            port = simulator!.Port;
            SampleConsole.Note($"Simulated slave on 127.0.0.1:{port}.");
        }

        await using var client = new ModbusTcpClient(new ModbusTcpOptions
        {
            UnitId = unitId,
            Timeout = TimeSpan.FromSeconds(2),
            Tcp = new TcpTransportOptions { Host = host, Port = port },
        });

        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<string[]>();

        SampleConsole.Step("Reading");
        var coils = await client.ReadCoilsAsync(0, 8, cancellationToken).ConfigureAwait(false);
        rows.Add(new[] { "FC01", "coils 0..7", Join(coils) });

        var inputs = await client.ReadDiscreteInputsAsync(0, 8, cancellationToken).ConfigureAwait(false);
        rows.Add(new[] { "FC02", "inputs 0..7", Join(inputs) });

        var holding = await client.ReadHoldingRegistersAsync(0, 4, cancellationToken).ConfigureAwait(false);
        rows.Add(new[] { "FC03", "HR0..HR3", string.Join(", ", holding) });

        var inputRegisters = await client.ReadInputRegistersAsync(0, 4, cancellationToken).ConfigureAwait(false);
        rows.Add(new[] { "FC04", "IR0..IR3", string.Join(", ", inputRegisters) });

        SampleConsole.Step("Writing");
        await client.WriteSingleCoilAsync(1, true, cancellationToken).ConfigureAwait(false);
        rows.Add(new[] { "FC05", "coil 1 = true", "ok" });

        await client.WriteSingleRegisterAsync(5, 1234, cancellationToken).ConfigureAwait(false);
        rows.Add(new[] { "FC06", "HR5 = 1234", "ok" });

        await client.WriteMultipleCoilsAsync(20, new[] { true, false, false, true, true }, cancellationToken)
            .ConfigureAwait(false);
        rows.Add(new[] { "FC15", "coils 20..24", "ok" });

        await client.WriteMultipleRegistersAsync(30, new ushort[] { 9, 8, 7 }, cancellationToken).ConfigureAwait(false);
        rows.Add(new[] { "FC16", "HR30..HR32", "ok" });

        SampleConsole.Step("Combined");
        var exchanged = await client.ReadWriteMultipleRegistersAsync(40, 2, 42, new ushort[] { 555, 666 }, cancellationToken)
            .ConfigureAwait(false);
        rows.Add(new[] { "FC23", "write HR42..43, read HR40..41", string.Join(", ", exchanged) });

        SampleConsole.Step("Device identification and file records");
        var identity = await client
            .ReadDeviceIdentificationAsync(unitId, ModbusDeviceIdCode.Regular, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        rows.Add(new[]
        {
            "FC43",
            "device identification",
            $"{identity.VendorName} / {identity.ProductCode} rev {identity.MajorMinorRevision}",
        });

        await client.WriteFileRecordsAsync(
            new[] { new ModbusFileRecord(4, 0, new ushort[] { 111, 222 }) },
            cancellationToken).ConfigureAwait(false);
        var fileRecords = await client.ReadFileRecordsAsync(
            new[] { new ModbusFileRecordRequest(4, 0, 2) },
            cancellationToken).ConfigureAwait(false);
        rows.Add(new[] { "FC20/21", "file 4, record 0", string.Join(", ", fileRecords[0]) });

        SampleConsole.Table(new[] { "FC", "Operation", "Result" }, rows);

        if (simulated)
        {
            SampleConsole.Step("Verify through the slave's own store");
            SampleConsole.Value("Coil 1", simulator!.Data.ReadCoils(1, 1)![0]);
            SampleConsole.Value("HR5", simulator.Data.ReadHoldingRegisters(5, 1)![0]);
            SampleConsole.Value("HR42..HR43", string.Join(", ", simulator.Data.ReadHoldingRegisters(42, 2)!));
            SampleConsole.Check(simulator.Data.ReadHoldingRegisters(5, 1)![0] == 1234, "FC06 and FC16 landed in the slave");
            SampleConsole.Check(simulator.Data.ReadCoils(1, 1)![0], "FC05 and FC15 landed in the slave");
            SampleConsole.Check(
                simulator.Data.ReadFileRecord(4, 0, 2)!.SequenceEqual(new ushort[] { 111, 222 }),
                "FC20/21 round-tripped a file record");
        }

        await client.DisconnectAsync().ConfigureAwait(false);
        SampleConsole.Note("Next: 'run modbus/errors' to see exception responses and how they surface.");
    }

    private static string Join(bool[] values) => string.Join(", ", values.Select(value => value ? '1' : '0'));

    private static async Task<ModbusTcpSimulator> StartSimulatorAsync(CancellationToken cancellationToken)
    {
        var options = new ModbusSimulatorOptions { Port = 0 };
        options.DeviceIdentification[0x04] = "Function code rig";
        var server = new ModbusTcpSimulator(options);
        await server.StartAsync(cancellationToken).ConfigureAwait(false);
        server.Data.TryWriteHoldingRegisters(0, new ushort[] { 10, 20, 30, 40 });
        server.Data.TryWriteInputRegisters(0, new ushort[] { 1, 2, 3, 4 });
        server.Data.TryWriteCoils(0, new[] { true, false, true, false, true, false, true, false });
        server.Data.TryWriteDiscreteInputs(0, new[] { true, true, false, false, true, true, false, false });
        server.Data.TryWriteHoldingRegisters(40, new ushort[] { 100, 200 });
        server.Data.DefineFile(4, 2, 4);
        return server;
    }
}
