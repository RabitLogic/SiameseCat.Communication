using SiameseCat.Core;

namespace SiameseCat.Tests.Core;

public class PlcValueTests
{
    [Fact]
    public void NumericConversions_AreChecked()
    {
        Assert.Equal(42, PlcValue.FromInt32(42).AsInt64());
        Assert.Equal((ushort)7, PlcValue.FromUInt16(7).AsUInt16());
        Assert.Equal(1.5, PlcValue.FromFloat(1.5f).AsDouble(), 6);
        Assert.True(PlcValue.FromBool(true).AsBool());
    }

    [Fact]
    public void InvalidConversions_Throw()
    {
        Assert.Throws<InvalidCastException>(() => PlcValue.FromString("x").AsDouble());
        Assert.Throws<InvalidCastException>(() => PlcValue.FromBytes(new byte[] { 1 }).AsBool());
        Assert.Throws<OverflowException>(() => PlcValue.FromInt32(1000).AsByte());
    }

    [Fact]
    public void Equality_ComparesTypeAndValue()
    {
        Assert.Equal(PlcValue.FromUInt16(5), PlcValue.FromUInt16(5));
        Assert.NotEqual(PlcValue.FromUInt16(5), PlcValue.FromInt16(5));
        Assert.Equal(PlcValue.FromBytes(new byte[] { 1, 2 }), PlcValue.FromBytes(new byte[] { 1, 2 }));
        Assert.NotEqual(PlcValue.FromBytes(new byte[] { 1, 2 }), PlcValue.FromBytes(new byte[] { 1, 3 }));
        Assert.Equal(PlcValue.Null, default);
    }

    [Fact]
    public void ToObject_ReturnsBoxedValue()
    {
        Assert.Equal((ushort)9, PlcValue.FromUInt16(9).ToObject());
        Assert.Equal("abc", PlcValue.FromString("abc").ToObject());
        Assert.Null(PlcValue.Null.ToObject());
    }

    [Fact]
    public void Quality_DefaultsToGoodAndIsMetadata()
    {
        var value = PlcValue.FromUInt16(5);
        Assert.Equal(PlcValueQuality.Good, value.Quality);
        Assert.True(value.IsGood);
        Assert.Null(value.Timestamp);

        var bad = value.WithQuality(PlcValueQuality.Bad);
        Assert.False(bad.IsGood);
        Assert.Equal((ushort)5, bad.AsUInt16());

        // Quality/timestamp are metadata: equality still compares payload only.
        Assert.Equal(value, bad);
    }

    [Fact]
    public void WithTimestamp_PreservesPayloadAndQuality()
    {
        var sampledAt = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
        var value = PlcValue.FromDouble(1.5)
            .WithQuality(PlcValueQuality.Uncertain)
            .WithTimestamp(sampledAt);

        Assert.Equal(sampledAt, value.Timestamp);
        Assert.Equal(PlcValueQuality.Uncertain, value.Quality);
        Assert.Equal(1.5, value.AsDouble(), 6);
    }

    [Fact]
    public void Arrays_RoundTripElements()
    {
        var value = PlcValue.FromUInt16Array(new ushort[] { 10, 20, 30 });

        Assert.True(value.IsArray);
        Assert.Equal(3, value.Length);
        Assert.Equal(PlcDataType.UInt16, value.DataType);
        Assert.Equal(new ushort[] { 10, 20, 30 }, value.AsUInt16Array());
        Assert.Equal("UInt16[3]", value.ToString());
    }

    [Fact]
    public void Arrays_CompareByElement()
    {
        Assert.Equal(
            PlcValue.FromUInt16Array(new ushort[] { 1, 2 }),
            PlcValue.FromUInt16Array(new ushort[] { 1, 2 }));
        Assert.NotEqual(
            PlcValue.FromUInt16Array(new ushort[] { 1, 2 }),
            PlcValue.FromUInt16Array(new ushort[] { 1, 3 }));
        Assert.NotEqual(
            PlcValue.FromUInt16Array(new ushort[] { 1 }),
            PlcValue.FromUInt16(1));
    }

    [Fact]
    public void Arrays_RejectScalarAccess()
    {
        var value = PlcValue.FromDoubleArray(new double[] { 1.5 });

        Assert.Throws<InvalidCastException>(() => value.AsDouble());
        Assert.Throws<InvalidCastException>(() => value.AsInt64());
        Assert.Throws<InvalidCastException>(() => value.AsUInt16Array());
    }

    [Fact]
    public void ArrayToObject_ReturnsClone()
    {
        var source = new[] { 1, 2 };
        var value = PlcValue.FromInt32Array(source);

        var array = Assert.IsType<int[]>(value.ToObject());
        array[0] = 99;

        Assert.Equal(source, value.AsInt32Array());
    }
}

public class PlcAddressTests
{
    [Fact]
    public void Constructor_AllowsNumericArrays()
    {
        var address = new PlcAddress("holding", 0, PlcDataType.UInt16, length: 10);

        Assert.Equal(10, address.Length);
        Assert.Equal(20, address.ByteSize);
    }

    [Fact]
    public void Constructor_RejectsBoolArrays()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlcAddress("coil", 0, PlcDataType.Bool, length: 2));
    }

    [Fact]
    public void Constructor_RejectsBitOffsetForNonBool()
    {
        Assert.Throws<ArgumentException>(
            () => new PlcAddress("holding", 0, PlcDataType.UInt16, bitOffset: 1));
    }

    [Fact]
    public void ByteSize_ReflectsTypeAndLength()
    {
        Assert.Equal(1, new PlcAddress("m", 0, PlcDataType.Bool).ByteSize);
        Assert.Equal(4, new PlcAddress("m", 0, PlcDataType.Float).ByteSize);
        Assert.Equal(8, new PlcAddress("m", 0, PlcDataType.Double).ByteSize);
        Assert.Equal(10, new PlcAddress("m", 0, PlcDataType.String, length: 10).ByteSize);
        Assert.Equal(6, new PlcAddress("m", 0, PlcDataType.Raw, length: 6).ByteSize);
    }

    [Fact]
    public void WithLength_PreservesOtherComponents()
    {
        var address = new PlcAddress("DB1", 4, PlcDataType.String).WithLength(12);

        Assert.Equal(12, address.Length);
        Assert.Equal("DB1", address.Region);
        Assert.Equal(4, address.Offset);
    }

    [Fact]
    public void FromSymbol_CreatesSymbolicAddress()
    {
        var address = PlcAddress.FromSymbol("ns=2", "s=Temperature", PlcDataType.Double);

        Assert.Equal("ns=2", address.Region);
        Assert.Equal("s=Temperature", address.Symbol);
        Assert.Equal(0, address.Offset);
        Assert.Equal("ns=2:s=Temperature[Double]", address.ToString());
    }

    [Fact]
    public void Symbol_IsPreservedByWithMethods()
    {
        var address = PlcAddress.FromSymbol("ns=2", "s=Speed", PlcDataType.Int16, length: 1);

        Assert.Equal("s=Speed", address.WithDataType(PlcDataType.Int32).Symbol);
        Assert.Equal("s=Speed", address.WithLength(1).Symbol);
        Assert.Equal("s=Speed", address.WithSymbol("s=Speed").Symbol);
    }

    [Fact]
    public void FromSymbol_RejectsEmptySymbol()
    {
        Assert.Throws<ArgumentException>(
            () => PlcAddress.FromSymbol("ns=2", " ", PlcDataType.Bool));
    }
}

public class PlcValueCodecTests
{
    [Fact]
    public void RoundTripsBigEndianValues()
    {
        Assert.Equal(PlcValue.FromUInt16(0x1234), PlcValueCodec.FromBytes(PlcDataType.UInt16, new byte[] { 0x12, 0x34 }));
        Assert.Equal(PlcValue.FromInt32(-2), PlcValueCodec.FromBytes(PlcDataType.Int32, new byte[] { 0xFF, 0xFF, 0xFF, 0xFE }));
        Assert.Equal(PlcValue.FromFloat(1.5f), PlcValueCodec.FromBytes(PlcDataType.Float, PlcValueCodec.ToBytes(PlcValue.FromFloat(1.5f))));
        Assert.Equal(PlcValue.FromDouble(Math.PI), PlcValueCodec.FromBytes(PlcDataType.Double, PlcValueCodec.ToBytes(PlcValue.FromDouble(Math.PI))));
    }

    [Fact]
    public void GetByteSize_MatchesDataType()
    {
        Assert.Equal(2, PlcValueCodec.GetByteSize(PlcDataType.Int16));
        Assert.Equal(8, PlcValueCodec.GetByteSize(PlcDataType.Double));
        Assert.Equal(5, PlcValueCodec.GetByteSize(PlcDataType.String, 5));
    }

    [Fact]
    public void StringEncoding_IsRejectedByCodec()
    {
        Assert.Throws<NotSupportedException>(() => PlcValueCodec.ToBytes(PlcValue.FromString("x")));
    }

    [Fact]
    public void RoundTripsArrays()
    {
        var words = PlcValue.FromUInt16Array(new ushort[] { 0x1234, 0x5678 });
        var wordBytes = PlcValueCodec.ToBytes(words);

        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, wordBytes);
        Assert.Equal(words, PlcValueCodec.FromBytes(PlcDataType.UInt16, wordBytes, count: 2));

        var reals = PlcValue.FromFloatArray(new[] { 1.5f, -2.5f });
        Assert.Equal(reals, PlcValueCodec.FromBytes(PlcDataType.Float, PlcValueCodec.ToBytes(reals), count: 2));
    }

    [Fact]
    public void ArrayDecoding_RejectsTooFewBytes()
    {
        Assert.Throws<ArgumentException>(
            () => PlcValueCodec.FromBytes(PlcDataType.UInt16, new byte[] { 0x00, 0x01 }, count: 2));
    }
}

public class TagTests
{
    [Fact]
    public void ToEngineering_AppliesScaleAndOffset()
    {
        var tag = new PlcTag("temperature", "40001", PlcDataType.Int16, scale: 0.1, offset: -40, unit: "°C");

        Assert.Equal(-30, tag.ToEngineering(100), 6);
        Assert.Equal(100, tag.ToRaw(-30), 6);
    }

    [Fact]
    public void Constructor_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() => new PlcTag("", "40001", PlcDataType.UInt16));
        Assert.Throws<ArgumentException>(() => new PlcTag("t", "", PlcDataType.UInt16));
    }
}
