using SiameseCat.Communication;
using SiameseCat.Communication.Diagnostics;
using SiameseCat.Communication.Drivers;
using SiameseCat.Communication.Exceptions;

namespace SiameseCat.Tests.Fakes;

/// <summary>In-memory driver used to validate the driver contract without a real protocol.</summary>
internal sealed class MockDriver : DriverBase
{
    private readonly Dictionary<DeviceAddress, DeviceValue> _store = new();

    public MockDriver(
        DriverConfiguration? configuration = null,
        ICommunicationLogger? logger = null,
        CommunicationMetrics? metrics = null,
        DriverCapabilities capabilities = DriverCapabilities.All)
        : base(
            new DriverDescriptor(
                "mock",
                "Mock",
                "In-memory test driver",
                TransportKind.Tcp,
                capabilities: capabilities),
            new MockAddressParser(),
            configuration ?? new DriverConfiguration(),
            logger,
            metrics)
    {
    }

    public ConcurrencyProbe? Probe { get; set; }

    public Func<DeviceAddress, TimeSpan, Func<DeviceValue, Task>, CancellationToken, Task<IAsyncDisposable?>>? SubscribeHandler { get; set; }

    public int ConnectCount { get; private set; }

    public int ConnectFailuresRemaining { get; set; }

    public int DisconnectCount { get; private set; }

    public int TransientFailuresRemaining { get; set; }

    public int ReadCount { get; private set; }

    public int BatchReadCount { get; private set; }

    public TimeSpan ReadDelay { get; set; }

    public DeviceValue this[DeviceAddress address]
    {
        get => _store.TryGetValue(address, out var value) ? value : DeviceValue.Null;
        set => _store[address] = value;
    }

    protected override Task DoConnectAsync(CancellationToken cancellationToken)
    {
        ConnectCount++;

        if (ConnectFailuresRemaining > 0)
        {
            ConnectFailuresRemaining--;
            throw new CommunicationTimeoutException("Simulated connect failure.");
        }

        return Task.CompletedTask;
    }

    protected override Task DoDisconnectAsync()
    {
        DisconnectCount++;
        return Task.CompletedTask;
    }

    protected override async Task<DeviceValue> DoReadAsync(DeviceAddress address, CancellationToken cancellationToken)
    {
        ReadCount++;
        Probe?.Enter();

        try
        {
            if (ReadDelay > TimeSpan.Zero)
            {
                await Task.Delay(ReadDelay, cancellationToken).ConfigureAwait(false);
            }

            CheckFailure();
            return _store.TryGetValue(address, out var value) ? value : DeviceValue.Null;
        }
        finally
        {
            Probe?.Exit();
        }
    }

    protected override Task<IAsyncDisposable?> DoSubscribeAsync(
        DeviceAddress address,
        TimeSpan interval,
        Func<DeviceValue, Task> onChanged,
        CancellationToken cancellationToken) =>
        SubscribeHandler?.Invoke(address, interval, onChanged, cancellationToken)
        ?? Task.FromResult<IAsyncDisposable?>(null);

    protected override Task DoWriteAsync(
        DeviceAddress address,
        DeviceValue value,
        CancellationToken cancellationToken)
    {
        CheckFailure();
        _store[address] = value;
        return Task.CompletedTask;
    }

    private void CheckFailure()
    {
        if (TransientFailuresRemaining > 0)
        {
            TransientFailuresRemaining--;
            throw new CommunicationTimeoutException("Simulated transient failure.");
        }
    }

    protected override Task<DeviceValue[]> DoReadBatchAsync(
        IReadOnlyList<DeviceAddress> addresses,
        CancellationToken cancellationToken)
    {
        BatchReadCount++;

        var values = new DeviceValue[addresses.Count];
        for (var i = 0; i < addresses.Count; i++)
        {
            values[i] = _store.TryGetValue(addresses[i], out var value) ? value : DeviceValue.Null;
        }

        return Task.FromResult(values);
    }
}
