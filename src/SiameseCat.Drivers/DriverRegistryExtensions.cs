using SiameseCat.Core;
using SiameseCat.Drivers.Inovance;
using SiameseCat.Drivers.Siemens;
using SiameseCat.Protocols.Modbus;

namespace SiameseCat.Drivers;

/// <summary>Registers the drivers shipped with SiameseCat.</summary>
/// <remarks>
/// Registration is explicit so it stays compatible with Native AOT. Third-party drivers follow the
/// same pattern: implement <see cref="IPlcDriverFactory"/> and call
/// <see cref="DriverRegistry.Register"/>.
/// </remarks>
public static class DriverRegistryExtensions
{
    /// <summary>Registers the generic Modbus driver and the Siemens and Inovance brand drivers.</summary>
    /// <param name="registry">Registry to extend.</param>
    /// <returns>The same registry, for chaining.</returns>
    public static DriverRegistry AddSiameseCatDrivers(this DriverRegistry registry)
    {
        Guard.NotNull(registry, nameof(registry));

        registry.Register(new ModbusDriverFactory());
        registry.Register(new SiemensDriverFactory());
        registry.Register(new InovanceDriverFactory());
        return registry;
    }

    /// <summary>Creates a registry that already contains every driver shipped with SiameseCat.</summary>
    public static DriverRegistry CreateDefaultRegistry()
    {
        return new DriverRegistry().AddSiameseCatDrivers();
    }
}
