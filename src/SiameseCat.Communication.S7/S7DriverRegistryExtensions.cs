using SiameseCat.Communication.Drivers;

namespace SiameseCat.Communication.S7;

/// <summary>Registers the generic S7 driver.</summary>
public static class S7DriverRegistryExtensions
{
    /// <summary>Registers the <c>s7</c> driver factory.</summary>
    /// <param name="registry">Registry to extend.</param>
    /// <returns>The same registry, for chaining.</returns>
    public static DriverRegistry AddS7Driver(this DriverRegistry registry)
    {
        Guard.NotNull(registry, nameof(registry));

        registry.Register(new S7DriverFactory());
        return registry;
    }
}
