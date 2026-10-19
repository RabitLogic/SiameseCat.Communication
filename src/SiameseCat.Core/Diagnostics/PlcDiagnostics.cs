using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SiameseCat.Core;

/// <summary>
/// OpenTelemetry-compatible instruments published under the <c>SiameseCat</c> meter.
/// </summary>
/// <remarks>
/// The instruments use <see cref="System.Diagnostics.Metrics"/> from the base class library, so exposing
/// metrics needs no extra package: register <c>PlcDiagnostics.MeterName</c> with the exporter of your
/// choice (for example <c>OpenTelemetry.Metrics</c> <c>AddMeter(PlcDiagnostics.MeterName)</c>).
/// </remarks>
public static class PlcDiagnostics
{
    /// <summary>Gets the meter name to subscribe to.</summary>
    public const string MeterName = "SiameseCat";

    /// <summary>Gets the meter that owns every instrument.</summary>
    public static Meter Meter { get; } = new(MeterName, "1.0.0");

    private static readonly Counter<long> RequestCounter =
        Meter.CreateCounter<long>("plc.requests", "{request}", "Number of PLC requests.");

    private static readonly Counter<long> FailureCounter =
        Meter.CreateCounter<long>("plc.failures", "{failure}", "Number of failed PLC requests.");

    private static readonly Counter<long> RetryCounter =
        Meter.CreateCounter<long>("plc.retries", "{retry}", "Number of request retries.");

    private static readonly Counter<long> BytesReadCounter =
        Meter.CreateCounter<long>("plc.bytes.read", "By", "Payload bytes read from the device.");

    private static readonly Counter<long> BytesWrittenCounter =
        Meter.CreateCounter<long>("plc.bytes.written", "By", "Payload bytes written to the device.");

    private static readonly Histogram<double> LatencyHistogram =
        Meter.CreateHistogram<double>("plc.latency", "ms", "Request latency.");

    internal static void RecordRequest(string driver, PlcOperation operation) =>
        RequestCounter.Add(1, Tags(driver, operation));

    internal static void RecordFailure(string driver, PlcOperation operation, Exception? error) =>
        FailureCounter.Add(1, Tags(driver, operation, error));

    internal static void RecordRetry(string driver) =>
        RetryCounter.Add(1, new TagList { { "driver", driver } });

    internal static void RecordBytesRead(string driver, int bytes) =>
        BytesReadCounter.Add(bytes, new TagList { { "driver", driver } });

    internal static void RecordBytesWritten(string driver, int bytes) =>
        BytesWrittenCounter.Add(bytes, new TagList { { "driver", driver } });

    internal static void RecordLatency(string driver, PlcOperation operation, TimeSpan latency) =>
        LatencyHistogram.Record(latency.TotalMilliseconds, Tags(driver, operation));

    private static TagList Tags(string driver, PlcOperation operation, Exception? error = null)
    {
        var tags = new TagList
        {
            { "driver", driver },
            { "operation", operation.ToString() },
        };

        if (error is not null)
        {
            tags.Add("error", error.GetType().Name);
        }

        return tags;
    }
}
