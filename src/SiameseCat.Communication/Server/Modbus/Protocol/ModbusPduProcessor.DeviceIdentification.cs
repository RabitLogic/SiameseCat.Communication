namespace SiameseCat.Communication.Server.Modbus;

internal sealed partial class ModbusPduProcessor
{

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

}
