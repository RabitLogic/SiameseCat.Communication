using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using SiameseCat.Communication;
using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.Simulation.Modbus;
using SiameseCat.Communication.Transport;
using SiameseCat.Communication.Transport.Tcp;

// Proves that the package is not capped at a single .NET version: every supported runtime loads the
// nearest asset at or below its own version and can complete a real Modbus round trip.
var failures = 0;

var runtime = Environment.Version;
var asset = typeof(ModbusTcpClient).Assembly
    .GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? "(unknown)";
var assetVersion = ParseAssetVersion(asset);

Console.WriteLine("SiameseCat.Communication framework matrix");
Console.WriteLine($"  runtime        : {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"  runtime version: {runtime}");
Console.WriteLine($"  RID            : {RuntimeInformation.RuntimeIdentifier}");
Console.WriteLine($"  assembly       : {typeof(ModbusTcpClient).Assembly.GetName().Name} "
    + $"{typeof(ModbusTcpClient).Assembly.GetName().Version}");
Console.WriteLine($"  asset TFM      : {asset}");
Console.WriteLine();

if (assetVersion is null)
{
    Console.WriteLine($"[FAIL] The assembly has no parsable TargetFrameworkAttribute: {asset}");
    return 1;
}

if (assetVersion > runtime)
{
    Console.WriteLine($"[FAIL] asset {asset} is newer than the running runtime {runtime}");
    failures++;
}
else if (assetVersion.Major < 8)
{
    Console.WriteLine($"[FAIL] asset {asset} is older than the supported floor (net8.0)");
    failures++;
}
else
{
    Console.WriteLine($"[ok] runtime {runtime.Major} resolved the {assetVersion} asset (nearest lower or equal)");
}

failures += await CheckModbusRoundTripAsync().ConfigureAwait(false);
failures += CheckValueModel();

Console.WriteLine();
Console.WriteLine(failures == 0 ? "Framework matrix check passed." : $"Framework matrix check failed: {failures} issue(s).");
return failures == 0 ? 0 : 1;

static Version? ParseAssetVersion(string frameworkName)
{
    // ".NETCoreApp,Version=v10.0" -> 10.0 ; ".NETStandard,Version=v2.0" -> 2.0
    var marker = "Version=v";
    var index = frameworkName.IndexOf(marker, StringComparison.Ordinal);
    if (index < 0)
    {
        return null;
    }

    var text = frameworkName[(index + marker.Length)..];
    return Version.TryParse(text, out var version) ? version : null;
}

static async Task<int> CheckModbusRoundTripAsync()
{
    try
    {
        await using var slave = new ModbusTcpSimulator(new ModbusSimulatorOptions { Port = 0 });
        slave.Data.TryWriteHoldingRegisters(0, new ushort[] { 11, 22, 33 });
        await slave.StartAsync().ConfigureAwait(false);

        await using var client = new ModbusTcpClient(new ModbusTcpOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromSeconds(5),
            Tcp = new TcpTransportOptions { Host = "127.0.0.1", Port = slave.Port },
        });

        await client.ConnectAsync().ConfigureAwait(false);
        var registers = await client.ReadHoldingRegistersAsync(0, 3).ConfigureAwait(false);
        await client.WriteSingleRegisterAsync(1, 4242).ConfigureAwait(false);
        var readBack = await client.ReadHoldingRegistersAsync(1, 1).ConfigureAwait(false);
        await client.DisconnectAsync().ConfigureAwait(false);

        var ok = registers.SequenceEqual(new ushort[] { 11, 22, 33 }) && readBack[0] == 4242;
        Console.WriteLine(ok
            ? "[ok] Modbus TCP read/write round trip on this runtime"
            : $"[FAIL] unexpected values {string.Join(",", registers)} / {readBack[0]}");
        return ok ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FAIL] Modbus round trip threw {ex.GetType().Name}: {ex.Message}");
        return 1;
    }
}

static int CheckValueModel()
{
    var address = new DeviceAddress("holding", 10, DeviceDataType.UInt16, length: 4);
    var array = DeviceValue.FromUInt16Array(new ushort[] { 1, 2, 3, 4 });
    var crc = ModbusCrc16.Compute(new byte[] { 1, 3, 0, 0, 0, 1 });

    var ok = address.ByteSize == 8
        && array.IsArray
        && array.Length == 4
        && array.AsUInt16Array()[3] == 4
        && crc == 0x0A84;

    Console.WriteLine(ok
        ? "[ok] address/value model and CRC on this runtime"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"[FAIL] address {address.ByteSize}, array {array.Length}, crc 0x{crc:X4}"));
    return ok ? 0 : 1;
}
