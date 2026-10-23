using System.Net;
using System.Net.Sockets;
using SiameseCat.Communication;
using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.Simulation.Modbus;
using SiameseCat.Communication.Transport;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.Modbus;

/// <summary>
/// Step 4 of the Modbus track: what goes wrong and how it surfaces.
/// </summary>
/// <remarks>
/// Shows the three failure families every call site has to handle: a Modbus exception response
/// (<see cref="ModbusException"/>), a timeout (<see cref="CommunicationTimeoutException"/>) and a closed
/// link (<see cref="CommunicationException"/>).
/// </remarks>
public sealed class ModbusErrorsSample : ISample
{
    /// <inheritdoc/>
    public string Id => "modbus/errors";

    /// <inheritdoc/>
    public string Title => "Modbus: exceptions, timeouts and broken links";

    /// <inheritdoc/>
    public string Description =>
        "Trigger an illegal-address exception, a response timeout and a dropped connection, and read the slave's error counters.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run modbus/errors";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        await using var slave = new ModbusTcpSimulator(new ModbusSimulatorOptions
        {
            Port = 0,
            HoldingRegisters = 16,
        });
        await slave.StartAsync(cancellationToken).ConfigureAwait(false);
        SampleConsole.Note($"Slave with only 16 holding registers on 127.0.0.1:{slave.Port}.");

        await using var client = new ModbusTcpClient(new ModbusTcpOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromMilliseconds(500),
            Tcp = new TcpTransportOptions { Host = "127.0.0.1", Port = slave.Port },
        });
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

        SampleConsole.Step("1. Illegal data address (exception code 0x02)");
        try
        {
            await client.ReadHoldingRegistersAsync(500, 4, cancellationToken).ConfigureAwait(false);
            SampleConsole.Warn("The slave answered, which was not expected.");
        }
        catch (ModbusException ex)
        {
            SampleConsole.Value("Exception code", $"0x{ex.ExceptionCode:X2} ({ModbusException.GetExceptionName(ex.ExceptionCode)})");
            SampleConsole.Value("Message", ex.Message);
            SampleConsole.Check(ex.ExceptionCode == 0x02, "an out-of-range read raises ModbusException 0x02");
        }

        SampleConsole.Step("2. The slave counted the failed request");
        SampleConsole.Value("Total requests", slave.Statistics.TotalRequests);
        SampleConsole.Value("Total exceptions", slave.Statistics.TotalExceptions);
        SampleConsole.Check(slave.Statistics.GetExceptions(0x03) >= 1, "the slave recorded the FC03 exception");

        SampleConsole.Step("3. Response timeout against a silent peer");
        using var silent = new SilentListener();
        await using var slowClient = new ModbusTcpClient(new ModbusTcpOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromMilliseconds(300),
            RetryCount = 1,
            RetryDelay = TimeSpan.FromMilliseconds(50),
            Tcp = new TcpTransportOptions { Host = "127.0.0.1", Port = silent.Port },
        });
        await slowClient.ConnectAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await slowClient.ReadHoldingRegistersAsync(0, 1, cancellationToken).ConfigureAwait(false);
            SampleConsole.Warn("The silent peer answered, which was not expected.");
        }
        catch (CommunicationTimeoutException ex)
        {
            SampleConsole.Value("Exception", ex.GetType().Name);
            SampleConsole.Value("Message", ex.Message);
            SampleConsole.Check(true, "a silent peer ends in CommunicationTimeoutException, not a hang");
        }

        SampleConsole.Step("4. Requests after a disconnect");
        await client.DisconnectAsync().ConfigureAwait(false);
        try
        {
            await client.ReadHoldingRegistersAsync(0, 1, cancellationToken).ConfigureAwait(false);
            SampleConsole.Warn("The request succeeded, which was not expected.");
        }
        catch (CommunicationException ex)
        {
            SampleConsole.Value("Exception", ex.GetType().Name);
            SampleConsole.Check(!client.IsConnected, "calls on a closed client fail fast instead of silently reconnecting");
        }

        SampleConsole.Step("5. Retry policy knobs");
        SampleConsole.Detail("Timeout, RetryCount, RetryDelay and RetryWrites live on every options object.");
        SampleConsole.Detail("Retries only repeat transient failures; protocol exceptions are returned immediately.");
        SampleConsole.Detail("Set RetryWrites = true when the peer is idempotent (registers usually are, coils may not be).");
        SampleConsole.Note("Next: 'run drivers/resilience' shows the same behaviour one layer up, on the driver.");
    }

    /// <summary>TCP listener that accepts connections and never answers.</summary>
    private sealed class SilentListener : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public SilentListener()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = AcceptAsync(_cts.Token);
        }

        public int Port { get; }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
            try
            {
                _loop.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task AcceptAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(_listener.Stop);
            var accepted = new List<TcpClient>();
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    accepted.Add(await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false));
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                foreach (var connection in accepted)
                {
                    connection.Dispose();
                }
            }
        }
    }
}
