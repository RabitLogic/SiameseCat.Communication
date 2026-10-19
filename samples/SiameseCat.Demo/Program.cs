using System.Globalization;
using System.Text;
using SiameseCat.Core;
using SiameseCat.Drivers;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Protocols.S7;
using SiameseCat.Server;
using SiameseCat.Transport;

namespace SiameseCat.Demo;

internal static class Program
{
    private static readonly ushort[] ExpectedRegisters = { 100, 200, 300 };
    private static readonly ushort[] WriteOnlyRegisters = { 7, 8, 9 };
    private static readonly bool[] ExpectedCoils = { true, false, true };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "selftest":
                    await RunSelfTestAsync();
                    return 0;
                case "driver":
                    await RunDriverDemoAsync();
                    return 0;
                case "modbus-tcp":
                    await RunModbusTcpAsync(args);
                    return 0;
                case "modbus-rtu":
                    await RunModbusRtuAsync(args);
                    return 0;
                case "siemens":
                case "s7":
                    await RunS7Async(args);
                    return 0;
                case "tcp":
                    await RunTcpAsync(args);
                    return 0;
                case "serial":
                    await RunSerialAsync(args);
                    return 0;
                default:
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            if (ex.InnerException is not null)
            {
                Console.Error.WriteLine($"原因: {ex.InnerException.Message}");
            }

            return 2;
        }
    }

    private static async Task RunSelfTestAsync()
    {
        Console.WriteLine("== Modbus TCP 自测（内置从站）==");
        await using var server = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        await server.StartAsync();

        var options = new ModbusTcpOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromSeconds(2),
            Tcp = new TcpTransportOptions
            {
                Host = "127.0.0.1",
                Port = server.Port,
            },
        };

        await using var client = new ModbusTcpClient(options);
        await client.ConnectAsync();
        Console.WriteLine($"已连接 127.0.0.1:{server.Port}");

        await client.WriteMultipleRegistersAsync(0, ExpectedRegisters);
        var registers = await client.ReadHoldingRegistersAsync(0, 3);
        Ensure(registers.SequenceEqual(ExpectedRegisters), "保持寄存器读写");

        await client.WriteMultipleCoilsAsync(0, ExpectedCoils);
        var coils = await client.ReadCoilsAsync(0, 3);
        Ensure(coils[0] && !coils[1] && coils[2], "线圈读写");

        var floatRegisters = new ushort[2];
        ModbusRegisterConverter.WriteSingle(floatRegisters, 0, 3.14159f);
        await client.WriteMultipleRegistersAsync(10, floatRegisters);
        var floatResult = await client.ReadHoldingRegistersAsync(10, 2);
        Ensure(
            Math.Abs(ModbusRegisterConverter.ReadSingle(floatResult, 0) - 3.14159f) < 1e-4,
            "32 位浮点寄存器转换");

        var readWrite = await client.ReadWriteMultipleRegistersAsync(
            0, 3, 20, WriteOnlyRegisters);
        Ensure(readWrite.SequenceEqual(ExpectedRegisters), "FC23 读写多个寄存器");

        var address = S7Address.Parse("DB1.DBX2.3");
        Ensure(address.BitAddress == 19, "S7 地址解析");

        Console.WriteLine("自测通过。");
    }

    private static async Task RunDriverDemoAsync()
    {
        Console.WriteLine("== 可插拔驱动示例（内置 Modbus TCP 从站）==");
        await using var server = new ModbusTcpServer(new ModbusServerOptions { Port = 0 });
        await server.StartAsync();

        var registry = DriverRegistryExtensions.CreateDefaultRegistry();
        Console.WriteLine("已注册驱动: " + string.Join(", ", registry.Descriptors.Select(d => $"{d.Id} ({d.Name})")));

        await using var driver = registry.Create("modbus", new DriverConfiguration
        {
            Transport = TransportKind.Tcp,
            Host = "127.0.0.1",
            Port = server.Port,
            Timeout = TimeSpan.FromSeconds(2),
            RetryCount = 1,
            RetryDelay = TimeSpan.FromMilliseconds(100),
        });

        driver.StateChanged += (_, args) => Console.WriteLine($"  状态: {args.OldState} -> {args.NewState}");
        await driver.ConnectAsync();
        Console.WriteLine($"已连接，状态 = {driver.State}");

        var readable = (IReadable)driver;
        var writable = (IWritable)driver;
        var batch = (IBatchReadable)driver;

        await writable.WriteAsync(driver.AddressParser.Parse("40001"), PlcValue.FromUInt16(1234));
        Ensure((await readable.ReadAsync(driver.AddressParser.Parse("40001"))).AsUInt16() == 1234, "驱动读写");

        await writable.WriteAsync(driver.AddressParser.Parse("40002"), PlcValue.FromUInt16(5678));
        var values = await batch.ReadBatchAsync(new[]
        {
            driver.AddressParser.Parse("40001"),
            driver.AddressParser.Parse("40002"),
        });
        Ensure(values[0].AsUInt16() == 1234 && values[1].AsUInt16() == 5678, "批量读取");

        var tag = new PlcTag("speed", "40001", PlcDataType.UInt16, scale: 0.1, unit: "rpm");
        var speed = await driver.ReadTagAsync(tag);
        Ensure(Math.Abs(tag.ToEngineering(speed.AsDouble()) - 123.4) < 1e-6, "PlcTag 读取与换算");

        Console.WriteLine($"指标: {driver.Metrics.Snapshot()}");
        Console.WriteLine("驱动示例通过。");
    }

    private static async Task RunModbusTcpAsync(string[] args)
    {
        var host = Arg(args, 1, "127.0.0.1");
        var port = int.Parse(Arg(args, 2, "502"), CultureInfo.InvariantCulture);
        var unit = byte.Parse(Arg(args, 3, "1"), CultureInfo.InvariantCulture);

        var options = new ModbusTcpOptions
        {
            UnitId = unit,
            Timeout = TimeSpan.FromSeconds(3),
            Tcp = new TcpTransportOptions { Host = host, Port = port },
        };

        await using var client = new ModbusTcpClient(options);
        await client.ConnectAsync();
        Console.WriteLine($"已连接 {host}:{port} (unit {unit})");

        var registers = await client.ReadHoldingRegistersAsync(0, 10);
        PrintRegisters("保持寄存器 0-9", registers);

        var coils = await client.ReadCoilsAsync(0, 16);
        Console.WriteLine("线圈 0-15: " + string.Join(" ", coils.Select(value => value ? '1' : '0')));
    }

    private static async Task RunModbusRtuAsync(string[] args)
    {
        var portName = Arg(args, 1, "COM1");
        var baudRate = int.Parse(Arg(args, 2, "9600"), CultureInfo.InvariantCulture);
        var unit = byte.Parse(Arg(args, 3, "1"), CultureInfo.InvariantCulture);

        var options = new ModbusRtuOptions
        {
            UnitId = unit,
            Timeout = TimeSpan.FromSeconds(3),
            RetryCount = 1,
            SerialPort = new SerialPortOptions
            {
                PortName = portName,
                BaudRate = baudRate,
            },
        };

        await using var client = new ModbusRtuClient(options);
        await client.ConnectAsync();
        Console.WriteLine($"已打开 {portName} @ {baudRate} (unit {unit})");

        var registers = await client.ReadHoldingRegistersAsync(0, 10);
        PrintRegisters("保持寄存器 0-9", registers);
    }

    private static async Task RunS7Async(string[] args)
    {
        var host = Arg(args, 1, "192.168.0.10");
        var rack = int.Parse(Arg(args, 2, "0"), CultureInfo.InvariantCulture);
        var slot = int.Parse(Arg(args, 3, "1"), CultureInfo.InvariantCulture);
        var addressText = Arg(args, 4, "DB1.DBB0");

        var options = new S7Options
        {
            Host = host,
            Rack = rack,
            Slot = slot,
            Timeout = TimeSpan.FromSeconds(3),
        };

        await using var client = new S7Client(options);
        await client.ConnectAsync();
        Console.WriteLine($"已连接 {host} (rack {rack}, slot {slot}, PDU {client.PduSize})");

        var address = S7Address.Parse(addressText);
        var bytes = await client.ReadBytesAsync(address, 10);
        Console.WriteLine($"{address}: {BitConverter.ToString(bytes)}");
    }

    private static async Task RunTcpAsync(string[] args)
    {
        var host = Arg(args, 1, "127.0.0.1");
        var port = int.Parse(Arg(args, 2, "5000"), CultureInfo.InvariantCulture);
        var text = Arg(args, 3, "Hello PLC\r\n");

        await using var transport = new TcpTransport(new TcpTransportOptions
        {
            Host = host,
            Port = port,
            ConnectTimeout = TimeSpan.FromSeconds(3),
        });

        await transport.OpenAsync();
        Console.WriteLine($"已连接 {host}:{port}");

        await transport.WriteAsync(Encoding.UTF8.GetBytes(text));
        var response = await ReadForAsync(transport, TimeSpan.FromSeconds(1.5));
        PrintPayload(response);
    }

    private static async Task RunSerialAsync(string[] args)
    {
        var portName = Arg(args, 1, "COM1");
        var baudRate = int.Parse(Arg(args, 2, "9600"), CultureInfo.InvariantCulture);
        var text = Arg(args, 3, "Hello PLC\r\n");

        await using var transport = new SerialPortTransport(new SerialPortOptions
        {
            PortName = portName,
            BaudRate = baudRate,
            ReadTimeout = TimeSpan.FromMilliseconds(200),
        });

        await transport.OpenAsync();
        Console.WriteLine($"已打开 {portName} @ {baudRate}");

        await transport.WriteAsync(Encoding.UTF8.GetBytes(text));
        var response = await ReadForAsync(transport, TimeSpan.FromSeconds(1.5));
        PrintPayload(response);
    }

    private static async Task<byte[]> ReadForAsync(ITransport transport, TimeSpan duration)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        using var cts = new CancellationTokenSource(duration);

        try
        {
            while (true)
            {
                var read = await transport.ReadAsync(buffer, cts.Token);
                if (read <= 0)
                {
                    break;
                }

                stream.Write(buffer, 0, read);
            }
        }
        catch (OperationCanceledException)
        {
            // The idle timeout ended the read loop.
        }
        catch (ConnectionClosedException)
        {
        }

        return stream.ToArray();
    }

    private static void PrintRegisters(string title, ushort[] registers) =>
        Console.WriteLine($"{title}: {string.Join(", ", registers)}");

    private static void PrintPayload(byte[] payload)
    {
        if (payload.Length == 0)
        {
            Console.WriteLine("(没有收到数据)");
            return;
        }

        Console.WriteLine($"收到 {payload.Length} 字节");
        Console.WriteLine($"HEX: {BitConverter.ToString(payload)}");

        var text = Encoding.UTF8.GetString(payload);
        Console.WriteLine($"TEXT: {text.Replace("\r", "\\r").Replace("\n", "\\n")}");
    }

    private static void Ensure(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"自测失败: {name}");
        }

        Console.WriteLine($"  [通过] {name}");
    }

    private static string Arg(string[] args, int index, string defaultValue) =>
        args.Length > index ? args[index] : defaultValue;

    private static void PrintUsage()
    {
        Console.WriteLine("SiameseCat 通讯库示例");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  dotnet run -- selftest");
        Console.WriteLine("  dotnet run -- driver");
        Console.WriteLine("  dotnet run -- modbus-tcp <host> [port] [unit]");
        Console.WriteLine("  dotnet run -- modbus-rtu <com> [baud] [unit]");
        Console.WriteLine("  dotnet run -- siemens <host> [rack] [slot] [address]");
        Console.WriteLine("  dotnet run -- tcp <host> <port> [text]");
        Console.WriteLine("  dotnet run -- serial <com> [baud] [text]");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  dotnet run -- selftest");
        Console.WriteLine("  dotnet run -- driver");
        Console.WriteLine("  dotnet run -- modbus-tcp 192.168.0.10 502 1");
        Console.WriteLine("  dotnet run -- modbus-rtu COM3 9600 1");
        Console.WriteLine("  dotnet run -- siemens 192.168.0.10 0 1 DB1.DBB0");
    }
}

