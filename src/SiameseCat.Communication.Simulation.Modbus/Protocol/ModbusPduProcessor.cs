using SiameseCat.Communication;

namespace SiameseCat.Communication.Simulation.Modbus;

/// <summary>
/// Shared Modbus PDU handling used by the TCP and RTU slaves.
/// Responses are written into a caller-provided buffer so a serving loop reuses its memory.
/// </summary>
internal sealed partial class ModbusPduProcessor
{
    private readonly ModbusDataStore _data;
    private readonly ModbusSimulatorStatistics _statistics;
    private readonly byte[] _identifierBytes;
    private readonly bool _runIndicator;
    private readonly ICommunicationLogger? _logger;
    private readonly KeyValuePair<byte, byte[]>[] _deviceObjects;
    private readonly byte _conformityLevel;

    public ModbusPduProcessor(
        ModbusDataStore data,
        ModbusSimulatorStatistics statistics,
        string serverIdentifier,
        bool runIndicator,
        ICommunicationLogger? logger,
        IDictionary<byte, string>? deviceIdentification = null)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _identifierBytes = System.Text.Encoding.ASCII.GetBytes(serverIdentifier);
        _runIndicator = runIndicator;
        _logger = logger;

        var objects = deviceIdentification ?? ModbusSimulatorIdentifier.CreateDefaultDeviceIdentification();
        var list = new List<KeyValuePair<byte, byte[]>>(objects.Count);
        var level = (byte)1;
        foreach (var pair in objects)
        {
            list.Add(new KeyValuePair<byte, byte[]>(pair.Key, System.Text.Encoding.ASCII.GetBytes(pair.Value)));
            if (pair.Key >= 0x80)
            {
                level = 3;
            }
            else if (pair.Key >= 0x03 && level < 2)
            {
                level = 2;
            }
        }

        list.Sort((left, right) => left.Key.CompareTo(right.Key));
        _deviceObjects = list.ToArray();
        _conformityLevel = level;
    }

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
    private const byte FunctionReadFileRecord = 0x14;
    private const byte FunctionWriteFileRecord = 0x15;
    private const byte FunctionMaskWriteRegister = 0x16;
    private const byte FunctionReadWriteRegisters = 0x17;
    private const byte FunctionReadDeviceIdentification = 0x2B;

    private const byte MeiTypeDeviceIdentification = 0x0E;
    private const byte FileRecordReferenceType = 0x06;

    private const byte ExceptionIllegalFunction = 0x01;
    private const byte ExceptionIllegalDataAddress = 0x02;
    private const byte ExceptionIllegalDataValue = 0x03;

    private const int MaxReadBits = 2000;
    private const int MaxReadRegisters = 125;
    private const int MaxWriteBits = 1968;
    private const int MaxWriteRegisters = 123;
    private const int MaxFileRecordRegisters = 125;
    private const int MaxPduLength = 253;

    /// <summary>Handles one request PDU and writes the response PDU into <paramref name="destination"/>.</summary>
    /// <param name="unitId">Unit identifier; 0 is a broadcast.</param>
    /// <param name="pdu">Request PDU.</param>
    /// <param name="destination">Buffer that receives the response PDU (at least 254 bytes).</param>
    /// <returns>Number of bytes written to <paramref name="destination"/>, or 0 when nothing must be sent.</returns>
    public int Process(byte unitId, ReadOnlySpan<byte> pdu, Span<byte> destination)
    {
        if (pdu.Length == 0)
        {
            return 0;
        }

        var function = pdu[0];
        _statistics.RecordRequest(function);

        var written = ProcessCore(unitId, pdu, destination);
        if (written >= 1 && (destination[0] & 0x80) != 0)
        {
            _statistics.RecordException(function);
            _logger?.Log(
                CommunicationLogLevel.Debug,
                $"Modbus slave answered function {function} with exception 0x{destination[1]:X2}.");
        }

        return written;
    }

    private int ProcessCore(byte unitId, ReadOnlySpan<byte> pdu, Span<byte> destination)
    {
        var broadcast = unitId == 0;

        switch (pdu[0])
        {
            case FunctionReadCoils:
                return broadcast ? 0 : ReadBits(pdu, destination, FunctionReadCoils, coils: true);

            case FunctionReadDiscreteInputs:
                return broadcast ? 0 : ReadBits(pdu, destination, FunctionReadDiscreteInputs, coils: false);

            case FunctionReadHoldingRegisters:
                return broadcast ? 0 : ReadRegisters(pdu, destination, holding: true);

            case FunctionReadInputRegisters:
                return broadcast ? 0 : ReadRegisters(pdu, destination, holding: false);

            case FunctionWriteSingleCoil:
                return WriteSingleCoil(pdu, destination, broadcast);

            case FunctionWriteSingleRegister:
                return WriteSingleRegister(pdu, destination, broadcast);

            case FunctionReadExceptionStatus:
                if (broadcast)
                {
                    return 0;
                }

                destination[0] = FunctionReadExceptionStatus;
                destination[1] = _data.ExceptionStatus;
                return 2;

            case FunctionGetCommEventCounter:
                if (broadcast)
                {
                    return 0;
                }

                var count = _data.EventCount;
                destination[0] = FunctionGetCommEventCounter;
                destination[1] = 0x00;
                destination[2] = 0x00;
                destination[3] = (byte)((count >> 8) & 0xFF);
                destination[4] = (byte)(count & 0xFF);
                return 5;

            case FunctionWriteMultipleCoils:
                return WriteMultipleCoils(pdu, destination, broadcast);

            case FunctionWriteMultipleRegisters:
                return WriteMultipleRegisters(pdu, destination, broadcast);

            case FunctionReportServerId:
                if (broadcast)
                {
                    return 0;
                }

                destination[0] = FunctionReportServerId;
                destination[1] = (byte)(_identifierBytes.Length + 1);
                _identifierBytes.CopyTo(destination.Slice(2));
                destination[2 + _identifierBytes.Length] = _runIndicator ? (byte)0xFF : (byte)0x00;
                return 3 + _identifierBytes.Length;

            case FunctionReadFileRecord:
                return broadcast ? 0 : ReadFileRecords(pdu, destination);

            case FunctionWriteFileRecord:
                return WriteFileRecords(pdu, destination, broadcast);

            case FunctionMaskWriteRegister:
                return MaskWriteRegister(pdu, destination, broadcast);

            case FunctionReadWriteRegisters:
                return broadcast ? 0 : ReadWriteRegisters(pdu, destination);

            case FunctionReadDeviceIdentification:
                return broadcast ? 0 : ReadDeviceIdentification(pdu, destination);

            default:
                _logger?.Warning($"Modbus slave received unsupported function code 0x{pdu[0]:X2}.");
                return broadcast ? 0 : WriteException(destination, pdu[0], ExceptionIllegalFunction);
        }
    }

    private int ReadBits(ReadOnlySpan<byte> pdu, Span<byte> destination, byte function, bool coils)
    {
        if (pdu.Length < 5)
        {
            return WriteException(destination, function, ExceptionIllegalDataValue);
        }

        var start = ReadUInt16(pdu, 1);
        var count = ReadUInt16(pdu, 3);
        if (count is < 1 or > MaxReadBits)
        {
            return WriteException(destination, function, ExceptionIllegalDataValue);
        }

        var byteCount = (count + 7) / 8;
        destination[0] = function;
        destination[1] = (byte)byteCount;

        var copied = coils
            ? _data.TryCopyCoils(start, count, destination.Slice(2, byteCount))
            : _data.TryCopyDiscreteInputs(start, count, destination.Slice(2, byteCount));

        return copied ? 2 + byteCount : WriteException(destination, function, ExceptionIllegalDataAddress);
    }

    private int ReadRegisters(ReadOnlySpan<byte> pdu, Span<byte> destination, bool holding)
    {
        if (pdu.Length < 5)
        {
            return WriteException(destination, pdu[0], ExceptionIllegalDataValue);
        }

        var function = pdu[0];
        var start = ReadUInt16(pdu, 1);
        var count = ReadUInt16(pdu, 3);
        if (count is < 1 or > MaxReadRegisters)
        {
            return WriteException(destination, function, ExceptionIllegalDataValue);
        }

        destination[0] = function;
        destination[1] = (byte)(count * 2);

        var copied = holding
            ? _data.TryCopyHoldingRegisters(start, count, destination.Slice(2, count * 2))
            : _data.TryCopyInputRegisters(start, count, destination.Slice(2, count * 2));

        return copied ? 2 + (count * 2) : WriteException(destination, function, ExceptionIllegalDataAddress);
    }

    private int WriteSingleCoil(ReadOnlySpan<byte> pdu, Span<byte> destination, bool broadcast)
    {
        if (pdu.Length < 5)
        {
            return broadcast ? 0 : WriteException(destination, pdu[0], ExceptionIllegalDataValue);
        }

        var address = ReadUInt16(pdu, 1);
        var value = ReadUInt16(pdu, 3);
        if (value is not (0x0000 or 0xFF00))
        {
            return broadcast ? 0 : WriteException(destination, pdu[0], ExceptionIllegalDataValue);
        }

        var written = _data.TryWriteSingleCoil(address, value == 0xFF00);
        if (written)
        {
            _data.RecordEvent();
        }

        if (broadcast)
        {
            return 0;
        }

        return written ? Echo(pdu, destination, 5) : WriteException(destination, pdu[0], ExceptionIllegalDataAddress);
    }

    private int WriteSingleRegister(ReadOnlySpan<byte> pdu, Span<byte> destination, bool broadcast)
    {
        if (pdu.Length < 5)
        {
            return broadcast ? 0 : WriteException(destination, pdu[0], ExceptionIllegalDataValue);
        }

        var address = ReadUInt16(pdu, 1);
        var value = ReadUInt16(pdu, 3);
        var written = _data.TryWriteSingleRegister(address, value);
        if (written)
        {
            _data.RecordEvent();
        }

        if (broadcast)
        {
            return 0;
        }

        return written ? Echo(pdu, destination, 5) : WriteException(destination, pdu[0], ExceptionIllegalDataAddress);
    }

    private int WriteMultipleCoils(ReadOnlySpan<byte> pdu, Span<byte> destination, bool broadcast)
    {
        if (pdu.Length < 6)
        {
            return broadcast ? 0 : WriteException(destination, pdu[0], ExceptionIllegalDataValue);
        }

        var start = ReadUInt16(pdu, 1);
        var count = ReadUInt16(pdu, 3);
        var byteCount = pdu[5];

        if (count is < 1 or > MaxWriteBits || byteCount != (count + 7) / 8 || pdu.Length < 6 + byteCount)
        {
            return broadcast ? 0 : WriteException(destination, pdu[0], ExceptionIllegalDataValue);
        }

        var written = _data.TryWriteCoils(start, pdu.Slice(6, byteCount), count);
        if (written)
        {
            _data.RecordEvent();
        }

        if (broadcast)
        {
            return 0;
        }

        return written
            ? WriteStartCount(destination, pdu[0], start, count)
            : WriteException(destination, pdu[0], ExceptionIllegalDataAddress);
    }

    private int WriteMultipleRegisters(ReadOnlySpan<byte> pdu, Span<byte> destination, bool broadcast)
    {
        if (pdu.Length < 6)
        {
            return broadcast ? 0 : WriteException(destination, pdu[0], ExceptionIllegalDataValue);
        }

        var start = ReadUInt16(pdu, 1);
        var count = ReadUInt16(pdu, 3);
        var byteCount = pdu[5];

        if (count is < 1 or > MaxWriteRegisters || byteCount != count * 2 || pdu.Length < 6 + byteCount)
        {
            return broadcast ? 0 : WriteException(destination, pdu[0], ExceptionIllegalDataValue);
        }

        var written = WriteRegistersFrom(pdu.Slice(6, byteCount), start, count);
        if (written)
        {
            _data.RecordEvent();
        }

        if (broadcast)
        {
            return 0;
        }

        return written
            ? WriteStartCount(destination, pdu[0], start, count)
            : WriteException(destination, pdu[0], ExceptionIllegalDataAddress);
    }

    private int ReadWriteRegisters(ReadOnlySpan<byte> pdu, Span<byte> destination)
    {
        if (pdu.Length < 10)
        {
            return WriteException(destination, pdu[0], ExceptionIllegalDataValue);
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
            return WriteException(destination, pdu[0], ExceptionIllegalDataValue);
        }

        if (!WriteRegistersFrom(pdu.Slice(10, byteCount), writeStart, writeCount))
        {
            return WriteException(destination, pdu[0], ExceptionIllegalDataAddress);
        }

        _data.RecordEvent();

        destination[0] = FunctionReadWriteRegisters;
        destination[1] = (byte)(readCount * 2);
        return _data.TryCopyHoldingRegisters(readStart, readCount, destination.Slice(2, readCount * 2))
            ? 2 + (readCount * 2)
            : WriteException(destination, pdu[0], ExceptionIllegalDataAddress);
    }

    private int MaskWriteRegister(ReadOnlySpan<byte> pdu, Span<byte> destination, bool broadcast)
    {
        if (pdu.Length < 7)
        {
            return broadcast ? 0 : WriteException(destination, pdu[0], ExceptionIllegalDataValue);
        }

        var address = ReadUInt16(pdu, 1);
        var andMask = ReadUInt16(pdu, 3);
        var orMask = ReadUInt16(pdu, 5);
        var written = _data.TryMaskWriteHoldingRegister(address, andMask, orMask, out _);

        if (broadcast)
        {
            return 0;
        }

        if (!written)
        {
            return WriteException(destination, pdu[0], ExceptionIllegalDataAddress);
        }

        _data.RecordEvent();
        return Echo(pdu, destination, 7);
    }

    private bool WriteRegistersFrom(ReadOnlySpan<byte> payload, int start, int count)
    {
        Span<ushort> values = stackalloc ushort[MaxWriteRegisters];
        for (var i = 0; i < count; i++)
        {
            values[i] = (ushort)((payload[i * 2] << 8) | payload[(i * 2) + 1]);
        }

        return _data.TryWriteHoldingRegisters(start, values.Slice(0, count));
    }

    private static int Echo(ReadOnlySpan<byte> pdu, Span<byte> destination, int length)
    {
        pdu.Slice(0, length).CopyTo(destination);
        return length;
    }

    private static int WriteStartCount(Span<byte> destination, byte function, ushort start, ushort count)
    {
        destination[0] = function;
        destination[1] = (byte)(start >> 8);
        destination[2] = (byte)start;
        destination[3] = (byte)(count >> 8);
        destination[4] = (byte)count;
        return 5;
    }

    private static int WriteException(Span<byte> destination, byte function, byte code)
    {
        destination[0] = (byte)(function | 0x80);
        destination[1] = code;
        return 2;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> buffer, int offset) =>
        (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
}
