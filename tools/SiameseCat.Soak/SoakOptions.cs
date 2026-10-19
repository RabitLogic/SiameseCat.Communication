using System.Globalization;

namespace SiameseCat.Soak;

/// <summary>Command-line settings of the soak run.</summary>
internal sealed class SoakOptions
{
    /// <summary>Gets the total run time.</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets the number of concurrent drivers.</summary>
    public int Workers { get; init; } = 4;

    /// <summary>Gets the pause between two polling cycles of one worker.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(25);

    /// <summary>Gets the pause after a failed cycle, which keeps a broken link from spinning.</summary>
    public TimeSpan Backoff { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets the number of contiguous registers each worker reads.</summary>
    public int RegisterCount { get; init; } = 8;

    /// <summary>Gets how many successful cycles happen between two writes.</summary>
    public int WriteEvery { get; init; } = 20;

    /// <summary>Gets the pause between fault injections; <see cref="TimeSpan.Zero"/> disables them.</summary>
    public TimeSpan FaultPeriod { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Gets how long each fault lasts.</summary>
    public TimeSpan FaultDuration { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets the minimum acceptable success rate.</summary>
    public double MinSuccessRate { get; init; } = 0.85;

    /// <summary>Gets the maximum accepted managed-heap growth.</summary>
    public int MaxHeapGrowthMb { get; init; } = 64;

    /// <summary>Gets the maximum accepted handle growth (Windows only).</summary>
    public int MaxHandleGrowth { get; init; } = 300;

    /// <summary>Gets the maximum accepted thread growth.</summary>
    public int MaxThreadGrowth { get; init; } = 50;

    /// <summary>Gets whether the run should print the usage instead of running.</summary>
    public bool ShowHelp { get; init; }

    /// <summary>Parses the command line.</summary>
    /// <param name="arguments">Raw arguments.</param>
    /// <exception cref="ArgumentException">An argument is malformed.</exception>
    public static SoakOptions Parse(IReadOnlyList<string> arguments)
    {
        var options = new SoakOptions();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < arguments.Count; i++)
        {
            var token = arguments[i];
            if (token is "--help" or "-h" or "/?")
            {
                return new SoakOptions { ShowHelp = true };
            }

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{token}'. Options use the form --name value.");
            }

            var name = token[2..];
            if (i + 1 >= arguments.Count || arguments[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option '{token}' needs a value.");
            }

            values[name] = arguments[++i];
        }

        if (values.Count == 0)
        {
            return options;
        }

        return new SoakOptions
        {
            Duration = ReadDuration(values, "--duration", options.Duration),
            Workers = ReadInteger(values, "--workers", options.Workers),
            Interval = ReadDuration(values, "--interval", options.Interval),
            Backoff = ReadDuration(values, "--backoff", options.Backoff),
            RegisterCount = ReadInteger(values, "--registers", options.RegisterCount),
            WriteEvery = ReadInteger(values, "--write-every", options.WriteEvery),
            FaultPeriod = ReadDuration(values, "--fault-period", options.FaultPeriod),
            FaultDuration = ReadDuration(values, "--fault-duration", options.FaultDuration),
            MinSuccessRate = ReadDecimal(values, "--min-success-rate", options.MinSuccessRate),
            MaxHeapGrowthMb = ReadInteger(values, "--max-heap-growth-mb", options.MaxHeapGrowthMb),
            MaxHandleGrowth = ReadInteger(values, "--max-handle-growth", options.MaxHandleGrowth),
            MaxThreadGrowth = ReadInteger(values, "--max-thread-growth", options.MaxThreadGrowth),
        };
    }

    /// <summary>Prints the usage.</summary>
    public static void PrintHelp()
    {
        Console.WriteLine("SiameseCat soak test: drives several Modbus drivers against an in-process slave while a");
        Console.WriteLine("proxy injects link failures, then verifies recovery, stability and resource usage.");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run --project tools/SiameseCat.Soak -- [options]");
        Console.WriteLine();
        Console.WriteLine("  --duration 00:01:00        total run time (default 1 minute)");
        Console.WriteLine("  --workers 4                number of concurrent drivers (default 4)");
        Console.WriteLine("  --interval 00:00:00.025    pause between polling cycles (default 25 ms)");
        Console.WriteLine("  --backoff 00:00:00.250     pause after a failed cycle (default 250 ms)");
        Console.WriteLine("  --registers 8              registers read per cycle (default 8)");
        Console.WriteLine("  --write-every 20           successful cycles between two writes (default 20)");
        Console.WriteLine("  --fault-period 00:00:08    pause between link failures, 00:00:00 disables them (default 8 s)");
        Console.WriteLine("  --fault-duration 00:00:02  length of each link failure (default 2 s)");
        Console.WriteLine("  --min-success-rate 0.85    minimum success rate, failures during faults included");
        Console.WriteLine("  --max-heap-growth-mb 64    maximum managed-heap growth after warm-up");
        Console.WriteLine("  --max-handle-growth 300    maximum handle growth (Windows only)");
        Console.WriteLine("  --max-thread-growth 50     maximum thread growth");
        Console.WriteLine();
        Console.WriteLine("Exit code 0 means every invariant held; 1 means at least one failed; 2 means bad arguments.");
    }

    private static TimeSpan ReadDuration(Dictionary<string, string> values, string name, TimeSpan fallback) =>
        values.TryGetValue(name.TrimStart('-'), out var value)
            ? TimeSpan.Parse(value, CultureInfo.InvariantCulture)
            : fallback;

    private static int ReadInteger(Dictionary<string, string> values, string name, int fallback) =>
        values.TryGetValue(name.TrimStart('-'), out var value)
            ? int.Parse(value, CultureInfo.InvariantCulture)
            : fallback;

    private static double ReadDecimal(Dictionary<string, string> values, string name, double fallback) =>
        values.TryGetValue(name.TrimStart('-'), out var value)
            ? double.Parse(value, CultureInfo.InvariantCulture)
            : fallback;
}
