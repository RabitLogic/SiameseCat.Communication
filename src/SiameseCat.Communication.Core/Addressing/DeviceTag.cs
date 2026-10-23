namespace SiameseCat.Communication;

/// <summary>
/// Describes a device tag: a named address plus optional engineering scaling.
/// </summary>
/// <remarks>Tags are deliberately simple: they carry metadata, not protocol knowledge.</remarks>
public sealed class DeviceTag
{
    /// <summary>Creates a tag.</summary>
    /// <param name="name">Unique tag name used by the application.</param>
    /// <param name="address">Protocol-native address text.</param>
    /// <param name="dataType">Data type of the tag.</param>
    /// <param name="scale">Multiplier applied by <see cref="ToEngineering"/>.</param>
    /// <param name="offset">Offset applied by <see cref="ToEngineering"/>.</param>
    /// <param name="unit">Optional engineering unit such as <c>°C</c>.</param>
    public DeviceTag(
        string name,
        string address,
        DeviceDataType dataType,
        double scale = 1.0,
        double offset = 0.0,
        string? unit = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tag name must be specified.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Tag address must be specified.", nameof(address));
        }

        Name = name;
        Address = address;
        DataType = dataType;
        Scale = scale;
        Offset = offset;
        Unit = unit;
    }

    /// <summary>Gets the tag name.</summary>
    public string Name { get; }

    /// <summary>Gets the protocol-native address text.</summary>
    public string Address { get; }

    /// <summary>Gets the data type.</summary>
    public DeviceDataType DataType { get; }

    /// <summary>Gets the multiplier applied by <see cref="ToEngineering"/>.</summary>
    public double Scale { get; }

    /// <summary>Gets the offset applied by <see cref="ToEngineering"/>.</summary>
    public double Offset { get; }

    /// <summary>Gets the optional engineering unit.</summary>
    public string? Unit { get; }

    /// <summary>Converts a raw value to an engineering value: <c>raw * Scale + Offset</c>.</summary>
    /// <param name="raw">Raw numeric value read from the device.</param>
    public double ToEngineering(double raw) => raw * Scale + Offset;

    /// <summary>Converts an engineering value back to a raw value: <c>(value - Offset) / Scale</c>.</summary>
    /// <param name="value">Engineering value.</param>
    /// <exception cref="DivideByZeroException">The scale is zero.</exception>
    public double ToRaw(double value) => Scale == 0 ? throw new DivideByZeroException("Tag scale cannot be zero.") : (value - Offset) / Scale;

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Address}, {DataType})";
}
