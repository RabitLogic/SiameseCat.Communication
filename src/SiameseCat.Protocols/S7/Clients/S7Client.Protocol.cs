using SiameseCat.Core;

namespace SiameseCat.Protocols.S7;

/// <summary>Frame construction and parsing for the S7 protocol.</summary>
public sealed partial class S7Client
{
    private const byte S7ProtocolId = 0x32;
    private const byte RosctrJob = 0x01;
    private const byte RosctrAckData = 0x03;
    private const byte RosctrUserData = 0x07;
    private const byte CotpConnectionConfirm = 0xD0;
    private const byte SetupCommunicationFunction = 0xF0;
    private const byte FunctionReadVar = 0x04;
    private const byte FunctionWriteVar = 0x05;
    private const int MinimumPduSize = 240;
    private const int MaximumPduSize = 960;
    private const int AddressSpaceSize = 0x200000;
    private const int VariableSpecificationLength = 12;
    private const int JobHeaderLength = 10;
    private const int AckDataHeaderLength = 12;

    private async Task<S7Response> ExecuteJobAsync(
        byte[] parameter,
        ReadOnlyMemory<byte> data,
        bool isWrite,
        CancellationToken cancellationToken)
    {
        return await RetryPolicy.ExecuteAsync(
            token => ExecuteJobAttemptAsync(parameter, data, token),
            _options,
            cancellationToken,
            isWrite,
            reconnect: ReconnectAsync).ConfigureAwait(false);
    }

    private async Task<S7Response> ExecuteJobAttemptAsync(
        byte[] parameter,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        var frame = BuildJobFrame(parameter, data.Span, out var pduReference);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        try
        {
            _transport.DiscardInput();
            await _transport.WriteAsync(frame, timeoutCts.Token).ConfigureAwait(false);
            var responseFrame = await ReceiveTpktAsync(timeoutCts.Token).ConfigureAwait(false);
            return ParseResponse(responseFrame, pduReference);
        }
        catch (OperationCanceledException)
        {
            // A late or partial response would otherwise be consumed by the next request.
            await _transport.CloseAsync().ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new CommunicationTimeoutException($"The S7 request timed out after {_options.Timeout}.");
        }
    }

    private byte[] BuildConnectionRequest()
    {
        var remoteTsap = (byte)(_options.Rack * 0x20 + _options.Slot);

        return new byte[]
        {
            0x03, 0x00, 0x00, 0x16,
            0x11, 0xE0, 0x00, 0x00, 0x00, 0x01, 0x00,
            0xC1, 0x02, 0x01, 0x00,
            0xC2, 0x02, 0x01, remoteTsap,
            0xC0, 0x01, 0x0A,
        };
    }

    private static byte[] BuildSetupCommunicationRequest() => new byte[]
    {
        SetupCommunicationFunction, 0x00, 0x00, 0x01, 0x00, 0x01, 0x03, 0xC0,
    };

    private byte[] BuildJobFrame(
        ReadOnlySpan<byte> parameter,
        ReadOnlySpan<byte> data,
        out ushort pduReference)
    {
        pduReference = unchecked(++_pduReference);
        if (pduReference == 0)
        {
            pduReference = unchecked(++_pduReference);
        }

        var frame = new byte[4 + 3 + JobHeaderLength + parameter.Length + data.Length];
        frame[0] = 0x03;
        frame[1] = 0x00;
        frame[2] = (byte)(frame.Length >> 8);
        frame[3] = (byte)frame.Length;

        frame[4] = 0x02;
        frame[5] = 0xF0;
        frame[6] = 0x80;

        var s7 = frame.AsSpan(7);
        s7[0] = S7ProtocolId;
        s7[1] = RosctrJob;
        s7[2] = 0x00;
        s7[3] = 0x00;
        s7[4] = (byte)(pduReference >> 8);
        s7[5] = (byte)pduReference;
        s7[6] = (byte)(parameter.Length >> 8);
        s7[7] = (byte)parameter.Length;
        s7[8] = (byte)(data.Length >> 8);
        s7[9] = (byte)data.Length;

        parameter.CopyTo(s7[JobHeaderLength..]);
        data.CopyTo(s7[(JobHeaderLength + parameter.Length)..]);
        return frame;
    }

    private async Task<byte[]> ReceiveTpktAsync(CancellationToken cancellationToken)
    {
        var header = await _transport.ReadExactlyAsync(4, cancellationToken).ConfigureAwait(false);
        if (header[0] != 0x03)
        {
            throw new ProtocolException($"Unexpected TPKT version 0x{header[0]:X2}.");
        }

        var length = (header[2] << 8) | header[3];
        if (length is < 7 or > 4096)
        {
            throw new ProtocolException($"Unexpected TPKT length {length}.");
        }

        var frame = new byte[length];
        header.CopyTo(frame, 0);
        var body = await _transport.ReadExactlyAsync(length - 4, cancellationToken).ConfigureAwait(false);
        body.CopyTo(frame, 4);
        return frame;
    }

    private static S7Response ParseResponse(byte[] frame, ushort expectedPduReference)
    {
        if (frame.Length < 7 + AckDataHeaderLength)
        {
            throw new ProtocolException("The S7 response is too short.");
        }

        if (frame[5] != 0xF0 || frame[6] != 0x80)
        {
            throw new ProtocolException("The S7 response has an unexpected COTP header.");
        }

        var s7 = frame.AsSpan(7);
        if (s7[0] != S7ProtocolId)
        {
            throw new ProtocolException($"Unexpected S7 protocol id 0x{s7[0]:X2}.");
        }

        var rosctr = s7[1];
        if (rosctr is not (0x02 or RosctrAckData or RosctrUserData))
        {
            throw new ProtocolException($"Unexpected S7 ROSCTR 0x{rosctr:X2}.");
        }

        var pduReference = (ushort)((s7[4] << 8) | s7[5]);
        if (pduReference != expectedPduReference)
        {
            throw new ProtocolException(
                $"The S7 response has PDU reference {pduReference} but {expectedPduReference} was expected.");
        }

        var parameterLength = (s7[6] << 8) | s7[7];
        var dataLength = (s7[8] << 8) | s7[9];
        var headerLength = rosctr == RosctrJob ? JobHeaderLength : AckDataHeaderLength;

        if (s7.Length < headerLength + parameterLength + dataLength)
        {
            throw new ProtocolException("The S7 response is shorter than its declared parameter and data lengths.");
        }

        if (headerLength == AckDataHeaderLength)
        {
            var errorClass = s7[10];
            var errorCode = s7[11];
            if (errorClass != 0 || errorCode != 0)
            {
                throw new S7Exception(errorClass, errorCode);
            }
        }

        var parameter = s7.Slice(headerLength, parameterLength).ToArray();
        var data = s7.Slice(headerLength + parameterLength, dataLength).ToArray();
        return new S7Response(parameter, data);
    }

    private async Task<byte[]> ReadVarSingleAsync(
        S7Address address,
        int length,
        bool bit,
        CancellationToken cancellationToken)
    {
        var parameter = BuildVariableParameter(FunctionReadVar, address, length, bit);
        var response = await ExecuteJobAsync(
            parameter, ReadOnlyMemory<byte>.Empty, isWrite: false, cancellationToken)
            .ConfigureAwait(false);

        ValidateReadParameter(response.Parameter, 1);

        try
        {
            return ParseReadData(response.Data, 1)[0];
        }
        catch (S7ItemException ex)
        {
            throw new S7ItemException(
                ex.ReturnCode,
                $"{ex.Message} Address: {address}.");
        }
    }

    private static byte[] BuildVariableParameter(
        byte function,
        S7Address address,
        int length,
        bool bit)
    {
        var parameter = new byte[2 + VariableSpecificationLength];
        parameter[0] = function;
        parameter[1] = 0x01;
        WriteVariableSpecification(parameter.AsSpan(2), address, length, bit);
        return parameter;
    }

    private static void WriteVariableSpecification(
        Span<byte> destination,
        S7Address address,
        int length,
        bool bit)
    {
        destination[0] = 0x12;
        destination[1] = 0x0A;
        destination[2] = 0x10;
        destination[3] = bit ? (byte)0x01 : (byte)0x02;

        var elements = bit ? 1 : length;
        destination[4] = (byte)(elements >> 8);
        destination[5] = (byte)elements;
        destination[6] = (byte)(address.DbNumber >> 8);
        destination[7] = (byte)address.DbNumber;
        destination[8] = (byte)address.Area;

        var bitAddress = address.BitAddress;
        destination[9] = (byte)(bitAddress >> 16);
        destination[10] = (byte)(bitAddress >> 8);
        destination[11] = (byte)bitAddress;
    }

    private static byte[] BuildMultiReadParameter(IReadOnlyList<S7ReadItem> items)
    {
        var parameter = new byte[2 + items.Count * VariableSpecificationLength];
        parameter[0] = FunctionReadVar;
        parameter[1] = (byte)items.Count;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var bit = item.Address.DataType == S7DataType.Bool;
            WriteVariableSpecification(
                parameter.AsSpan(2 + i * VariableSpecificationLength),
                item.Address,
                item.Length,
                bit);
        }

        return parameter;
    }

    private static byte[] BuildMultiWriteParameter(IReadOnlyList<S7WriteItem> items)
    {
        var parameter = new byte[2 + items.Count * VariableSpecificationLength];
        parameter[0] = FunctionWriteVar;
        parameter[1] = (byte)items.Count;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var bit = item.Address.DataType == S7DataType.Bool;
            WriteVariableSpecification(
                parameter.AsSpan(2 + i * VariableSpecificationLength),
                item.Address,
                item.Data.Length,
                bit);
        }

        return parameter;
    }

    private static byte[] BuildMultiWriteData(IReadOnlyList<S7WriteItem> items)
    {
        var totalLength = 0;
        foreach (var item in items)
        {
            totalLength += GetWriteDataItemLength(item.Data.Length);
        }

        var data = new byte[totalLength];
        var offset = 0;
        foreach (var item in items)
        {
            var bit = item.Address.DataType == S7DataType.Bool;
            WriteDataItem(data.AsSpan(offset), item.Data.Span, bit);
            offset += GetWriteDataItemLength(item.Data.Length);
        }

        return data;
    }

    private static byte[] BuildWriteData(ReadOnlySpan<byte> payload, bool bit)
    {
        var data = new byte[GetWriteDataItemLength(payload.Length)];
        WriteDataItem(data, payload, bit);
        return data;
    }

    private static int GetWriteDataItemLength(int payloadLength) => 4 + (payloadLength + 1) / 2 * 2;

    private static void WriteDataItem(Span<byte> destination, ReadOnlySpan<byte> payload, bool bit)
    {
        destination[0] = 0x00;
        destination[1] = bit ? (byte)0x03 : (byte)0x04;

        var bitLength = bit ? 1 : payload.Length * 8;
        destination[2] = (byte)(bitLength >> 8);
        destination[3] = (byte)bitLength;
        payload.CopyTo(destination[4..]);

        // Odd payloads carry one padding byte so the next item starts on an even boundary.
        var padding = destination.Length - 4 - payload.Length;
        if (padding > 0)
        {
            destination[^(padding)..].Clear();
        }
    }

    private static void ValidateReadParameter(byte[] parameter, int itemCount)
    {
        if (parameter.Length < 2 || parameter[0] != FunctionReadVar || parameter[1] != itemCount)
        {
            throw new ProtocolException("The S7 read response contains an unexpected parameter block.");
        }
    }

    private static byte[][] ParseReadData(byte[] data, int itemCount)
    {
        var result = new byte[itemCount][];
        var offset = 0;

        for (var i = 0; i < itemCount; i++)
        {
            if (data.Length < offset + 4)
            {
                throw new ProtocolException("The S7 read response data is truncated.");
            }

            var returnCode = data[offset];
            var bitLength = (data[offset + 2] << 8) | data[offset + 3];
            offset += 4;

            if (returnCode != 0xFF)
            {
                var location = itemCount > 1 ? $" on item {i}" : string.Empty;
                throw new S7ItemException(
                    returnCode,
                    $"S7 item error 0x{returnCode:X2}{location}: {S7ItemException.GetReturnCodeName(returnCode)}.");
            }

            var byteLength = (bitLength + 7) / 8;
            if (data.Length < offset + byteLength)
            {
                throw new ProtocolException("The S7 read response data is truncated.");
            }

            var item = new byte[byteLength];
            Array.Copy(data, offset, item, 0, byteLength);
            result[i] = item;
            offset += byteLength;

            if ((byteLength & 1) != 0 && offset < data.Length)
            {
                offset++;
            }
        }

        return result;
    }

    private static void ValidateWriteResponse(S7Response response)
    {
        if (response.Parameter.Length < 2 || response.Parameter[0] != FunctionWriteVar || response.Parameter[1] != 0x01)
        {
            throw new ProtocolException("The S7 write response contains an unexpected parameter block.");
        }

        if (response.Data.Length < 1)
        {
            throw new ProtocolException("The S7 write response does not contain a return code.");
        }

        if (response.Data[0] != 0xFF)
        {
            throw new S7ItemException(
                response.Data[0],
                $"S7 write failed: {S7ItemException.GetReturnCodeName(response.Data[0])}.");
        }
    }

    private static void ValidateMultiWriteResponse(S7Response response, int itemCount)
    {
        if (response.Parameter.Length < 2 || response.Parameter[0] != FunctionWriteVar || response.Parameter[1] != itemCount)
        {
            throw new ProtocolException("The S7 multi-write response contains an unexpected parameter block.");
        }

        if (response.Data.Length < itemCount)
        {
            throw new ProtocolException("The S7 multi-write response does not contain a return code for every item.");
        }

        for (var i = 0; i < itemCount; i++)
        {
            if (response.Data[i] != 0xFF)
            {
                throw new S7ItemException(
                    response.Data[i],
                    $"S7 write failed on item {i}: {S7ItemException.GetReturnCodeName(response.Data[i])}.");
            }
        }
    }

    private static void ValidateRange(S7Address address, int count)
    {
        if (count < 0 || address.ByteOffset + count > AddressSpaceSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                $"The address range exceeds the S7 address space (maximum {AddressSpaceSize} bytes).");
        }
    }

    private int GetReadChunkSize() => AlignToWord(Math.Max(1, _pduSize - 32));

    private int GetWriteChunkSize() => AlignToWord(Math.Max(1, _pduSize - 35));

    private static int AlignToWord(int size) => size > 1 && (size & 1) != 0 ? size - 1 : size;

    private readonly record struct S7Response(byte[] Parameter, byte[] Data);
}
