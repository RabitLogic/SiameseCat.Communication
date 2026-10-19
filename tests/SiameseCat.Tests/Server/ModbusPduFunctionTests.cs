using System.Net.Sockets;

using SiameseCat.Communication.Server.Modbus;
using SiameseCat.Communication.Server.S7;

namespace SiameseCat.Tests.Server;

public class ModbusPduFunctionTests
{
    [Fact]
    public async Task ReadExceptionStatus_ReturnsConfiguredByte()
    {
        await using var server = await StartServerAsync();
        server.Data.ExceptionStatus = 0x5A;

        var response = await SendRawPduAsync(server, 1, new byte[] { 0x07 });

        Assert.Equal(new byte[] { 0x07, 0x5A }, response);
    }

    [Fact]
    public async Task GetCommEventCounter_ReportsCompletedWrites()
    {
        await using var server = await StartServerAsync();

        var empty = await SendRawPduAsync(server, 1, new byte[] { 0x0B });
        Assert.Equal(new byte[] { 0x0B, 0x00, 0x00, 0x00, 0x00 }, empty);

        await SendRawPduAsync(server, 1, new byte[] { 0x06, 0x00, 0x01, 0x12, 0x34 });
        await SendRawPduAsync(server, 1, new byte[] { 0x05, 0x00, 0x02, 0xFF, 0x00 });

        var response = await SendRawPduAsync(server, 1, new byte[] { 0x0B });

        Assert.Equal(new byte[] { 0x0B, 0x00, 0x00, 0x00, 0x02 }, response);
        Assert.Equal(2, server.Data.EventCount);
    }

    [Fact]
    public async Task ReportServerId_ReturnsIdentifierAndRunIndicator()
    {
        await using var server = await StartServerAsync(o =>
        {
            o.ServerIdentifier = "SC-1";
            o.RunIndicator = false;
        });

        var response = await SendRawPduAsync(server, 1, new byte[] { 0x11 });
        var expected = new byte[] { 0x11, 0x05, (byte)'S', (byte)'C', (byte)'-', (byte)'1', 0x00 };

        Assert.Equal(expected, response);
    }

    [Fact]
    public async Task MaskWriteRegister_AppliesAndAndOrMasks()
    {
        await using var server = await StartServerAsync();
        server.Data.TryWriteHoldingRegisters(0, new ushort[] { 0x0012 });

        var request = new byte[] { 0x16, 0x00, 0x00, 0x00, 0xF2, 0x00, 0x25 };
        var response = await SendRawPduAsync(server, 1, request);

        Assert.Equal(request, response);
        Assert.Equal((ushort)0x0017, server.Data[0]);
    }

    [Fact]
    public async Task MaskWriteRegister_OutsideBufferReturnsIllegalAddress()
    {
        await using var server = await StartServerAsync();

        var response = await SendRawPduAsync(server, 1, new byte[] { 0x16, 0xFF, 0xFF, 0x00, 0xF2, 0x00, 0x25 });

        Assert.Equal(new byte[] { 0x96, 0x02 }, response);
    }

    [Fact]
    public async Task UnknownFunction_ReturnsIllegalFunctionAndCountsException()
    {
        await using var server = await StartServerAsync();

        var response = await SendRawPduAsync(server, 1, new byte[] { 0x41 });

        Assert.Equal(new byte[] { 0xC1, 0x01 }, response);
        Assert.Equal(1, server.Statistics.GetExceptions(0x41));
        Assert.Equal(1, server.Statistics.TotalExceptions);
    }

    [Fact]
    public async Task Statistics_TrackRequestsPerFunctionCode()
    {
        await using var server = await StartServerAsync();
        server.Data.TryWriteHoldingRegisters(0, new ushort[] { 1, 2 });

        await SendRawPduAsync(server, 1, new byte[] { 0x03, 0x00, 0x00, 0x00, 0x02 });
        await SendRawPduAsync(server, 1, new byte[] { 0x04, 0x00, 0x00, 0x00, 0x01 });
        await SendRawPduAsync(server, 1, new byte[] { 0x11 });

        Assert.Equal(3, server.Statistics.TotalRequests);
        Assert.Equal(1, server.Statistics.GetRequests(0x03));
        Assert.Equal(1, server.Statistics.GetRequests(0x04));
        Assert.Equal(1, server.Statistics.GetRequests(0x11));
        Assert.Equal(3, server.Statistics.FramesReceived);
        Assert.Equal(0, server.Statistics.TotalExceptions);
    }

    [Fact]
    public async Task InvalidOptions_AreRejected()
    {
        var options = new ModbusServerOptions { ServerIdentifier = string.Empty };
        Assert.Throws<ArgumentException>(options.Validate);

        options = new ModbusServerOptions { ServerIdentifier = "中文标识" };
        Assert.Throws<ArgumentException>(options.Validate);
    }

    private static async Task<ModbusTcpServer> StartServerAsync(Action<ModbusServerOptions>? configure = null)
    {
        var options = new ModbusServerOptions { Port = 0 };
        configure?.Invoke(options);
        var server = new ModbusTcpServer(options);
        await server.StartAsync();
        return server;
    }

    private static async Task<byte[]> SendRawPduAsync(ModbusTcpServer server, byte unitId, byte[] pdu)
    {
        using var socket = new TcpClient();
        await socket.ConnectAsync("127.0.0.1", server.Port);
        var stream = socket.GetStream();

        var frame = new byte[7 + pdu.Length];
        frame[1] = 0x01;
        frame[4] = (byte)((pdu.Length + 1) >> 8);
        frame[5] = (byte)(pdu.Length + 1);
        frame[6] = unitId;
        pdu.CopyTo(frame, 7);

        await stream.WriteAsync(frame);

        var header = new byte[7];
        await ReadExactlyAsync(stream, header);
        var length = (header[4] << 8) | header[5];
        var response = new byte[length - 1];
        await ReadExactlyAsync(stream, response);
        return response;
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
            if (read <= 0)
            {
                throw new IOException("The server closed the connection.");
            }

            offset += read;
        }
    }
}
