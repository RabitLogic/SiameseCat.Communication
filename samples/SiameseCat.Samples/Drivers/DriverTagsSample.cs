using SiameseCat.Core;
using SiameseCat.Drivers;
using SiameseCat.Samples.Infrastructure;
using SiameseCat.Server.Modbus;

namespace SiameseCat.Samples.Drivers;

/// <summary>
/// Step 2 of the driver track: tags, engineering scaling and value conversions.
/// </summary>
public sealed class DriverTagsSample : ISample
{
    /// <inheritdoc/>
    public string Id => "drivers/tags";

    /// <inheritdoc/>
    public string Title => "Drivers: tags, scaling and typed values";

    /// <inheritdoc/>
    public string Description =>
        "Describe tags once, read them through any driver, and convert raw values to engineering units.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run drivers/tags";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        await using var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        await slave.StartAsync(cancellationToken).ConfigureAwait(false);
        slave.Data.TryWriteHoldingRegisters(0, new ushort[] { 1234, 0, 77, 88 });

        var registry = DriverRegistryExtensions.CreateDefaultRegistry();
        await using var driver = registry.Create("modbus", new DriverConfiguration
        {
            Transport = TransportKind.Tcp,
            Host = "127.0.0.1",
            Port = slave.Port,
            Timeout = TimeSpan.FromSeconds(1),
        });
        await driver.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var reader = DriverAccess.Reader(driver);
        var writer = DriverAccess.Writer(driver);

        SampleConsole.Step("1. Tags carry metadata, not protocol knowledge");
        var tags = new[]
        {
            new PlcTag("Temperature", "HR0", PlcDataType.UInt16, scale: 0.1, offset: -40, unit: "degC"),
            new PlcTag("Setpoint", "HR2", PlcDataType.Int16, scale: 0.5, unit: "bar"),
            new PlcTag("Running", "C0", PlcDataType.Bool),
        };

        foreach (var tag in tags)
        {
            SampleConsole.Value(tag.Name, $"{tag.Address} as {tag.DataType}, scale {tag.Scale}, offset {tag.Offset}");
        }

        SampleConsole.Step("2. Read tags through the driver");
        var values = await driver.ReadTagsAsync(tags, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < tags.Length; i++)
        {
            var tag = tags[i];
            var raw = values[i];
            var engineering = tag.DataType == PlcDataType.Bool
                ? double.NaN
                : tag.ToEngineering(raw.AsDouble());
            SampleConsole.Value(
                tag.Name,
                tag.DataType == PlcDataType.Bool
                    ? $"raw {raw.AsBool()}"
                    : $"raw {raw.AsDouble()} -> {engineering:0.##} {tag.Unit}");
        }

        SampleConsole.Check(values[0].AsUInt16() == 1234, "ReadTagsAsync used the driver's batch read");

        SampleConsole.Step("3. Values are strongly typed");
        var asInt32 = values[0].AsInt32();
        var asDouble = values[0].AsDouble();
        var asString = values[0].ToString();
        SampleConsole.Value("AsUInt16", values[0].AsUInt16());
        SampleConsole.Value("AsInt32", asInt32);
        SampleConsole.Value("AsDouble", asDouble);
        SampleConsole.Value("ToString", asString);
        SampleConsole.Value("Quality", values[0].Quality);
        SampleConsole.Value("IsGood", values[0].IsGood);

        SampleConsole.Step("4. Arrays and timestamps");
        var arrayAddress = driver.AddressParser.Parse("HR4", PlcDataType.UInt16).WithLength(2);
        await writer.WriteAsync(arrayAddress, PlcValue.FromUInt16Array(new ushort[] { 5, 6 }), cancellationToken)
            .ConfigureAwait(false);
        var array = await reader.ReadAsync(arrayAddress, cancellationToken).ConfigureAwait(false);
        SampleConsole.Value("IsArray", array.IsArray);
        SampleConsole.Value("Length", array.Length);
        SampleConsole.Value("Values", string.Join(", ", array.AsUInt16Array()));

        var stamped = array.WithTimestamp(DateTimeOffset.UtcNow).WithQuality(PlcValueQuality.Good);
        SampleConsole.Value("Timestamp", stamped.Timestamp?.ToString("O"));

        SampleConsole.Step("5. Read-only regions reject writes");
        try
        {
            await writer.WriteAsync(driver.AddressParser.Parse("IR0"), PlcValue.FromUInt16(1), cancellationToken)
                .ConfigureAwait(false);
            SampleConsole.Warn("Writing to an input register succeeded, which was not expected.");
        }
        catch (NotSupportedException ex)
        {
            SampleConsole.Value("Exception", ex.GetType().Name);
            SampleConsole.Value("Message", ex.Message);
            SampleConsole.Check(true, "input registers and discrete inputs are rejected before the request is sent");
        }

        SampleConsole.Note("Next: 'run drivers/batch' to see how batching changes the number of requests.");
    }
}
