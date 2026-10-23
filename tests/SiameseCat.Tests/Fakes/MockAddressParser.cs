using System.Globalization;
using System.Text.RegularExpressions;

using SiameseCat.Communication;

namespace SiameseCat.Tests.Fakes;

/// <summary>Address parser for the in-memory mock driver: <c>R0</c> registers and <c>B0</c> bits.</summary>
internal sealed partial class MockAddressParser : IDeviceAddressParser
{
    public string ProtocolName => "Mock";

    public DeviceAddress Parse(string address) => Parse(address, DeviceDataType.UInt16);

    public DeviceAddress Parse(string address, DeviceDataType dataType)
    {
        var match = Pattern().Match(address ?? string.Empty);
        if (!match.Success)
        {
            throw new FormatException($"Invalid mock address '{address}'. Use R0 or B0.");
        }

        var offset = int.Parse(match.Groups["offset"].Value, CultureInfo.InvariantCulture);
        var region = match.Groups["kind"].Value.ToUpperInvariant() switch
        {
            "B" => "bit",
            _ => "reg",
        };

        return new DeviceAddress(region, offset, dataType);
    }

    [GeneratedRegex(@"^(?<kind>R|B)(?<offset>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
