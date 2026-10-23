using SiameseCat.Communication;
using SiameseCat.Communication.Exceptions;
using SiameseCat.Communication.Modbus;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Core;

public class MbapFrameCodecTests
{
    [Fact]
    public async Task Reader_ReturnsCompleteMbapFrame()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(0x12, 0x34, 0x00, 0x00, 0x00, 0x05, 0x01, 0x03, 0x02, 0x12, 0x34);

        var frame = await new MbapFrameCodec().CreateReader().ReadFrameAsync(transport);

        Assert.Equal(
            new byte[] { 0x12, 0x34, 0x00, 0x00, 0x00, 0x05, 0x01, 0x03, 0x02, 0x12, 0x34 },
            frame);
    }

    [Fact]
    public async Task Reader_ReassemblesFragmentedHeader()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(0x12);
        transport.EnqueueResponse(0x34, 0x00, 0x00);
        transport.EnqueueResponse(0x00, 0x02, 0x01, 0x83);

        var frame = await new MbapFrameCodec().CreateReader().ReadFrameAsync(transport);

        Assert.Equal(new byte[] { 0x12, 0x34, 0x00, 0x00, 0x00, 0x02, 0x01, 0x83 }, frame);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    public async Task Reader_RejectsInvalidLength(int length)
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(0x00, 0x01, 0x00, 0x00, 0x00, (byte)length);

        await Assert.ThrowsAsync<ProtocolException>(async () =>
            await new MbapFrameCodec().CreateReader().ReadFrameAsync(transport));
    }
}
