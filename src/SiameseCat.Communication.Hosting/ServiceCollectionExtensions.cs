using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SiameseCat.Communication;
using SiameseCat.Communication.Diagnostics;
using SiameseCat.Communication.Drivers;
using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.S7;

namespace SiameseCat.Communication.Hosting;

/// <summary>Dependency-injection registration for SiameseCat drivers.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="DriverRegistry"/> with every built-in driver and, when logging is
    /// available, an <see cref="ICommunicationLogger"/> that writes through <see cref="ILoggerFactory"/>.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSiameseCat(this IServiceCollection services)
    {
        Guard.NotNull(services, nameof(services));

        services.TryAddSingleton(static _ => new DriverRegistry().AddModbusDriver().AddS7Driver());
        services.TryAddSingleton<ICommunicationLogger>(static provider =>
        {
            var factory = provider.GetService<ILoggerFactory>();
            return factory is null
                ? NullCommunicationLogger.Instance
                : new MicrosoftLoggerAdapter(factory.CreateLogger("SiameseCat"));
        });

        return services;
    }

    /// <summary>
    /// Registers a keyed transient driver that is resolved with <c>[FromKeyedServices("id")]</c> or by
    /// <c>serviceProvider.GetRequiredKeyedService&lt;IDeviceDriver&gt;("id")</c>.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="driverId">Driver id such as <c>modbus</c> or <c>s7</c>.</param>
    /// <param name="configure">Configures the connection settings.</param>
    /// <param name="metrics">Optional metrics collector shared by every resolved driver.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSiameseCatDriver(
        this IServiceCollection services,
        string driverId,
        Action<DriverConfiguration> configure,
        CommunicationMetrics? metrics = null)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNullOrWhiteSpace(driverId, nameof(driverId));
        Guard.NotNull(configure, nameof(configure));

        services.AddSiameseCat();
        services.AddKeyedTransient<IDeviceDriver>(driverId, (provider, key) =>
        {
            var registry = provider.GetRequiredService<DriverRegistry>();
            var configuration = new DriverConfiguration();
            configure(configuration);

            return registry.Create(
                (string)key!,
                configuration,
                provider.GetService<ICommunicationLogger>(),
                metrics);
        });

        return services;
    }
}
