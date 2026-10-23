using SiameseCat.Communication;
using SiameseCat.Communication.S7;

namespace SiameseCat.Communication.Server.S7;

/// <summary>
/// S7 PDU handling used by <see cref="S7Server"/>.
/// The response PDU is written into a caller-provided buffer so the serving loop reuses its memory.
/// </summary>
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
    private readonly ICommunicationLogger? _logger;

    public S7PduProcessor(S7DataStore data, S7ServerStatistics statistics, int pduSize, ICommunicationLogger? logger)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _pduSize = pduSize;
        _logger = logger;
    }

    /// <summary>Handles one S7 PDU (the body after TPKT and COTP).</summary>
    /// <param name="pdu">Request PDU, starting with the S7 protocol id.</param>
    /// <param name="destination">Buffer for the response PDU (at least 300 bytes).</param>
    /// <returns>Number of bytes written to <paramref name="destination"/>, or 0 when nothing must be sent.</returns>
    public int Process(ReadOnlySpan<byte> pdu, Span<byte> destination)
    {
        if (pdu.Length < JobHeaderLength || pdu[0] != ProtocolId || pdu[1] != RosctrJob)
        {
            _statistics.RecordDiscardedFrame();
            _logger?.Warning("S7 slave dropped a frame with an unsupported COTP/S7 header.");
            return 0;
        }

        var pduReference = ReadUInt16(pdu, 4);
        var parameterLength = ReadUInt16(pdu, 6);
        var dataLength = ReadUInt16(pdu, 8);

        if (pdu.Length < JobHeaderLength + parameterLength + dataLength)
        {
            _statistics.RecordDiscardedFrame();
            _logger?.Warning("S7 slave dropped a frame that is shorter than its declared lengths.");
            return 0;
        }

        var parameter = pdu.Slice(JobHeaderLength, parameterLength);
        var data = pdu.Slice(JobHeaderLength + parameterLength, dataLength);

        if (parameter.Length == 0)
        {
            return BuildError(destination, pduReference, 0x81, 0x04);
        }

        _statistics.RecordRequest();

        return parameter[0] switch
        {
            FunctionSetupCommunication => SetupCommunication(destination, pduReference),
            FunctionReadVar => ReadVariables(destination, pduReference, parameter),
            FunctionWriteVar => WriteVariables(destination, pduReference, parameter, data),
            _ => LogUnsupported(destination, pduReference, parameter[0]),
        };
    }

    private int LogUnsupported(Span<byte> destination, ushort pduReference, byte function)
    {
        _logger?.Warning($"S7 slave received unsupported function 0x{function:X2}.");
        return BuildError(destination, pduReference, 0x81, 0x04);
    }

    private int SetupCommunication(Span<byte> destination, ushort pduReference)
    {
        _statistics.RecordSetup();
        WriteAckHeader(destination, pduReference, 8, 0);

        var parameter = destination.Slice(AckDataHeaderLength, 8);
        parameter[0] = FunctionSetupCommunication;
        parameter[1] = 0x00;
        parameter[2] = 0x00;
        parameter[3] = 0x01;
        parameter[4] = 0x00;
        parameter[5] = 0x01;
        parameter[6] = (byte)(_pduSize >> 8);
        parameter[7] = (byte)_pduSize;
        return AckDataHeaderLength + 8;
    }

    private int ReadVariables(Span<byte> destination, ushort pduReference, ReadOnlySpan<byte> parameter)
    {
        var count = parameter[1];
        if (count == 0 || parameter.Length < 2 + (count * VariableSpecificationLength))
        {
            return BuildError(destination, pduReference, 0x81, 0x04);
        }

        var offset = AckDataHeaderLength + 2;
        var dataStart = offset;
        var errors = 0;

        for (var i = 0; i < count; i++)
        {
            var item = AppendReadItem(
                destination.Slice(offset),
                parameter.Slice(2 + (i * VariableSpecificationLength), VariableSpecificationLength));
            offset += item.Length;
            if (!item.Success)
            {
                errors++;
            }
        }

        if (errors > 0)
        {
            for (var i = 0; i < errors; i++)
            {
                _statistics.RecordItemError();
            }
        }

        WriteAckHeader(destination, pduReference, 2, offset - dataStart);
        var responseParameter = destination.Slice(AckDataHeaderLength, 2);
        responseParameter[0] = FunctionReadVar;
        responseParameter[1] = count;

        _statistics.RecordRead(count);
        return offset;
    }

    private (int Length, bool Success) AppendReadItem(Span<byte> destination, ReadOnlySpan<byte> specification)
    {
        if (!TryReadSpecification(specification, out var transport, out var elements, out var dbNumber, out var area, out var address))
        {
            WriteItemError(destination, ReturnDataError);
            return (4, false);
        }

        if (transport == TransportBit)
        {
            if (elements != 1)
            {
                WriteItemError(destination, ReturnDataError);
                return (4, false);
            }

            if (!_data.TryReadBit(area, dbNumber, address, out var value))
            {
                WriteItemError(destination, ResolveMissingCode(area, dbNumber));
                return (4, false);
            }

            destination[0] = ReturnSuccess;
            destination[1] = DataTransportBit;
            destination[2] = 0x00;
            destination[3] = 0x01;
            destination[4] = value ? (byte)0x01 : (byte)0x00;
            // The client aligns items on even offsets, so even a one-byte bit item carries padding.
            destination[5] = 0x00;
            return (6, true);
        }

        var byteLength = GetByteLength(transport, elements);
        if (byteLength <= 0)
        {
            WriteItemError(destination, ReturnDataTypeNotSupported);
            return (4, false);
        }

        var padded = byteLength + (byteLength & 1);
        if (!_data.TryCopyBytes(area, dbNumber, address / 8, destination.Slice(4, byteLength)))
        {
            WriteItemError(destination, ResolveMissingCode(area, dbNumber));
            return (4, false);
        }

        var bitLength = byteLength * 8;
        destination[0] = ReturnSuccess;
        destination[1] = DataTransportByte;
        destination[2] = (byte)(bitLength >> 8);
        destination[3] = (byte)bitLength;
        if ((byteLength & 1) != 0)
        {
            destination[4 + byteLength] = 0x00;
        }

        return (4 + padded, true);
    }

    private int WriteVariables(
        Span<byte> destination,
        ushort pduReference,
        ReadOnlySpan<byte> parameter,
        ReadOnlySpan<byte> data)
    {
        var count = parameter[1];
        if (count == 0 || parameter.Length < 2 + (count * VariableSpecificationLength))
        {
            return BuildError(destination, pduReference, 0x81, 0x04);
        }

        var codes = destination.Slice(AckDataHeaderLength + 2, count);
        var offset = 0;

        for (var i = 0; i < count; i++)
        {
            var specification = parameter.Slice(2 + (i * VariableSpecificationLength), VariableSpecificationLength);
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

            var payload = data.Slice(offset + 4, byteLength);
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

        WriteAckHeader(destination, pduReference, 2, count);
        var responseParameter = destination.Slice(AckDataHeaderLength, 2);
        responseParameter[0] = FunctionWriteVar;
        responseParameter[1] = count;
        return AckDataHeaderLength + 2 + count;
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

    private static void WriteItemError(Span<byte> destination, byte code)
    {
        destination[0] = code;
        destination[1] = 0x00;
        destination[2] = 0x00;
        destination[3] = 0x00;
    }

    private static int BuildError(Span<byte> destination, ushort pduReference, byte errorClass, byte errorCode)
    {
        WriteAckHeader(destination, pduReference, 0, 0, errorClass, errorCode);
        return AckDataHeaderLength;
    }

    private static void WriteAckHeader(
        Span<byte> destination,
        ushort pduReference,
        int parameterLength,
        int dataLength,
        byte errorClass = 0,
        byte errorCode = 0)
    {
        destination[0] = ProtocolId;
        destination[1] = RosctrAckData;
        destination[2] = 0x00;
        destination[3] = 0x00;
        destination[4] = (byte)(pduReference >> 8);
        destination[5] = (byte)pduReference;
        destination[6] = (byte)(parameterLength >> 8);
        destination[7] = (byte)parameterLength;
        destination[8] = (byte)(dataLength >> 8);
        destination[9] = (byte)dataLength;
        destination[10] = errorClass;
        destination[11] = errorCode;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> buffer, int offset) =>
        (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
}
