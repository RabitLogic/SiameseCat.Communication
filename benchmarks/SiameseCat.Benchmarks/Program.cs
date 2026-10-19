using System.Text;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Protocols.S7;
using SiameseCat.Server.Modbus;
using SiameseCat.Server.S7;

namespace SiameseCat.Benchmarks;

internal static class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

/// <summary>Hot paths of the Modbus register helpers.</summary>
[MemoryDiagnoser]
public class RegisterConverterBenchmarks
{
    private readonly ushort[] _registers = new ushort[128];
    private readonly byte[] _bytes = new byte[64];

    [Benchmark]
    public uint ReadUInt32() => ModbusRegisterConverter.ReadUInt32(_registers, 4);

    [Benchmark]
    public double ReadDouble() => ModbusRegisterConverter.ReadDouble(_registers, 8);

    [Benchmark]
    public string ReadString() => ModbusRegisterConverter.ReadString(_registers, 0, 16);

    [Benchmark]
    public void WriteUInt32() => ModbusRegisterConverter.WriteUInt32(_registers, 4, 0xDEADBEEF);

    [Benchmark]
    public ushort Crc16() => ModbusCrc16.Compute(_bytes);
}

/// <summary>Address parsing for both protocol families.</summary>
[MemoryDiagnoser]
public class AddressParsingBenchmarks
{
    private readonly ModbusAddressParser _modbus = new();
    private readonly string _s7Address = "DB1.DBX2.3";

    [Benchmark]
    public PlcAddress ModbusReferenceNumber() => _modbus.Parse("40001");

    [Benchmark]
    public PlcAddress ModbusNamed() => _modbus.Parse("HR10");

    [Benchmark]
    public S7Address S7DataBlock() => S7Address.Parse(_s7Address);
}

/// <summary>Value encoding and decoding, including numeric arrays.</summary>
[MemoryDiagnoser]
public class ValueCodecBenchmarks
{
    private readonly PlcValue _scalar = PlcValue.FromUInt16(1234);
    private readonly PlcValue _array = PlcValue.FromUInt16Array(new ushort[10]);
    private readonly byte[] _scalarBytes = PlcValueCodec.ToBytes(PlcValue.FromUInt32(0xDEADBEEF));
    private readonly byte[] _arrayBytes = PlcValueCodec.ToBytes(PlcValue.FromUInt16Array(new ushort[10]));
    private readonly Encoding _ascii = Encoding.ASCII;

    [Benchmark]
    public byte[] EncodeScalar() => PlcValueCodec.ToBytes(_scalar);

    [Benchmark]
    public PlcValue DecodeScalar() => PlcValueCodec.FromBytes(PlcDataType.UInt16, _scalarBytes);

    [Benchmark]
    public byte[] EncodeArray() => PlcValueCodec.ToBytes(_array);

    [Benchmark]
    public PlcValue DecodeArray() => PlcValueCodec.FromBytes(PlcDataType.UInt16, _arrayBytes, count: 10);

    [Benchmark]
    public bool CompareArrays() => _array == PlcValue.FromUInt16Array(new ushort[10]);

    [Benchmark]
    public string EncodeString() => _ascii.GetString(_arrayBytes);
}

/// <summary>End-to-end request latency for the in-process slaves (loopback TCP).</summary>
[MemoryDiagnoser]
public class SlaveThroughputBenchmarks : IDisposable
{
    private ModbusTcpServer _modbusServer = null!;
    private ModbusTcpClient _modbusClient = null!;
    private S7Server _s7Server = null!;
    private S7Client _s7Client = null!;
    private ushort[] _writeValues = null!;
    private S7Address _s7WriteAddress;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _modbusServer = new ModbusTcpServer(new ModbusServerOptions { Port = 0, HoldingRegisters = 256 });
        await _modbusServer.StartAsync().ConfigureAwait(false);
        _modbusClient = new ModbusTcpClient(new ModbusTcpOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromSeconds(5),
            Tcp = { Host = "127.0.0.1", Port = _modbusServer.Port },
        });
        await _modbusClient.ConnectAsync().ConfigureAwait(false);

        _writeValues = Enumerable.Range(1, 10).Select(value => (ushort)value).ToArray();

        _s7Server = new S7Server(new S7ServerOptions { Port = 0, DefaultDataBlockSize = 64 });
        await _s7Server.StartAsync().ConfigureAwait(false);
        _s7Client = new S7Client(new S7Options
        {
            Host = "127.0.0.1",
            Port = _s7Server.Port,
            Timeout = TimeSpan.FromSeconds(5),
            ConnectTimeout = TimeSpan.FromSeconds(5),
        });
        await _s7Client.ConnectAsync().ConfigureAwait(false);
        _s7WriteAddress = S7Address.Parse("DB1.DBW4");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _modbusClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _modbusServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _s7Client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _s7Server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public Task<ushort[]> ModbusRead100() => _modbusClient.ReadHoldingRegistersAsync(1, 0, 100);

    [Benchmark]
    public Task ModbusWrite10() => _modbusClient.WriteMultipleRegistersAsync(1, 0, _writeValues);

    [Benchmark]
    public Task<float> S7ReadReal() => _s7Client.ReadSingleAsync(S7Address.Parse("DB1.DBD0"));

    [Benchmark]
    public Task S7WriteWord() => _s7Client.WriteUInt16Async(_s7WriteAddress, 0x1234);
}
