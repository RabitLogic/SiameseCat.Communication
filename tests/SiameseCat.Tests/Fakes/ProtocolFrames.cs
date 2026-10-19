using SiameseCat.Protocols.Modbus;

namespace SiameseCat.Tests.Fakes;

internal static class ProtocolFrames
{
    public static byte[] Rtu(params byte[] body)
    {
        var frame = new byte[body.Length + 2];
        body.CopyTo(frame, 0);
        ModbusCrc16.Write(frame.AsSpan(0, body.Length), frame.AsSpan(body.Length));
        return frame;
    }

    public static byte[] Mbap(ushort transactionId, byte unitId, params byte[] pdu)
    {
        var frame = new byte[7 + pdu.Length];
        frame[0] = (byte)(transactionId >> 8);
        frame[1] = (byte)transactionId;
        frame[4] = 0;
        frame[5] = (byte)(pdu.Length + 1);
        frame[6] = unitId;
        pdu.CopyTo(frame, 7);
        return frame;
    }

    public static byte[] CrConfirm() =>
        Tpkt(0x11, 0xD0, 0x00, 0x01, 0x00, 0x00, 0x00);

    public static byte[] SetupAck(ushort pduReference, int pduSize = 960)
    {
        var parameter = new byte[]
        {
            0xF0, 0x00, 0x00, 0x00, 0x00, 0x01,
            (byte)(pduSize >> 8), (byte)pduSize,
        };

        return S7Response(pduReference, parameter, Array.Empty<byte>());
    }

    public static byte[] S7Response(ushort pduReference, byte[] parameter, byte[] data)
    {
        var payload = new byte[3 + 12 + parameter.Length + data.Length];
        payload[0] = 0x02;
        payload[1] = 0xF0;
        payload[2] = 0x80;
        payload[3] = 0x32;
        payload[4] = 0x03;
        payload[7] = (byte)(pduReference >> 8);
        payload[8] = (byte)pduReference;
        payload[9] = (byte)(parameter.Length >> 8);
        payload[10] = (byte)parameter.Length;
        payload[11] = (byte)(data.Length >> 8);
        payload[12] = (byte)data.Length;
        parameter.CopyTo(payload, 15);
        data.CopyTo(payload, 15 + parameter.Length);
        return Tpkt(payload);
    }

    public static byte[] DataItem(byte[] data)
    {
        var item = new byte[4 + data.Length];
        item[0] = 0xFF;
        item[1] = 0x04;
        var bitLength = data.Length * 8;
        item[2] = (byte)(bitLength >> 8);
        item[3] = (byte)bitLength;
        data.CopyTo(item, 4);
        return item;
    }

    public static byte[] Tpkt(params byte[] payload)
    {
        var frame = new byte[4 + payload.Length];
        frame[0] = 0x03;
        frame[2] = (byte)(frame.Length >> 8);
        frame[3] = (byte)frame.Length;
        payload.CopyTo(frame, 4);
        return frame;
    }
}
