using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SiameseCat.Communication;
using SiameseCat.Communication.Simulation.Modbus;
using SiameseCat.Communication.Simulation.S7;

namespace SiameseCat.Communication.Hosting;

/// <summary>Dependency-injection registration for the in-process Modbus and S7 slaves.</summary>
public static class SimulatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Modbus TCP slave as a singleton <see cref="IModbusSimulator"/> and starts it with the host.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="options">Endpoint and buffer settings.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddModbusSimulator(this IServiceCollection services, ModbusSimulatorOptions options)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(options, nameof(options));

        services.AddSiameseCat();
        services.AddSingleton(provider =>
        {
            options.Logger ??= provider.GetService<ICommunicationLogger>();
            return new ModbusTcpSimulator(options);
        });
        services.AddSingleton<IModbusSimulator>(provider => provider.GetRequiredService<ModbusTcpSimulator>());
        AddHostedService(services);
        return services;
    }

    /// <summary>
    /// Registers a Modbus TCP slave with a callback that configures its options.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddModbusSimulator(
        this IServiceCollection services,
        Action<ModbusSimulatorOptions>? configure = null)
    {
        var options = new ModbusSimulatorOptions();
        configure?.Invoke(options);
        return services.AddModbusSimulator(options);
    }

    /// <summary>
    /// Registers a Modbus RTU slave as a singleton <see cref="IModbusSimulator"/> and starts it with the host.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="options">Serial and buffer settings.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddModbusRtuSimulator(this IServiceCollection services, ModbusRtuSimulatorOptions options)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(options, nameof(options));

        services.AddSiameseCat();
        services.AddSingleton(provider =>
        {
            options.Logger ??= provider.GetService<ICommunicationLogger>();
            return new ModbusRtuSimulator(options);
        });
        services.AddSingleton<IModbusSimulator>(provider => provider.GetRequiredService<ModbusRtuSimulator>());
        AddHostedService(services);
        return services;
    }

    /// <summary>
    /// Registers a Modbus RTU slave with a callback that configures its options.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddModbusRtuSimulator(
        this IServiceCollection services,
        Action<ModbusRtuSimulatorOptions>? configure = null)
    {
        var options = new ModbusRtuSimulatorOptions();
        configure?.Invoke(options);
        return services.AddModbusRtuSimulator(options);
    }

    /// <summary>
    /// Registers an S7 (ISO-on-TCP) slave as a singleton <see cref="IS7Simulator"/> and starts it with the host.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="options">Endpoint and memory settings.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddS7Simulator(this IServiceCollection services, S7SimulatorOptions options)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(options, nameof(options));

        services.AddSiameseCat();
        services.AddSingleton(provider =>
        {
            options.Logger ??= provider.GetService<ICommunicationLogger>();
            return new S7Simulator(options);
        });
        services.AddSingleton<IS7Simulator>(provider => provider.GetRequiredService<S7Simulator>());
        AddHostedService(services);
        return services;
    }

    /// <summary>
    /// Registers an S7 (ISO-on-TCP) slave with a callback that configures its options.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddS7Simulator(
        this IServiceCollection services,
        Action<S7SimulatorOptions>? configure = null)
    {
        var options = new S7SimulatorOptions();
        configure?.Invoke(options);
        return services.AddS7Simulator(options);
    }

    private static void AddHostedService(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SlaveServerHost>());
}

internal sealed class SlaveServerHost : IHostedService
{
    private readonly IEnumerable<IModbusSimulator> _modbusServers;
    private readonly IEnumerable<IS7Simulator> _s7Servers;

    public SlaveServerHost(IEnumerable<IModbusSimulator> modbusServers, IEnumerable<IS7Simulator> s7Servers)
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

