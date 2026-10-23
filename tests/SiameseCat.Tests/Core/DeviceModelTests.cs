using SiameseCat.Communication;

namespace SiameseCat.Tests.Core;

public class DeviceValueTests
{
    [Fact]
    public void NumericConversions_AreChecked()
    {
        Assert.Equal(42, DeviceValue.FromInt32(42).AsInt64());
        Assert.Equal((ushort)7, DeviceValue.FromUInt16(7).AsUInt16());
        Assert.Equal(1.5, DeviceValue.FromFloat(1.5f).AsDouble(), 6);
        Assert.True(DeviceValue.FromBool(true).AsBool());
    }

    [Fact]
    public void InvalidConversions_Throw()
    {
        Assert.Throws<InvalidCastException>(() => DeviceValue.FromString("x").AsDouble());
        Assert.Throws<InvalidCastException>(() => DeviceValue.FromBytes(new byte[] { 1 }).AsBool());
        Assert.Throws<OverflowException>(() => DeviceValue.FromInt32(1000).AsByte());
    }

    [Fact]
    public void Equality_ComparesTypeAndValue()
    {
        Assert.Equal(DeviceValue.FromUInt16(5), DeviceValue.FromUInt16(5));
        Assert.NotEqual(DeviceValue.FromUInt16(5), DeviceValue.FromInt16(5));
        Assert.Equal(DeviceValue.FromBytes(new byte[] { 1, 2 }), DeviceValue.FromBytes(new byte[] { 1, 2 }));
        Assert.NotEqual(DeviceValue.FromBytes(new byte[] { 1, 2 }), DeviceValue.FromBytes(new byte[] { 1, 3 }));
        Assert.Equal(DeviceValue.Null, default);
    }

    [Fact]
    public void ToObject_ReturnsBoxedValue()
    {
        Assert.Equal((ushort)9, DeviceValue.FromUInt16(9).ToObject());
        Assert.Equal("abc", DeviceValue.FromString("abc").ToObject());
        Assert.Null(DeviceValue.Null.ToObject());
    }

    [Fact]
    public void Quality_DefaultsToGoodAndIsMetadata()
    {
        var value = DeviceValue.FromUInt16(5);
        Assert.Equal(DeviceValueQuality.Good, value.Quality);
        Assert.True(value.IsGood);
        Assert.Null(value.Timestamp);

        var bad = value.WithQuality(DeviceValueQuality.Bad);
        Assert.False(bad.IsGood);
        Assert.Equal((ushort)5, bad.AsUInt16());

        // Quality/timestamp are metadata: equality still compares payload only.
        Assert.Equal(value, bad);
    }

    [Fact]
    public void WithTimestamp_PreservesPayloadAndQuality()
    {
        var sampledAt = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
        var value = DeviceValue.FromDouble(1.5)
            .WithQuality(DeviceValueQuality.Uncertain)
            .WithTimestamp(sampledAt);

        Assert.Equal(sampledAt, value.Timestamp);
        Assert.Equal(DeviceValueQuality.Uncertain, value.Quality);
        Assert.Equal(1.5, value.AsDouble(), 6);
    }

    [Fact]
    public void Arrays_RoundTripElements()
    {
        var value = DeviceValue.FromUInt16Array(new ushort[] { 10, 20, 30 });

        Assert.True(value.IsArray);
        Assert.Equal(3, value.Length);
        Assert.Equal(DeviceDataType.UInt16, value.DataType);
        Assert.Equal(new ushort[] { 10, 20, 30 }, value.AsUInt16Array());
        Assert.Equal("UInt16[3]", value.ToString());
    }

    [Fact]
    public void Arrays_CompareByElement()
    {
        Assert.Equal(
            DeviceValue.FromUInt16Array(new ushort[] { 1, 2 }),
            DeviceValue.FromUInt16Array(new ushort[] { 1, 2 }));
        Assert.NotEqual(
            DeviceValue.FromUInt16Array(new ushort[] { 1, 2 }),
            DeviceValue.FromUInt16Array(new ushort[] { 1, 3 }));
        Assert.NotEqual(
            DeviceValue.FromUInt16Array(new ushort[] { 1 }),
            DeviceValue.FromUInt16(1));
    }

    [Fact]
    public void Arrays_RejectScalarAccess()
    {
        var value = DeviceValue.FromDoubleArray(new double[] { 1.5 });

        Assert.Throws<InvalidCastException>(() => value.AsDouble());
        Assert.Throws<InvalidCastException>(() => value.AsInt64());
        Assert.Throws<InvalidCastException>(() => value.AsUInt16Array());
    }

    [Fact]
    public void ArrayToObject_ReturnsClone()
    {
        var source = new[] { 1, 2 };
        var value = DeviceValue.FromInt32Array(source);

        var array = Assert.IsType<int[]>(value.ToObject());
        array[0] = 99;

        Assert.Equal(source, value.AsInt32Array());
    }
}

public class DeviceAddressTests
{
    [Fact]
    public void Constructor_AllowsNumericArrays()
    {
        var address = new DeviceAddress("holding", 0, DeviceDataType.UInt16, length: 10);

        Assert.Equal(10, address.Length);
        Assert.Equal(20, address.ByteSize);
    }

    [Fact]
    public void Constructor_RejectsBoolArrays()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DeviceAddress("coil", 0, DeviceDataType.Bool, length: 2));
    }

    [Fact]
    public void Constructor_RejectsBitOffsetForNonBool()
    {
        Assert.Throws<ArgumentException>(
            () => new DeviceAddress("holding", 0, DeviceDataType.UInt16, bitOffset: 1));
    }

    [Fact]
    public void ByteSize_ReflectsTypeAndLength()
    {
        Assert.Equal(1, new DeviceAddress("m", 0, DeviceDataType.Bool).ByteSize);
        Assert.Equal(4, new DeviceAddress("m", 0, DeviceDataType.Float).ByteSize);
        Assert.Equal(8, new DeviceAddress("m", 0, DeviceDataType.Double).ByteSize);
        Assert.Equal(10, new DeviceAddress("m", 0, DeviceDataType.String, length: 10).ByteSize);
        Assert.Equal(6, new DeviceAddress("m", 0, DeviceDataType.Raw, length: 6).ByteSize);
    }

    [Fact]
    public void WithLength_PreservesOtherComponents()
    {
        var address = new DeviceAddress("DB1", 4, DeviceDataType.String).WithLength(12);

        Assert.Equal(12, address.Length);
        Assert.Equal("DB1", address.Region);
        Assert.Equal(4, address.Offset);
    }

    [Fact]
    public void FromSymbol_CreatesSymbolicAddress()
    {
        var address = DeviceAddress.FromSymbol("ns=2", "s=Temperature", DeviceDataType.Double);

        Assert.Equal("ns=2", address.Region);
        Assert.Equal("s=Temperature", address.Symbol);
        Assert.Equal(0, address.Offset);
        Assert.Equal("ns=2:s=Temperature[Double]", address.ToString());
    }

    [Fact]
    public void Symbol_IsPreservedByWithMethods()
    {
        var address = DeviceAddress.FromSymbol("ns=2", "s=Speed", DeviceDataType.Int16, length: 1);

        Assert.Equal("s=Speed", address.WithDataType(DeviceDataType.Int32).Symbol);
        Assert.Equal("s=Speed", address.WithLength(1).Symbol);
        Assert.Equal("s=Speed", address.WithSymbol("s=Speed").Symbol);
    }

    [Fact]
    public void FromSymbol_RejectsEmptySymbol()
    {
        Assert.Throws<ArgumentException>(
            () => DeviceAddress.FromSymbol("ns=2", " ", DeviceDataType.Bool));
    }
}

public class DeviceValueCodecTests
{
    [Fact]
    public void RoundTripsBigEndianValues()
    {
        Assert.Equal(DeviceValue.FromUInt16(0x1234), DeviceValueCodec.FromBytes(DeviceDataType.UInt16, new byte[] { 0x12, 0x34 }));
        Assert.Equal(DeviceValue.FromInt32(-2), DeviceValueCodec.FromBytes(DeviceDataType.Int32, new byte[] { 0xFF, 0xFF, 0xFF, 0xFE }));
        Assert.Equal(DeviceValue.FromFloat(1.5f), DeviceValueCodec.FromBytes(DeviceDataType.Float, DeviceValueCodec.ToBytes(DeviceValue.FromFloat(1.5f))));
        Assert.Equal(DeviceValue.FromDouble(Math.PI), DeviceValueCodec.FromBytes(DeviceDataType.Double, DeviceValueCodec.ToBytes(DeviceValue.FromDouble(Math.PI))));
    }

    [Fact]
    public void GetByteSize_MatchesDataType()
    {
        Assert.Equal(2, DeviceValueCodec.GetByteSize(DeviceDataType.Int16));
        Assert.Equal(8, DeviceValueCodec.GetByteSize(DeviceDataType.Double));
        Assert.Equal(5, DeviceValueCodec.GetByteSize(DeviceDataType.String, 5));
    }

    [Fact]
    public void StringEncoding_IsRejectedByCodec()
    {
        Assert.Throws<NotSupportedException>(() => DeviceValueCodec.ToBytes(DeviceValue.FromString("x")));
    }

    [Fact]
    public void RoundTripsArrays()
    {
        var words = DeviceValue.FromUInt16Array(new ushort[] { 0x1234, 0x5678 });
        var wordBytes = DeviceValueCodec.ToBytes(words);

        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, wordBytes);
        Assert.Equal(words, DeviceValueCodec.FromBytes(DeviceDataType.UInt16, wordBytes, count: 2));

        var reals = DeviceValue.FromFloatArray(new[] { 1.5f, -2.5f });
        Assert.Equal(reals, DeviceValueCodec.FromBytes(DeviceDataType.Float, DeviceValueCodec.ToBytes(reals), count: 2));
    }

    [Fact]
    public void ArrayDecoding_RejectsTooFewBytes()
    {
        Assert.Throws<ArgumentException>(
            () => DeviceValueCodec.FromBytes(DeviceDataType.UInt16, new byte[] { 0x00, 0x01 }, count: 2));
    }
}

public class TagTests
{
    [Fact]
    public void ToEngineering_AppliesScaleAndOffset()
    {
        var tag = new DeviceTag("temperature", "40001", DeviceDataType.Int16, scale: 0.1, offset: -40, unit: "°C");

        Assert.Equal(-30, tag.ToEngineering(100), 6);
        Assert.Equal(100, tag.ToRaw(-30), 6);
    }

    [Fact]
    public void Constructor_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() => new DeviceTag("", "40001", DeviceDataType.UInt16));
        Assert.Throws<ArgumentException>(() => new DeviceTag("t", "", DeviceDataType.UInt16));
    }
}
