using System.Net;
using System.Net.Sockets;
using SiameseCat.Communication;
using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.Transport;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Modbus;

public class ModbusTcpClientTests
{
    [Fact]
    public async Task ReadHoldingRegisters_UsesMbapFraming()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(1, 1, 0x03, 0x04, 0x00, 0x0A, 0x00, 0x0B));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        var registers = await client.ReadHoldingRegistersAsync(0, 2);

        Assert.Equal(new ushort[] { 10, 11 }, registers);
        Assert.Equal(
            Mbap(1, 1, 0x03, 0x00, 0x00, 0x00, 0x02),
            Assert.Single(transport.Writes));
    }

    [Fact]
    public async Task TransactionId_IncrementsPerRequest()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(1, 1, 0x03, 0x02, 0x00, 0x01));
        transport.EnqueueResponse(Mbap(2, 1, 0x03, 0x02, 0x00, 0x02));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        Assert.Equal(1, (await client.ReadHoldingRegistersAsync(0, 1))[0]);
        Assert.Equal(2, (await client.ReadHoldingRegistersAsync(0, 1))[0]);
        Assert.Equal(1, (transport.Writes[0][0] << 8) | transport.Writes[0][1]);
        Assert.Equal(2, (transport.Writes[1][0] << 8) | transport.Writes[1][1]);
    }

    [Fact]
    public async Task WrongTransactionId_ThrowsProtocolException()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(99, 1, 0x03, 0x02, 0x00, 0x01));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        await Assert.ThrowsAsync<ProtocolException>(() => client.ReadHoldingRegistersAsync(0, 1));
    }

    [Fact]
    public async Task ExceptionResponse_ThrowsModbusException()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(Mbap(1, 1, 0x83, 0x02));

        await using var client = new ModbusTcpClient(transport, CreateOptions());
        await client.ConnectAsync();

        var exception = await Assert.ThrowsAsync<ModbusException>(
            () => client.ReadHoldingRegistersAsync(0, 1));

        Assert.Equal(2, exception.ExceptionCode);
    }

    [Fact]
    public async Task LoopbackServer_EndToEnd()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var server = Task.Run(async () =>
            {
                using var tcp = await listener.AcceptTcpClientAsync();
                var stream = tcp.GetStream();

                var header = new byte[7];
                await stream.ReadExactlyAsync(header);
                var length = (header[4] << 8) | header[5];
                var requestPdu = new byte[length - 1];
                await stream.ReadExactlyAsync(requestPdu);

                var responsePdu = new byte[] { 0x03, 0x02, 0x00, 0x2A };
                var response = new byte[7 + responsePdu.Length];
                response[0] = header[0];
                response[1] = header[1];
                response[4] = 0;
                response[5] = (byte)(responsePdu.Length + 1);
                response[6] = header[6];
                responsePdu.CopyTo(response, 7);

                await stream.WriteAsync(response);
            });

            var options = new ModbusTcpOptions
            {
                Tcp = new TcpTransportOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    ConnectTimeout = TimeSpan.FromSeconds(2),
                },
                Timeout = TimeSpan.FromSeconds(2),
            };

            await using var client = new ModbusTcpClient(options);
            await client.ConnectAsync();

            var registers = await client.ReadHoldingRegistersAsync(0, 1);

            Assert.Equal(42, registers[0]);
            await server;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static ModbusTcpOptions CreateOptions() => new()
    {
        UnitId = 1,
        Timeout = TimeSpan.FromSeconds(1),
        RetryDelay = TimeSpan.Zero,
    };

    private static byte[] Mbap(ushort transactionId, byte unitId, params byte[] pdu)
    {
        var frame = new byte[7 + pdu.Length];
        frame[0] = (byte)(transactionId >> 8);
        frame[1] = (byte)transactionId;
        frame[2] = 0;
        frame[3] = 0;
        var length = (ushort)(pdu.Length + 1);
        frame[4] = (byte)(length >> 8);
        frame[5] = (byte)length;
        frame[6] = unitId;
        pdu.CopyTo(frame, 7);
        return frame;
    }
}
