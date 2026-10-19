namespace SiameseCat.Communication.Server.Modbus;

internal sealed partial class ModbusPduProcessor
{

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

}
