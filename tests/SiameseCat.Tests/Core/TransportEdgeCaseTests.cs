using System.Net;
using System.Net.Sockets;
using SiameseCat.Core;
using SiameseCat.Transport;

namespace SiameseCat.Tests.Core;

/// <summary>Edge cases of the socket transports: reopening, closed links and disconnect detection.</summary>
public class TransportEdgeCaseTests
{
    [Fact]
    public async Task TcpTransport_OpenIsIdempotent()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var accepted = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                return client;
            });

            await using var transport = new TcpTransport(new TcpTransportOptions { Host = "127.0.0.1", Port = port });
            await transport.OpenAsync();
            await transport.OpenAsync();

            using var server = await accepted;
            Assert.True(transport.IsOpen);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TcpTransport_ClosedByPeer_IsReportedOnRead()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                await Task.Delay(50);
            });

            await using var transport = new TcpTransport(new TcpTransportOptions { Host = "127.0.0.1", Port = port });
            await transport.OpenAsync();
            await server;

            var buffer = new byte[4];
            var read = await transport.ReadAsync(buffer);

            Assert.Equal(0, read);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TcpTransport_WriteAfterClose_Throws()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var accepted = Task.Run(() => listener.AcceptTcpClientAsync());

            await using var transport = new TcpTransport(new TcpTransportOptions { Host = "127.0.0.1", Port = port });
            await transport.OpenAsync();
            using var server = await accepted;
            await transport.CloseAsync();

            await Assert.ThrowsAnyAsync<Exception>(async () => await transport.WriteAsync(new byte[] { 1 }));
            Assert.False(transport.IsOpen);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task UdpTransport_OpenIsIdempotent_AndReadAfterCloseReturnsZeroOrThrows()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        await using var transport = new UdpTransport(new UdpTransportOptions { Host = "127.0.0.1", Port = port });
        await transport.OpenAsync();
        await transport.OpenAsync();

        Assert.True(transport.IsOpen);

        await transport.CloseAsync();
        Assert.False(transport.IsOpen);

        await Assert.ThrowsAnyAsync<Exception>(async () => await transport.WriteAsync(new byte[] { 1 }));
    }

    [Fact]
    public async Task UdpTransport_RoundTripsConsecutiveDatagrams()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var echo = Task.Run(async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                var received = await server.ReceiveAsync();
                await server.SendAsync(received.Buffer.AsMemory(), received.RemoteEndPoint);
            }
        });

        await using var transport = new UdpTransport(new UdpTransportOptions { Host = "127.0.0.1", Port = port });
        await transport.OpenAsync();

        for (var i = 0; i < 3; i++)
        {
            await transport.WriteAsync(new[] { (byte)i });
            var response = await transport.ReadExactlyAsync(1);
            Assert.Equal(new[] { (byte)i }, response);
        }

        await echo;
    }
}
