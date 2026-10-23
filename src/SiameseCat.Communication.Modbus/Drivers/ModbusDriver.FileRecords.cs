namespace SiameseCat.Communication.Modbus;

public partial class ModbusDriver
{
    /// <summary>Reads file records (function code 0x14) from the connected unit.</summary>
    /// <param name="requests">One entry per record to read; all entries are sent in one transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One register array per entry of <paramref name="requests"/>, in the same order.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public Task<ushort[][]> ReadFileRecordsAsync(
        IReadOnlyList<ModbusFileRecordRequest> requests,
        CancellationToken cancellationToken = default) =>
        _client.ReadFileRecordsAsync(_unitId, requests, cancellationToken);

    /// <summary>Writes file records (function code 0x15) to the connected unit.</summary>
    /// <param name="records">One entry per record to write; all entries are sent in one transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public Task WriteFileRecordsAsync(
        IReadOnlyList<ModbusFileRecord> records,
        CancellationToken cancellationToken = default) =>
        _client.WriteFileRecordsAsync(_unitId, records, cancellationToken);
}
