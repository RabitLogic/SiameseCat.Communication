using SiameseCat.Core;
using SiameseCat.Drivers;
using SiameseCat.Samples.Infrastructure;

namespace SiameseCat.Samples.Drivers;

/// <summary>
/// Step 1 of the driver track: the registry that maps a driver id to a factory.
/// </summary>
public sealed class DriverRegistrySample : ISample
{
    /// <inheritdoc/>
    public string Id => "drivers/registry";

    /// <inheritdoc/>
    public string Title => "Drivers: discovery and capabilities";

    /// <inheritdoc/>
    public string Description =>
        "List the built-in drivers, inspect their transports, ports and capabilities, then create one by id.";

    /// <inheritdoc/>
    public bool RequiresHardware => false;

    /// <inheritdoc/>
    public string Usage => "run drivers/registry";

    /// <inheritdoc/>
    public async Task RunAsync(SampleArguments arguments, CancellationToken cancellationToken)
    {
        SampleConsole.Banner(Title, Description);

        var registry = DriverRegistryExtensions.CreateDefaultRegistry();

        SampleConsole.Step("1. Registered drivers");
        var rows = registry.Descriptors
            .Select(descriptor => new[]
            {
                descriptor.Id,
                descriptor.Transports.ToString(),
                descriptor.DefaultPort == 0 ? "-" : descriptor.DefaultPort.ToString(),
                descriptor.Capabilities.ToString(),
            })
            .ToArray();
        SampleConsole.Table(new[] { "Id", "Transports", "Port", "Capabilities" }, rows);

        SampleConsole.Step("2. Address examples");
        foreach (var descriptor in registry.Descriptors)
        {
            SampleConsole.Value(descriptor.Id, string.Join(", ", descriptor.AddressExamples));
        }

        SampleConsole.Step("3. Query the registry");
        SampleConsole.Value("Contains 'modbus'", registry.Contains("modbus"));
        SampleConsole.Value("Contains 'mitsubishi'", registry.Contains("mitsubishi"));
        SampleConsole.Check(registry.Contains("modbus"), "the Modbus driver is registered");
        SampleConsole.Check(!registry.Contains("mitsubishi"), "removed drivers are really gone");

        SampleConsole.Step("4. Create a driver by id");
        await using var driver = registry.Create(
            "modbus",
            new DriverConfiguration
            {
                Transport = TransportKind.Tcp,
                Host = "127.0.0.1",
                Port = 502,
                Timeout = TimeSpan.FromSeconds(1),
            });

        SampleConsole.Value("Descriptor", driver.Descriptor);
        SampleConsole.Value("Parser", driver.AddressParser.ProtocolName);
        SampleConsole.Value("State", driver.State);
        SampleConsole.Value("HR10 parses to", driver.AddressParser.Parse("HR10"));

        SampleConsole.Step("5. Unknown ids fail loudly");
        try
        {
            registry.Create("vendor-x", new DriverConfiguration());
            SampleConsole.Warn("Creating an unknown driver succeeded, which was not expected.");
        }
        catch (KeyNotFoundException ex)
        {
            SampleConsole.Value("Exception", ex.GetType().Name);
            SampleConsole.Value("Message", ex.Message);
            SampleConsole.Check(true, "an unknown id names the known drivers in the exception message");
        }

        SampleConsole.Note("RegisterFromAssembly() exists for scanning third-party assemblies, but it uses reflection");
        SampleConsole.Note("and is therefore unavailable under Native AOT. Register factories explicitly in AOT apps.");
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
