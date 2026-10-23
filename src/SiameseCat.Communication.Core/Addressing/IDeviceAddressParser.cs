namespace SiameseCat.Communication;

/// <summary>Parses protocol-native address strings into a <see cref="DeviceAddress"/>.</summary>
public interface IDeviceAddressParser
{
    /// <summary>Gets the protocol name, for example <c>Modbus</c> or <c>S7</c>.</summary>
    string ProtocolName { get; }

    /// <summary>
    /// Parses a protocol-native address using the protocol default data type
    /// (bits for coils, 16-bit unsigned integers for registers).
    /// </summary>
    /// <param name="address">Protocol-native address text, for example <c>40001</c> or <c>DB1.DBD0</c>.</param>
    DeviceAddress Parse(string address);

    /// <summary>Parses a protocol-native address and overrides the data type.</summary>
    /// <param name="address">Protocol-native address text.</param>
    /// <param name="dataType">Data type to read or write.</param>
    DeviceAddress Parse(string address, DeviceDataType dataType);
}
