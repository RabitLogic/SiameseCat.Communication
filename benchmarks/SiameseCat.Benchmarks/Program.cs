using System.Text;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Protocols.S7;

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
