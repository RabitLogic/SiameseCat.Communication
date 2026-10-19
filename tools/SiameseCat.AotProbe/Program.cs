using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SiameseCat.Communication;
using SiameseCat.Communication.Drivers;
using SiameseCat.Communication.Hosting;
using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.S7;
using SiameseCat.Communication.Server.Modbus;
using SiameseCat.Communication.Server.S7;
using SiameseCat.Communication.Transport;

namespace SiameseCat.AotProbe;

/// <summary>
/// Hardware-free, Native AOT compatible smoke test for every SiameseCat library.
/// It is published for several operating systems in CI and executed there.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("SiameseCat AOT probe");
        Console.WriteLine($"  Framework      : {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"  OS / RID       : {RuntimeInformation.RuntimeIdentifier}");
        Console.WriteLine($"  Dynamic code   : {RuntimeFeature.IsDynamicCodeSupported}");
        Console.WriteLine($"  Native AOT     : {!RuntimeFeature.IsDynamicCodeSupported}");
        Console.WriteLine();

        var failures = 0;
        failures += await CheckAsync("Modbus CRC16", CheckCrcAsync);
        failures += await CheckAsync("Modbus register conversion", CheckConvertersAsync);
        failures += await CheckAsync("Modbus options validation", CheckModbusOptionsAsync);
        failures += await CheckAsync("S7 address parsing", CheckS7AddressAsync);
        failures += await CheckAsync("Serial port options", CheckSerialOptionsAsync);
        failures += await CheckAsync("TCP transport loopback", CheckTcpAsync);
        failures += await CheckAsync("Modbus TCP round trip", CheckModbusTcpAsync);
        failures += await CheckAsync("Pluggable driver contract", CheckPlcDriverAsync);
        failures += await CheckAsync("Numeric arrays", CheckArrayValuesAsync);
        failures += await CheckAsync("Batch subscription", CheckBatchSubscriptionAsync);
        failures += await CheckAsync("Slave data store", CheckDataStoreAsync);
        failures += await CheckAsync("Modbus slave hosting", CheckSlaveHostingAsync);
        failures += await CheckAsync("S7 slave round trip", CheckS7SlaveAsync);
        failures += await CheckAsync("Transport injection", CheckTransportInjectionAsync);
        failures += await CheckAsync("Logging integration", CheckLoggingAsync);

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "AOT probe passed." : $"AOT probe failed: {failures} check(s).");
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

    private static Task CheckCrcAsync()
    {
        var crc = ModbusCrc16.Compute(new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A });
        Ensure(crc == 0xCDC5, $"Unexpected CRC 0x{crc:X4}.");

        var frame = new byte[8];
        new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A }.CopyTo(frame, 0);
        ModbusCrc16.Write(frame.AsSpan(0, 6), frame.AsSpan(6));
        Ensure(ModbusCrc16.Validate(frame), "CRC validation failed for a valid frame.");
        return Task.CompletedTask;
    }

    private static Task CheckConvertersAsync()
    {
        var registers = new ushort[4];
        ModbusRegisterConverter.WriteSingle(registers, 0, 12.5f);
        var single = ModbusRegisterConverter.ReadSingle(registers, 0);
        Ensure(Math.Abs(single - 12.5f) < 1e-6, $"Single round trip failed ({single}).");

        ModbusRegisterConverter.WriteDouble(registers, 0, Math.PI, ModbusWordOrder.LowWordFirst);
        var value = ModbusRegisterConverter.ReadDouble(registers, 0, ModbusWordOrder.LowWordFirst);
        Ensure(Math.Abs(value - Math.PI) < 1e-12, $"Double round trip failed ({value}).");

        const ulong raw = 0x0123456789ABCDEFUL;
        ModbusRegisterConverter.WriteUInt64(registers, 0, raw);
        Ensure(ModbusRegisterConverter.ReadUInt64(registers, 0) == raw, "UInt64 round trip failed.");

        var bytes = ModbusRegisterConverter.ReadBytes(registers, 0, 4);
        Ensure(bytes.Length == 8 && bytes[0] == 0x01 && bytes[7] == 0xEF, "Byte conversion failed.");

        var text = new ushort[4];
        ModbusRegisterConverter.WriteString(text, 0, 4, "AB");
        Ensure(ModbusRegisterConverter.ReadString(text, 0, 4) == "AB", "String round trip failed.");
        return Task.CompletedTask;
    }

    private static Task CheckModbusOptionsAsync()
    {
        var options = new ModbusTcpOptions
        {
            UnitId = 1,
            Tcp = { Host = "127.0.0.1", Port = 502 },
        };
        options.Validate();

        EnsureThrows<ArgumentOutOfRangeException>(() =>
            new ModbusTcpOptions { Tcp = { Port = 0 } }.Validate());
        EnsureThrows<ArgumentOutOfRangeException>(() =>
            new ModbusRtuOptions { SerialPort = { BaudRate = 0 } }.Validate());
        return Task.CompletedTask;
    }

    private static Task CheckS7AddressAsync()
    {
        var address = S7Address.Parse("DB1.DBX2.3");
        Ensure(address.Area == S7Area.DataBlock, "Unexpected S7 area.");
        Ensure(address.BitAddress == 19, $"Unexpected S7 bit address ({address.BitAddress}).");
        Ensure(address.ToString() == "DB1.DBX2.3", "S7 address round trip failed.");
        Ensure(address == S7Address.Parse("db1.dbx2.3"), "S7 address equality failed.");
        Ensure(address != S7Address.Parse("DB1.DBX2.4"), "S7 address inequality failed.");
        Ensure(!S7Address.TryParse("DB1.DBX", out _), "Invalid S7 address was accepted.");
        return Task.CompletedTask;
    }

    private static Task CheckSerialOptionsAsync()
    {
        new SerialPortOptions
        {
            PortName = "COM_TEST",
            BaudRate = 115200,
            Parity = Parity.Even,
            StopBits = StopBits.One,
        }.Validate();

        EnsureThrows<ArgumentException>(() => new SerialPortOptions { PortName = string.Empty }.Validate());
        return Task.CompletedTask;
    }

    private static async Task CheckTcpAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                var stream = client.GetStream();
                var buffer = new byte[4];
                await stream.ReadExactlyAsync(buffer).ConfigureAwait(false);
                await stream.WriteAsync(buffer).ConfigureAwait(false);
            });

            await using var transport = new TcpTransport(new TcpTransportOptions
            {
                Host = "127.0.0.1",
                Port = port,
                ConnectTimeout = TimeSpan.FromSeconds(2),
            });

            await transport.OpenAsync().ConfigureAwait(false);
            await transport.WriteAsync(new byte[] { 1, 2, 3, 4 }).ConfigureAwait(false);
            var echo = await transport.ReadExactlyAsync(4).ConfigureAwait(false);

            Ensure(echo.AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 4 }), "TCP echo mismatch.");
            await server.ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static readonly ushort[] SeedRegisters = { 100, 200 };

    private static async Task CheckModbusTcpAsync()
    {
        await using var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        await slave.StartAsync().ConfigureAwait(false);
        slave.Data.TryWriteHoldingRegisters(0, SeedRegisters);

        await using var client = new ModbusTcpClient(new ModbusTcpOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromSeconds(2),
            Tcp = { Host = "127.0.0.1", Port = slave.Port },
        });

        await client.ConnectAsync().ConfigureAwait(false);

        var registers = await client.ReadHoldingRegistersAsync(0, 2).ConfigureAwait(false);
        Ensure(registers[0] == 100 && registers[1] == 200, "Holding register read failed.");

        await client.WriteSingleRegisterAsync(2, 300).ConfigureAwait(false);
        var written = await client.ReadHoldingRegistersAsync(2, 1).ConfigureAwait(false);
        Ensure(written[0] == 300, "Holding register write failed.");
    }

    private static async Task CheckPlcDriverAsync()
    {
        await using var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        await slave.StartAsync().ConfigureAwait(false);
        slave.Data.TryWriteHoldingRegisters(0, SeedRegisters);

        var registry = DriverRegistryExtensions.CreateDefaultRegistry();
        Ensure(
            registry.Contains("modbus") && registry.Contains("siemens") && !registry.Contains("inovance"),
            "Built-in drivers are not registered correctly.");
        Ensure(registry.Descriptors.Count() == 2, "Unexpected number of registered drivers.");

        await using var driver = registry.Create("modbus", new DriverConfiguration
        {
            Transport = TransportKind.Tcp,
            Host = "127.0.0.1",
            Port = slave.Port,
            Timeout = TimeSpan.FromSeconds(2),
            ProbeAddress = "40001",
        });

        Ensure(driver.State == DriverState.Disconnected, "Driver should start disconnected.");
        await driver.ConnectAsync().ConfigureAwait(false);
        Ensure(driver.State == DriverState.Connected, "Driver did not reach the connected state.");

        var readable = (IReadable)driver;
        var writable = (IWritable)driver;
        var batch = (IBatchReadable)driver;

        Ensure(await ((IHealthCheckable)driver).PingAsync().ConfigureAwait(false), "Health probe failed.");

        var values = await batch.ReadBatchAsync(new[]
        {
            driver.AddressParser.Parse("40001"),
            driver.AddressParser.Parse("40002"),
        }).ConfigureAwait(false);

        Ensure(values[0].AsUInt16() == 100 && values[1].AsUInt16() == 200, "Batch read failed.");

        await writable.WriteAsync(driver.AddressParser.Parse("40003"), PlcValue.FromUInt16(42))
            .ConfigureAwait(false);
        var written = await readable.ReadAsync(driver.AddressParser.Parse("40003")).ConfigureAwait(false);
        Ensure(written.AsUInt16() == 42, "Driver write failed.");

        var batchWrite = (IBatchWritable)driver;
        await batchWrite.WriteBatchAsync(new[]
        {
            new PlcWriteRequest(driver.AddressParser.Parse("40010"), PlcValue.FromUInt16(7)),
            new PlcWriteRequest(driver.AddressParser.Parse("40011"), PlcValue.FromUInt16(8)),
        }).ConfigureAwait(false);

        var batchWritten = await batch.ReadBatchAsync(new[]
        {
            driver.AddressParser.Parse("40010"),
            driver.AddressParser.Parse("40011"),
        }).ConfigureAwait(false);

        Ensure(
            batchWritten[0].AsUInt16() == 7 && batchWritten[1].AsUInt16() == 8,
            "Batch write failed.");

        var metrics = driver.Metrics.Snapshot();
        Ensure(metrics.Requests >= 5, "Driver metrics were not recorded.");
        Ensure(
            metrics.Operations.Read >= 1
            && metrics.Operations.Write >= 1
            && metrics.Operations.ReadBatch >= 1
            && metrics.Operations.WriteBatch >= 1
            && metrics.Operations.Ping >= 1,
            "Per-operation metrics were not recorded.");
        Ensure(
            metrics.P95LatencyMs > 0 && metrics.MaxLatencyMs > 0,
            "Latency metrics were not recorded.");

        await driver.DisconnectAsync().ConfigureAwait(false);
        Ensure(driver.State == DriverState.Disconnected, "Driver did not disconnect.");

        await using var siemensDriver = registry.Create("siemens", new DriverConfiguration
        {
            Transport = TransportKind.Tcp,
            Host = "127.0.0.1",
            Port = 102,
        });

        Ensure(siemensDriver.AddressParser.Parse("DB1.DBD0").Region == "DB1", "Siemens address parsing failed.");
        Ensure(siemensDriver.AddressParser.Parse("MW2").Offset == 2, "Siemens memory address parsing failed.");
    }

    private static async Task CheckArrayValuesAsync()
    {
        var value = PlcValue.FromUInt16Array(new ushort[] { 1, 2, 3 });
        Ensure(value.IsArray && value.Length == 3, "Array value metadata is wrong.");
        Ensure(value.AsUInt16Array().SequenceEqual(new ushort[] { 1, 2, 3 }), "Array round trip failed.");
        Ensure(value == PlcValue.FromUInt16Array(new ushort[] { 1, 2, 3 }), "Array equality failed.");
        Ensure(value.ToString() == "UInt16[3]", $"Unexpected array text '{value}'.");

        var address = new PlcAddress("holding", 0, PlcDataType.UInt16, length: 3);
        Ensure(address.ByteSize == 6, $"Unexpected array byte size ({address.ByteSize}).");

        var bytes = PlcValueCodec.ToBytes(value);
        var decoded = PlcValueCodec.FromBytes(PlcDataType.UInt16, bytes, count: 3);
        Ensure(decoded.AsUInt16Array().SequenceEqual(new ushort[] { 1, 2, 3 }), "Array codec round trip failed.");

        await using var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        await slave.StartAsync().ConfigureAwait(false);

        var registry = DriverRegistryExtensions.CreateDefaultRegistry();
        await using var driver = registry.Create("modbus", new DriverConfiguration
        {
            Transport = TransportKind.Tcp,
            Host = "127.0.0.1",
            Port = slave.Port,
            Timeout = TimeSpan.FromSeconds(2),
            ProbeAddress = "40001",
        });

        await driver.ConnectAsync().ConfigureAwait(false);

        var arrayAddress = driver.AddressParser.Parse("HR0", PlcDataType.UInt16).WithLength(3);
        await ((IWritable)driver).WriteAsync(arrayAddress, value).ConfigureAwait(false);

        var readBack = await ((IReadable)driver).ReadAsync(arrayAddress).ConfigureAwait(false);
        Ensure(readBack.IsArray && readBack.Length == 3, "Array read did not return three elements.");
        Ensure(readBack.AsUInt16Array().SequenceEqual(new ushort[] { 1, 2, 3 }), "Array driver round trip failed.");
    }

    private static async Task CheckBatchSubscriptionAsync()
    {
        await using var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        await slave.StartAsync().ConfigureAwait(false);
        slave.Data.TryWriteHoldingRegisters(0, SeedRegisters);

        var registry = DriverRegistryExtensions.CreateDefaultRegistry();
        await using var driver = registry.Create("modbus", new DriverConfiguration
        {
            Transport = TransportKind.Tcp,
            Host = "127.0.0.1",
            Port = slave.Port,
            Timeout = TimeSpan.FromSeconds(2),
            ProbeAddress = "40001",
            SubscriptionJitter = false,
        });

        await driver.ConnectAsync().ConfigureAwait(false);

        var first = new TaskCompletionSource<IReadOnlyList<PlcValue>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await ((IBatchSubscribable)driver).SubscribeBatchAsync(
            new[]
            {
                driver.AddressParser.Parse("40001"),
                driver.AddressParser.Parse("40002"),
            },
            TimeSpan.FromMilliseconds(20),
            values =>
            {
                first.TrySetResult(values);
                return Task.CompletedTask;
            }).ConfigureAwait(false);

        var values = await first.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Ensure(
            values.Count == 2 && values[0].AsUInt16() == 100 && values[1].AsUInt16() == 200,
            "Batch subscription returned unexpected values.");
    }

    private static async Task CheckDataStoreAsync()
    {
        await using var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0, Coils = 64 });
        await slave.StartAsync().ConfigureAwait(false);

        ModbusRegisterWriteEventArgs? registerWrite = null;
        ModbusCoilWriteEventArgs? coilWrite = null;
        slave.Data.HoldingRegistersWritten += (_, args) => registerWrite = args;
        slave.Data.CoilsWritten += (_, args) => coilWrite = args;

        await using var client = new ModbusTcpClient(new ModbusTcpOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromSeconds(2),
            Tcp = { Host = "127.0.0.1", Port = slave.Port },
        });

        await client.ConnectAsync().ConfigureAwait(false);
        await client.WriteSingleRegisterAsync(3, 0x1234).ConfigureAwait(false);
        await client.WriteSingleCoilAsync(33, true).ConfigureAwait(false);

        Ensure(registerWrite is { Start: 3 } && registerWrite.Values[0] == 0x1234, "Register write event was not raised.");
        Ensure(coilWrite is { Start: 33 } && coilWrite.Values[0], "Coil write event was not raised.");

        var snapshot = slave.Data.Snapshot();
        Ensure(snapshot.HoldingRegisters[3] == 0x1234, "Snapshot does not contain the written register.");

        slave.Data.Reset();
        Ensure(slave.Data[3] == 0 && !slave.Data.ReadCoils(33, 1)![0], "Reset did not clear the store.");

        slave.Data.Load(snapshot);
        Ensure(slave.Data[3] == 0x1234 && slave.Data.ReadCoils(33, 1)![0], "Load did not restore the snapshot.");
    }

    private static async Task CheckTransportInjectionAsync()
    {
        await using var slave = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        await slave.StartAsync().ConfigureAwait(false);
        slave.Data.TryWriteHoldingRegisters(0, SeedRegisters);

        await using var factory = new SharedTransportFactory(() => new TcpTransport(new TcpTransportOptions
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            ConnectTimeout = TimeSpan.FromSeconds(2),
        }));

        await using var firstDriver = CreateSharedDriver(factory, unitId: 1);
        await using var secondDriver = CreateSharedDriver(factory, unitId: 2);

        Ensure(
            (firstDriver.Descriptor.Capabilities & DriverCapabilities.Read) != 0,
            "Driver capabilities were not declared.");

        await firstDriver.ConnectAsync().ConfigureAwait(false);
        await secondDriver.ConnectAsync().ConfigureAwait(false);

        var first = await ((IReadable)firstDriver)
            .ReadAsync(firstDriver.AddressParser.Parse("40001")).ConfigureAwait(false);
        var second = await ((IReadable)secondDriver)
            .ReadAsync(secondDriver.AddressParser.Parse("40002")).ConfigureAwait(false);

        Ensure(first.AsUInt16() == 100 && second.AsUInt16() == 200, "Shared transport read failed.");
    }

    private static IPlcDriver CreateSharedDriver(SharedTransportFactory factory, byte unitId)
    {
        var configuration = new DriverConfiguration
        {
            Transport = TransportKind.Custom,
            TransportFactory = factory,
            RequestLock = factory.RequestLock,
            Timeout = TimeSpan.FromSeconds(2),
            LogFrames = true,
        };
        configuration.Options["unitId"] = unitId.ToString(CultureInfo.InvariantCulture);
        configuration.Options["framing"] = "tcp";

        return DriverRegistryExtensions.CreateDefaultRegistry().Create("modbus", configuration);
    }

    private static Task CheckLoggingAsync()
    {
        var logger = new MicrosoftLoggerAdapter(NullLogger.Instance);

        logger.Log(PlcLogLevel.Warning, "AOT probe log entry");
        Ensure(
            MicrosoftLoggerAdapter.ToLogLevel(PlcLogLevel.Warning) == LogLevel.Warning,
            "Log level mapping failed.");

        return Task.CompletedTask;
    }

    private static async Task CheckSlaveHostingAsync()
    {
        var services = new ServiceCollection();
        services.AddModbusServer(options => options.Port = 0);
        await using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().Single();
        await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);

        var server = provider.GetRequiredService<IModbusServer>();
        Ensure(server.IsRunning, "The hosted Modbus slave did not start.");

        await using var client = new ModbusTcpClient(new ModbusTcpOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromSeconds(2),
            Tcp = { Host = "127.0.0.1", Port = ((ModbusTcpServer)server).Port },
        });

        await client.ConnectAsync().ConfigureAwait(false);
        await client.WriteSingleRegisterAsync(1, 4, 321).ConfigureAwait(false);
        var values = await client.ReadHoldingRegistersAsync(1, 4, 1).ConfigureAwait(false);

        Ensure(values[0] == 321, "The hosted slave did not serve the request.");
        Ensure(server.Statistics.GetRequests(0x03) == 1 && server.Statistics.GetRequests(0x06) == 1, "Statistics were not recorded.");
        Ensure(server.Data.EventCount == 1, "The event counter did not record the write.");

        await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
        Ensure(!server.IsRunning, "The hosted Modbus slave did not stop.");
    }

    private static async Task CheckS7SlaveAsync()
    {
        await using var server = new S7Server(new S7ServerOptions { Port = 0, DefaultDataBlockSize = 64 });
        await server.StartAsync().ConfigureAwait(false);

        await using var client = new S7Client(new S7Options
        {
            Host = "127.0.0.1",
            Port = server.Port,
            Timeout = TimeSpan.FromSeconds(2),
            ConnectTimeout = TimeSpan.FromSeconds(2),
        });

        await client.ConnectAsync().ConfigureAwait(false);
        Ensure(client.PduSize == 960, $"Unexpected negotiated PDU size ({client.PduSize}).");

        await client.WriteSingleAsync(S7Address.Parse("DB1.DBD0"), 1.5f).ConfigureAwait(false);
        var value = await client.ReadSingleAsync(S7Address.Parse("DB1.DBD0")).ConfigureAwait(false);
        Ensure(Math.Abs(value - 1.5f) < 1e-6, "The S7 slave did not return the written value.");

        await client.WriteBoolAsync(S7Address.Parse("M0.1"), true).ConfigureAwait(false);
        Ensure(await client.ReadBoolAsync(S7Address.Parse("M0.1")).ConfigureAwait(false), "S7 bit write failed.");

        Ensure(
            server.Statistics.ReadRequests == 2 && server.Statistics.WriteRequests == 2,
            "S7 slave statistics were not recorded.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void EnsureThrows<TException>(Action action)
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

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
