using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.Simulation.Modbus;
using SiameseCat.Communication.Transport;
using SiameseCat.Communication.Transport.Tcp;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.Simulation;

/// <summary>
/// Step 1 of the slave track: a Modbus TCP slave you can point a real HMI or SCADA at.
/// </summary>
public sealed class ModbusTcpSimulatorSample : ISample
{
    /// <inheritdoc/>
    public string Id => "simulation/modbus-tcp";

    /// <inheritdoc/>
    public string Title => "Simulation: Modbus TCP simulator";

    /// <inheritdoc/>
    public string Description =>
        "Expose holding registers, coils and discrete inputs, observe writes as events, take snapshots and read the request counters.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run simulation/modbus-tcp [--port 1502]";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        var port = arguments.GetInt("port", 0);
        await using var slave = new ModbusTcpSimulator(new ModbusSimulatorOptions
        {
            Host = "127.0.0.1",
            Port = port,
            HoldingRegisters = 64,
            Coils = 64,
            ServerIdentifier = "SiameseCat-Sample",
        });

        SampleConsole.Step("1. Hook the write events before starting");
        slave.Data.HoldingRegistersWritten += (_, e) =>
            SampleConsole.Detail($"master wrote HR{e.Start}..+{e.Count}");
        slave.Data.CoilsWritten += (_, e) =>
            SampleConsole.Detail($"master wrote C{e.Start}..+{e.Count}");

        SampleConsole.Step("2. Start listening");
        await slave.StartAsync(cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Endpoint", $"127.0.0.1:{slave.Port}");
        SampleConsole.Value("Running", slave.IsRunning);
        SampleConsole.Value("Holding registers", slave.Data.HoldingRegisterCount);
        SampleConsole.Value("Coils", slave.Data.CoilCount);

        SampleConsole.Step("3. Preload a process image");
        slave.Data.TryWriteHoldingRegisters(0, new ushort[] { 42, 43, 44, 45 });
        slave.Data.TryWriteInputRegisters(0, new ushort[] { 1, 2, 3, 4 });
        slave.Data.TryWriteCoils(0, new[] { true, false, true, false });
        slave.Data.TryWriteDiscreteInputs(0, new[] { true, true, false, false });

        SampleConsole.Step("4. Drive it like a master would");
        await using var client = new ModbusTcpClient(new ModbusTcpOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromSeconds(2),
            Tcp = new TcpTransportOptions { Host = "127.0.0.1", Port = slave.Port },
        });
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await client.WriteMultipleRegistersAsync(10, new ushort[] { 7, 8, 9 }, cancellationToken).ConfigureAwait(false);
        await client.WriteMultipleCoilsAsync(10, new[] { true, true, false }, cancellationToken).ConfigureAwait(false);
        var read = await client.ReadHoldingRegistersAsync(10, 3, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Read back", string.Join(", ", read));
        SampleConsole.Check(read.SequenceEqual(new ushort[] { 7, 8, 9 }), "the slave applied the write and answered the read");

        SampleConsole.Step("5. Watch an application update the process image");
        slave.Data[20] = 1234;
        SampleConsole.Value("HR20", slave.Data[20]);
        SampleConsole.Value("Event count", slave.Data.EventCount);

        SampleConsole.Step("6. Snapshots move the image between processes");
        var snapshot = slave.Data.Snapshot();
        slave.Data.Reset();
        SampleConsole.Value("HR20 after Reset", slave.Data[20]);
        slave.Data.Load(snapshot);
        SampleConsole.Value("HR20 after Load", slave.Data[20]);
        SampleConsole.Check(slave.Data[20] == 1234, "Snapshot/Load round-trips the whole process image");

        SampleConsole.Step("7. Statistics");
        SampleConsole.Value("Frames received", slave.Statistics.FramesReceived);
        SampleConsole.Value("Frames discarded", slave.Statistics.FramesDiscarded);
        SampleConsole.Value("Requests", slave.Statistics.TotalRequests);
        SampleConsole.Value("Exceptions", slave.Statistics.TotalExceptions);
        SampleConsole.Value("FC03 requests", slave.Statistics.GetRequests((byte)ModbusFunctionCode.ReadHoldingRegisters));
        SampleConsole.Value("FC16 requests", slave.Statistics.GetRequests((byte)ModbusFunctionCode.WriteMultipleRegisters));

        await client.DisconnectAsync().ConfigureAwait(false);
        await slave.StopAsync().ConfigureAwait(false);
        SampleConsole.Value("Running", slave.IsRunning);
        SampleConsole.Note("Bind to 0.0.0.0 to let an HMI or SCADA test tool reach the slave over the network.");
        SampleConsole.Note("Next: 'run simulation/modbus-rtu' for a serial slave and 'run simulation/s7' for S7.");
    }
}
