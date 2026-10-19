namespace SiameseCat.Communication.Transport;

/// <summary>
/// The subset of serial port behaviour that <see cref="SerialPortTransport"/> relies on.
/// </summary>
/// <remarks>
/// The abstraction exists so the transport can be exercised without hardware: the tests substitute an
/// in-memory connection for the real port. It is internal on purpose — the public surface stays
/// <see cref="SerialPortTransport"/> plus <see cref="SerialPortOptions"/>.
/// </remarks>
internal interface ISerialConnection : IDisposable
{
    /// <summary>Gets whether the connection is open.</summary>
    bool IsOpen { get; }

    /// <summary>Opens the connection. Synchronous, exactly like the platform API it wraps.</summary>
    void Open();

    /// <summary>Closes the connection. Must unblock a pending <see cref="Read"/>.</summary>
    void Close();

    /// <summary>Reads up to <paramref name="count"/> bytes, honouring the port read timeout.</summary>
    /// <param name="buffer">Destination buffer.</param>
    /// <param name="offset">Offset in <paramref name="buffer"/>.</param>
    /// <param name="count">Maximum number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    int Read(byte[] buffer, int offset, int count);

    /// <summary>Writes <paramref name="count"/> bytes.</summary>
    /// <param name="buffer">Source buffer.</param>
    /// <param name="offset">Offset in <paramref name="buffer"/>.</param>
    /// <param name="count">Number of bytes to write.</param>
    void Write(byte[] buffer, int offset, int count);

    /// <summary>Discards whatever is already buffered by the driver.</summary>
    void DiscardInBuffer();
}
