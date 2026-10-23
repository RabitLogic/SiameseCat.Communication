using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using SiameseCat.Communication.Drivers;
using SiameseCat.Communication.Simulation.Modbus;
using SiameseCat.Soak;

SoakOptions options;
try
{
    options = SoakOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    Console.Error.WriteLine($"Bad arguments: {ex.Message}");
    SoakOptions.PrintHelp();
    return 2;
}

if (options.ShowHelp)
{
    SoakOptions.PrintHelp();
    return 0;
}

Console.WriteLine("SiameseCat soak test");
Console.WriteLine(new string('=', 72));
Console.WriteLine($"duration        {options.Duration}");
Console.WriteLine($"workers         {options.Workers}");
Console.WriteLine($"cycle           read {options.RegisterCount} registers, write every {options.WriteEvery} cycles");
Console.WriteLine($"timing          interval {options.Interval}, backoff {options.Backoff}");
Console.WriteLine($"fault injection {(options.FaultPeriod > TimeSpan.Zero ? $"every {options.FaultPeriod} for {options.FaultDuration}" : "disabled")}");
Console.WriteLine($"thresholds      success rate >= {options.MinSuccessRate:P0}, heap +{options.MaxHeapGrowthMb} MB, "
    + $"handles +{options.MaxHandleGrowth}, threads +{options.MaxThreadGrowth}");
Console.WriteLine();

await using var slave = new ModbusTcpSimulator(new ModbusSimulatorOptions { Port = 0, HoldingRegisters = 256 });
await slave.StartAsync();
for (var bank = 0; bank < 128; bank += options.RegisterCount + 8)
{
    slave.Data.TryWriteHoldingRegisters(bank, Enumerable.Range(0, Math.Min(8, 128 - bank)).Select(value => (ushort)value).ToArray());
}

await using var proxy = new FaultProxy(slave.Port);
proxy.Start();

Console.WriteLine($"slave  127.0.0.1:{slave.Port}");
Console.WriteLine($"proxy  127.0.0.1:{proxy.Port}");
Console.WriteLine();

var registry = DriverRegistryExtensions.CreateDefaultRegistry();
var counters = new SoakCounters();

using var runCts = new CancellationTokenSource(options.Duration);
var workerTasks = Enumerable.Range(0, options.Workers)
    .Select(index => new SoakWorker(index, registry, options, proxy.Port, counters).RunAsync(runCts.Token))
    .ToArray();
var faultTask = options.FaultPeriod > TimeSpan.Zero
    ? InjectFaultsAsync(proxy, options, runCts.Token)
    : Task.CompletedTask;

var process = Process.GetCurrentProcess();
var clock = Stopwatch.StartNew();
var warmup = TimeSpan.FromSeconds(5);
long baselineHeap = 0;
var baselineHandles = 0;
var baselineThreads = 0;
var baselineTaken = false;
var previousAttempts = 0L;

Console.WriteLine($"{"t",-6}{"ops",-9}{"op/s",-8}{"err",-7}{"ok%",-7}{"heap",-9}{"ws",-9}{"threads",-9}{"handles",-9}state");

while (!runCts.IsCancellationRequested)
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(1), runCts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }

    process.Refresh();
    var heap = GC.GetTotalMemory(forceFullCollection: false);
    var handles = TryGetHandleCount(process);

    if (!baselineTaken && clock.Elapsed >= warmup)
    {
        baselineHeap = heap;
        baselineHandles = handles;
        baselineThreads = process.Threads.Count;
        baselineTaken = true;
    }

    var attempts = counters.Attempts;
    var failures = counters.Failures;
    var deltaAttempts = attempts - previousAttempts;
    previousAttempts = attempts;

    Console.WriteLine(
        $"{clock.Elapsed,-6:mm\\:ss}"
        + $"{attempts,-9}"
        + $"{deltaAttempts,-8}"
        + $"{failures,-7}"
        + $"{counters.SuccessRate,-7:P0}"
        + $"{(heap / 1024.0 / 1024.0),-9:0.0}"
        + $"{(process.WorkingSet64 / 1024.0 / 1024.0),-9:0.0}"
        + $"{process.Threads.Count,-9}"
        + $"{(handles < 0 ? "n/a" : handles.ToString(CultureInfo.InvariantCulture)),-9}"
        + (proxy.IsFaulted ? "link down" : "ok"));
}

await Task.WhenAll(workerTasks);
await faultTask;

proxy.SetFault(false);

var verification = new List<string>();

var finalRecovery = options.FaultPeriod > TimeSpan.Zero
    && await SoakWorker.VerifyRecoveryAsync(registry, options, proxy.Port, CancellationToken.None);
if (options.FaultPeriod > TimeSpan.Zero && !finalRecovery)
{
    verification.Add("the link did not recover after the last fault window");
}

if (counters.SuccessRate < options.MinSuccessRate)
{
    verification.Add($"success rate {counters.SuccessRate:P1} is below {options.MinSuccessRate:P0}");
}

process.Refresh();
var finalHeap = GC.GetTotalMemory(forceFullCollection: false);
var finalHandles = TryGetHandleCount(process);
var heapGrowthMb = (finalHeap - baselineHeap) / 1024.0 / 1024.0;

if (baselineTaken && heapGrowthMb > options.MaxHeapGrowthMb)
{
    verification.Add($"managed heap grew by {heapGrowthMb:0.0} MB (limit {options.MaxHeapGrowthMb} MB)");
}

if (baselineTaken && baselineHandles >= 0 && finalHandles - baselineHandles > options.MaxHandleGrowth)
{
    verification.Add($"handles grew by {finalHandles - baselineHandles} (limit {options.MaxHandleGrowth})");
}

if (baselineTaken && process.Threads.Count - baselineThreads > options.MaxThreadGrowth)
{
    verification.Add($"threads grew by {process.Threads.Count - baselineThreads} (limit {options.MaxThreadGrowth})");
}

Console.WriteLine();
Console.WriteLine(new string('=', 72));
Console.WriteLine($"elapsed            {clock.Elapsed:mm\\:ss}");
Console.WriteLine($"cycles             {counters.Attempts} ({counters.Successes} ok, {counters.Failures} failed)");
Console.WriteLine($"success rate       {counters.SuccessRate:P2}");
Console.WriteLine($"reconnects         {counters.Reconnects}");
Console.WriteLine($"last success       {counters.LastSuccess?.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "never"}");
Console.WriteLine($"proxy              dropped {proxy.DroppedConnections} connections, rejected {proxy.RejectedConnections} attempts");
Console.WriteLine($"slave              {slave.Statistics.TotalRequests} requests, {slave.Statistics.TotalExceptions} exceptions, "
    + $"{slave.Statistics.FramesDiscarded} discarded frames");
Console.WriteLine($"managed heap       {(finalHeap / 1024.0 / 1024.0):0.0} MB (growth {heapGrowthMb:+0.0;-0.0;0.0} MB)");
Console.WriteLine($"handles            {(finalHandles < 0 ? "n/a" : finalHandles.ToString(CultureInfo.InvariantCulture))} "
    + (baselineTaken && finalHandles >= 0 ? $"(growth {finalHandles - baselineHandles:+0;-0;0})" : string.Empty));
Console.WriteLine($"threads            {process.Threads.Count} "
    + (baselineTaken ? $"(growth {process.Threads.Count - baselineThreads:+0;-0;0})" : string.Empty));
Console.WriteLine($"gc collections     gen0 {GC.CollectionCount(0)}, gen1 {GC.CollectionCount(1)}, gen2 {GC.CollectionCount(2)}");
Console.WriteLine($"recovery probe     {(options.FaultPeriod > TimeSpan.Zero ? finalRecovery.ToString() : "n/a")}");

if (counters.ErrorKinds.Count > 0)
{
    Console.WriteLine("failure kinds");
    foreach (var kind in counters.ErrorKinds.OrderByDescending(pair => pair.Value))
    {
        Console.WriteLine($"  {kind.Key,-32} {kind.Value}");
    }
}

Console.WriteLine();

if (verification.Count == 0)
{
    Console.WriteLine("PASS: every invariant held.");
    return 0;
}

Console.WriteLine("FAIL:");
foreach (var reason in verification)
{
    Console.WriteLine($"  - {reason}");
}

return 1;

static async Task InjectFaultsAsync(FaultProxy proxy, SoakOptions options, CancellationToken token)
{
    try
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(options.FaultPeriod, token);
            proxy.SetFault(true);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} link down");
            await Task.Delay(options.FaultDuration, token);
            proxy.SetFault(false);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} link restored");
        }
    }
    catch (OperationCanceledException)
    {
    }
}

static int TryGetHandleCount(Process process)
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        return -1;
    }

    try
    {
        return process.HandleCount;
    }
    catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
    {
        return -1;
    }
}
