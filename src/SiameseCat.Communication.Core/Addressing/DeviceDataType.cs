namespace SiameseCat.Communication;

/// <summary>Data type of a device value.</summary>
/// <remarks>
/// Numeric types and <see cref="Bool"/> describe a single element. <see cref="String"/> and
/// <see cref="Raw"/> use <see cref="DeviceAddress.Length"/> to describe their size.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Device data types are named after the CLR primitive they represent.")]
public enum DeviceDataType
{
    /// <summary>No value. This is the default of <see cref="DeviceValue"/>.</summary>
    Null,

    /// <summary>Single bit.</summary>
    Bool,

    /// <summary>Single 8-bit unsigned value.</summary>
    Byte,

    /// <summary>16-bit signed integer.</summary>
    Int16,

    /// <summary>16-bit unsigned integer.</summary>
    UInt16,

    /// <summary>32-bit signed integer.</summary>
    Int32,

    /// <summary>32-bit unsigned integer.</summary>
    UInt32,

    /// <summary>64-bit signed integer.</summary>
    Int64,

    /// <summary>64-bit unsigned integer.</summary>
    UInt64,

    /// <summary>IEEE 754 single-precision value.</summary>
    Float,

    /// <summary>IEEE 754 double-precision value.</summary>
    Double,

    /// <summary>Text. <see cref="DeviceAddress.Length"/> is the maximum character count.</summary>
    String,

    /// <summary>Raw bytes. <see cref="DeviceAddress.Length"/> is the byte count.</summary>
    Raw,
}
