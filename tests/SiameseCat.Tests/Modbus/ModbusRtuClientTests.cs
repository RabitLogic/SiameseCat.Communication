using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;
using SiameseCat.Tests.Fakes;

namespace SiameseCat.Tests.Modbus;

public class ModbusRtuClientTests
{
    [Fact]
    public async Task ReadHoldingRegisters_DecodesBigEndianValues()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(RtuFrame(0x01, 0x03, 0x04, 0x00, 0x0A, 0x00, 0x0B));

        await using var client = new ModbusRtuClient(transport, CreateOptions());
        await client.ConnectAsync();

        var registers = await client.ReadHoldingRegistersAsync(0, 2);

        Assert.Equal(new ushort[] { 10, 11 }, registers);
        Assert.Equal(
            RtuFrame(0x01, 0x03, 0x00, 0x00, 0x00, 0x02),
            Assert.Single(transport.Writes));
    }

    [Fact]
    public async Task ReadCoils_DecodesLsbFirstBits()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(RtuFrame(0x01, 0x01, 0x02, 0xCD, 0x01));

        await using var client = new ModbusRtuClient(transport, CreateOptions());
        await client.ConnectAsync();

        var coils = await client.ReadCoilsAsync(0, 10);

        Assert.Equal(10, coils.Length);
        Assert.True(coils[0]);
        Assert.False(coils[1]);
        Assert.True(coils[2]);
        Assert.True(coils[3]);
        Assert.True(coils[8]);
        Assert.False(coils[9]);
    }

    [Fact]
    public async Task WriteMultipleRegisters_PacksRegistersBigEndian()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(RtuFrame(0x01, 0x10, 0x00, 0x0A, 0x00, 0x02));

        await using var client = new ModbusRtuClient(transport, CreateOptions());
        await client.ConnectAsync();

        await client.WriteMultipleRegistersAsync(10, new ushort[] { 0x1234, 0x5678 });

        Assert.Equal(
            RtuFrame(0x01, 0x10, 0x00, 0x0A, 0x00, 0x02, 0x04, 0x12, 0x34, 0x56, 0x78),
            Assert.Single(transport.Writes));
    }

    [Fact]
    public async Task ExceptionResponse_ThrowsModbusException()
    {
        var transport = new MockTransport();
        transport.EnqueueResponse(RtuFrame(0x01, 0x83, 0x02));

        await using var client = new ModbusRtuClient(transport, CreateOptions());
        await client.ConnectAsync();

        var exception = await Assert.ThrowsAsync<ModbusException>(
            () => client.ReadHoldingRegistersAsync(0, 1));

        Assert.Equal(2, exception.ExceptionCode);
        Assert.Contains("unit 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("function 0x03", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Illegal data address", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BadCrc_ThrowsProtocolException()
    {
        var transport = new MockTransport();
        var response = RtuFrame(0x01, 0x03, 0x02, 0x00, 0x2A);
        response[^1] ^= 0xFF;
        transport.EnqueueResponse(response);

        await using var client = new ModbusRtuClient(transport, CreateOptions());
        await client.ConnectAsync();

        await Assert.ThrowsAsync<ProtocolException>(() => client.ReadHoldingRegistersAsync(0, 1));
    }

    [Fact]
    public async Task EchoedRequest_IsDiscardedWhenEnabled()
    {
        var transport = new MockTransport();
        var request = RtuFrame(0x01, 0x03, 0x00, 0x00, 0x00, 0x01);
        var response = RtuFrame(0x01, 0x03, 0x02, 0x12, 0x34);
        transport.EnqueueResponse(request.Concat(response).ToArray());

        var options = CreateOptions();
        options.DiscardEcho = true;

        await using var client = new ModbusRtuClient(transport, options);
        await client.ConnectAsync();

        var registers = await client.ReadHoldingRegistersAsync(0, 1);

        Assert.Equal(0x1234, registers[0]);
    }

    [Fact]
    public async Task TransientTimeout_IsRetried()
    {
        var transport = new MockTransport
        {
            TimeoutFailuresRemaining = 1,
        };
        transport.EnqueueResponse(RtuFrame(0x01, 0x03, 0x02, 0x00, 0x2A));

        var options = CreateOptions();
        options.Timeout = TimeSpan.FromMilliseconds(100);
        options.RetryCount = 1;

        await using var client = new ModbusRtuClient(transport, options);
        await client.ConnectAsync();

        var registers = await client.ReadHoldingRegistersAsync(0, 1);

        Assert.Equal(42, registers[0]);
        Assert.Equal(2, transport.Writes.Count);
    }

    [Fact]
    public async Task RequestTimeout_ThrowsCommunicationTimeoutException()
    {
        var transport = new MockTransport { BlockReads = true };

        var options = CreateOptions();
        options.Timeout = TimeSpan.FromMilliseconds(50);

        await using var client = new ModbusRtuClient(transport, options);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<CommunicationTimeoutException>(
            () => client.ReadHoldingRegistersAsync(0, 1));
    }

    private static ModbusRtuOptions CreateOptions() => new()
    {
        UnitId = 1,
        Timeout = TimeSpan.FromSeconds(1),
        RetryDelay = TimeSpan.Zero,
    };

    private static byte[] RtuFrame(params byte[] body)
    {
        var frame = new byte[body.Length + 2];
        body.CopyTo(frame, 0);
        ModbusCrc16.Write(frame.AsSpan(0, body.Length), frame.AsSpan(body.Length));
        return frame;
    }
}
