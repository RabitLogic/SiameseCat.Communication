using System.Diagnostics.Metrics;
using SiameseCat.Core;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Core;

public class MetricsTests
{
    [Fact]
    public void Snapshot_CountsOperationsSeparately()
    {
        var metrics = new PlcMetrics();

        metrics.RecordRequest(PlcOperation.Read);
        metrics.RecordRequest(PlcOperation.Read);
        metrics.RecordRequest(PlcOperation.Write);
        metrics.RecordRequest(PlcOperation.ReadBatch);
        metrics.RecordRequest(PlcOperation.WriteBatch);
        metrics.RecordRequest(PlcOperation.Ping);

        var snapshot = metrics.Snapshot();

        Assert.Equal(6, snapshot.Requests);
        Assert.Equal(2, snapshot.Operations.Read);
        Assert.Equal(1, snapshot.Operations.Write);
        Assert.Equal(1, snapshot.Operations.ReadBatch);
        Assert.Equal(1, snapshot.Operations.WriteBatch);
        Assert.Equal(1, snapshot.Operations.Ping);
    }

    [Fact]
    public void Snapshot_ClassifiesFailures()
    {
        var metrics = new PlcMetrics();

        metrics.RecordFailure(PlcOperation.Read, new CommunicationTimeoutException("timeout"));
        metrics.RecordFailure(PlcOperation.Read, new ConnectionClosedException("closed"));
        metrics.RecordFailure(PlcOperation.Write, new ProtocolException("bad frame"));
        metrics.RecordFailure(PlcOperation.Write, new InvalidOperationException("other"));

        var snapshot = metrics.Snapshot();

        Assert.Equal(4, snapshot.Failures);
        Assert.Equal(1, snapshot.FailureKinds.Timeouts);
        Assert.Equal(1, snapshot.FailureKinds.ConnectionClosed);
        Assert.Equal(1, snapshot.FailureKinds.Protocol);
        Assert.Equal(1, snapshot.FailureKinds.Other);
    }

    [Fact]
    public void Snapshot_ComputesLatencyPercentilesAndMax()
    {
        var metrics = new PlcMetrics();

        metrics.RecordRequest(PlcOperation.Read);
        metrics.RecordRequest(PlcOperation.Read);
        metrics.RecordRequest(PlcOperation.Read);
        metrics.RecordLatency(TimeSpan.FromMilliseconds(1));
        metrics.RecordLatency(TimeSpan.FromMilliseconds(10));
        metrics.RecordLatency(TimeSpan.FromMilliseconds(100));

        var snapshot = metrics.Snapshot();

        Assert.InRange(snapshot.P50LatencyMs, 1, 20);
        Assert.InRange(snapshot.P95LatencyMs, 90, 120);
        Assert.InRange(snapshot.MaxLatencyMs, 99, 101);
        Assert.InRange(snapshot.AverageLatencyMs, 36, 38);
        Assert.True(snapshot.P50LatencyMs <= snapshot.P95LatencyMs);
        Assert.True(snapshot.P95LatencyMs <= snapshot.MaxLatencyMs);
    }

    [Fact]
    public void Snapshot_ReportsZeroPercentilesWithoutSamples()
    {
        var snapshot = new PlcMetrics().Snapshot();

        Assert.Equal(0, snapshot.P50LatencyMs);
        Assert.Equal(0, snapshot.P95LatencyMs);
        Assert.Equal(0, snapshot.MaxLatencyMs);
        Assert.Contains("requests=0", snapshot.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Driver_PublishesRequestsAndLatencyToMeter()
    {
        var counters = new List<(string Instrument, long Value, string? Driver, string? Operation)>();
        var histograms = new List<(string Instrument, double Value, string? Operation)>();
        var gate = new object();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == PlcDiagnostics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var (driver, operation) = ReadTags(tags);
            lock (gate)
            {
                counters.Add((instrument.Name, value, driver, operation));
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            var (_, operation) = ReadTags(tags);
            lock (gate)
            {
                histograms.Add((instrument.Name, value, operation));
            }
        });
        listener.Start();

        var driver = new MockDriver(new DriverConfiguration
        {
            ProbeAddress = "R0",
            Timeout = TimeSpan.FromSeconds(1),
        });
        await driver.ConnectAsync();
        await driver.ReadAsync(driver.AddressParser.Parse("R0"));
        await driver.WriteAsync(driver.AddressParser.Parse("R0"), PlcValue.FromUInt16(3));
        await driver.PingAsync();
        await driver.DisposeAsync();

        lock (gate)
        {
            Assert.Contains(
                counters,
                entry => entry.Instrument == "plc.requests"
                    && entry.Driver == "mock"
                    && entry.Operation == "Read"
                    && entry.Value == 1);
            Assert.Contains(
                counters,
                entry => entry.Instrument == "plc.requests" && entry.Operation == "Write");
            Assert.Contains(
                counters,
                entry => entry.Instrument == "plc.requests" && entry.Operation == "Ping");
            Assert.Contains(
                counters,
                entry => entry.Instrument == "plc.bytes.read" && entry.Driver == "mock");
            Assert.Contains(
                histograms,
                entry => entry.Instrument == "plc.latency" && entry.Operation == "Read");
        }
    }

    [Fact]
    public async Task DriverMetrics_SeparatesOperations()
    {
        var driver = new MockDriver(new DriverConfiguration
        {
            ProbeAddress = "R0",
            Timeout = TimeSpan.FromSeconds(1),
        });
        await driver.ConnectAsync();

        await driver.ReadAsync(driver.AddressParser.Parse("R0"));
        await driver.WriteAsync(driver.AddressParser.Parse("R0"), PlcValue.FromUInt16(1));
        await driver.ReadBatchAsync(new[] { driver.AddressParser.Parse("R0") });
        await driver.WriteBatchAsync(new[]
        {
            new PlcWriteRequest(driver.AddressParser.Parse("R0"), PlcValue.FromUInt16(2)),
        });
        await driver.PingAsync();

        var snapshot = driver.Metrics.Snapshot();

        Assert.Equal(1, snapshot.Operations.Read);
        Assert.Equal(1, snapshot.Operations.Write);
        Assert.Equal(1, snapshot.Operations.ReadBatch);
        Assert.Equal(1, snapshot.Operations.WriteBatch);
        Assert.Equal(1, snapshot.Operations.Ping);
        Assert.Equal(5, snapshot.Requests);

        await driver.DisposeAsync();
    }

    private static (string? Driver, string? Operation) ReadTags(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? driver = null;
        string? operation = null;

        foreach (var metricTag in tags)
        {
            switch (metricTag.Key)
            {
                case "driver":
                    driver = metricTag.Value?.ToString();
                    break;
                case "operation":
                    operation = metricTag.Value?.ToString();
                    break;
            }
        }

        return (driver, operation);
    }
}
