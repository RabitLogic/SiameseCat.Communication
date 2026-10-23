using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SiameseCat.Communication;

/// <summary>
/// Registry of driver factories, used to discover and create drivers at runtime.
/// </summary>
/// <remarks>
/// Registration is explicit so that Native AOT applications work without reflection. Use
/// <see cref="RegisterFromAssembly"/> only when trimming is not required.
/// </remarks>
public sealed class DriverRegistry
{
    private readonly Dictionary<string, IDeviceDriverFactory> _factories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the registered factories.</summary>
    public IReadOnlyCollection<IDeviceDriverFactory> Factories => _factories.Values;

    /// <summary>Gets descriptors of all registered drivers.</summary>
    public IEnumerable<DriverDescriptor> Descriptors => _factories.Values.Select(factory => factory.Descriptor);

    /// <summary>Registers a factory, replacing an existing registration with the same id.</summary>
    /// <param name="factory">Factory to register.</param>
    public DriverRegistry Register(IDeviceDriverFactory factory)
    {
        Guard.NotNull(factory, nameof(factory));
        _factories[factory.Descriptor.Id] = factory;
        return this;
    }

    /// <summary>Removes a factory.</summary>
    /// <param name="driverId">Driver id.</param>
    /// <returns><see langword="true"/> when a factory was removed.</returns>
    public bool Unregister(string driverId) => _factories.Remove(driverId);

    /// <summary>Gets whether a driver id is registered.</summary>
    /// <param name="driverId">Driver id.</param>
    public bool Contains(string driverId) => _factories.ContainsKey(driverId);

    /// <summary>Tries to get a factory.</summary>
    /// <param name="driverId">Driver id.</param>
    /// <param name="factory">Registered factory, when found.</param>
    public bool TryGetFactory(string driverId, [NotNullWhen(true)] out IDeviceDriverFactory? factory) =>
        _factories.TryGetValue(driverId, out factory);

    /// <summary>Creates a driver by id.</summary>
    /// <param name="driverId">Driver id.</param>
    /// <param name="configuration">Connection and retry settings.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="metrics">Optional metrics collector.</param>
    /// <exception cref="KeyNotFoundException">The driver id is not registered.</exception>
    public IDeviceDriver Create(
        string driverId,
        DriverConfiguration configuration,
        ICommunicationLogger? logger = null,
        CommunicationMetrics? metrics = null)
    {
        if (!TryGetFactory(driverId, out var factory))
        {
            var known = string.Join(", ", _factories.Keys);
            throw new KeyNotFoundException($"Driver '{driverId}' is not registered. Known drivers: {known}.");
        }

        return factory.Create(configuration, logger, metrics);
    }

    /// <summary>
    /// Scans an assembly for public parameterless types implementing <see cref="IDeviceDriverFactory"/>
    /// and registers them.
    /// </summary>
    /// <param name="assembly">Assembly to scan.</param>
    /// <returns>Number of registered factories.</returns>
    /// <remarks>Reflection is incompatible with trimming and Native AOT; register factories explicitly there.</remarks>
    [RequiresUnreferencedCode("Driver discovery scans assemblies with reflection.")]
    [RequiresDynamicCode("Driver discovery creates instances with reflection.")]
    public int RegisterFromAssembly(Assembly assembly)
    {
        Guard.NotNull(assembly, nameof(assembly));

        var count = 1;
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.IsAbstract || !typeof(IDeviceDriverFactory).IsAssignableFrom(type))
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            Register((IDeviceDriverFactory)Activator.CreateInstance(type)!);
            count++;
        }

        return count;
    }
}
