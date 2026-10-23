using System.Collections.Concurrent;
using System.Diagnostics;
using SiameseCat.Samples;
using SiameseCat.Samples.Infrastructure;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var rest = args.Skip(1).ToArray();

switch (command)
{
    case "list":
    case "ls":
        PrintCatalog();
        return 0;

    case "run":
        if (rest.Length == 0)
        {
            Console.WriteLine("Name a sample: dotnet run -- run modbus/tcp");
            PrintCatalog();
            return 2;
        }

        var sample = SampleCatalog.Find(rest[0]);
        if (sample is null)
        {
            Console.WriteLine($"Unknown sample '{rest[0]}'.");
            PrintCatalog();
            return 2;
        }

        return await RunAsync(sample, SampleArguments.Parse(rest.Skip(1).ToArray()), CancellationToken.None);

    case "selftest":
        // Runs every sample that has a built-in simulator; fails when one of them misbehaves.
        return await SelfTestAsync(rest);

    case "help":
    case "--help":
    case "-h":
        PrintHelp();
        return 0;

    default:
        Console.WriteLine($"Unknown command '{command}'.");
        PrintHelp();
        return 2;
}

static async Task<int> RunAsync(ISample sample, SampleArguments arguments, CancellationToken cancellationToken)
{
    try
    {
        await sample.RunAsync(arguments, cancellationToken).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"[done] {sample.Id}");
        return 0;
    }
    catch (SampleFailedException ex)
    {
        Console.WriteLine();
        Console.WriteLine($"[failed] {sample.Id}: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine($"[error] {sample.Id}: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
        return 1;
    }
}

static async Task<int> SelfTestAsync(string[] filters)
{
    var selection = SampleCatalog.All
        .Where(sample => !sample.RequiresHardware)
        .Where(sample => filters.Length == 0
            || filters.Any(filter => sample.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    if (selection.Length == 0)
    {
        Console.WriteLine("No sample matched the filter.");
        return 2;
    }

    Console.WriteLine($"Running {selection.Length} sample(s) that do not need hardware...");
    var failures = new ConcurrentBag<string>();
    var durations = new List<(string Id, TimeSpan Duration)>();
    var stopwatch = Stopwatch.StartNew();

    foreach (var sample in selection)
    {
        var sampleWatch = Stopwatch.StartNew();
        var exitCode = await RunAsync(sample, SampleArguments.Empty, CancellationToken.None).ConfigureAwait(false);
        sampleWatch.Stop();
        durations.Add((sample.Id, sampleWatch.Elapsed));

        if (exitCode != 0)
        {
            failures.Add(sample.Id);
        }
    }

    stopwatch.Stop();

    Console.WriteLine();
    Console.WriteLine(new string('=', 72));
    Console.WriteLine($"Selftest: {selection.Length - failures.Count}/{selection.Length} passed in {stopwatch.Elapsed.TotalSeconds:0.##} s");
    foreach (var (id, duration) in durations)
    {
        Console.WriteLine($"  {id,-28} {duration.TotalMilliseconds,7:0} ms");
    }

    if (!failures.IsEmpty)
    {
        Console.WriteLine("Failed: " + string.Join(", ", failures));
        return 1;
    }

    return 0;
}

static void PrintCatalog()
{
    Console.WriteLine("SiameseCat samples");
    Console.WriteLine();
    Console.WriteLine("  samples/SiameseCat.Samples/");
    Console.WriteLine("    Modbus/      Modbus TCP and RTU, function codes, error handling");
    Console.WriteLine("    S7/          S7 client and the S7 driver layer");
    Console.WriteLine("    Drivers/     registry, tags, batching, subscriptions, resilience, dependency injection");
    Console.WriteLine("    Simulation/  Modbus TCP, Modbus RTU and S7 simulators");
    Console.WriteLine("    Transports/  raw frames and custom ITransport injection");
    Console.WriteLine();

    foreach (var group in SampleCatalog.All.GroupBy(SampleCatalog.GroupOf))
    {
        Console.WriteLine($"  {group.Key}");
        foreach (var sample in group)
        {
            Console.WriteLine($"    {sample.Id,-26} {sample.Title}");
        }

        Console.WriteLine();
    }

    Console.WriteLine("Run one with:  dotnet run --project samples/SiameseCat.Samples -- run <id> [--option value]");
    Console.WriteLine("Run all offline samples:  dotnet run --project samples/SiameseCat.Samples -- selftest");
}

static void PrintHelp()
{
    Console.WriteLine("SiameseCat samples");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project samples/SiameseCat.Samples -- list");
    Console.WriteLine("  dotnet run --project samples/SiameseCat.Samples -- run <id> [--option value]");
    Console.WriteLine("  dotnet run --project samples/SiameseCat.Samples -- selftest [filter]");
    Console.WriteLine();
    Console.WriteLine("Every sample that shows a simulator also accepts --host/--port/--com to target real hardware.");
    Console.WriteLine("Ready-to-run transcripts, including expected output, are in docs/tutorial.md.");
    Console.WriteLine();

    foreach (var sample in SampleCatalog.All)
    {
        Console.WriteLine($"  {sample.Usage}");
        Console.WriteLine($"      {sample.Description}");
    }
}
