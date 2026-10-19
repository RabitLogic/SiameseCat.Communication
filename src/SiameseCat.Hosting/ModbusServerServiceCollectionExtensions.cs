using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SiameseCat.Core;
using SiameseCat.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Dependency-injection registration for the in-process Modbus slaves.</summary>
public static class ModbusServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Modbus TCP slave as a singleton <see cref="IModbusServer"/> and starts it with the host.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="options">Endpoint and buffer settings.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddModbusServer(this IServiceCollection services, ModbusServerOptions options)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(options, nameof(options));

        services.AddSiameseCat();
        services.AddSingleton(provider =>
        {
            options.Logger ??= provider.GetService<IPlcLogger>();
            return new ModbusTcpServer(options);
        });
        services.AddSingleton<IModbusServer>(provider => provider.GetRequiredService<ModbusTcpServer>());
        AddHostedService(services);
        return services;
    }

    /// <summary>
    /// Registers a Modbus TCP slave with a callback that configures its options.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddModbusServer(
        this IServiceCollection services,
        Action<ModbusServerOptions>? configure = null)
    {
        var options = new ModbusServerOptions();
        configure?.Invoke(options);
        return services.AddModbusServer(options);
    }

    /// <summary>
    /// Registers a Modbus RTU slave as a singleton <see cref="IModbusServer"/> and starts it with the host.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="options">Serial and buffer settings.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddModbusRtuServer(this IServiceCollection services, ModbusRtuServerOptions options)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(options, nameof(options));

        services.AddSiameseCat();
        services.AddSingleton(provider =>
        {
            options.Logger ??= provider.GetService<IPlcLogger>();
            return new ModbusRtuServer(options);
        });
        services.AddSingleton<IModbusServer>(provider => provider.GetRequiredService<ModbusRtuServer>());
        AddHostedService(services);
        return services;
    }

    /// <summary>
    /// Registers a Modbus RTU slave with a callback that configures its options.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddModbusRtuServer(
        this IServiceCollection services,
        Action<ModbusRtuServerOptions>? configure = null)
    {
        var options = new ModbusRtuServerOptions();
        configure?.Invoke(options);
        return services.AddModbusRtuServer(options);
    }

    private static void AddHostedService(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ModbusServerHost>());
}

internal sealed class ModbusServerHost : IHostedService
{
    private readonly IEnumerable<IModbusServer> _servers;

    public ModbusServerHost(IEnumerable<IModbusServer> servers) => _servers = servers;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var server in _servers)
        {
            await server.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var server in _servers)
        {
            await server.StopAsync().ConfigureAwait(false);
        }
    }
}
