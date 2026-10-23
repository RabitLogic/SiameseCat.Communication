using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SiameseCat.Communication;
using SiameseCat.Communication.Server.Modbus;
using SiameseCat.Communication.Server.S7;

namespace SiameseCat.Communication.Hosting;

/// <summary>Dependency-injection registration for the in-process Modbus and S7 slaves.</summary>
public static class SlaveServerServiceCollectionExtensions
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
            options.Logger ??= provider.GetService<ICommunicationLogger>();
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
            options.Logger ??= provider.GetService<ICommunicationLogger>();
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

    /// <summary>
    /// Registers an S7 (ISO-on-TCP) slave as a singleton <see cref="IS7Server"/> and starts it with the host.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="options">Endpoint and memory settings.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddS7Server(this IServiceCollection services, S7ServerOptions options)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(options, nameof(options));

        services.AddSiameseCat();
        services.AddSingleton(provider =>
        {
            options.Logger ??= provider.GetService<ICommunicationLogger>();
            return new S7Server(options);
        });
        services.AddSingleton<IS7Server>(provider => provider.GetRequiredService<S7Server>());
        AddHostedService(services);
        return services;
    }

    /// <summary>
    /// Registers an S7 (ISO-on-TCP) slave with a callback that configures its options.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddS7Server(
        this IServiceCollection services,
        Action<S7ServerOptions>? configure = null)
    {
        var options = new S7ServerOptions();
        configure?.Invoke(options);
        return services.AddS7Server(options);
    }

    private static void AddHostedService(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SlaveServerHost>());
}

internal sealed class SlaveServerHost : IHostedService
{
    private readonly IEnumerable<IModbusServer> _modbusServers;
    private readonly IEnumerable<IS7Server> _s7Servers;

    public SlaveServerHost(IEnumerable<IModbusServer> modbusServers, IEnumerable<IS7Server> s7Servers)
    {
        _modbusServers = modbusServers;
        _s7Servers = s7Servers;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var server in _modbusServers)
        {
            await server.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var server in _s7Servers)
        {
            await server.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var server in _modbusServers)
        {
            await server.StopAsync().ConfigureAwait(false);
        }

        foreach (var server in _s7Servers)
        {
            await server.StopAsync().ConfigureAwait(false);
        }
    }
}

