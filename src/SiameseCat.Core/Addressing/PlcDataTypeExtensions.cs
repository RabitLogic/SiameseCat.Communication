namespace SiameseCat.Core;

/// <summary>Helpers for <see cref="PlcDataType"/>.</summary>
public static class PlcDataTypeExtensions
{
    /// <summary>Gets the storage size in bytes of one element.</summary>
    /// <param name="dataType">Data type.</param>
    /// <returns>
    /// 1, 2, 4 or 8 for fixed-size types; the value of <see cref="PlcAddress.Length"/> for
    /// <see cref="PlcDataType.String"/> and <see cref="PlcDataType.Raw"/>; 0 for <see cref="PlcDataType.Null"/>.
    /// </returns>
    public static int GetByteSize(this PlcDataType dataType) => dataType switch
    {
        PlcDataType.Null => 0,
        PlcDataType.Bool or PlcDataType.Byte => 1,
        PlcDataType.Int16 or PlcDataType.UInt16 => 2,
        PlcDataType.Int32 or PlcDataType.UInt32 or PlcDataType.Float => 4,
        PlcDataType.Int64 or PlcDataType.UInt64 or PlcDataType.Double => 8,
        PlcDataType.String or PlcDataType.Raw => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unknown data type."),
    };

    /// <summary>Gets whether the type describes a single element whose length must be 1.</summary>
    /// <param name="dataType">Data type.</param>
    public static bool IsSingleElement(this PlcDataType dataType) =>
        dataType is not (PlcDataType.Null or PlcDataType.String or PlcDataType.Raw);
}
