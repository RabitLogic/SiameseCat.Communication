using System.Globalization;
using System.Text.RegularExpressions;
using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;

namespace SiameseCat.Drivers.Inovance;

/// <summary>
/// Parses Inovance soft-element addresses such as <c>D100</c>, <c>M10</c>, <c>X17</c> or <c>Y10</c>.
/// </summary>
/// <remarks>
/// <para><c>D</c> maps to holding registers, <c>M</c> and <c>Y</c> to coils and <c>X</c> to
/// discrete inputs. <c>X</c>/<c>Y</c> numbers are octal.</para>
/// <para>Plain Modbus addresses (<c>40001</c>, <c>HR10</c>, <c>IR0</c>, <c>C0</c>, <c>DI0</c>) are
/// accepted as well, so device documentation that only lists register numbers can be used
/// unchanged.</para>
/// </remarks>
public sealed partial class InovanceAddressParser : IPlcAddressParser
{
    private static readonly ModbusAddressParser ModbusParser = new();

    [GeneratedRegex(@"^(?<device>[DMXY])(?<number>[0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ElementPattern();

    /// <summary>Creates a parser with the default mapping.</summary>
    public InovanceAddressParser()
        : this(InovanceMapping.Default)
    {
    }

    /// <summary>Creates a parser with an explicit element-to-Modbus mapping.</summary>
    /// <param name="mapping">Area bases used to translate element numbers.</param>
    public InovanceAddressParser(InovanceMapping mapping)
    {
        mapping.Validate();
        Mapping = mapping;
    }

    /// <inheritdoc/>
    public string ProtocolName => "Inovance";

    /// <summary>Gets the element-to-Modbus mapping used by this parser.</summary>
    public InovanceMapping Mapping { get; }

    /// <inheritdoc/>
    public PlcAddress Parse(string address) => ParseCore(address, null);

    /// <inheritdoc/>
    public PlcAddress Parse(string address, PlcDataType dataType) => ParseCore(address, dataType);

    private PlcAddress ParseCore(string address, PlcDataType? dataType)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new FormatException("The Inovance address is empty.");
        }

        var text = address.Trim();
        var match = ElementPattern().Match(text);
        if (!match.Success)
        {
            return dataType is null ? ModbusParser.Parse(text) : ModbusParser.Parse(text, dataType.Value);
        }

        var device = char.ToUpperInvariant(match.Groups["device"].Value[0]);
        var numberText = match.Groups["number"].Value;
        var number = ParseNumber(device, numberText, address);

        var (region, offset, isBit) = device switch
        {
            'D' => (ModbusAddressParser.HoldingRegisterRegion, Mapping.DBase + number, false),
            'M' => (ModbusAddressParser.CoilRegion, Mapping.MBase + number, true),
            'X' => (ModbusAddressParser.DiscreteInputRegion, Mapping.XBase + number, true),
            'Y' => (ModbusAddressParser.CoilRegion, Mapping.YBase + number, true),
            _ => throw new FormatException($"Unsupported Inovance element '{device}'."),
        };

        if (offset > ushort.MaxValue)
        {
            throw new FormatException(
                $"Inovance address '{address}' maps to offset {offset}, which exceeds the Modbus address space.");
        }

        var type = dataType ?? (isBit ? PlcDataType.Bool : PlcDataType.UInt16);
        return new PlcAddress(region, offset, type);
    }

    private static int ParseNumber(char device, string numberText, string address)
    {
        try
        {
            return device is 'X' or 'Y'
                ? Convert.ToInt32(numberText, 8)
                : int.Parse(numberText, NumberStyles.None, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new FormatException($"Invalid number in Inovance address '{address}'.", ex);
        }
    }
}
