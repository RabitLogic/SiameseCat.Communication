using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

using SiameseCat.Communication;
using SiameseCat.Communication.Transport;
using SiameseCat.Communication.Transport.Serial;
using SiameseCat.Communication.Transport.Tcp;
using SiameseCat.Communication.Transport.Udp;

namespace SiameseCat.Tests.Core;

public class TransportTests
{
    [Fact]
    public async Task TcpTransport_RoundTripsDataOverLoopback()
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
                var buffer = new byte[5];
                await stream.ReadExactlyAsync(buffer);
                await stream.WriteAsync(buffer);
            });

            await using var transport = new TcpTransport(new TcpTransportOptions
            {
                Host = "127.0.0.1",
                Port = port,
                ConnectTimeout = TimeSpan.FromSeconds(2),
            });

            await transport.OpenAsync();
            Assert.True(transport.IsOpen);

            await transport.WriteAsync(new byte[] { 1, 2, 3, 4, 5 });
            var response = await transport.ReadExactlyAsync(5);

            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, response);

            await transport.CloseAsync();
            Assert.False(transport.IsOpen);
            await server;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Dispose_IsIdempotent_AndOpenFailsAfterDispose()
    {
        await using var transport = new TcpTransport(new TcpTransportOptions
        {
            Host = "127.0.0.1",
            Port = 1,
        });

        await transport.DisposeAsync();
        await transport.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.OpenAsync());
    }

    [Fact]
    public void SerialPortTransport_RejectsEmptyPortName()
    {
        var options = new SerialPortOptions { PortName = string.Empty };

        Assert.Throws<ArgumentException>(() => new SerialPortTransport(options));
    }

    [Fact]
    public void SerialPortTransport_RejectsInvalidBaudRate()
    {
        var options = new SerialPortOptions { BaudRate = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(() => new SerialPortTransport(options));
    }

    [Fact]
    public void SerialPortTransport_AcceptsValidConfiguration()
    {
        var options = new SerialPortOptions
        {
            PortName = "COM_TEST",
            BaudRate = 115200,
            Parity = Parity.Even,
            StopBits = StopBits.Two,
        };

        var transport = new SerialPortTransport(options);

        Assert.False(transport.IsOpen);
    }

    [Fact]
    public async Task UdpTransport_RoundTripsDatagramOverLoopback()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serverTask = Task.Run(async () =>
        {
            var received = await server.ReceiveAsync();
            await server.SendAsync(received.Buffer.AsMemory(), received.RemoteEndPoint);
        });

        await using var transport = new UdpTransport(new UdpTransportOptions
        {
            Host = "127.0.0.1",
            Port = port,
            ConnectTimeout = TimeSpan.FromSeconds(2),
        });

        await transport.OpenAsync();
        Assert.True(transport.IsOpen);

        await transport.WriteAsync(new byte[] { 9, 8, 7 });
        var response = await transport.ReadExactlyAsync(3);

        Assert.Equal(new byte[] { 9, 8, 7 }, response);

        await transport.CloseAsync();
        Assert.False(transport.IsOpen);
        await serverTask;
    }

    [Fact]
    public void UdpOptions_RejectInvalidPort()
    {
        var options = new UdpTransportOptions { Port = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }
}
