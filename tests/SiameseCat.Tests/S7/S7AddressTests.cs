using SiameseCat.Communication.S7;

namespace SiameseCat.Tests.S7;

public class S7AddressTests
{
    [Theory]
    [InlineData("DB1.DBX0.0", S7Area.DataBlock, 1, 0, S7DataType.Bool, 0)]
    [InlineData("DB1.DBX12.7", S7Area.DataBlock, 1, 12, S7DataType.Bool, 7)]
    [InlineData("DB5.DBB10", S7Area.DataBlock, 5, 10, S7DataType.Byte, 0)]
    [InlineData("DB5.DBW12", S7Area.DataBlock, 5, 12, S7DataType.Word, 0)]
    [InlineData("DB5.DBD14", S7Area.DataBlock, 5, 14, S7DataType.DWord, 0)]
    [InlineData("M0.0", S7Area.Merkers, 0, 0, S7DataType.Bool, 0)]
    [InlineData("MB4", S7Area.Merkers, 0, 4, S7DataType.Byte, 0)]
    [InlineData("MW6", S7Area.Merkers, 0, 6, S7DataType.Word, 0)]
    [InlineData("MD8", S7Area.Merkers, 0, 8, S7DataType.DWord, 0)]
    [InlineData("I0.1", S7Area.Inputs, 0, 0, S7DataType.Bool, 1)]
    [InlineData("IB2", S7Area.Inputs, 0, 2, S7DataType.Byte, 0)]
    [InlineData("Q0.2", S7Area.Outputs, 0, 0, S7DataType.Bool, 2)]
    [InlineData("QW4", S7Area.Outputs, 0, 4, S7DataType.Word, 0)]
    [InlineData("E0.0", S7Area.Inputs, 0, 0, S7DataType.Bool, 0)]
    [InlineData("A0.0", S7Area.Outputs, 0, 0, S7DataType.Bool, 0)]
    public void Parse_ReturnsExpectedAddress(
        string text,
        S7Area area,
        int dbNumber,
        int byteOffset,
        S7DataType dataType,
        byte bitOffset)
    {
        var address = S7Address.Parse(text);

        Assert.Equal(area, address.Area);
        Assert.Equal(dbNumber, address.DbNumber);
        Assert.Equal(byteOffset, address.ByteOffset);
        Assert.Equal(dataType, address.DataType);
        Assert.Equal(bitOffset, address.BitOffset);
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        var address = S7Address.Parse("db3.dbw20");

        Assert.Equal(S7Area.DataBlock, address.Area);
        Assert.Equal(3, address.DbNumber);
        Assert.Equal(20, address.ByteOffset);
        Assert.Equal(S7DataType.Word, address.DataType);
    }

    [Fact]
    public void BitAddress_CombinesByteAndBitOffsets()
    {
        var address = S7Address.Parse("DB1.DBX2.3");

        Assert.Equal(19, address.BitAddress);
    }

    [Theory]
    [InlineData("DB1.DBW0.0")]
    [InlineData("XYZ")]
    [InlineData("M0")]
    [InlineData("")]
    public void Parse_RejectsInvalidText(string text)
    {
        Assert.ThrowsAny<Exception>(() => S7Address.Parse(text));
    }

    [Fact]
    public void Parse_RejectsInvalidBitOffset()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => S7Address.Parse("M0.8"));
    }

    [Theory]
    [InlineData("DB1.DBX0.0")]
    [InlineData("DB1.DBW4")]
    [InlineData("M0.1")]
    [InlineData("MW2")]
    [InlineData("I0.0")]
    [InlineData("QD8")]
    public void ToString_RoundTrips(string text)
    {
        Assert.Equal(text, S7Address.Parse(text).ToString());
    }

    [Fact]
    public void TryParse_ReturnsFalseForInvalidInput()
    {
        Assert.False(S7Address.TryParse(null, out _));
        Assert.False(S7Address.TryParse(" ", out _));
        Assert.False(S7Address.TryParse("DB1.DBX", out _));
        Assert.False(S7Address.TryParse("M0.8", out _));
    }

    [Fact]
    public void TryParse_ReturnsTrueForValidInput()
    {
        Assert.True(S7Address.TryParse("DB1.DBW2", out var address));
        Assert.Equal(S7Area.DataBlock, address.Area);
        Assert.Equal(2, address.ByteOffset);
    }

    [Fact]
    public void Equality_ComparesAllComponents()
    {
        var left = S7Address.Parse("DB1.DBX2.3");
        var right = S7Address.Parse("db1.dbx2.3");

        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, S7Address.Parse("DB1.DBX2.4"));
        Assert.NotEqual(left, S7Address.Parse("DB2.DBX2.3"));
        Assert.NotEqual(left, S7Address.Parse("DB1.DBB2"));
    }

    [Fact]
    public void Equality_WorksAsDictionaryKey()
    {
        var map = new Dictionary<S7Address, string>
        {
            [S7Address.Parse("DB1.DBW0")] = "speed",
        };

        Assert.Equal("speed", map[S7Address.Parse("DB1.DBW0")]);
    }
}
