using SiameseCat.Communication;
using SiameseCat.Communication.Exceptions;
using SiameseCat.Communication.Transport;
using SiameseCat.Communication.Transport.Tcp;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Core;

public class TcpFrameCodecTests
{
    [Fact]
    public void LengthPrefix_Encode_WritesBigEndianLength()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions
        {
            Mode = TcpFrameMode.LengthPrefix,
            PrefixLength = 2,
        });

        Assert.Equal(new byte[] { 0x00, 0x03, 0x01, 0x02, 0x03 }, codec.Encode(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void LengthPrefix_Encode_CountsItsOwnBytesWhenConfigured()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions
        {
            Mode = TcpFrameMode.LengthPrefix,
            PrefixLength = 1,
            LengthIncludesPrefix = true,
        });

        Assert.Equal(new byte[] { 0x03, 0x0A, 0x0B }, codec.Encode(new byte[] { 0x0A, 0x0B }));
    }

    [Fact]
    public async Task LengthPrefix_Reader_ReassemblesFragmentedFrame()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions
        {
            Mode = TcpFrameMode.LengthPrefix,
            PrefixLength = 2,
        });
        var transport = new MockTransport();
        transport.EnqueueResponse(0x00);
        transport.EnqueueResponse(0x03, 0x01);
        transport.EnqueueResponse(0x02, 0x03);

        var frame = await codec.CreateReader().ReadFrameAsync(transport);

        Assert.Equal(new byte[] { 1, 2, 3 }, frame);
    }

    [Fact]
    public async Task LengthPrefix_Reader_RejectsFrameAboveLimit()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions
        {
            Mode = TcpFrameMode.LengthPrefix,
            PrefixLength = 2,
            MaxFrameLength = 8,
        });
        var transport = new MockTransport();
        transport.EnqueueResponse(0x00, 0x09);

        await Assert.ThrowsAsync<ProtocolException>(async () =>
            await codec.CreateReader().ReadFrameAsync(transport));
    }

    [Fact]
    public async Task Delimiter_Reader_HandlesDelimiterSplitAcrossReads()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions { Mode = TcpFrameMode.Delimiter });
        var transport = new MockTransport();
        transport.EnqueueResponse((byte)'O', (byte)'K', (byte)'\r');
        transport.EnqueueResponse((byte)'\n');

        var frame = await codec.CreateReader().ReadFrameAsync(transport);

        Assert.Equal("OK", System.Text.Encoding.ASCII.GetString(frame!));
    }

    [Fact]
    public async Task Delimiter_Reader_KeepsDelimiterWhenConfigured()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions
        {
            Mode = TcpFrameMode.Delimiter,
            IncludeDelimiter = true,
        });
        var transport = new MockTransport();
        transport.EnqueueResponse("OK\r\n"u8.ToArray());

        var frame = await codec.CreateReader().ReadFrameAsync(transport);

        Assert.Equal("OK\r\n", System.Text.Encoding.ASCII.GetString(frame!));
    }

    [Fact]
    public async Task Delimiter_Reader_RejectsFrameAboveLimit()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions
        {
            Mode = TcpFrameMode.Delimiter,
            MaxFrameLength = 4,
            IncludeDelimiter = true,
        });
        var transport = new MockTransport();
        transport.EnqueueResponse("ABCDEF"u8.ToArray());

        await Assert.ThrowsAsync<ProtocolException>(async () =>
            await codec.CreateReader().ReadFrameAsync(transport));
    }

    [Fact]
    public async Task Delimiter_Reader_ThrowsWhenConnectionClosesInsideFrame()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions { Mode = TcpFrameMode.Delimiter });
        var transport = new MockTransport();
        transport.EnqueueResponse("ABC"u8.ToArray());

        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
            await codec.CreateReader().ReadFrameAsync(transport));
    }

    [Fact]
    public async Task FixedLength_Reader_ReadsExactlyOneFrame()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions
        {
            Mode = TcpFrameMode.FixedLength,
            FixedLength = 4,
        });
        var transport = new MockTransport();
        transport.EnqueueResponse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var reader = codec.CreateReader();

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await reader.ReadFrameAsync(transport));
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, await reader.ReadFrameAsync(transport));
    }

    [Fact]
    public void FixedLength_Encode_RejectsWrongPayloadSize()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions
        {
            Mode = TcpFrameMode.FixedLength,
            FixedLength = 4,
        });

        Assert.Throws<ArgumentException>(() => codec.Encode(new byte[] { 1, 2 }));
    }

    [Fact]
    public async Task Raw_Reader_ReturnsWhatTheTransportDelivered()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions { Mode = TcpFrameMode.Raw });
        var transport = new MockTransport();
        transport.EnqueueResponse(new byte[] { 1, 2 });
        transport.EnqueueResponse(new byte[] { 3 });

        var reader = codec.CreateReader();

        Assert.Equal(new byte[] { 1, 2 }, await reader.ReadFrameAsync(transport));
        Assert.Equal(new byte[] { 3 }, await reader.ReadFrameAsync(transport));
    }

    [Fact]
    public async Task Reader_ReturnsNullWhenPeerClosesBeforeNextFrame()
    {
        var codec = new TcpFrameCodec(new TcpFrameOptions
        {
            Mode = TcpFrameMode.LengthPrefix,
            PrefixLength = 2,
        });

        Assert.Null(await codec.CreateReader().ReadFrameAsync(new EofTransport()));
    }

    [Theory]
    [InlineData(TcpFrameMode.LengthPrefix, 3)]
    [InlineData(TcpFrameMode.FixedLength, 0)]
    public void Validate_RejectsInvalidOptions(TcpFrameMode mode, int value)
    {
        var options = new TcpFrameOptions { Mode = mode };

        if (mode == TcpFrameMode.LengthPrefix)
        {
            options.PrefixLength = value;
        }
        else
        {
            options.FixedLength = value;
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    private sealed class EofTransport : ITransport
    {
        public bool IsOpen => true;

        public Task OpenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CloseAsync() => Task.CompletedTask;

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(0);

        public void DiscardInput()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
