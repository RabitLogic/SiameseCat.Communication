using System.Net;
using System.Net.Sockets;

namespace SiameseCat.Demo;

/// <summary>
/// Minimal in-process Modbus TCP slave used by the <c>selftest</c> command.
/// It implements the function codes exercised by the demo and is not intended for production use.
/// </summary>
internal sealed class ModbusTcpTestServer : IAsyncDisposable
{
    private readonly ushort[] _holdingRegisters = new ushort[1000];
    private readonly ushort[] _inputRegisters = new ushort[1000];
    private readonly bool[] _coils = new bool[1000];
    private readonly bool[] _discreteInputs = new bool[1000];
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public int Port { get; private set; }

    public async Task StartAsync()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();

        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts?.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var stream = client.GetStream();
            var header = new byte[7];

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await stream.ReadExactlyAsync(header, cancellationToken);
                }
                catch (Exception)
                {
                    break;
                }

                var length = (header[4] << 8) | header[5];
                if (length < 2)
                {
                    break;
                }

                var pdu = new byte[length - 1];
                try
                {
                    await stream.ReadExactlyAsync(pdu, cancellationToken);
                }
                catch (Exception)
                {
                    break;
                }

                var responsePdu = Process(pdu);
                var response = BuildMbap(header, responsePdu);
                await stream.WriteAsync(response, cancellationToken);
            }
        }
    }

    private byte[] Process(byte[] pdu)
    {
        if (pdu.Length < 2)
        {
            return ExceptionResponse(pdu[0], 0x03);
        }

        return pdu[0] switch
        {
            0x01 => ReadBits(pdu, _coils),
            0x02 => ReadBits(pdu, _discreteInputs),
            0x03 => ReadRegisters(pdu, _holdingRegisters),
            0x04 => ReadRegisters(pdu, _inputRegisters),
            0x05 => WriteSingleCoil(pdu),
            0x06 => WriteSingleRegister(pdu),
            0x0F => WriteMultipleCoils(pdu),
            0x10 => WriteMultipleRegisters(pdu),
            0x17 => ReadWriteMultipleRegisters(pdu),
            _ => ExceptionResponse(pdu[0], 0x01),
        };
    }

    private static byte[] ReadBits(byte[] pdu, bool[] source)
    {
        if (!TryGetAddressAndQuantity(pdu, source.Length, out var start, out var quantity))
        {
            return ExceptionResponse(pdu[0], 0x02);
        }

        var byteCount = (quantity + 7) / 8;
        var response = new byte[2 + byteCount];
        response[0] = pdu[0];
        response[1] = (byte)byteCount;

        for (var i = 0; i < quantity; i++)
        {
            if (source[start + i])
            {
                response[2 + i / 8] |= (byte)(1 << (i % 8));
            }
        }

        return response;
    }

    private static byte[] ReadRegisters(byte[] pdu, ushort[] source)
    {
        if (!TryGetAddressAndQuantity(pdu, source.Length, out var start, out var quantity))
        {
            return ExceptionResponse(pdu[0], 0x02);
        }

        var response = new byte[2 + quantity * 2];
        response[0] = pdu[0];
        response[1] = (byte)(quantity * 2);

        for (var i = 0; i < quantity; i++)
        {
            var value = source[start + i];
            response[2 + i * 2] = (byte)(value >> 8);
            response[3 + i * 2] = (byte)value;
        }

        return response;
    }

    private byte[] WriteSingleCoil(byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            return ExceptionResponse(pdu[0], 0x03);
        }

        var address = (pdu[1] << 8) | pdu[2];
        var value = (pdu[3] << 8) | pdu[4];
        if (address >= _coils.Length || value is not (0x0000 or 0xFF00))
        {
            return ExceptionResponse(pdu[0], 0x02);
        }

        _coils[address] = value == 0xFF00;
        return pdu[..5];
    }

    private byte[] WriteSingleRegister(byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            return ExceptionResponse(pdu[0], 0x03);
        }

        var address = (pdu[1] << 8) | pdu[2];
        if (address >= _holdingRegisters.Length)
        {
            return ExceptionResponse(pdu[0], 0x02);
        }

        _holdingRegisters[address] = (ushort)((pdu[3] << 8) | pdu[4]);
        return pdu[..5];
    }

    private byte[] WriteMultipleCoils(byte[] pdu)
    {
        if (pdu.Length < 6)
        {
            return ExceptionResponse(pdu[0], 0x03);
        }

        var start = (pdu[1] << 8) | pdu[2];
        var quantity = (pdu[3] << 8) | pdu[4];
        var byteCount = pdu[5];
        if (quantity < 1 || start + quantity > _coils.Length || byteCount != (quantity + 7) / 8 || pdu.Length < 6 + byteCount)
        {
            return ExceptionResponse(pdu[0], 0x02);
        }

        for (var i = 0; i < quantity; i++)
        {
            _coils[start + i] = (pdu[6 + i / 8] & (1 << (i % 8))) != 0;
        }

        return pdu[..5];
    }

    private byte[] WriteMultipleRegisters(byte[] pdu)
    {
        if (pdu.Length < 6)
        {
            return ExceptionResponse(pdu[0], 0x03);
        }

        var start = (pdu[1] << 8) | pdu[2];
        var quantity = (pdu[3] << 8) | pdu[4];
        var byteCount = pdu[5];
        if (quantity < 1 || start + quantity > _holdingRegisters.Length || byteCount != quantity * 2 || pdu.Length < 6 + byteCount)
        {
            return ExceptionResponse(pdu[0], 0x02);
        }

        for (var i = 0; i < quantity; i++)
        {
            _holdingRegisters[start + i] = (ushort)((pdu[6 + i * 2] << 8) | pdu[7 + i * 2]);
        }

        return pdu[..5];
    }

    private byte[] ReadWriteMultipleRegisters(byte[] pdu)
    {
        if (pdu.Length < 10)
        {
            return ExceptionResponse(pdu[0], 0x03);
        }

        var readStart = (pdu[1] << 8) | pdu[2];
        var readQuantity = (pdu[3] << 8) | pdu[4];
        var writeStart = (pdu[5] << 8) | pdu[6];
        var writeQuantity = (pdu[7] << 8) | pdu[8];
        var byteCount = pdu[9];

        if (readQuantity < 1
            || writeQuantity < 1
            || readStart + readQuantity > _holdingRegisters.Length
            || writeStart + writeQuantity > _holdingRegisters.Length
            || byteCount != writeQuantity * 2
            || pdu.Length < 10 + byteCount)
        {
            return ExceptionResponse(pdu[0], 0x02);
        }

        for (var i = 0; i < writeQuantity; i++)
        {
            _holdingRegisters[writeStart + i] = (ushort)((pdu[10 + i * 2] << 8) | pdu[11 + i * 2]);
        }

        var readPdu = new byte[2 + readQuantity * 2];
        readPdu[0] = 0x17;
        readPdu[1] = (byte)(readQuantity * 2);

        for (var i = 0; i < readQuantity; i++)
        {
            var value = _holdingRegisters[readStart + i];
            readPdu[2 + i * 2] = (byte)(value >> 8);
            readPdu[3 + i * 2] = (byte)value;
        }

        return readPdu;
    }

    private static bool TryGetAddressAndQuantity(
        byte[] pdu,
        int capacity,
        out int start,
        out int quantity)
    {
        start = 0;
        quantity = 0;
        if (pdu.Length < 5)
        {
            return false;
        }

        start = (pdu[1] << 8) | pdu[2];
        quantity = (pdu[3] << 8) | pdu[4];
        return quantity > 0 && start + quantity <= capacity;
    }

    private static byte[] ExceptionResponse(byte functionCode, byte exceptionCode) =>
        new[] { (byte)(functionCode | 0x80), exceptionCode };

    private static byte[] BuildMbap(byte[] requestHeader, byte[] pdu)
    {
        var response = new byte[7 + pdu.Length];
        response[0] = requestHeader[0];
        response[1] = requestHeader[1];
        response[2] = 0;
        response[3] = 0;
        var length = (ushort)(pdu.Length + 1);
        response[4] = (byte)(length >> 8);
        response[5] = (byte)length;
        response[6] = requestHeader[6];
        pdu.CopyTo(response, 7);
        return response;
    }
}
