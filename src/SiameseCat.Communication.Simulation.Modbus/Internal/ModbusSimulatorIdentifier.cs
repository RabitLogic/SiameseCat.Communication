namespace SiameseCat.Communication.Simulation.Modbus;

/// <summary>Validation shared by the slave options.</summary>
internal static class ModbusSimulatorIdentifier
{
    public const int MaxLength = 240;

    /// <summary>Largest object value that fits a 253-byte device identification response.</summary>
    public const int MaxDeviceIdentificationValueLength = 244;

    /// <summary>Creates the device identification objects a slave exposes before the application overrides them.</summary>
    /// <returns>Vendor name, product code and revision.</returns>
    public static IDictionary<byte, string> CreateDefaultDeviceIdentification()
    {
        var version = typeof(ModbusSimulatorIdentifier).Assembly.GetName().Version;
        var revision = version is null
            ? "1.0"
            : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

        return new Dictionary<byte, string>
        {
            [0x00] = "SiameseCat",
            [0x01] = "SiameseCat.Communication",
            [0x02] = revision,
        };
    }

    /// <summary>Validates the device identification objects exposed by a slave.</summary>
    /// <param name="objects">Object id to ASCII value map.</param>
    public static void ValidateDeviceIdentification(IDictionary<byte, string> objects)
    {
        Guard.NotNull(objects, nameof(objects));

        foreach (var pair in objects)
        {
            var value = pair.Value;
            if (value is null)
            {
                throw new ArgumentException(
                    $"The device identification object 0x{pair.Key:X2} has no value.",
                    nameof(objects));
            }

            if (value.Length > MaxDeviceIdentificationValueLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(objects),
                    value.Length,
                    $"A device identification object must not exceed {MaxDeviceIdentificationValueLength} characters.");
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] > 0x7F)
                {
                    throw new ArgumentException(
                        "Device identification values must contain ASCII characters only.",
                        nameof(objects));
                }
            }
        }
    }

    public static void Validate(string? identifier)
    {
        if (identifier is null || string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("The server identifier must be specified.", nameof(identifier));
        }

        if (identifier.Length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(identifier),
                identifier.Length,
                $"The server identifier must not exceed {MaxLength} characters.");
        }

        for (var i = 0; i < identifier.Length; i++)
        {
            if (identifier[i] > 0x7F)
            {
                throw new ArgumentException(
                    "The server identifier must contain ASCII characters only.",
                    nameof(identifier));
            }
        }
    }
}
