using Microsoft.Extensions.Diagnostics.HealthChecks;
using SiameseCat.Core;

namespace SiameseCat.Hosting;

/// <summary>
/// Health check that probes one PLC driver with <see cref="IHealthCheckable.PingAsync"/>.
/// </summary>
/// <remarks>
/// The check never throws: every failure becomes <see cref="HealthStatus.Unhealthy"/> (or the failure status
/// configured in the registration) with the exception attached, so ASP.NET Core health endpoints,
/// Kubernetes probes and dashboards get a usable answer instead of a 500.
/// </remarks>
internal sealed class PlcDriverHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _provider;
    private readonly Func<IServiceProvider, IPlcDriver> _driverAccessor;

    /// <summary>Creates the check.</summary>
    /// <param name="provider">Provider used to resolve the driver on every probe.</param>
    /// <param name="driverAccessor">Resolves the driver to probe.</param>
    public PlcDriverHealthCheck(IServiceProvider provider, Func<IServiceProvider, IPlcDriver> driverAccessor)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _driverAccessor = driverAccessor ?? throw new ArgumentNullException(nameof(driverAccessor));
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(context, nameof(context));

        IPlcDriver driver;
        try
        {
            driver = _driverAccessor(_provider);
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "The PLC driver could not be resolved.",
                ex);
        }

        var data = Describe(driver);

        try
        {
            if (driver.State == DriverState.Faulted || driver.State == DriverState.Disconnected)
            {
                await driver.ConnectAsync(cancellationToken).ConfigureAwait(false);
                data["state"] = driver.State.ToString();
            }

            if (driver is not IHealthCheckable health)
            {
                return new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"Driver '{driver.Descriptor.Id}' does not declare the Health capability.",
                    data: data);
            }

            var alive = await health.PingAsync(cancellationToken).ConfigureAwait(false);

            // Refresh the snapshot so the report carries the counters of the probe itself.
            foreach (var pair in Describe(driver))
            {
                data[pair.Key] = pair.Value;
            }

            return alive
                ? HealthCheckResult.Healthy(DescribeSuccess(driver), data)
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"Driver '{driver.Descriptor.Id}' is {driver.State} and did not answer the probe.",
                    data: data);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ProbeAddress", StringComparison.Ordinal))
        {
            return new HealthCheckResult(
                HealthStatus.Degraded,
                $"Driver '{driver.Descriptor.Id}' cannot be probed: set DriverConfiguration.ProbeAddress.",
                ex,
                data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(
                HealthStatus.Degraded,
                $"The health probe for '{driver.Descriptor.Id}' was cancelled.",
                data: data);
        }
        catch (Exception ex)
        {
            data["state"] = driver.State.ToString();
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"Driver '{driver.Descriptor.Id}' failed the health probe: {ex.Message}",
                ex,
                data);
        }
    }

    private static string DescribeSuccess(IPlcDriver driver)
    {
        var snapshot = driver.Metrics.Snapshot();
        return $"Driver '{driver.Descriptor.Id}' is {driver.State}; "
            + $"{snapshot.Requests} requests, {snapshot.Failures} failures, p95 {snapshot.P95LatencyMs:0.###} ms.";
    }

    private static Dictionary<string, object> Describe(IPlcDriver driver)
    {
        var snapshot = driver.Metrics.Snapshot();

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["driver"] = driver.Descriptor.Id,
            ["state"] = driver.State.ToString(),
            ["requests"] = snapshot.Requests,
            ["failures"] = snapshot.Failures,
            ["retries"] = snapshot.Retries,
            ["timeouts"] = snapshot.FailureKinds.Timeouts,
            ["connectionClosed"] = snapshot.FailureKinds.ConnectionClosed,
            ["protocolErrors"] = snapshot.FailureKinds.Protocol,
            ["p50LatencyMs"] = snapshot.P50LatencyMs,
            ["p95LatencyMs"] = snapshot.P95LatencyMs,
            ["averageLatencyMs"] = snapshot.AverageLatencyMs,
        };
    }
}
