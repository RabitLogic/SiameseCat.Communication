using SiameseCat.Communication;

namespace SiameseCat.Communication.Modbus;

public abstract partial class ModbusClientBase
{
    private const byte FunctionReadFileRecord = 0x14;
    private const byte FunctionWriteFileRecord = 0x15;
    private const byte FileRecordReferenceType = 0x06;
    private const int MaxFileRecordRegisters = 125;
    private const int MaxReadFileRecordRequests = 35;

    /// <summary>Reads file records (function code 0x14) from <see cref="DefaultUnitId"/>.</summary>
    /// <param name="requests">One entry per record to read; all entries are sent in one transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One register array per entry of <paramref name="requests"/>, in the same order.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public Task<ushort[][]> ReadFileRecordsAsync(
        IReadOnlyList<ModbusFileRecordRequest> requests,
        CancellationToken cancellationToken = default) =>
        ReadFileRecordsAsync(DefaultUnitId, requests, cancellationToken);

    /// <summary>Reads file records (function code 0x14) from a specific unit.</summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="requests">One entry per record to read; all entries are sent in one transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One register array per entry of <paramref name="requests"/>, in the same order.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task<ushort[][]> ReadFileRecordsAsync(
        byte unitId,
        IReadOnlyList<ModbusFileRecordRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var length = ValidateReadRequests(requests);

        var pdu = new byte[length];
        pdu[0] = FunctionReadFileRecord;
        pdu[1] = (byte)(requests.Count * 7);
        var position = 2;
        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            pdu[position] = FileRecordReferenceType;
            pdu[position + 1] = High(request.FileNumber);
            pdu[position + 2] = Low(request.FileNumber);
            pdu[position + 3] = High(request.RecordNumber);
            pdu[position + 4] = Low(request.RecordNumber);
            pdu[position + 5] = High(request.RecordLength);
            pdu[position + 6] = Low(request.RecordLength);
            position += 7;
        }

        byte[] response = await ExecutePduAsync(unitId, pdu, isWrite: false, cancellationToken)
            .ConfigureAwait(false);
        return ParseFileRecordResponse(response, requests);
    }

    /// <summary>Writes file records (function code 0x15) to <see cref="DefaultUnitId"/>.</summary>
    /// <param name="records">One entry per record to write; all entries are sent in one transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public Task WriteFileRecordsAsync(
        IReadOnlyList<ModbusFileRecord> records,
        CancellationToken cancellationToken = default) =>
        WriteFileRecordsAsync(DefaultUnitId, records, cancellationToken);

    /// <summary>
    /// Writes file records (function code 0x15) to a specific unit. Unit id 0 broadcasts the write
    /// without waiting for a response.
    /// </summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="records">One entry per record to write; all entries are sent in one transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task WriteFileRecordsAsync(
        byte unitId,
        IReadOnlyList<ModbusFileRecord> records,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(records, nameof(records));

        if (records.Count == 0)
        {
            throw new ArgumentException("At least one file record must be written.", nameof(records));
        }

        var length = 2;
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i] ?? throw new ArgumentException(
                "The file record list must not contain null entries.", nameof(records));
            ValidateFileNumber(record.FileNumber, nameof(records));
            ValidateRecordNumber(record.RecordNumber, nameof(records));
            ValidateRecordLength(record.Data.Length, nameof(records));
            length += 7 + (record.Data.Length * 2);
        }

        if (length > 253)
        {
            throw new ArgumentOutOfRangeException(
                nameof(records), length, "The write request exceeds the 253-byte Modbus PDU limit.");
        }

        var pdu = new byte[length];
        pdu[0] = FunctionWriteFileRecord;
        pdu[1] = (byte)(length - 2);
        var position = 2;
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            pdu[position] = FileRecordReferenceType;
            pdu[position + 1] = High(record.FileNumber);
            pdu[position + 2] = Low(record.FileNumber);
            pdu[position + 3] = High(record.RecordNumber);
            pdu[position + 4] = Low(record.RecordNumber);
            pdu[position + 5] = High((ushort)record.Data.Length);
            pdu[position + 6] = Low((ushort)record.Data.Length);
            position += 7;
            for (var r = 0; r < record.Data.Length; r++)
            {
                pdu[position++] = High(record.Data[r]);
                pdu[position++] = Low(record.Data[r]);
            }
        }

        byte[] response = await ExecutePduAsync(unitId, pdu, isWrite: true, cancellationToken)
            .ConfigureAwait(false);

        if (unitId == 0)
        {
            return;
        }

        if (response.Length != pdu.Length)
        {
            throw new ProtocolException("The Modbus file record response does not echo the request.");
        }

        for (var i = 0; i < pdu.Length; i++)
        {
            if (response[i] != pdu[i])
            {
                throw new ProtocolException("The Modbus file record response does not echo the request.");
            }
        }
    }

    private static int ValidateReadRequests(IReadOnlyList<ModbusFileRecordRequest> requests)
    {
        Guard.NotNull(requests, nameof(requests));

        if (requests.Count is < 1 or > MaxReadFileRecordRequests)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requests),
                requests.Count,
                $"Between 1 and {MaxReadFileRecordRequests} file records can be read per request.");
        }

        var length = 2;
        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            ValidateFileNumber(request.FileNumber, nameof(requests));
            ValidateRecordNumber(request.RecordNumber, nameof(requests));
            ValidateRecordLength(request.RecordLength, nameof(requests));
            length += 2 + (request.RecordLength * 2);
        }

        if (length > 253)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requests), length, "The expected response exceeds the 253-byte Modbus PDU limit.");
        }

        return 2 + (requests.Count * 7);
    }

    private static ushort[][] ParseFileRecordResponse(
        byte[] response,
        IReadOnlyList<ModbusFileRecordRequest> requests)
    {
        if (response.Length < 3 || response[0] != FunctionReadFileRecord)
        {
            throw new ProtocolException("The Modbus file record response has an unexpected header.");
        }

        if (response.Length != 2 + response[1])
        {
            throw new ProtocolException("The Modbus file record response has an unexpected length.");
        }

        var result = new ushort[requests.Count][];
        var position = 2;
        for (var i = 0; i < requests.Count; i++)
        {
            if (position + 2 > response.Length)
            {
                throw new ProtocolException("The Modbus file record response is truncated.");
            }

            var subLength = response[position];
            if (subLength < 2 || position + 1 + subLength > response.Length)
            {
                throw new ProtocolException("The Modbus file record response is truncated.");
            }

            if (response[position + 1] != FileRecordReferenceType)
            {
                throw new ProtocolException("The Modbus file record response uses an unexpected reference type.");
            }

            var recordLength = requests[i].RecordLength;
            if (subLength - 1 != recordLength * 2)
            {
                throw new ProtocolException(
                    "The Modbus file record response returns a different record length than requested.");
            }

            var values = new ushort[recordLength];
            for (var r = 0; r < values.Length; r++)
            {
                values[r] = (ushort)((response[position + 2 + (r * 2)] << 8) | response[position + 3 + (r * 2)]);
            }

            result[i] = values;
            position += 1 + subLength;
        }

        if (position != response.Length)
        {
            throw new ProtocolException("The Modbus file record response has an unexpected length.");
        }

        return result;
    }

    private static void ValidateFileNumber(ushort fileNumber, string parameterName)
    {
        if (fileNumber == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, fileNumber, "File numbers start at 1.");
        }
    }

    private static void ValidateRecordNumber(ushort recordNumber, string parameterName)
    {
        if (recordNumber > 9999)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, recordNumber, "Record numbers must be between 0 and 9999.");
        }
    }

    private static void ValidateRecordLength(int recordLength, string parameterName)
    {
        if (recordLength is < 1 or > MaxFileRecordRegisters)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, recordLength, $"A record holds between 1 and {MaxFileRecordRegisters} registers.");
        }
    }
}
