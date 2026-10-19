using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using SiameseCat.Communication;
using SiameseCat.Communication.Drivers;
using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.S7;
using SiameseCat.Communication.Server.Modbus;
using SiameseCat.Communication.Server.S7;

namespace SiameseCat.Compatibility;

/// <summary>
/// Runs the netstandard2.0 assets on .NET Framework. The unit tests run on net8.0; this runner proves
/// that addressing, protocol clients and the driver runtime also work on a classic .NET Framework host.
/// </summary>
internal static class Program
{
    private static readonly ushort[] SeedRegisters = { 100, 200 };

    private static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("SiameseCat .NET Framework compatibility runner");
        Console.WriteLine($"  Runtime        : {typeof(object).Assembly.ImageRuntimeVersion}");
        Console.WriteLine($"  CLR version    : {Environment.Version}");
        Console.WriteLine();

        var failures = 0;
        failures += await CheckAsync("Modbus address parsing", CheckModbusAddressAsync);
        failures += await CheckAsync("S7 address parsing", CheckS7AddressAsync);
        failures += await CheckAsync("Value model", CheckValueModelAsync);
        failures += await CheckAsync("Argument checks", CheckArgumentChecksAsync);
        failures += await CheckAsync("Modbus TCP round trip", CheckModbusTcpAsync);
        failures += await CheckAsync("Data store events", CheckDataStoreEventsAsync);
        failures += await CheckAsync("S7 slave round trip", CheckS7ServerAsync);
        failures += await CheckAsync("Driver contract", CheckDriverAsync);
        failures += await CheckAsync("Frame logging", CheckFrameLoggingAsync);

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "Compatibility runner passed." : $"Compatibility runner failed: {failures} check(s).");
        return failures == 0 ? 0 : 1;
    }

    private static async Task<int> CheckAsync(string name, Func<Task> check)
    {
        try
        {
            await check().ConfigureAwait(false);
            Console.WriteLine($"  [PASS] {name}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static Task CheckModbusAddressAsync()
    {
        var parser = new ModbusAddressParser();

        var holding = parser.Parse("40001");
        Ensure(holding.Region == ModbusAddressParser.HoldingRegisterRegion && holding.Offset == 0, "40001 did not parse.");

        var named = parser.Parse("HR10", PlcDataType.Float);
        Ensure(named.Offset == 10 && named.DataType == PlcDataType.Float, "HR10 did not parse.");

        return Task.CompletedTask;
    }

    private static Task CheckS7AddressAsync()
    {
        var address = S7Address.Parse("DB1.DBX2.3");
        Ensure(address.Area == S7Area.DataBlock && address.DbNumber == 1, "S7 DB address did not parse.");
        Ensure(S7Address.TryParse("MW4", out var word) && word.ByteOffset == 4, "S7 MW4 did not parse.");

        return Task.CompletedTask;
    }

    private static Task CheckValueModelAsync()
    {
        var value = PlcValue.FromDouble(1.5).WithTimestamp(DateTimeOffset.UtcNow);
        Ensure(Math.Abs(value.AsDouble() - 1.5) < 1e-9, "Double conversion failed.");
        Ensure(value.IsGood && value.Timestamp.HasValue, "Value metadata is missing.");
        Ensure(PlcValue.FromUInt16(7) == PlcValue.FromUInt16(7), "Value equality failed.");

        var symbol = PlcAddress.FromSymbol("ns=2", "s=Speed", PlcDataType.Int16);
        Ensure(symbol.Symbol == "s=Speed" && symbol.ToString() == "ns=2:s=Speed[Int16]", "Symbolic address failed.");

        var array = PlcValue.FromUInt16Array(new ushort[] { 10, 20, 30 });
        Ensure(array.IsArray && array.Length == 3 && array.AsUInt16Array()[2] == 30, "Numeric arrays failed.");
        Ensure(
            new PlcAddress("holding", 0, PlcDataType.UInt16, length: 3).ByteSize == 6,
            "Array address size failed.");

        var tag = new PlcTag("temperature", "40001", PlcDataType.Int16, scale: 0.1, offset: -40);
        Ensure(Math.Abs(tag.ToEngineering(100) - (-30)) < 1e-9, "Tag scaling failed.");

        return Task.CompletedTask;
    }

    private static Task CheckArgumentChecksAsync()
    {
        EnsureThrows<ArgumentException>(() => new PlcTag(" ", "40001", PlcDataType.UInt16));

        var tag = new PlcTag("speed", "40001", PlcDataType.UInt16);
        EnsureThrows<ArgumentNullException>(() => PlcTagExtensions.ReadTagAsync(null!, tag));
        EnsureThrows<ArgumentException>(() => new PlcAddress(string.Empty, 0, PlcDataType.UInt16));
        EnsureThrows<ArgumentOutOfRangeException>(() => new PlcAddress("coil", 0, PlcDataType.Bool, 2));
        EnsureThrows<ArgumentOutOfRangeException>(() => new PlcAddress("holding", 0, PlcDataType.UInt16, 0));

        var configuration = new DriverConfiguration { Transport = TransportKind.Custom };
        EnsureThrows<ArgumentException>(() =>
        {
            configuration.Validate();
            return configuration;
        });

        return Task.CompletedTask;
    }

    private static async Task CheckModbusTcpAsync()
    {
        await using (var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 }))
        {
            await slave.StartAsync().ConfigureAwait(false);
            slave.Data.TryWriteHoldingRegisters(0, SeedRegisters);

            await using (var client = new ModbusTcpClient(new ModbusTcpOptions
            {
                UnitId = 1,
                Timeout = TimeSpan.FromSeconds(2),
                Tcp = { Host = "127.0.0.1", Port = slave.Port },
            }))
            {
                await client.ConnectAsync().ConfigureAwait(false);

                var registers = await client.ReadHoldingRegistersAsync(1, 0, 2).ConfigureAwait(false);
                Ensure(registers[0] == 100 && registers[1] == 200, "Holding register read failed.");

                await client.WriteSingleRegisterAsync(1, 2, 300).ConfigureAwait(false);
                var written = await client.ReadHoldingRegistersAsync(1, 2, 1).ConfigureAwait(false);
                Ensure(written[0] == 300, "Holding register write failed.");

                var snapshot = slave.Data.Snapshot();
                slave.Data.Reset();
                Ensure(slave.Data[2] == 0, "Reset did not clear the store.");
                slave.Data.Load(snapshot);
                Ensure(slave.Data[2] == 300, "Load did not restore the snapshot.");
            }
        }
    }

    private static Task CheckDataStoreEventsAsync()
    {
        var store = new ModbusDataStore(holdingRegisters: 8, coils: 64);
        ModbusRegisterWriteEventArgs? registerWrite = null;
        ModbusCoilWriteEventArgs? coilWrite = null;
        store.HoldingRegistersWritten += (_, args) => registerWrite = args;
        store.CoilsWritten += (_, args) => coilWrite = args;

        var written = new ushort[] { 7, 8 };
        var bits = new[] { true };
        store.TryWriteHoldingRegisters(4, written);
        store.TryWriteCoils(40, bits);

        Ensure(
            registerWrite != null && registerWrite.Start == 4 && registerWrite.Count == 2,
            "Register write event was not raised.");
        Ensure(
            coilWrite != null && coilWrite.Start == 40 && coilWrite.Values[0],
            "Coil write event was not raised.");
        return Task.CompletedTask;
    }

    private static async Task CheckS7ServerAsync()
    {
        await using (var slave = new S7Server(new S7ServerOptions { Port = 0, DefaultDataBlockSize = 32 }))
        {
            await slave.StartAsync().ConfigureAwait(false);

            await using (var client = new SiameseCat.Communication.S7.S7Client(new SiameseCat.Communication.S7.S7Options
            {
                Host = "127.0.0.1",
                Port = slave.Port,
                Timeout = TimeSpan.FromSeconds(2),
                ConnectTimeout = TimeSpan.FromSeconds(2),
            }))
            {
                await client.ConnectAsync().ConfigureAwait(false);

                var address = SiameseCat.Communication.S7.S7Address.Parse("DB1.DBW4");
                await client.WriteUInt16Async(address, 0x4321).ConfigureAwait(false);
                var value = await client.ReadUInt16Async(address).ConfigureAwait(false);

                Ensure(value == 0x4321, "The S7 slave did not return the written value.");
                Ensure(slave.Statistics.ReadRequests == 1, "S7 statistics were not recorded.");
            }
        }
    }

    private static async Task CheckDriverAsync()
    {
        await using (var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 }))
        {
            await slave.StartAsync().ConfigureAwait(false);
            slave.Data.TryWriteHoldingRegisters(0, SeedRegisters);

            var registry = DriverRegistryExtensions.CreateDefaultRegistry();
            Ensure(
                registry.Contains("modbus") && registry.Contains("siemens") && !registry.Contains("inovance"),
                "Built-in drivers are missing.");

            var driver = registry.Create("modbus", new DriverConfiguration
            {
                Transport = TransportKind.Tcp,
                Host = "127.0.0.1",
                Port = slave.Port,
                Timeout = TimeSpan.FromSeconds(2),
                ProbeAddress = "40001",
            });

            try
            {
                await driver.ConnectAsync().ConfigureAwait(false);
                Ensure(driver.State == DriverState.Connected, "Driver did not connect.");

                var readable = (IReadable)driver;
                var writable = (IWritable)driver;

                var value = await readable.ReadAsync(driver.AddressParser.Parse("40001")).ConfigureAwait(false);
                Ensure(value.AsUInt16() == 100, "Driver read failed.");

                await writable.WriteAsync(driver.AddressParser.Parse("40005"), PlcValue.FromUInt16(42))
                    .ConfigureAwait(false);
                var written = await readable.ReadAsync(driver.AddressParser.Parse("40005")).ConfigureAwait(false);
                Ensure(written.AsUInt16() == 42, "Driver write failed.");

                Ensure(await ((IHealthCheckable)driver).PingAsync().ConfigureAwait(false), "Health probe failed.");
                Ensure(driver.Metrics.Snapshot().Requests >= 4, "Metrics were not recorded.");
            }
            finally
            {
                await driver.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task CheckFrameLoggingAsync()
    {
        await using (var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 }))
        {
            await slave.StartAsync().ConfigureAwait(false);
            slave.Data.TryWriteHoldingRegisters(0, SeedRegisters);

            var logger = new RecordingLogger();
            await using (var client = new ModbusTcpClient(new ModbusTcpOptions
            {
                UnitId = 1,
                Timeout = TimeSpan.FromSeconds(2),
                LogFrames = true,
                Logger = logger,
                Tcp = { Host = "127.0.0.1", Port = slave.Port },
            }))
            {
                await client.ConnectAsync().ConfigureAwait(false);
                await client.ReadHoldingRegistersAsync(1, 0, 1).ConfigureAwait(false);
            }

            Ensure(logger.Contains(PlcLogLevel.Trace, "TX"), "TX frame was not logged.");
            Ensure(logger.Contains(PlcLogLevel.Trace, "RX"), "RX frame was not logged.");
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void EnsureThrows<TException>(Func<object> action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
    }

    private sealed class RecordingLogger : IPlcLogger
    {
        private readonly List<string> _messages = new List<string>();

        public void Log(PlcLogLevel level, string message, Exception? exception = null) =>
            _messages.Add(level + ":" + message);

        public bool Contains(PlcLogLevel level, string text)
        {
            var prefix = level + ":";
            foreach (var message in _messages)
            {
                if (message.StartsWith(prefix, StringComparison.Ordinal)
                    && message.IndexOf(text, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

}
