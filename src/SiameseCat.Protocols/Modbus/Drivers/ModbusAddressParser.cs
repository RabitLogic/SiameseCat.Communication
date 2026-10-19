using System.Globalization;
using System.Text.RegularExpressions;
using SiameseCat.Core;

namespace SiameseCat.Protocols.Modbus;

/// <summary>
/// Parses Modbus addresses in two notations:
/// <list type="bullet">
/// <item><description>Classic reference numbers: <c>00001</c> coils, <c>10001</c> discrete inputs, <c>30001</c> input registers, <c>40001</c> holding registers.</description></item>
/// <item><description>Named offsets: <c>HR10</c>, <c>IR10</c>, <c>C0</c>, <c>DI0</c> (also <c>holding:10</c>).</description></item>
/// </list>
/// </summary>
/// <remarks>
/// A bare number shorter than five digits is treated as a zero-based holding-register offset.
/// </remarks>
public sealed partial class ModbusAddressParser : IPlcAddressParser
{
    /// <summary>Region key for coils (function code 01/05).</summary>
    public const string CoilRegion = "coil";

    /// <summary>Region key for discrete inputs (function code 02).</summary>
    public const string DiscreteInputRegion = "discrete";

    /// <summary>Region key for holding registers (function code 03/06/16).</summary>
    public const string HoldingRegisterRegion = "holding";

    /// <summary>Region key for input registers (function code 04).</summary>
    public const string InputRegisterRegion = "input";

#if NET8_0_OR_GREATER
    [GeneratedRegex(
        @"^(?<kind>HR|IR|C|DI|HOLDING|INPUT|COIL|DISCRETE)\s*[:=]?\s*(?<offset>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedPattern();
#else
    private static readonly Regex NamedRegex = new(
        @"^(?<kind>HR|IR|C|DI|HOLDING|INPUT|COIL|DISCRETE)\s*[:=]?\s*(?<offset>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static Regex NamedPattern() => NamedRegex;
#endif

    /// <inheritdoc/>
    public string ProtocolName => "Modbus";

    /// <inheritdoc/>
    public PlcAddress Parse(string address) => ParseCore(address, null);

    /// <inheritdoc/>
    public PlcAddress Parse(string address, PlcDataType dataType) => ParseCore(address, dataType);

    /// <summary>Gets the default data type of a region.</summary>
    /// <param name="region">Region key.</param>
    public static PlcDataType GetDefaultDataType(string region) =>
        region is CoilRegion or DiscreteInputRegion ? PlcDataType.Bool : PlcDataType.UInt16;

    private static PlcAddress ParseCore(string address, PlcDataType? dataType)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new FormatException("The Modbus address is empty.");
        }

        var text = address.Trim();
        var named = NamedPattern().Match(text);
        if (named.Success)
        {
            var region = named.Groups["kind"].Value.ToUpperInvariant() switch
            {
                "HR" or "HOLDING" => HoldingRegisterRegion,
                "IR" or "INPUT" => InputRegisterRegion,
                "C" or "COIL" => CoilRegion,
                "DI" or "DISCRETE" => DiscreteInputRegion,
                _ => throw new FormatException($"Unsupported Modbus address '{address}'."),
            };

            var offset = ParseOffset(named.Groups["offset"].Value, address);
            return Build(region, offset, dataType);
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            if (text.Length is 5 or 6)
            {
                var divisor = text.Length == 6 ? 100000 : 10000;
                var prefix = number / divisor;
                var low = number % divisor;

                var region = prefix switch
                {
                    0 => CoilRegion,
                    1 => DiscreteInputRegion,
                    3 => InputRegisterRegion,
                    4 => HoldingRegisterRegion,
                    _ => throw new FormatException(
                        $"Unsupported Modbus reference number '{address}'. Use 00001, 10001, 30001 or 40001 prefixes, or a named address such as HR10."),
                };

                return Build(region, low - 1, dataType);
            }

            return Build(HoldingRegisterRegion, number, dataType);
        }

        throw new FormatException(
            $"Invalid Modbus address '{address}'. Examples: 40001, HR10, IR0, C0, DI0.");
    }

    private static PlcAddress Build(string region, int offset, PlcDataType? dataType)
    {
        if (offset is < 0 or > ushort.MaxValue)
        {
            throw new FormatException($"The Modbus offset {offset} is outside the range 0..65535.");
        }

        return new PlcAddress(region, offset, dataType ?? GetDefaultDataType(region));
    }

    private static int ParseOffset(string value, string address)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
        {
            throw new FormatException($"Invalid numeric value in Modbus address '{address}'.");
        }

        return offset;
    }
}
