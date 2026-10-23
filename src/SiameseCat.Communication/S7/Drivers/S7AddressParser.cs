using System.Globalization;
using SiameseCat.Communication;

namespace SiameseCat.Communication.S7;

/// <summary>
/// Parses S7 native addresses (<c>DB1.DBX0.0</c>, <c>DB1.DBW2</c>, <c>M0.0</c>, <c>IW4</c>) into
/// <see cref="DeviceAddress"/> and maps them back.
/// </summary>
/// <remarks>
/// The region key is <c>DB{n}</c> for data blocks and <c>I</c>, <c>Q</c>, <c>M</c>, <c>T</c> or <c>C</c>
/// for the other areas. Offsets are byte offsets.
/// </remarks>
public sealed class S7AddressParser : IDeviceAddressParser
{
    /// <inheritdoc/>
    public string ProtocolName => "S7";

    /// <inheritdoc/>
    public DeviceAddress Parse(string address) => ParseCore(address, null);

    /// <inheritdoc/>
    public DeviceAddress Parse(string address, DeviceDataType dataType) => ParseCore(address, dataType);

    /// <summary>Maps a protocol-neutral address back to an <see cref="S7Address"/>.</summary>
    /// <param name="address">Address produced by this parser.</param>
    public static S7Address ToS7Address(DeviceAddress address)
    {
        var (area, dbNumber) = ParseRegion(address.Region);

        var dataType = address.DataType switch
        {
            DeviceDataType.Bool => S7DataType.Bool,
            DeviceDataType.Byte => S7DataType.Byte,
            DeviceDataType.Int16 or DeviceDataType.UInt16 => S7DataType.Word,
            DeviceDataType.Int32 or DeviceDataType.UInt32 or DeviceDataType.Float => S7DataType.DWord,
            _ => S7DataType.Byte,
        };

        var bitOffset = address.DataType == DeviceDataType.Bool ? address.BitOffset ?? 0 : (byte)0;
        return new S7Address(area, dbNumber, address.Offset, dataType, bitOffset);
    }

    private static DeviceAddress ParseCore(string address, DeviceDataType? overrideType)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new FormatException("The S7 address is empty.");
        }

        var s7 = S7Address.Parse(address);
        var region = s7.Area switch
        {
            S7Area.DataBlock => $"DB{s7.DbNumber}",
            S7Area.Inputs => "I",
            S7Area.Outputs => "Q",
            S7Area.Merkers => "M",
            S7Area.Timer => "T",
            S7Area.Counter => "C",
            _ => throw new FormatException($"Unsupported S7 area '{s7.Area}'."),
        };

        var dataType = overrideType ?? s7.DataType switch
        {
            S7DataType.Bool => DeviceDataType.Bool,
            S7DataType.Word => DeviceDataType.UInt16,
            S7DataType.DWord => DeviceDataType.UInt32,
            _ => DeviceDataType.Byte,
        };

        return new DeviceAddress(
            region,
            s7.ByteOffset,
            dataType,
            length: 1,
            bitOffset: s7.DataType == S7DataType.Bool ? s7.BitOffset : null);
    }

    private static (S7Area Area, int DbNumber) ParseRegion(string region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            throw new FormatException("The S7 address has no region.");
        }

        var text = region.Trim();
        if (text.StartsWith("DB", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(text.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var db))
            {
                throw new FormatException($"Invalid S7 data block region '{region}'.");
            }

            return (S7Area.DataBlock, db);
        }

        return text.ToUpperInvariant() switch
        {
            "I" or "E" => (S7Area.Inputs, 0),
            "Q" or "A" => (S7Area.Outputs, 0),
            "M" => (S7Area.Merkers, 0),
            "T" => (S7Area.Timer, 0),
            "C" => (S7Area.Counter, 0),
            _ => throw new FormatException(
                $"Unknown S7 region '{region}'. Use DB{{n}}, I, Q, M, T or C."),
        };
    }
}
