using System.Collections.Concurrent;

using SiameseCat.Communication;
using SiameseCat.Communication.Exceptions;
using SiameseCat.Communication.Modbus;
using SiameseCat.Communication.Transport;
using SiameseCat.Communication.Transport.Serial;

namespace SiameseCat.Tests.Core;

/// <summary>
/// Drives <see cref="SerialPortTransport"/> through a fake <see cref="ISerialConnection"/> so the serial
/// path is covered without a physical port or a virtual COM driver.
/// </summary>
public class SerialPortTransportTests
{
    private static SerialPortOptions Options(TimeSpan? readTimeout = null) => new()
    {
        PortName = "COM_TEST",
        BaudRate = 9600,
        ReadTimeout = readTimeout ?? TimeSpan.FromMilliseconds(100),
        WriteTimeout = TimeSpan.FromMilliseconds(100),
    };

    [Fact]
    public async Task Open_Writes_And_Reads_RoundTrip()
    {
        var connection = new FakeSerialConnection();
        await using var transport = new SerialPortTransport(Options(), _ => connection);

        Assert.False(transport.IsOpen);

        await transport.OpenAsync();
        Assert.True(transport.IsOpen);
        Assert.True(connection.IsOpen);

        await transport.WriteAsync(new byte[] { 1, 2, 3 });
        Assert.Equal(new byte[] { 1, 2, 3 }, connection.Written);

        connection.EnqueueResponse(new byte[] { 9, 8 });
        var buffer = new byte[4];
        var read = await transport.ReadAsync(buffer);

        Assert.Equal(2, read);
        Assert.Equal(new byte[] { 9, 8, 0, 0 }, buffer);
    }

    [Fact]
    public async Task Open_IsIdempotent_And_CreatesOneConnection()
    {
        var created = 0;
        var connection = new FakeSerialConnection();
        await using var transport = new SerialPortTransport(Options(), _ =>
        {
            created++;
            return connection;
        });

        await transport.OpenAsync();
        await transport.OpenAsync();

        Assert.Equal(1, created);
        Assert.Equal(1, connection.OpenCount);
    }

    [Fact]
    public async Task OpenAsync_MapsUnauthorizedAccessToCommunicationException()
    {
        var connection = new FakeSerialConnection { OpenError = new UnauthorizedAccessException("Access denied.") };
        await using var transport = new SerialPortTransport(Options(), _ => connection);

        var exception = await Assert.ThrowsAsync<CommunicationException>(() => transport.OpenAsync());

        Assert.Contains("COM_TEST", exception.Message, StringComparison.Ordinal);
        Assert.False(transport.IsOpen);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task ReadAsync_MapsTimeoutToCommunicationTimeoutException()
    {
        var connection = new FakeSerialConnection { ReadError = new TimeoutException() };
        await using var transport = new SerialPortTransport(Options(), _ => connection);
        await transport.OpenAsync();

        var exception = await Assert.ThrowsAsync<CommunicationTimeoutException>(
            async () => await transport.ReadAsync(new byte[4]));

        Assert.Contains("COM_TEST", exception.Message, StringComparison.Ordinal);
        Assert.True(transport.IsOpen);
    }

    [Fact]
    public async Task ReadAsync_MapsIoErrorToConnectionClosed_AndClosesTheTransport()
    {
        var connection = new FakeSerialConnection();
        await using var transport = new SerialPortTransport(Options(), _ => connection);
        await transport.OpenAsync();

        connection.ReadError = new IOException("The device is gone.");
        await Assert.ThrowsAsync<ConnectionClosedException>(async () => await transport.ReadAsync(new byte[4]));

        Assert.False(transport.IsOpen);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () => await transport.ReadAsync(new byte[4]));
    }

    [Fact]
    public async Task ReadAsync_CancellationClosesThePortAndThrows()
    {
        var connection = new FakeSerialConnection { BlockReads = true };
        await using var transport = new SerialPortTransport(Options(TimeSpan.FromSeconds(30)), _ => connection);
        await transport.OpenAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await transport.ReadAsync(new byte[4], cts.Token));

        Assert.False(transport.IsOpen);
        Assert.True(connection.CloseCount >= 1);
    }

    [Fact]
    public async Task ReadAsync_OnClosedTransport_Throws()
    {
        var connection = new FakeSerialConnection();
        await using var transport = new SerialPortTransport(Options(), _ => connection);

        await Assert.ThrowsAsync<ConnectionClosedException>(async () => await transport.ReadAsync(new byte[1]));
        await Assert.ThrowsAsync<ConnectionClosedException>(async () => await transport.WriteAsync(new byte[] { 1 }));
    }

    [Fact]
    public async Task DiscardInput_ForwardsToTheConnection()
    {
        var connection = new FakeSerialConnection();
        await using var transport = new SerialPortTransport(Options(), _ => connection);
        await transport.OpenAsync();

        transport.DiscardInput();

        Assert.Equal(1, connection.DiscardCount);
    }

    [Fact]
    public async Task CloseAsync_AllowsReopening()
    {
        var connection = new FakeSerialConnection();
        await using var transport = new SerialPortTransport(Options(), _ => connection);
        await transport.OpenAsync();

        await transport.CloseAsync();
        Assert.False(transport.IsOpen);

        await transport.OpenAsync();
        Assert.True(transport.IsOpen);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var connection = new FakeSerialConnection();
        var transport = new SerialPortTransport(Options(), _ => connection);
        await transport.OpenAsync();

        await transport.DisposeAsync();
        await transport.DisposeAsync();

        Assert.True(connection.Disposed);
        Assert.False(transport.IsOpen);
    }

    [Fact]
    public async Task ModbusRtuClient_RoundTripsThroughTheSerialTransport()
    {
        // FC03 request for 2 registers answered by a frame built on the fake side, so the whole
        // serial + CRC path is exercised end to end.
        var connection = new FakeSerialConnection
        {
            AutoRespond = request => request.Length >= 8 && request[1] == 0x03
                ? ModbusFrame(request[0], 0x03, 0x04, 0x00, 0x11, 0x00, 0x22)
                : null,
        };

        var options = new SerialPortOptions { PortName = "COM_TEST", BaudRate = 19200, ReadTimeout = TimeSpan.FromSeconds(1) };
        var rtu = new ModbusRtuOptions
        {
            UnitId = 1,
            Timeout = TimeSpan.FromSeconds(2),
            SerialPort = options,
        };

        await using var transport = new SerialPortTransport(options, _ => connection);
        await using var client = new ModbusRtuClient(transport, rtu);

        await client.ConnectAsync();
        var registers = await client.ReadHoldingRegistersAsync(0, 2);

        Assert.Equal(new ushort[] { 0x0011, 0x0022 }, registers);
        Assert.Equal(new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 }, connection.Written.Take(6));
    }

    private static byte[] ModbusFrame(byte unitId, params byte[] pdu)
    {
        var frame = new byte[pdu.Length + 3];
        frame[0] = unitId;
        pdu.CopyTo(frame, 1);
        ModbusCrc16.Write(frame.AsSpan(0, frame.Length - 2), frame.AsSpan(frame.Length - 2));
        return frame;
    }

    /// <summary>In-memory stand-in for a serial port, including blocking reads and error injection.</summary>
    private sealed class FakeSerialConnection : ISerialConnection
    {
        private readonly BlockingCollection<byte> _incoming = new();
        private readonly ManualResetEventSlim _closed = new(false);
        private readonly List<byte> _written = new();

        public bool IsOpen { get; private set; }

        public int OpenCount { get; private set; }

        public int CloseCount { get; private set; }

        public int DiscardCount { get; private set; }

        public bool Disposed { get; private set; }

        public Exception? OpenError { get; init; }

        public Exception? ReadError { get; set; }

        public bool BlockReads { get; init; }

        public Func<byte[], byte[]?>? AutoRespond { get; init; }

        public byte[] Written
        {
            get
            {
                lock (_written)
                {
                    return _written.ToArray();
                }
            }
        }

        public void EnqueueResponse(byte[] data)
        {
            foreach (var value in data)
            {
                _incoming.Add(value);
            }
        }

        public void Open()
        {
            if (OpenError is not null)
            {
                throw OpenError;
            }

            OpenCount++;
            IsOpen = true;
        }

        public void Close()
        {
            CloseCount++;
            IsOpen = false;

            // A real port unblocks a pending read when it is closed.
            _closed.Set();
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (BlockReads)
            {
                _closed.Wait();
                throw new IOException("The port was closed while the read was pending.");
            }

            if (ReadError is not null)
            {
                throw ReadError;
            }

            if (!_incoming.TryTake(out var first, 100))
            {
                throw new TimeoutException();
            }

            buffer[offset] = first;
            var read = 1;

            while (read < count && _incoming.TryTake(out var next))
            {
                buffer[offset + read] = next;
                read++;
            }

            return read;
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            byte[] payload = buffer[offset..(offset + count)];
            lock (_written)
            {
                _written.AddRange(payload);
            }

            var response = AutoRespond?.Invoke(Written);
            if (response is not null)
            {
                EnqueueResponse(response);
            }
        }

        public void DiscardInBuffer()
        {
            DiscardCount++;
            while (_incoming.TryTake(out _))
            {
            }
        }

        public void Dispose() => Disposed = true;
    }
}
