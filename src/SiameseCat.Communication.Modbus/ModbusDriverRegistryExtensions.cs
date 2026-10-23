namespace SiameseCat.Communication.Modbus;

/// <summary>Registers the generic Modbus driver.</summary>
public static class ModbusDriverRegistryExtensions
{
    /// <summary>Registers the <c>modbus</c> driver factory.</summary>
    /// <param name="registry">Registry to extend.</param>
    /// <returns>The same registry, for chaining.</returns>
    public static DriverRegistry AddModbusDriver(this DriverRegistry registry)
    {
        Guard.NotNull(registry, nameof(registry));

        registry.Register(new ModbusDriverFactory());
        return registry;
    }
}
