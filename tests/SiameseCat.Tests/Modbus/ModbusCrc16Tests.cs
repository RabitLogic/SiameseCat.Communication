using SiameseCat.Protocols.Modbus;

namespace SiameseCat.Tests.Modbus;

public class ModbusCrc16Tests
{
    [Fact]
    public void Compute_MatchesKnownVector()
    {
        var frame = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A };

        var crc = ModbusCrc16.Compute(frame);

        Assert.Equal(0xCDC5, crc);
    }

    [Fact]
    public void Write_AppendsLowByteFirst()
    {
        var frame = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00 };

        ModbusCrc16.Write(frame.AsSpan(0, 6), frame.AsSpan(6));

        Assert.Equal(0xC5, frame[6]);
        Assert.Equal(0xCD, frame[7]);
        Assert.True(ModbusCrc16.Validate(frame));
    }

    [Fact]
    public void Validate_ReturnsFalseForCorruptedFrame()
    {
        var frame = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A, 0xC5, 0x00 };

        Assert.False(ModbusCrc16.Validate(frame));
    }
}
