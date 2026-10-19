using SiameseCat.Core;
using SiameseCat.Protocols.S7;

namespace SiameseCat.Server;

/// <summary>S7 PDU handling used by <see cref="S7Server"/>.</summary>
internal sealed class S7PduProcessor
{
    private const byte ProtocolId = 0x32;
    private const byte RosctrJob = 0x01;
    private const byte RosctrAckData = 0x03;
    private const byte FunctionSetupCommunication = 0xF0;
    private const byte FunctionReadVar = 0x04;
    private const byte FunctionWriteVar = 0x05;
    private const int JobHeaderLength = 10;
    private const int AckDataHeaderLength = 12;
    private const int VariableSpecificationLength = 12;

    private const byte TransportBit = 0x01;
    private const byte DataTransportBit = 0x03;
    private const byte DataTransportByte = 0x04;

    private const byte ReturnSuccess = 0xFF;
    private const byte ReturnAccessDenied = 0x03;
    private const byte ReturnDataTypeNotSupported = 0x04;
    private const byte ReturnAddressOutOfRange = 0x05;
    private const byte ReturnDataError = 0x06;
    private const byte ReturnObjectNotExist = 0x0A;

    private readonly S7DataStore _data;
    private readonly S7ServerStatistics _statistics;
    private readonly int _pduSize;
    private readonly IPlcLogger? _logger;

    public S7PduProcessor(S7DataStore data, S7ServerStatistics statistics, int pduSize, IPlcLogger? logger)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _pduSize = pduSize;
        _logger = logger;
    }

    /// <summary>Handles one S7 PDU (the body after TPKT and COTP).</summary>
    /// <returns>The response PDU, or <see langword="null"/> when the frame is malformed or unsupported.</returns>
    public byte[]? Process(byte[] pdu)
    {
        if (pdu.Length < JobHeaderLength || pdu[0] != ProtocolId || pdu[1] != RosctrJob)
        {
            _statistics.RecordDiscardedFrame();
            _logger?.Warning("S7 slave dropped a frame with an unsupported COTP/S7 header.");
            return null;
        }

        var pduReference = ReadUInt16(pdu, 4);
        var parameterLength = ReadUInt16(pdu, 6);
        var dataLength = ReadUInt16(pdu, 8);

        if (pdu.Length < JobHeaderLength + parameterLength + dataLength)
        {
            _statistics.RecordDiscardedFrame();
            _logger?.Warning("S7 slave dropped a frame that is shorter than its declared lengths.");
            return null;
        }

        var parameter = new byte[parameterLength];
        Array.Copy(pdu, JobHeaderLength, parameter, 0, parameterLength);
        var data = new byte[dataLength];
        Array.Copy(pdu, JobHeaderLength + parameterLength, data, 0, dataLength);

        if (parameter.Length == 0)
        {
            return BuildAck(pduReference, Array.Empty<byte>(), Array.Empty<byte>(), 0x81, 0x04);
        }

        _statistics.RecordRequest();

        switch (parameter[0])
        {
            case FunctionSetupCommunication:
                return SetupCommunication(pduReference);

            case FunctionReadVar:
                return ReadVariables(pduReference, parameter);

            case FunctionWriteVar:
                return WriteVariables(pduReference, parameter, data);

            default:
                _logger?.Warning($"S7 slave received unsupported function 0x{parameter[0]:X2}.");
                return BuildAck(pduReference, Array.Empty<byte>(), Array.Empty<byte>(), 0x81, 0x04);
        }
    }

    private byte[] SetupCommunication(ushort pduReference)
    {
        _statistics.RecordSetup();

        var parameter = new byte[]
        {
            FunctionSetupCommunication,
            0x00,
            0x00,
            0x01,
            0x00,
            0x01,
            (byte)(_pduSize >> 8),
            (byte)_pduSize,
        };

        return BuildAck(pduReference, parameter, Array.Empty<byte>());
    }

    private byte[] ReadVariables(ushort pduReference, byte[] parameter)
    {
        var count = parameter[1];
        if (count == 0 || parameter.Length < 2 + (count * VariableSpecificationLength))
        {
            return BuildAck(pduReference, Array.Empty<byte>(), Array.Empty<byte>(), 0x81, 0x04);
        }

        var responseParameter = new byte[] { FunctionReadVar, count };
        using var data = new MemoryStream();

        for (var i = 0; i < count; i++)
        {
            if (!AppendReadItem(data, parameter.AsSpan(2 + (i * VariableSpecificationLength), VariableSpecificationLength)))
            {
                _statistics.RecordItemError();
            }
        }

        _statistics.RecordRead(count);
        return BuildAck(pduReference, responseParameter, data.ToArray());
    }

    private bool AppendReadItem(MemoryStream data, ReadOnlySpan<byte> specification)
    {
        if (!TryReadSpecification(specification, out var transport, out var elements, out var dbNumber, out var area, out var address))
        {
            WriteItemError(data, ReturnDataError);
            return false;
        }

        if (transport == TransportBit)
        {
            if (elements != 1)
            {
                WriteItemError(data, ReturnDataError);
                return false;
            }

            if (!_data.TryReadBit(area, dbNumber, address, out var value))
            {
                WriteItemError(data, ResolveMissingCode(area, dbNumber));
                return false;
            }

            data.WriteByte(ReturnSuccess);
            data.WriteByte(DataTransportBit);
            data.WriteByte(0x00);
            data.WriteByte(0x01);
            data.WriteByte(value ? (byte)0x01 : (byte)0x00);
            // The client aligns items on even offsets, so even a one-byte bit item carries padding.
            data.WriteByte(0x00);
            return true;
        }

        var byteLength = GetByteLength(transport, elements);
        if (byteLength <= 0)
        {
            WriteItemError(data, ReturnDataTypeNotSupported);
            return false;
        }

        // The specification always carries a bit address; byte access divides it by eight.
        var bytes = _data.ReadBytes(area, dbNumber, address / 8, byteLength);
        if (bytes is null)
        {
            WriteItemError(data, ResolveMissingCode(area, dbNumber));
            return false;
        }

        var bitLength = byteLength * 8;
        data.WriteByte(ReturnSuccess);
        data.WriteByte(DataTransportByte);
        data.WriteByte((byte)(bitLength >> 8));
        data.WriteByte((byte)bitLength);
        data.Write(bytes, 0, bytes.Length);

        if ((bytes.Length & 1) != 0)
        {
            data.WriteByte(0x00);
        }

        return true;
    }

    private byte[] WriteVariables(ushort pduReference, byte[] parameter, byte[] data)
    {
        var count = parameter[1];
        if (count == 0 || parameter.Length < 2 + (count * VariableSpecificationLength))
        {
            return BuildAck(pduReference, Array.Empty<byte>(), Array.Empty<byte>(), 0x81, 0x04);
        }

        var responseParameter = new byte[] { FunctionWriteVar, count };
        var codes = new byte[count];
        var offset = 0;

        for (var i = 0; i < count; i++)
        {
            var specification = parameter.AsSpan(2 + (i * VariableSpecificationLength), VariableSpecificationLength);
            if (!TryReadSpecification(specification, out var transport, out var elements, out var dbNumber, out var area, out var address))
            {
                codes[i] = ReturnDataError;
                continue;
            }

            if (data.Length < offset + 4)
            {
                codes[i] = ReturnDataError;
                offset = data.Length;
                continue;
            }

            var bitLength = ReadUInt16(data, offset + 2);
            var byteLength = (bitLength + 7) / 8;
            if (data.Length < offset + 4 + byteLength)
            {
                codes[i] = ReturnDataError;
                offset = data.Length;
                continue;
            }

            var payload = data.AsSpan(offset + 4, byteLength);
            offset += 4 + byteLength + (byteLength & 1);

            codes[i] = transport == TransportBit
                ? WriteBit(area, dbNumber, address, payload, byteLength)
                : WriteBytes(area, dbNumber, address / 8, transport, elements, payload, byteLength);
        }

        _statistics.RecordWrite(count);
        foreach (var code in codes)
        {
            if (code != ReturnSuccess)
            {
                _statistics.RecordItemError();
            }
        }

        return BuildAck(pduReference, responseParameter, codes);
    }

    private byte WriteBit(S7Area area, int dbNumber, int address, ReadOnlySpan<byte> payload, int byteLength)
    {
        if (byteLength != 1)
        {
            return ReturnDataError;
        }

        return _data.TryWriteBit(area, dbNumber, address, payload[0] != 0)
            ? ReturnSuccess
            : ResolveMissingCode(area, dbNumber);
    }

    private byte WriteBytes(
        S7Area area,
        int dbNumber,
        int address,
        byte transport,
        int elements,
        ReadOnlySpan<byte> payload,
        int byteLength)
    {
        var expected = GetByteLength(transport, elements);
        if (expected <= 0)
        {
            return ReturnDataTypeNotSupported;
        }

        if (expected != byteLength)
        {
            return ReturnDataError;
        }

        return _data.TryWriteBytes(area, dbNumber, address, payload)
            ? ReturnSuccess
            : ResolveMissingCode(area, dbNumber);
    }

    private byte ResolveMissingCode(S7Area area, int dbNumber)
    {
        if (area is S7Area.Timer or S7Area.Counter)
        {
            return ReturnAccessDenied;
        }

        if (area == S7Area.DataBlock && !_data.ContainsDataBlock(dbNumber))
        {
            return ReturnObjectNotExist;
        }

        return ReturnAddressOutOfRange;
    }

    private static bool TryReadSpecification(
        ReadOnlySpan<byte> specification,
        out byte transport,
        out int elements,
        out int dbNumber,
        out S7Area area,
        out int address)
    {
        transport = 0;
        elements = 0;
        dbNumber = 0;
        area = S7Area.DataBlock;
        address = 0;

        if (specification.Length < VariableSpecificationLength
            || specification[0] != 0x12
            || specification[1] != 0x0A
            || specification[2] != 0x10)
        {
            return false;
        }

        transport = specification[3];
        elements = (specification[4] << 8) | specification[5];
        dbNumber = (specification[6] << 8) | specification[7];
        area = (S7Area)specification[8];
        address = (specification[9] << 16) | (specification[10] << 8) | specification[11];
        return true;
    }

    private static int GetByteLength(byte transport, int elements) =>
        transport switch
        {
            0x02 => elements,           // BYTE
            0x03 => elements,           // CHAR
            0x04 => elements * 2,       // WORD
            0x05 => elements * 2,       // INT
            0x06 => elements * 4,       // DWORD
            0x07 => elements * 4,       // DINT
            0x08 => elements * 4,       // REAL
            _ => 0,
        };

    private static void WriteItemError(MemoryStream data, byte code)
    {
        data.WriteByte(code);
        data.WriteByte(0x00);
        data.WriteByte(0x00);
        data.WriteByte(0x00);
    }

    private static byte[] BuildAck(ushort pduReference, byte[] parameter, byte[] data, byte errorClass = 0, byte errorCode = 0)
    {
        var response = new byte[AckDataHeaderLength + parameter.Length + data.Length];
        response[0] = ProtocolId;
        response[1] = RosctrAckData;
        response[2] = 0x00;
        response[3] = 0x00;
        response[4] = (byte)(pduReference >> 8);
        response[5] = (byte)pduReference;
        response[6] = (byte)(parameter.Length >> 8);
        response[7] = (byte)parameter.Length;
        response[8] = (byte)(data.Length >> 8);
        response[9] = (byte)data.Length;
        response[10] = errorClass;
        response[11] = errorCode;

        parameter.CopyTo(response, AckDataHeaderLength);
        data.CopyTo(response, AckDataHeaderLength + parameter.Length);
        return response;
    }

    private static ushort ReadUInt16(byte[] buffer, int offset) => (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
}
