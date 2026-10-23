namespace SiameseCat.Communication;

/// <summary>Helpers for <see cref="DeviceDataType"/>.</summary>
public static class DeviceDataTypeExtensions
{
    /// <summary>Gets the storage size in bytes of one element.</summary>
    /// <param name="dataType">Data type.</param>
    /// <returns>
    /// 1, 2, 4 or 8 for fixed-size types; the value of <see cref="DeviceAddress.Length"/> for
    /// <see cref="DeviceDataType.String"/> and <see cref="DeviceDataType.Raw"/>; 0 for <see cref="DeviceDataType.Null"/>.
    /// </returns>
    public static int GetByteSize(this DeviceDataType dataType) => dataType switch
    {
        DeviceDataType.Null => 0,
        DeviceDataType.Bool or DeviceDataType.Byte => 1,
        DeviceDataType.Int16 or DeviceDataType.UInt16 => 2,
        DeviceDataType.Int32 or DeviceDataType.UInt32 or DeviceDataType.Float => 4,
        DeviceDataType.Int64 or DeviceDataType.UInt64 or DeviceDataType.Double => 8,
        DeviceDataType.String or DeviceDataType.Raw => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unknown data type."),
    };

    /// <summary>Gets whether the type describes a single element whose length must be 1.</summary>
    /// <param name="dataType">Data type.</param>
    public static bool IsSingleElement(this DeviceDataType dataType) =>
        dataType is not (DeviceDataType.Null or DeviceDataType.String or DeviceDataType.Raw);
}
