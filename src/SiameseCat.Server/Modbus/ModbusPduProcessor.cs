using SiameseCat.Core;

namespace SiameseCat.Server.Modbus;

/// <summary>Shared Modbus PDU handling used by the TCP and RTU slaves.</summary>
internal sealed class ModbusPduProcessor
{
    private readonly ModbusDataStore _data;
    private readonly ModbusServerStatistics _statistics;
    private readonly byte[] _identifierBytes;
    private readonly bool _runIndicator;
    private readonly IPlcLogger? _logger;

    public ModbusPduProcessor(
        ModbusDataStore data,
        ModbusServerStatistics statistics,
        string serverIdentifier,
        bool runIndicator,
        IPlcLogger? logger)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _identifierBytes = System.Text.Encoding.ASCII.GetBytes(serverIdentifier);
        _runIndicator = runIndicator;
        _logger = logger;
    }

    private ModbusDataStore Data => _data;

    private const byte FunctionReadCoils = 0x01;
    private const byte FunctionReadDiscreteInputs = 0x02;
    private const byte FunctionReadHoldingRegisters = 0x03;
    private const byte FunctionReadInputRegisters = 0x04;
    private const byte FunctionWriteSingleCoil = 0x05;
    private const byte FunctionWriteSingleRegister = 0x06;
    private const byte FunctionReadExceptionStatus = 0x07;
    private const byte FunctionGetCommEventCounter = 0x0B;
    private const byte FunctionWriteMultipleCoils = 0x0F;
    private const byte FunctionWriteMultipleRegisters = 0x10;
    private const byte FunctionReportServerId = 0x11;
    private const byte FunctionMaskWriteRegister = 0x16;
    private const byte FunctionReadWriteRegisters = 0x17;

    private const byte ExceptionIllegalFunction = 0x01;
    private const byte ExceptionIllegalDataAddress = 0x02;
    private const byte ExceptionIllegalDataValue = 0x03;

    private const int MaxReadBits = 2000;
    private const int MaxReadRegisters = 125;
    private const int MaxWriteBits = 1968;
    private const int MaxWriteRegisters = 123;

    /// <summary>Handles one request PDU.</summary>
    /// <returns>The response PDU, or <see langword="null"/> when the request must not be answered.</returns>
    public byte[]? Process(byte unitId, byte[] pdu)
    {
        if (pdu.Length == 0)
        {
            return null;
        }

        var function = pdu[0];
        _statistics.RecordRequest(function);

        var response = ProcessCore(unitId, pdu);
        if (response is { Length: >= 1 } && (response[0] & 0x80) != 0)
        {
            _statistics.RecordException(function);
            _logger?.Log(
                PlcLogLevel.Debug,
                $"Modbus slave answered function {function} with exception 0x{response[1]:X2}.");
        }

        return response;
    }

    private byte[]? ProcessCore(byte unitId, byte[] pdu)
    {
        var broadcast = unitId == 0;

        switch (pdu[0])
        {
            case FunctionReadCoils:
                return broadcast ? null : ReadBits(pdu, FunctionReadCoils, coils: true);

            case FunctionReadDiscreteInputs:
                return broadcast ? null : ReadBits(pdu, FunctionReadDiscreteInputs, coils: false);

            case FunctionReadHoldingRegisters:
                return broadcast ? null : ReadRegisters(pdu, holding: true);

            case FunctionReadInputRegisters:
                return broadcast ? null : ReadRegisters(pdu, holding: false);

            case FunctionWriteSingleCoil:
                return WriteSingleCoil(pdu, broadcast);

            case FunctionWriteSingleRegister:
                return WriteSingleRegister(pdu, broadcast);

            case FunctionReadExceptionStatus:
                return broadcast ? null : ReadExceptionStatus();

            case FunctionGetCommEventCounter:
                return broadcast ? null : GetCommEventCounter();

            case FunctionWriteMultipleCoils:
                return WriteMultipleCoils(pdu, broadcast);

            case FunctionWriteMultipleRegisters:
                return WriteMultipleRegisters(pdu, broadcast);

            case FunctionReportServerId:
                return broadcast ? null : ReportServerId();

            case FunctionMaskWriteRegister:
                return MaskWriteRegister(pdu, broadcast);

            case FunctionReadWriteRegisters:
                return broadcast ? null : ReadWriteRegisters(pdu);

            default:
                _logger?.Warning($"Modbus slave received unsupported function code 0x{pdu[0]:X2}.");
                return broadcast ? null : Exception(pdu[0], ExceptionIllegalFunction);
        }
    }

    private byte[] ReadExceptionStatus() => new[] { FunctionReadExceptionStatus, Data.ExceptionStatus };

    private byte[] GetCommEventCounter()
    {
        var count = Data.EventCount;
        return new byte[]
        {
            FunctionGetCommEventCounter,
            0x00,
            0x00,
            (byte)((count >> 8) & 0xFF),
            (byte)(count & 0xFF),
        };
    }

    private byte[] ReportServerId()
    {
        var response = new byte[2 + _identifierBytes.Length + 1];
        response[0] = FunctionReportServerId;
        response[1] = (byte)(_identifierBytes.Length + 1);
        _identifierBytes.CopyTo(response, 2);
        response[response.Length - 1] = _runIndicator ? (byte)0xFF : (byte)0x00;
        return response;
    }

    private byte[]? MaskWriteRegister(byte[] pdu, bool broadcast)
    {
        if (pdu.Length < 7)
        {
            return broadcast ? null : Exception(pdu[0], ExceptionIllegalDataValue);
        }

        var address = ReadUInt16(pdu, 1);
        var andMask = ReadUInt16(pdu, 3);
        var orMask = ReadUInt16(pdu, 5);
        var written = Data.TryMaskWriteHoldingRegister(address, andMask, orMask, out _);

        if (broadcast)
        {
            return null;
        }

        if (!written)
        {
            return Exception(pdu[0], ExceptionIllegalDataAddress);
        }

        Data.RecordEvent();
        return pdu.Copy(0, 7);
    }

    private byte[]? ReadBits(byte[] pdu, byte function, bool coils)
    {
        if (pdu.Length < 5)
        {
            return Exception(function, ExceptionIllegalDataValue);
        }

        var start = ReadUInt16(pdu, 1);
        var count = ReadUInt16(pdu, 3);
        if (count is < 1 or > MaxReadBits)
        {
            return Exception(function, ExceptionIllegalDataValue);
        }

        var values = coils ? Data.ReadCoils(start, count) : Data.ReadDiscreteInputs(start, count);
        if (values is null)
        {
            return Exception(function, ExceptionIllegalDataAddress);
        }

        var byteCount = (count + 7) / 8;
        var response = new byte[2 + byteCount];
        response[0] = function;
        response[1] = (byte)byteCount;

        for (var i = 0; i < count; i++)
        {
            if (values[i])
            {
                response[2 + (i / 8)] |= (byte)(1 << (i % 8));
            }
        }

        return response;
    }

    private byte[]? ReadRegisters(byte[] pdu, bool holding)
    {
        if (pdu.Length < 5)
        {
            return Exception(pdu[0], ExceptionIllegalDataValue);
        }

        var start = ReadUInt16(pdu, 1);
        var count = ReadUInt16(pdu, 3);
        if (count is < 1 or > MaxReadRegisters)
        {
            return Exception(pdu[0], ExceptionIllegalDataValue);
        }

        var values = holding ? Data.ReadHoldingRegisters(start, count) : Data.ReadInputRegisters(start, count);
        if (values is null)
        {
            return Exception(pdu[0], ExceptionIllegalDataAddress);
        }

        var response = new byte[2 + (count * 2)];
        response[0] = pdu[0];
        response[1] = (byte)(count * 2);

        for (var i = 0; i < count; i++)
        {
            response[2 + (i * 2)] = (byte)(values[i] >> 8);
            response[3 + (i * 2)] = (byte)values[i];
        }

        return response;
    }

    private byte[]? WriteSingleCoil(byte[] pdu, bool broadcast)
    {
        if (pdu.Length < 5)
        {
            return broadcast ? null : Exception(pdu[0], ExceptionIllegalDataValue);
        }

        var address = ReadUInt16(pdu, 1);
        var value = ReadUInt16(pdu, 3);
        if (value is not (0x0000 or 0xFF00))
        {
            return broadcast ? null : Exception(pdu[0], ExceptionIllegalDataValue);
        }

        var written = Data.TryWriteSingleCoil(address, value == 0xFF00);
        if (written)
        {
            Data.RecordEvent();
        }

        if (broadcast)
        {
            return null;
        }

        return written ? pdu.Copy(0, 5) : Exception(pdu[0], ExceptionIllegalDataAddress);
    }

    private byte[]? WriteSingleRegister(byte[] pdu, bool broadcast)
    {
        if (pdu.Length < 5)
        {
            return broadcast ? null : Exception(pdu[0], ExceptionIllegalDataValue);
        }

        var address = ReadUInt16(pdu, 1);
        var value = ReadUInt16(pdu, 3);
        var written = Data.TryWriteSingleRegister(address, value);
        if (written)
        {
            Data.RecordEvent();
        }

        if (broadcast)
        {
            return null;
        }

        return written ? pdu.Copy(0, 5) : Exception(pdu[0], ExceptionIllegalDataAddress);
    }

    private byte[]? WriteMultipleCoils(byte[] pdu, bool broadcast)
    {
        if (pdu.Length < 6)
        {
            return broadcast ? null : Exception(pdu[0], ExceptionIllegalDataValue);
        }

        var start = ReadUInt16(pdu, 1);
        var count = ReadUInt16(pdu, 3);
        var byteCount = pdu[5];

        if (count is < 1 or > MaxWriteBits || byteCount != (count + 7) / 8 || pdu.Length < 6 + byteCount)
        {
            return broadcast ? null : Exception(pdu[0], ExceptionIllegalDataValue);
        }

        var values = new bool[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = (pdu[6 + (i / 8)] & (1 << (i % 8))) != 0;
        }

        var written = Data.TryWriteCoils(start, values);
        if (written)
        {
            Data.RecordEvent();
        }

        if (broadcast)
        {
            return null;
        }

        return written ? EchoWriteResponse(pdu, start, count) : Exception(pdu[0], ExceptionIllegalDataAddress);
    }

    private byte[]? WriteMultipleRegisters(byte[] pdu, bool broadcast)
    {
        if (pdu.Length < 6)
        {
            return broadcast ? null : Exception(pdu[0], ExceptionIllegalDataValue);
        }

        var start = ReadUInt16(pdu, 1);
        var count = ReadUInt16(pdu, 3);
        var byteCount = pdu[5];

        if (count is < 1 or > MaxWriteRegisters || byteCount != count * 2 || pdu.Length < 6 + byteCount)
        {
            return broadcast ? null : Exception(pdu[0], ExceptionIllegalDataValue);
        }

        var values = ReadRegistersFrom(pdu, 6 + byteCount, count);
        var written = Data.TryWriteHoldingRegisters(start, values);
        if (written)
        {
            Data.RecordEvent();
        }

        if (broadcast)
        {
            return null;
        }

        return written ? EchoWriteResponse(pdu, start, count) : Exception(pdu[0], ExceptionIllegalDataAddress);
    }

    private byte[]? ReadWriteRegisters(byte[] pdu)
    {
        if (pdu.Length < 10)
        {
            return Exception(pdu[0], ExceptionIllegalDataValue);
        }

        var readStart = ReadUInt16(pdu, 1);
        var readCount = ReadUInt16(pdu, 3);
        var writeStart = ReadUInt16(pdu, 5);
        var writeCount = ReadUInt16(pdu, 7);
        var byteCount = pdu[9];

        if (readCount is < 1 or > MaxReadRegisters
            || writeCount is < 1 or > MaxWriteRegisters
            || byteCount != writeCount * 2
            || pdu.Length < 10 + byteCount)
        {
            return Exception(pdu[0], ExceptionIllegalDataValue);
        }

        var writeValues = ReadRegistersFrom(pdu, 10 + byteCount, writeCount);
        if (!Data.TryWriteHoldingRegisters(writeStart, writeValues))
        {
            return Exception(pdu[0], ExceptionIllegalDataAddress);
        }

        Data.RecordEvent();

        var readValues = Data.ReadHoldingRegisters(readStart, readCount);
        if (readValues is null)
        {
            return Exception(pdu[0], ExceptionIllegalDataAddress);
        }

        var response = new byte[2 + (readCount * 2)];
        response[0] = FunctionReadWriteRegisters;
        response[1] = (byte)(readCount * 2);

        for (var i = 0; i < readCount; i++)
        {
            response[2 + (i * 2)] = (byte)(readValues[i] >> 8);
            response[3 + (i * 2)] = (byte)readValues[i];
        }

        return response;
    }

    private static byte[] EchoWriteResponse(byte[] pdu, ushort start, ushort count)
    {
        var response = new byte[5];
        response[0] = pdu[0];
        response[1] = (byte)(start >> 8);
        response[2] = (byte)start;
        response[3] = (byte)(count >> 8);
        response[4] = (byte)count;
        return response;
    }

    private static ushort[] ReadRegistersFrom(byte[] pdu, int end, int count)
    {
        var values = new ushort[count];
        var offset = end - (count * 2);
        for (var i = 0; i < count; i++)
        {
            values[i] = (ushort)((pdu[offset + (i * 2)] << 8) | pdu[offset + (i * 2) + 1]);
        }

        return values;
    }

    private static byte[] Exception(byte function, byte code) =>
        new[] { (byte)(function | 0x80), code };

    private static ushort ReadUInt16(byte[] buffer, int offset) => (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
}

internal static class ByteArrayExtensions
{
    public static byte[] Copy(this byte[] source, int start, int? length = null)
    {
        var count = length ?? source.Length - start;
        var copy = new byte[count];
        Array.Copy(source, start, copy, 0, count);
        return copy;
    }
}
