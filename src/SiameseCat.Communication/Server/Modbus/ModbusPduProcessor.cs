using SiameseCat.Communication;

namespace SiameseCat.Communication.Server.Modbus;

/// <summary>
/// Shared Modbus PDU handling used by the TCP and RTU slaves.
/// Responses are written into a caller-provided buffer so a serving loop reuses its memory.
/// </summary>
internal sealed class ModbusPduProcessor
{
    private readonly ModbusDataStore _data;
    private readonly ModbusServerStatistics _statistics;
    private readonly byte[] _identifierBytes;
    private readonly bool _runIndicator;
    private readonly IPlcLogger? _logger;
    private readonly KeyValuePair<byte, byte[]>[] _deviceObjects;
    private readonly byte _conformityLevel;

    public ModbusPduProcessor(
        ModbusDataStore data,
        ModbusServerStatistics statistics,
        string serverIdentifier,
        bool runIndicator,
        IPlcLogger? logger,
        IDictionary<byte, string>? deviceIdentification = null)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _identifierBytes = System.Text.Encoding.ASCII.GetBytes(serverIdentifier);
        _runIndicator = runIndicator;
        _logger = logger;

        var objects = deviceIdentification ?? ModbusServerIdentifier.CreateDefaultDeviceIdentification();
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
                PlcLogLevel.Debug,
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

    private int ReadFileRecords(ReadOnlySpan<byte> pdu, Span<byte> destination)
    {
        if (pdu.Length < 3)
        {
            return WriteException(destination, FunctionReadFileRecord, ExceptionIllegalDataValue);
        }

        var byteCount = pdu[1];
        if (byteCount == 0 || byteCount % 7 != 0 || pdu.Length != 2 + byteCount)
        {
            return WriteException(destination, FunctionReadFileRecord, ExceptionIllegalDataValue);
        }

        var count = byteCount / 7;
        var responseLength = 2;
        for (var i = 0; i < count; i++)
        {
            var at = 2 + (i * 7);
            var length = ReadUInt16(pdu, at + 5);
            if (pdu[at] != FileRecordReferenceType || length is < 1 or > MaxFileRecordRegisters)
            {
                return WriteException(destination, FunctionReadFileRecord, ExceptionIllegalDataValue);
            }

            responseLength += 2 + (length * 2);
        }

        if (responseLength > MaxPduLength)
        {
            return WriteException(destination, FunctionReadFileRecord, ExceptionIllegalDataValue);
        }

        var position = 2;
        for (var i = 0; i < count; i++)
        {
            var at = 2 + (i * 7);
            var file = ReadUInt16(pdu, at + 1);
            var record = ReadUInt16(pdu, at + 3);
            var length = ReadUInt16(pdu, at + 5);
            if (!_data.TryCopyFileRecord(file, record, length, destination.Slice(position + 2, length * 2)))
            {
                return WriteException(destination, FunctionReadFileRecord, ExceptionIllegalDataAddress);
            }

            destination[position] = (byte)(1 + (length * 2));
            destination[position + 1] = FileRecordReferenceType;
            position += 2 + (length * 2);
        }

        destination[0] = FunctionReadFileRecord;
        destination[1] = (byte)(position - 2);
        return position;
    }

    private int WriteFileRecords(ReadOnlySpan<byte> pdu, Span<byte> destination, bool broadcast)
    {
        if (pdu.Length < 3)
        {
            return broadcast ? 0 : WriteException(destination, FunctionWriteFileRecord, ExceptionIllegalDataValue);
        }

        var byteCount = pdu[1];
        if (byteCount == 0 || pdu.Length != 2 + byteCount)
        {
            return broadcast ? 0 : WriteException(destination, FunctionWriteFileRecord, ExceptionIllegalDataValue);
        }

        var end = 2 + byteCount;
        var position = 2;
        while (position < end)
        {
            if (position + 7 > end || pdu[position] != FileRecordReferenceType)
            {
                return broadcast ? 0 : WriteException(destination, FunctionWriteFileRecord, ExceptionIllegalDataValue);
            }

            var length = ReadUInt16(pdu, position + 5);
            if (length is < 1 or > MaxFileRecordRegisters || position + 7 + (length * 2) > end)
            {
                return broadcast ? 0 : WriteException(destination, FunctionWriteFileRecord, ExceptionIllegalDataValue);
            }

            if (!_data.CanWriteFileRecord(
                    ReadUInt16(pdu, position + 1),
                    ReadUInt16(pdu, position + 3),
                    length))
            {
                return broadcast ? 0 : WriteException(destination, FunctionWriteFileRecord, ExceptionIllegalDataAddress);
            }

            position += 7 + (length * 2);
        }

        Span<ushort> values = stackalloc ushort[MaxFileRecordRegisters];
        position = 2;
        while (position < end)
        {
            var file = ReadUInt16(pdu, position + 1);
            var record = ReadUInt16(pdu, position + 3);
            var length = ReadUInt16(pdu, position + 5);
            for (var i = 0; i < length; i++)
            {
                values[i] = ReadUInt16(pdu, position + 7 + (i * 2));
            }

            _data.TryWriteFileRecord(file, record, values.Slice(0, length));
            position += 7 + (length * 2);
        }

        _data.RecordEvent();

        if (broadcast)
        {
            return 0;
        }

        pdu.CopyTo(destination);
        return pdu.Length;
    }

    private int ReadDeviceIdentification(ReadOnlySpan<byte> pdu, Span<byte> destination)
    {
        if (pdu.Length != 4)
        {
            return WriteException(destination, FunctionReadDeviceIdentification, ExceptionIllegalDataValue);
        }

        if (pdu[1] != MeiTypeDeviceIdentification)
        {
            return WriteException(destination, FunctionReadDeviceIdentification, ExceptionIllegalFunction);
        }

        var code = pdu[2];
        var objectId = pdu[3];
        if (code is < 1 or > 4)
        {
            return WriteException(destination, FunctionReadDeviceIdentification, ExceptionIllegalDataValue);
        }

        destination[0] = FunctionReadDeviceIdentification;
        destination[1] = MeiTypeDeviceIdentification;
        destination[2] = code;

        if (code == 4)
        {
            foreach (var pair in _deviceObjects)
            {
                if (pair.Key != objectId)
                {
                    continue;
                }

                destination[3] = (byte)(_conformityLevel | 0x80);
                destination[4] = 0x00;
                destination[5] = 0x00;
                destination[6] = 0x01;
                destination[7] = pair.Key;
                destination[8] = (byte)pair.Value.Length;
                pair.Value.AsSpan().CopyTo(destination.Slice(9));
                return 9 + pair.Value.Length;
            }

            return WriteException(destination, FunctionReadDeviceIdentification, ExceptionIllegalDataAddress);
        }

        var maxId = code switch
        {
            1 => (byte)0x02,
            2 => (byte)0x06,
            _ => (byte)0xFF,
        };

        if (objectId > maxId)
        {
            return WriteException(destination, FunctionReadDeviceIdentification, ExceptionIllegalDataAddress);
        }

        destination[3] = _conformityLevel;
        var position = 7;
        var count = 0;
        var more = (byte)0x00;
        var next = (byte)0x00;

        foreach (var pair in _deviceObjects)
        {
            if (pair.Key < objectId)
            {
                continue;
            }

            if (pair.Key > maxId)
            {
                break;
            }

            if (position + 2 + pair.Value.Length > MaxPduLength)
            {
                more = 0xFF;
                next = pair.Key;
                break;
            }

            destination[position] = pair.Key;
            destination[position + 1] = (byte)pair.Value.Length;
            pair.Value.AsSpan().CopyTo(destination.Slice(position + 2));
            position += 2 + pair.Value.Length;
            count++;
        }

        if (count == 0)
        {
            return WriteException(destination, FunctionReadDeviceIdentification, ExceptionIllegalDataAddress);
        }

        destination[4] = more;
        destination[5] = next;
        destination[6] = (byte)count;
        return position;
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
