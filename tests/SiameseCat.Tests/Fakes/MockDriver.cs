using System.Globalization;
using System.Text.RegularExpressions;
using SiameseCat.Core;

namespace SiameseCat.Tests.Fakes;

/// <summary>Address parser for the in-memory mock driver: <c>R0</c> registers and <c>B0</c> bits.</summary>
internal sealed partial class MockAddressParser : IPlcAddressParser
{
    public string ProtocolName => "Mock";

    public PlcAddress Parse(string address) => Parse(address, PlcDataType.UInt16);

    public PlcAddress Parse(string address, PlcDataType dataType)
    {
        var match = Pattern().Match(address ?? string.Empty);
        if (!match.Success)
        {
            throw new FormatException($"Invalid mock address '{address}'. Use R0 or B0.");
        }

        var offset = int.Parse(match.Groups["offset"].Value, CultureInfo.InvariantCulture);
        var region = match.Groups["kind"].Value.ToUpperInvariant() switch
        {
            "B" => "bit",
            _ => "reg",
        };

        return new PlcAddress(region, offset, dataType);
    }

    [GeneratedRegex(@"^(?<kind>R|B)(?<offset>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

/// <summary>In-memory driver used to validate the driver contract without a real protocol.</summary>
internal sealed class MockDriver : DriverBase
{
    private readonly Dictionary<PlcAddress, PlcValue> _store = new();

    public MockDriver(DriverConfiguration? configuration = null, IPlcLogger? logger = null, PlcMetrics? metrics = null)
        : base(
            new DriverDescriptor("mock", "Mock", "In-memory test driver", TransportKind.Tcp),
            new MockAddressParser(),
            configuration ?? new DriverConfiguration(),
            logger,
            metrics)
    {
    }

    public int ConnectCount { get; private set; }

    public int ConnectFailuresRemaining { get; set; }

    public int DisconnectCount { get; private set; }

    public int TransientFailuresRemaining { get; set; }

    public int ReadCount { get; private set; }

    public TimeSpan ReadDelay { get; set; }

    public PlcValue this[PlcAddress address]
    {
        get => _store.TryGetValue(address, out var value) ? value : PlcValue.Null;
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

    protected override async Task<PlcValue> DoReadAsync(PlcAddress address, CancellationToken cancellationToken)
    {
        ReadCount++;

        if (ReadDelay > TimeSpan.Zero)
        {
            await Task.Delay(ReadDelay, cancellationToken).ConfigureAwait(false);
        }

        CheckFailure();
        return _store.TryGetValue(address, out var value) ? value : PlcValue.Null;
    }

    protected override Task DoWriteAsync(
        PlcAddress address,
        PlcValue value,
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

    protected override Task<PlcValue[]> DoReadBatchAsync(
        IReadOnlyList<PlcAddress> addresses,
        CancellationToken cancellationToken)
    {
        var values = new PlcValue[addresses.Count];
        for (var i = 0; i < addresses.Count; i++)
        {
            values[i] = _store.TryGetValue(addresses[i], out var value) ? value : PlcValue.Null;
        }

        return Task.FromResult(values);
    }
}
