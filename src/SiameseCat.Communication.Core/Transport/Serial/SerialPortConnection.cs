using SiameseCat.Communication;

using SerialPortDevice = System.IO.Ports.SerialPort;

namespace SiameseCat.Communication.Transport.Serial;

/// <summary>
/// <see cref="ISerialConnection"/> implementation that wraps <see cref="System.IO.Ports.SerialPort"/>.
/// </summary>
internal sealed class SerialPortConnection : ISerialConnection
{
    private readonly SerialPortDevice _port;

    /// <summary>Creates the connection from validated options.</summary>
    /// <param name="options">Port settings.</param>
    public SerialPortConnection(SerialPortOptions options)
    {
        Guard.NotNull(options, nameof(options));

        _port = new SerialPortDevice(
            options.PortName,
            options.BaudRate,
            options.Parity,
            options.DataBits,
            options.StopBits)
        {
            Handshake = options.Handshake,
            ReadTimeout = (int)options.ReadTimeout.TotalMilliseconds,
            WriteTimeout = (int)options.WriteTimeout.TotalMilliseconds,
            DtrEnable = options.DtrEnable,
            RtsEnable = options.RtsEnable,
            ReadBufferSize = options.ReadBufferSize,
        };
    }

    /// <inheritdoc/>
    public bool IsOpen => _port.IsOpen;

    /// <inheritdoc/>
    public void Open() => _port.Open();

    /// <inheritdoc/>
    public void Close() => _port.Close();

    /// <inheritdoc/>
    public int Read(byte[] buffer, int offset, int count) => _port.Read(buffer, offset, count);

    /// <inheritdoc/>
    public void Write(byte[] buffer, int offset, int count) => _port.Write(buffer, offset, count);

    /// <inheritdoc/>
    public void DiscardInBuffer() => _port.DiscardInBuffer();

    /// <inheritdoc/>
    public void Dispose() => _port.Dispose();
}
