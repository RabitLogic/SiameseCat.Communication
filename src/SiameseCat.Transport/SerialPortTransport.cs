using System.Buffers;
using SiameseCat.Core;

namespace SiameseCat.Transport;

/// <summary>
/// Raw serial port transport. Reads and writes are executed on the thread pool because
/// <c>System.IO.Ports.SerialPort</c> has no cancellable asynchronous API.
/// </summary>
/// <remarks>
/// Cancelling a pending read or write closes the port: this is the only reliable way to abort a
/// blocking serial I/O call without leaving a partially transferred frame on the wire.
/// </remarks>
public sealed class SerialPortTransport : ITransport
{
    private readonly SerialPortOptions _options;
    private readonly Func<SerialPortOptions, ISerialConnection> _connectionFactory;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private ISerialConnection? _connection;
    private volatile bool _isOpen;
    private volatile bool _disposed;

    /// <summary>Creates a transport that opens the serial port described by <paramref name="options"/>.</summary>
    /// <param name="options">Port settings.</param>
    public SerialPortTransport(SerialPortOptions options)
        : this(options, static settings => new SerialPortConnection(settings))
    {
    }

    /// <summary>
    /// Creates the transport over a custom connection. Used by the tests to drive the transport
    /// through an in-memory link instead of a physical port.
    /// </summary>
    /// <param name="options">Port settings; only the timeouts and the name are read by the transport.</param>
    /// <param name="connectionFactory">Creates the connection when the transport is opened.</param>
    internal SerialPortTransport(
        SerialPortOptions options,
        Func<SerialPortOptions, ISerialConnection> connectionFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc/>
    public bool IsOpen => _isOpen;

    /// <inheritdoc/>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        Guard.NotDisposed(_disposed, this);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Guard.NotDisposed(_disposed, this);

            if (_isOpen)
            {
                return;
            }

            var connection = _connectionFactory(_options);

            try
            {
                // Opening a port is synchronous and not cancellable; cancellation is observed right after it returns.
                await Task.Run(connection.Open, CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                SafeClose(connection);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                SafeClose(connection);
                throw new CommunicationException(
                    $"Unable to open serial port '{_options.PortName}': {ex.Message}", ex);
            }

            _connection = connection;
            _isOpen = true;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task CloseAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _isOpen = false;
            var connection = Interlocked.Exchange(ref _connection, null);
            if (connection is not null)
            {
                await Task.Run(() => SafeClose(connection)).ConfigureAwait(false);
            }
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Guard.NotDisposed(_disposed, this);

        if (buffer.IsEmpty)
        {
            return 0;
        }

        var connection = _connection;
        if (!_isOpen || connection is null)
        {
            throw new ConnectionClosedException("The serial port is not open.");
        }

        var temp = ArrayPool<byte>.Shared.Rent(buffer.Length);
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureSameConnection(connection);

            try
            {
                var read = await RunIoAsync(connection, () => connection.Read(temp, 0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);

                temp.AsSpan(0, read).CopyTo(buffer.Span);
                return read;
            }
            catch (TimeoutException ex)
            {
                throw new CommunicationTimeoutException(
                    $"Reading from serial port '{_options.PortName}' timed out after {_options.ReadTimeout}.", ex);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                MarkClosed(connection);
                throw new ConnectionClosedException(
                    $"The serial port '{_options.PortName}' failed: {ex.Message}", ex);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(temp);
            _ioGate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Guard.NotDisposed(_disposed, this);

        var connection = _connection;
        if (!_isOpen || connection is null)
        {
            throw new ConnectionClosedException("The serial port is not open.");
        }

        var temp = ArrayPool<byte>.Shared.Rent(buffer.Length);
        buffer.CopyTo(temp);
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureSameConnection(connection);

            try
            {
                await RunIoAsync(
                        connection,
                        () =>
                        {
                            connection.Write(temp, 0, buffer.Length);
                            return true;
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new CommunicationTimeoutException(
                    $"Writing to serial port '{_options.PortName}' timed out after {_options.WriteTimeout}.", ex);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                MarkClosed(connection);
                throw new ConnectionClosedException(
                    $"The serial port '{_options.PortName}' failed: {ex.Message}", ex);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(temp);
            _ioGate.Release();
        }
    }

    /// <inheritdoc/>
    public void DiscardInput()
    {
        var connection = _connection;
        if (!_isOpen || connection is null)
        {
            return;
        }

        // Never block: DiscardInput is called from synchronous request setup.
        if (!_ioGate.Wait(0))
        {
            return;
        }

        try
        {
            connection.DiscardInBuffer();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            MarkClosed(connection);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _isOpen = false;
            var connection = Interlocked.Exchange(ref _connection, null);
            if (connection is not null)
            {
                await Task.Run(() => SafeClose(connection)).ConfigureAwait(false);
            }
        }
        finally
        {
            _stateGate.Release();
        }

        _stateGate.Dispose();
        _ioGate.Dispose();
    }

    private async Task<T> RunIoAsync<T>(
        ISerialConnection connection,
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        var task = Task.Run(operation);
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!task.IsCompleted)
        {
            // Closing the port aborts the blocking call and guarantees that no half-written frame
            // is left behind for the next request.
            MarkClosed(connection);
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // The operation was aborted by closing the port.
            }

            throw;
        }
    }

    private void EnsureSameConnection(ISerialConnection connection)
    {
        if (!_isOpen || !ReferenceEquals(_connection, connection))
        {
            throw new ConnectionClosedException("The serial port was closed while the operation was pending.");
        }
    }

    private void MarkClosed(ISerialConnection expected)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _connection, null, expected), expected))
        {
            return;
        }

        _isOpen = false;
        SafeClose(expected);
    }

    private static void SafeClose(ISerialConnection connection)
    {
        try
        {
            if (connection.IsOpen)
            {
                connection.Close();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // The port is already unusable.
        }
        finally
        {
            connection.Dispose();
        }
    }
}
