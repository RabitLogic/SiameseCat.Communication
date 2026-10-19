namespace SiameseCat.Core;

/// <summary>Parses protocol-native address strings into a <see cref="PlcAddress"/>.</summary>
public interface IPlcAddressParser
{
    /// <summary>Gets the protocol name, for example <c>Modbus</c> or <c>S7</c>.</summary>
    string ProtocolName { get; }

    /// <summary>
    /// Parses a protocol-native address using the protocol default data type
    /// (bits for coils, 16-bit unsigned integers for registers).
    /// </summary>
    /// <param name="address">Protocol-native address text, for example <c>40001</c> or <c>DB1.DBD0</c>.</param>
    PlcAddress Parse(string address);

    /// <summary>Parses a protocol-native address and overrides the data type.</summary>
    /// <param name="address">Protocol-native address text.</param>
    /// <param name="dataType">Data type to read or write.</param>
    PlcAddress Parse(string address, PlcDataType dataType);
}
