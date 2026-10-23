using SiameseCat.Communication;
using SiameseCat.Communication.Drivers;
using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.Simulation.Modbus;
using SiameseCat.Communication.Transport;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.Simulation;

/// <summary>
/// Step 2 of the slave track: a Modbus RTU slave driven with hand-built frames.
/// </summary>
/// <remarks>
/// The slave reuses the same PDU processor as the TCP slave; only the framing differs. Sending frames
/// by hand makes the unit-id filter, the CRC check and the broadcast rule visible.
/// </remarks>
public sealed class ModbusRtuSimulatorSample : ISample
{
    /// <inheritdoc/>
    public string Id => "simulation/modbus-rtu";

    /// <inheritdoc/>
    public string Title => "Simulation: Modbus RTU simulator (raw frames)";

    /// <inheritdoc/>
    public string Description =>
        "Answer FC03, ignore frames for other stations, drop bad CRC frames and apply broadcast writes without answering.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run simulation/modbus-rtu";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        var pair = LoopbackTransport.CreatePair();
        await using var master = pair.Client;
        await using var slave = new ModbusRtuSimulator(pair.Server, new ModbusRtuSimulatorOptions
        {
            UnitId = 1,
            Serial = new SerialSettings { ReadTimeout = TimeSpan.FromMilliseconds(25) },
            ServerIdentifier = "SiameseCat-RTU",
        });
        slave.Data.TryWriteHoldingRegisters(0, new ushort[] { 0x11, 0x22, 0x33, 0x44 });

        await slave.StartAsync(cancellationToken).ConfigureAwait(false);
        SampleConsole.Note("A serial port cannot host both a master and a slave, so the sample uses an in-memory cable.");
        SampleConsole.Value("Station address", 1);
        SampleConsole.Value("Holding registers", slave.Data.HoldingRegisterCount);

        SampleConsole.Step("1. Read 2 holding registers: 01 03 0000 0002 CRC");
        var readRequest = RawFrameExchange.Rtu(1, (byte)ModbusFunctionCode.ReadHoldingRegisters, 0x00, 0x00, 0x00, 0x02);
        SampleConsole.Value("Request", RawFrameExchange.Hex(readRequest));
        var readResponse = await RawFrameExchange.RequestAsync(master, readRequest, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Response", RawFrameExchange.Hex(readResponse));
        SampleConsole.Check(readResponse.Length == 9, "the slave answered with unit id, function, byte count, data and CRC");
        SampleConsole.Check(readResponse.Length > 4 && readResponse[3] == 0x00 && readResponse[4] == 0x11, "the first register is 0x0011");

        SampleConsole.Step("2. A frame for another station is ignored");
        var foreignRequest = RawFrameExchange.Rtu(9, (byte)ModbusFunctionCode.ReadHoldingRegisters, 0x00, 0x00, 0x00, 0x01);
        var foreignResponse = await RawFrameExchange.RequestAsync(master, foreignRequest, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Response", RawFrameExchange.Hex(foreignResponse));
        SampleConsole.Check(foreignResponse.Length == 0, "station 9 is not answered by a station-1 slave");

        SampleConsole.Step("3. A frame with a broken CRC is discarded");
        var brokenRequest = RawFrameExchange.Rtu(1, (byte)ModbusFunctionCode.ReadHoldingRegisters, 0x00, 0x00, 0x00, 0x01);
        brokenRequest[^1] ^= 0xFF;
        var brokenResponse = await RawFrameExchange.RequestAsync(master, brokenRequest, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Response", RawFrameExchange.Hex(brokenResponse));
        SampleConsole.Value("Frames discarded", slave.Statistics.FramesDiscarded);
        SampleConsole.Check(brokenResponse.Length == 0, "the CRC failure is silent, as the RTU specification requires");

        SampleConsole.Step("4. Broadcast write (unit id 0) is applied without a response");
        var broadcastRequest = RawFrameExchange.Rtu(0, (byte)ModbusFunctionCode.WriteSingleRegister, 0x00, 0x05, 0x04, 0xD2);
        var broadcastResponse = await RawFrameExchange.RequestAsync(master, broadcastRequest, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Response", RawFrameExchange.Hex(broadcastResponse));
        SampleConsole.Value("HR5", slave.Data[5]);
        SampleConsole.Check(broadcastResponse.Length == 0, "broadcast frames never produce a response");
        SampleConsole.Check(slave.Data[5] == 1234, "the broadcast write reached the process image");

        SampleConsole.Step("5. An illegal address returns exception 0x02");
        var illegalRequest = RawFrameExchange.Rtu(1, (byte)ModbusFunctionCode.ReadHoldingRegisters, 0x04, 0x00, 0x00, 0x01);
        var illegalResponse = await RawFrameExchange.RequestAsync(master, illegalRequest, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("Response", RawFrameExchange.Hex(illegalResponse));
        SampleConsole.Check(
            illegalResponse.Length >= 5 && illegalResponse[1] == (byte)((byte)ModbusFunctionCode.ReadHoldingRegisters | 0x80),
            "the function code comes back with the high bit set");

        SampleConsole.Step("6. Counters");
        SampleConsole.Value("Frames received", slave.Statistics.FramesReceived);
        SampleConsole.Value("Frames discarded", slave.Statistics.FramesDiscarded);
        SampleConsole.Value("Requests", slave.Statistics.TotalRequests);
        SampleConsole.Value("Exceptions", slave.Statistics.TotalExceptions);

        await slave.StopAsync().ConfigureAwait(false);
        SampleConsole.Note("The same slave code serves RTU and TCP; only ModbusPduProcessor is shared, framing stays per transport.");
        SampleConsole.Note("Next: 'run simulation/s7' for an S7 slave.");
    }
}
