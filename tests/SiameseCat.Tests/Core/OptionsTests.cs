using SiameseCat.Communication;
using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.S7;
using SiameseCat.Communication.Siemens;
using SiameseCat.Communication.Transport;

namespace SiameseCat.Tests.Core;

public class OptionsTests
{
    [Fact]
    public void ModbusTcpOptions_RejectsInvalidPort()
    {
        var options = new ModbusTcpOptions { Tcp = { Port = 0 } };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void ModbusRtuOptions_RejectsInvalidBaudRate()
    {
        var options = new ModbusRtuOptions { SerialPort = { BaudRate = 0 } };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void ModbusRtuOptions_RejectsNegativeInterFrameDelay()
    {
        var options = new ModbusRtuOptions { InterFrameDelay = TimeSpan.FromMilliseconds(-1) };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void S7Options_RejectsInvalidSlot()
    {
        var options = new S7Options { Slot = 32 };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void TcpOptions_RejectInvalidKeepAliveInterval()
    {
        var options = new TcpTransportOptions { KeepAliveInterval = TimeSpan.Zero };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void S7WriteItem_BoolRequiresSingleBitValue()
    {
        Assert.Throws<ArgumentException>(
            () => new S7WriteItem(S7Address.Parse("M0.0"), new byte[] { 0x00, 0x00 }));

        Assert.Throws<ArgumentException>(
            () => new S7WriteItem(S7Address.Parse("M0.0"), new byte[] { 0x02 }));
    }

    [Fact]
    public void RetryPolicy_TreatsProtocolErrorsAsPermanent()
    {
        Assert.False(RetryPolicy.IsTransient(new ProtocolException("bad frame")));
        Assert.False(RetryPolicy.IsTransient(new ModbusException(0x02)));
        Assert.False(RetryPolicy.IsTransient(new S7ItemException(0x0A)));
        Assert.True(RetryPolicy.IsTransient(new CommunicationTimeoutException("timeout")));
        Assert.True(RetryPolicy.IsTransient(new ConnectionClosedException("closed")));
    }

    [Fact]
    public void ModbusOptions_AllowBroadcastUnitId()
    {
        var options = new ModbusTcpOptions { UnitId = 0 };

        options.Validate();
        Assert.Equal(0, options.UnitId);
    }

    [Fact]
    public void ModbusClient_AllowsBroadcastDefaultUnitId()
    {
        var transport = new Fakes.MockTransport();
        var client = new ModbusRtuClient(transport, new ModbusRtuOptions());

        client.DefaultUnitId = 0;
        Assert.Equal(0, client.DefaultUnitId);

        client.DefaultUnitId = 7;
        Assert.Equal(7, client.DefaultUnitId);
    }

    [Fact]
    public async Task ModbusClient_RejectsBroadcastUnitIdOnRequest()
    {
        var transport = new Fakes.MockTransport();
        await using var client = new ModbusRtuClient(transport, new ModbusRtuOptions());
        await client.ConnectAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.ReadHoldingRegistersAsync(0, 0, 1));
    }

    [Fact]
    public void DriverConfiguration_RejectsNonIntegerOption()
    {
        var configuration = new DriverConfiguration();
        configuration.Options["unitId"] = "abc";

        Assert.Throws<ArgumentException>(() => configuration.GetOption("unitId", 1));
    }

    [Fact]
    public void DriverConfiguration_RejectsOutOfRangeOption()
    {
        var configuration = new DriverConfiguration();
        configuration.Options["unitId"] = "300";

        Assert.Throws<ArgumentOutOfRangeException>(() => configuration.GetByteOption("unitId", 1));
    }

    [Fact]
    public void DriverConfiguration_RejectsInvalidBooleanOption()
    {
        var configuration = new DriverConfiguration();
        configuration.Options["swapBytes"] = "yes";

        Assert.Throws<ArgumentException>(() => configuration.GetBooleanOption("swapBytes", false));
    }

    [Fact]
    public void DriverConfiguration_ReturnsDefaultsWhenOptionMissing()
    {
        var configuration = new DriverConfiguration();

        Assert.Equal(1, configuration.GetByteOption("unitId", 1));
        Assert.Equal((ushort)0x10, configuration.GetUInt16Option("monitoringTimer", 0x10));
        Assert.False(configuration.GetBooleanOption("swapBytes", false));
        Assert.False(configuration.HasOption("unitId"));
    }

    [Fact]
    public void ModbusClientFactory_AllowsBroadcastUnitId()
    {
        var configuration = new DriverConfiguration();
        configuration.Options["unitId"] = "0";

        Assert.Equal(0, ModbusClientFactory.GetUnitId(configuration));
    }

    [Fact]
    public void SiemensFactory_RejectsRackOutOfRange()
    {
        var configuration = new DriverConfiguration { Host = "192.168.0.10" };
        configuration.Options["rack"] = "9";

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SiemensDriverFactory().Create(configuration));
    }
}
