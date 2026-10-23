using System.Text;
using SiameseCat.Communication;
using SiameseCat.Communication.Server.Tcp;
using SiameseCat.Communication.Tcp;

namespace SiameseCat.Tests.Integration;

public class CustomTcpTests
{
    [Fact]
    public async Task ServerAndClient_ExchangeDelimitedFrames()
    {
        var codec = CreateLineCodec();
        await using var server = new CustomTcpServer(
            EchoAsync,
            new CustomTcpServerOptions { Codec = codec });

        await server.StartAsync();
        Assert.True(server.Port > 0);

        await using var client = new CustomTcpClient(new CustomTcpClientOptions
        {
            Tcp = { Host = "127.0.0.1", Port = server.Port },
            Codec = codec,
            Timeout = TimeSpan.FromSeconds(2),
        });

        await client.ConnectAsync();

        var first = await client.RequestAsync(Encoding.ASCII.GetBytes("hello"));
        var second = await client.RequestAsync(Encoding.ASCII.GetBytes("second"));

        Assert.Equal("HELLO", Encoding.ASCII.GetString(first));
        Assert.Equal("SECOND", Encoding.ASCII.GetString(second));
        Assert.True(server.Statistics.ConnectionsAccepted >= 1);
        Assert.True(server.Statistics.FramesReceived >= 2);
        Assert.True(server.Statistics.FramesSent >= 2);

        await client.DisconnectAsync();
        Assert.False(client.IsOpen);
    }

    [Fact]
    public async Task ServerAndClient_ExchangeLengthPrefixedBinaryFrames()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions
        {
            Mode = TcpFrameMode.LengthPrefix,
            PrefixLength = 4,
            MaxFrameLength = 1024,
        });

        await using var server = new CustomTcpServer(
            async (connection, token) =>
            {
                while (true)
                {
                    var frame = await connection.ReadFrameAsync(token);
                    if (frame is null)
                    {
                        return;
                    }

                    var reversed = (byte[])frame.Clone();
                    Array.Reverse(reversed);
                    await connection.SendAsync(reversed, token);
                }
            },
            new CustomTcpServerOptions { Codec = codec });

        await server.StartAsync();

        await using var client = new CustomTcpClient(new CustomTcpClientOptions
        {
            Tcp = { Host = "127.0.0.1", Port = server.Port },
            Codec = codec,
            Timeout = TimeSpan.FromSeconds(2),
        });

        await client.ConnectAsync();

        var response = await client.RequestAsync(new byte[] { 1, 2, 3, 4 });

        Assert.Equal(new byte[] { 4, 3, 2, 1 }, response);
    }

    [Fact]
    public async Task Client_ClosesConnectionWhenRequestTimesOut()
    {
        var transport = new Fakes.MockTransport { BlockReads = true };
        await using var client = new CustomTcpClient(transport, new CustomTcpClientOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50),
            Framing = new TcpFrameOptions { Mode = TcpFrameMode.Raw },
        });

        await client.ConnectAsync();

        await Assert.ThrowsAsync<CommunicationTimeoutException>(() => client.RequestAsync(new byte[] { 1 }));

        Assert.False(client.IsOpen);
        Assert.True(transport.CloseCount >= 1);
    }

    [Fact]
    public void ServerOptions_RejectNegativeMaxConnections()
    {
        var options = new CustomTcpServerOptions { MaxConnections = -1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    private static TcpFrameCodec CreateLineCodec() =>
        new(new TcpFrameOptions { Mode = TcpFrameMode.Delimiter, MaxFrameLength = 4096 });

    private static async Task EchoAsync(CustomTcpConnection connection, CancellationToken cancellationToken)
    {
        while (true)
        {
            var frame = await connection.ReadFrameAsync(cancellationToken);
            if (frame is null)
            {
                return;
            }

            var reply = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(frame).ToUpperInvariant());
            await connection.SendAsync(reply, cancellationToken);
        }
    }
}
