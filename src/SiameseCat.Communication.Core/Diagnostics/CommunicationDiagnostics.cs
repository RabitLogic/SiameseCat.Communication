using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SiameseCat.Communication;

/// <summary>
/// OpenTelemetry-compatible instruments published under the <c>SiameseCat</c> meter.
/// </summary>
/// <remarks>
/// The instruments use <see cref="System.Diagnostics.Metrics"/> from the base class library, so exposing
/// metrics needs no extra package: register <c>CommunicationDiagnostics.MeterName</c> with the exporter of your
/// choice (for example <c>OpenTelemetry.Metrics</c> <c>AddMeter(CommunicationDiagnostics.MeterName)</c>).
/// </remarks>
public static class CommunicationDiagnostics
{
    /// <summary>Gets the meter name to subscribe to.</summary>
    public const string MeterName = "SiameseCat";

    /// <summary>Gets the meter that owns every instrument.</summary>
    public static Meter Meter { get; } = new(MeterName, "1.0.0");

    private static readonly Counter<long> RequestCounter =
        Meter.CreateCounter<long>("device.requests", "{request}", "Number of device requests.");

    private static readonly Counter<long> FailureCounter =
        Meter.CreateCounter<long>("device.failures", "{failure}", "Number of failed device requests.");

    private static readonly Counter<long> RetryCounter =
        Meter.CreateCounter<long>("device.retries", "{retry}", "Number of request retries.");

    private static readonly Counter<long> BytesReadCounter =
        Meter.CreateCounter<long>("device.bytes.read", "By", "Payload bytes read from the device.");

    private static readonly Counter<long> BytesWrittenCounter =
        Meter.CreateCounter<long>("device.bytes.written", "By", "Payload bytes written to the device.");

    private static readonly Histogram<double> LatencyHistogram =
        Meter.CreateHistogram<double>("device.latency", "ms", "Request latency.");

    /// <summary>Cached tag values so recording a request does not format the enum on every call.</summary>
    private static readonly string[] OperationNames =
    {
        nameof(CommunicationOperation.Read),
        nameof(CommunicationOperation.Write),
        nameof(CommunicationOperation.ReadBatch),
        nameof(CommunicationOperation.WriteBatch),
        nameof(CommunicationOperation.ReadWrite),
        nameof(CommunicationOperation.Ping),
    };

    internal static void RecordRequest(string driver, CommunicationOperation operation) =>
        RequestCounter.Add(1, Tags(driver, operation));

    internal static void RecordFailure(string driver, CommunicationOperation operation, Exception? error) =>
        FailureCounter.Add(1, Tags(driver, operation, error));

    internal static void RecordRetry(string driver) =>
        RetryCounter.Add(1, new TagList { { "driver", driver } });

    internal static void RecordBytesRead(string driver, int bytes) =>
        BytesReadCounter.Add(bytes, new TagList { { "driver", driver } });

    internal static void RecordBytesWritten(string driver, int bytes) =>
        BytesWrittenCounter.Add(bytes, new TagList { { "driver", driver } });

    internal static void RecordLatency(string driver, CommunicationOperation operation, TimeSpan latency) =>
        LatencyHistogram.Record(latency.TotalMilliseconds, Tags(driver, operation));

    private static string GetOperationName(CommunicationOperation operation)
    {
        var index = (int)operation;
        return index >= 0 && index < OperationNames.Length ? OperationNames[index] : operation.ToString();
    }

    private static TagList Tags(string driver, CommunicationOperation operation, Exception? error = null)
    {
        var tags = new TagList
        {
            { "driver", driver },
            { "operation", GetOperationName(operation) },
        };

        if (error is not null)
        {
            tags.Add("error", error.GetType().Name);
        }

        return tags;
    }
}
