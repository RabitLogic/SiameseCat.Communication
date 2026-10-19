using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using SiameseCat.Core;
using SiameseCat.Drivers;
using SiameseCat.Drivers.Inovance;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Protocols.S7;

namespace SiameseCat.Compatibility;

/// <summary>
/// Runs the netstandard2.0 assets on .NET Framework. The unit tests run on net8.0; this runner proves
/// that addressing, protocol clients and the driver runtime also work on a classic .NET Framework host.
/// </summary>
internal static class Program
{
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
        failures += await CheckAsync("Inovance address parsing", CheckInovanceAddressAsync);
        failures += await CheckAsync("Value model", CheckValueModelAsync);
        failures += await CheckAsync("Argument checks", CheckArgumentChecksAsync);
        failures += await CheckAsync("Modbus TCP round trip", CheckModbusTcpAsync);
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

    private static Task CheckInovanceAddressAsync()
    {
        var parser = new InovanceAddressParser();
        var d = parser.Parse("D100");
        Ensure(d.Region == ModbusAddressParser.HoldingRegisterRegion && d.Offset == 100, "D100 did not parse.");

        var y = parser.Parse("Y10");
        Ensure(y.Offset == 8200, "Y10 octal mapping is wrong.");

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
        EnsureThrows<ArgumentOutOfRangeException>(() => new PlcAddress("holding", 0, PlcDataType.UInt16, 2));

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
        using (var slave = new ModbusTcpSlave())
        {
            slave.Start();

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
            }
        }
    }

    private static async Task CheckDriverAsync()
    {
        using (var slave = new ModbusTcpSlave())
        {
            slave.Start();

            var registry = DriverRegistryExtensions.CreateDefaultRegistry();
            Ensure(
                registry.Contains("modbus") && registry.Contains("siemens") && registry.Contains("inovance"),
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
        using (var slave = new ModbusTcpSlave())
        {
            slave.Start();

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

    /// <summary>Minimal Modbus TCP slave used to exercise the client without a real device.</summary>
    private sealed class ModbusTcpSlave : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly ushort[] _registers = new ushort[64];
        private readonly Thread _thread;
        private volatile bool _stopped;

        public ModbusTcpSlave()
        {
            _registers[0] = 100;
            _registers[1] = 200;
            _thread = new Thread(AcceptLoop) { IsBackground = true };
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public void Start()
        {
            _listener.Start();
            _thread.Start();
        }

        public void Dispose()
        {
            _stopped = true;
            _listener.Stop();
        }

        private void AcceptLoop()
        {
            while (!_stopped)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var header = new byte[7];

                while (!_stopped)
                {
                    if (!ReadExactly(stream, header, header.Length))
                    {
                        return;
                    }

                    var length = (header[4] << 8) | header[5];
                    var pdu = new byte[length - 1];
                    if (!ReadExactly(stream, pdu, pdu.Length))
                    {
                        return;
                    }

                    var response = Handle(pdu);
                    if (response == null)
                    {
                        continue;
                    }

                    var frame = new byte[7 + response.Length];
                    frame[0] = header[0];
                    frame[1] = header[1];
                    frame[4] = (byte)((response.Length + 1) >> 8);
                    frame[5] = (byte)(response.Length + 1);
                    frame[6] = header[6];
                    response.CopyTo(frame, 7);
                    stream.Write(frame, 0, frame.Length);
                }
            }
        }

        private byte[]? Handle(byte[] pdu)
        {
            var function = pdu[0];

            if (function == 0x03)
            {
                var start = (pdu[1] << 8) | pdu[2];
                var count = (pdu[3] << 8) | pdu[4];
                var response = new byte[2 + (count * 2)];
                response[0] = 0x03;
                response[1] = (byte)(count * 2);

                for (var i = 0; i < count; i++)
                {
                    var value = _registers[start + i];
                    response[2 + (i * 2)] = (byte)(value >> 8);
                    response[3 + (i * 2)] = (byte)value;
                }

                return response;
            }

            if (function == 0x06)
            {
                var address = (pdu[1] << 8) | pdu[2];
                _registers[address] = (ushort)((pdu[3] << 8) | pdu[4]);
                return pdu;
            }

            return null;
        }

        private static bool ReadExactly(NetworkStream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }
    }
}
