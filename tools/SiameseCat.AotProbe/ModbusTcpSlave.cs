using System.Net;
using System.Net.Sockets;

namespace SiameseCat.AotProbe;

/// <summary>Minimal in-process Modbus TCP slave used by the AOT probe.</summary>
internal sealed class ModbusTcpSlave : IAsyncDisposable
{
    private readonly ushort[] _registers = new ushort[16];
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    public ModbusTcpSlave()
    {
        _registers[0] = 100;
        _registers[1] = 200;
    }

    public int Port { get; private set; }

    public void Start()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
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
                    await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                var length = (header[4] << 8) | header[5];
                if (length < 2)
                {
                    return;
                }

                var pdu = new byte[length - 1];
                try
                {
                    await stream.ReadExactlyAsync(pdu, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                var responsePdu = Process(pdu);
                var response = new byte[7 + responsePdu.Length];
                header.AsSpan(0, 4).CopyTo(response);
                response[4] = (byte)((responsePdu.Length + 1) >> 8);
                response[5] = (byte)(responsePdu.Length + 1);
                response[6] = header[6];
                responsePdu.CopyTo(response, 7);

                try
                {
                    await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }
            }
        }
    }

    private byte[] Process(byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            return ExceptionResponse(pdu[0], 0x03);
        }

        var start = (pdu[1] << 8) | pdu[2];
        var quantity = (pdu[3] << 8) | pdu[4];

        switch (pdu[0])
        {
            case 0x03 when quantity > 0 && start + quantity <= _registers.Length:
                var read = new byte[2 + quantity * 2];
                read[0] = 0x03;
                read[1] = (byte)(quantity * 2);

                for (var i = 0; i < quantity; i++)
                {
                    read[2 + i * 2] = (byte)(_registers[start + i] >> 8);
                    read[3 + i * 2] = (byte)_registers[start + i];
                }

                return read;

            case 0x06 when start < _registers.Length:
                _registers[start] = (ushort)quantity;
                return pdu[..5];

            case 0x10 when pdu.Length >= 6:
                var count = pdu[5];
                var first = (pdu[3] << 8) | pdu[4];
                if (count != first * 2 || pdu.Length < 6 + count || start + first > _registers.Length)
                {
                    return ExceptionResponse(pdu[0], 0x03);
                }

                for (var i = 0; i < first; i++)
                {
                    _registers[start + i] = (ushort)((pdu[6 + i * 2] << 8) | pdu[7 + i * 2]);
                }

                return pdu[..5];

            default:
                return ExceptionResponse(pdu[0], 0x02);
        }
    }

    private static byte[] ExceptionResponse(byte functionCode, byte exceptionCode) =>
        new[] { (byte)(functionCode | 0x80), exceptionCode };
}
