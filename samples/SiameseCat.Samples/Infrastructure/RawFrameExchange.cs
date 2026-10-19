using SiameseCat.Communication;
using SiameseCat.Communication.Modbus;

namespace SiameseCat.Samples.Infrastructure;

/// <summary>
/// Sends hand-built frames over a transport and reads the answer.
/// </summary>
/// <remarks>
/// Useful when teaching or diagnosing a protocol: the client classes hide the framing, this helper
/// shows it. The read side is deliberately tolerant: it collects bytes until the peer goes quiet.
/// </remarks>
public static class RawFrameExchange
{
    /// <summary>Builds a Modbus RTU frame: unit id + PDU + CRC16.</summary>
    /// <param name="unitId">Station address.</param>
    /// <param name="pdu">Protocol data unit bytes.</param>
    public static byte[] Rtu(byte unitId, params byte[] pdu)
    {
        var frame = new byte[pdu.Length + 3];
        frame[0] = unitId;
        pdu.CopyTo(frame, 1);
        ModbusCrc16.Write(frame.AsSpan(0, frame.Length - 2), frame.AsSpan(frame.Length - 2));
        return frame;
    }

    /// <summary>Builds a Modbus TCP frame (MBAP header + PDU).</summary>
    /// <param name="transactionId">Transaction identifier echoed by the server.</param>
    /// <param name="unitId">Unit identifier.</param>
    /// <param name="pdu">Protocol data unit bytes.</param>
    public static byte[] Tcp(ushort transactionId, byte unitId, params byte[] pdu)
    {
        var frame = new byte[pdu.Length + 7];
        frame[0] = (byte)(transactionId >> 8);
        frame[1] = (byte)transactionId;
        frame[2] = 0;
        frame[3] = 0;
        frame[4] = (byte)((pdu.Length + 1) >> 8);
        frame[5] = (byte)(pdu.Length + 1);
        frame[6] = unitId;
        pdu.CopyTo(frame, 7);
        return frame;
    }

    /// <summary>Writes a request and collects the response bytes until the peer is quiet.</summary>
    /// <param name="transport">Open transport.</param>
    /// <param name="request">Bytes to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bytes that were received; empty when nothing arrived.</returns>
    public static async Task<byte[]> RequestAsync(
        ITransport transport,
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken)
    {
        await transport.WriteAsync(request, cancellationToken).ConfigureAwait(false);

        var buffer = new byte[512];
        var total = 0;

        while (total < buffer.Length)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(total == 0 ? TimeSpan.FromMilliseconds(400) : TimeSpan.FromMilliseconds(120));

            int read;
            try
            {
                read = await transport.ReadAsync(buffer.AsMemory(total), timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return buffer.AsSpan(0, total).ToArray();
    }

    /// <summary>Formats bytes as upper-case hexadecimal separated by spaces.</summary>
    /// <param name="bytes">Bytes to format.</param>
    public static string Hex(ReadOnlySpan<byte> bytes) =>
        bytes.Length == 0 ? "(no response)" : string.Join(" ", bytes.ToArray().Select(value => value.ToString("X2")));
}
