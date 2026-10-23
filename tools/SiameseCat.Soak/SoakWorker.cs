using SiameseCat.Communication;
using SiameseCat.Communication.Modbus;

namespace SiameseCat.Soak;

/// <summary>
/// One polling loop: connect, batch-read a small block, write occasionally, and supervise the driver.
/// </summary>
/// <remarks>
/// The supervisor part is the pattern an application should copy: when a cycle fails and the driver is
/// <see cref="DriverState.Faulted"/>, the worker calls <see cref="IDeviceDriver.ConnectAsync"/> again instead of
/// hammering requests at a dead link.
/// </remarks>
internal sealed class SoakWorker
{
    private readonly int _index;
    private readonly DriverRegistry _registry;
    private readonly SoakOptions _options;
    private readonly int _port;
    private readonly SoakCounters _counters;

    /// <summary>Creates a worker.</summary>
    /// <param name="index">Zero-based worker index, used in log output.</param>
    /// <param name="registry">Driver registry.</param>
    /// <param name="options">Run settings.</param>
    /// <param name="port">Proxy port to connect to.</param>
    /// <param name="counters">Shared counters.</param>
    public SoakWorker(int index, DriverRegistry registry, SoakOptions options, int port, SoakCounters counters)
    {
        _index = index;
        _registry = registry;
        _options = options;
        _port = port;
        _counters = counters;
    }

    /// <summary>Runs the loop until the token is cancelled.</summary>
    /// <param name="token">Cancellation token.</param>
    public async Task RunAsync(CancellationToken token)
    {
        _ = _index;

        await using var driver = CreateDriver(_registry, _options, _port);
        var reader = (IBatchReadable)driver;
        var writer = (IWritable)driver;
        var addresses = BuildAddresses(_options, driver);
        var writeAddress = driver.AddressParser.Parse("HR100");
        var cycle = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (driver.State != DriverState.Connected)
                {
                    await driver.ConnectAsync(token).ConfigureAwait(false);
                }

                var values = await reader.ReadBatchAsync(addresses, token).ConfigureAwait(false);
                cycle++;

                if (values.Length != addresses.Length)
                {
                    throw new InvalidOperationException("The batch read returned an unexpected number of values.");
                }

                if (cycle % _options.WriteEvery == 0)
                {
                    await writer.WriteAsync(writeAddress, DeviceValue.FromUInt16((ushort)(cycle & 0xFFFF)), token)
                        .ConfigureAwait(false);
                }

                _counters.RecordSuccess();
                await Task.Delay(_options.Interval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (ModbusException error)
            {
                // The device answered: the link is fine, the request was not.
                _counters.RecordFailure(error);
                if (!await BackoffAsync(token).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (CommunicationException error)
            {
                _counters.RecordFailure(error);
                if (driver.State != DriverState.Connected)
                {
                    _counters.RecordReconnect();
                }

                if (!await BackoffAsync(token).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
    }

    /// <summary>Checks that the link recovers after the last injected fault.</summary>
    /// <param name="registry">Driver registry.</param>
    /// <param name="options">Run settings.</param>
    /// <param name="port">Proxy port.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when a fresh driver managed a full cycle.</returns>
    public static async Task<bool> VerifyRecoveryAsync(
        DriverRegistry registry,
        SoakOptions options,
        int port,
        CancellationToken cancellationToken)
    {
        await using var driver = CreateDriver(registry, options, port);
        try
        {
            await driver.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var reader = (IBatchReadable)driver;
            await reader.ReadBatchAsync(BuildAddresses(options, driver), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (CommunicationException)
        {
            return false;
        }
    }

    private async Task<bool> BackoffAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_options.Backoff, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static DeviceAddress[] BuildAddresses(SoakOptions options, IDeviceDriver driver)
    {
        var addresses = new DeviceAddress[options.RegisterCount];
        for (var i = 0; i < addresses.Length; i++)
        {
            addresses[i] = driver.AddressParser.Parse($"HR{i}");
        }

        return addresses;
    }

    private static IDeviceDriver CreateDriver(DriverRegistry registry, SoakOptions options, int port) =>
        registry.Create("modbus", new DriverConfiguration
        {
            Transport = TransportKind.Tcp,
            Host = "127.0.0.1",
            Port = port,
            Timeout = TimeSpan.FromMilliseconds(500),
            ConnectTimeout = TimeSpan.FromMilliseconds(500),
            RetryCount = 2,
            RetryDelay = TimeSpan.FromMilliseconds(50),
            RetryJitter = false,
            ProbeAddress = "HR0",
        });
}
