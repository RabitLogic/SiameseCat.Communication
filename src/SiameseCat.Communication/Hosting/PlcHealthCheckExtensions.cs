using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SiameseCat.Communication;

namespace SiameseCat.Communication.Hosting;

/// <summary>
/// Health-check registration for SiameseCat drivers, usable with ASP.NET Core
/// <c>MapHealthChecks</c>, Kubernetes probes and any <see cref="HealthCheckService"/> consumer.
/// </summary>
/// <remarks>
/// A probe calls <see cref="IHealthCheckable.PingAsync"/>, which reads <see cref="DriverConfiguration.ProbeAddress"/>
/// and reports a result instead of throwing. Keep the probe address cheap for the device (one register or one
/// marker bit) because orchestrators call it every few seconds.
/// </remarks>
public static class PlcHealthCheckExtensions
{
    private const string DedicatedKeyPrefix = "siamese-health:";

    /// <summary>
    /// Registers a health check that owns a dedicated driver instance for <paramref name="driverId"/>.
    /// </summary>
    /// <param name="builder">Health-check builder, usually the result of <c>services.AddHealthChecks()</c>.</param>
    /// <param name="driverId">Driver id such as <c>modbus</c> or <c>siemens</c>.</param>
    /// <param name="configure">Configures the connection; set <see cref="DriverConfiguration.ProbeAddress"/>.</param>
    /// <param name="name">Health-check name; defaults to <c>siamese:{driverId}</c>.</param>
    /// <param name="failureStatus">Status reported on failure; defaults to the builder's setting.</param>
    /// <param name="tags">Optional tags, for example <c>ready</c>.</param>
    /// <param name="timeout">Optional per-check timeout.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHealthChecksBuilder AddSiameseCatDriverCheck(
        this IHealthChecksBuilder builder,
        string driverId,
        Action<DriverConfiguration>? configure = null,
        string? name = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        Guard.NotNull(builder, nameof(builder));
        Guard.NotNullOrWhiteSpace(driverId, nameof(driverId));

        var checkName = string.IsNullOrWhiteSpace(name) ? $"siamese:{driverId}" : name!;
        var key = DedicatedKeyPrefix + checkName;

        builder.Services.AddSiameseCat();
        builder.Services.AddKeyedSingleton<IPlcDriver>(key, (provider, _) =>
        {
            var configuration = new DriverConfiguration();
            configure?.Invoke(configuration);
            configuration.Validate();

            return provider.GetRequiredService<DriverRegistry>().Create(
                driverId,
                configuration,
                provider.GetService<IPlcLogger>());
        });

        return builder.Add(new HealthCheckRegistration(
            checkName,
            provider => new PlcDriverHealthCheck(provider, p => p.GetRequiredKeyedService<IPlcDriver>(key)),
            failureStatus,
            tags,
            timeout));
    }

    /// <summary>
    /// Registers a health check for a driver the application already owns, for example one registered with
    /// <c>AddSiameseCatDriver</c> or a long-lived singleton.
    /// </summary>
    /// <param name="builder">Health-check builder.</param>
    /// <param name="name">Health-check name, for example <c>plc-main</c>.</param>
    /// <param name="driverAccessor">Resolves the driver to probe.</param>
    /// <param name="failureStatus">Status reported on failure.</param>
    /// <param name="tags">Optional tags.</param>
    /// <param name="timeout">Optional per-check timeout.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHealthChecksBuilder AddSiameseCatDriverCheck(
        this IHealthChecksBuilder builder,
        string name,
        Func<IServiceProvider, IPlcDriver> driverAccessor,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        Guard.NotNull(builder, nameof(builder));
        Guard.NotNullOrWhiteSpace(name, nameof(name));
        Guard.NotNull(driverAccessor, nameof(driverAccessor));

        return builder.Add(new HealthCheckRegistration(
            name,
            provider => new PlcDriverHealthCheck(provider, driverAccessor),
            failureStatus,
            tags,
            timeout));
    }
}
