using System.Buffers;
using System.IO.Ports;
using SiameseCat.Core;
using SerialPortDevice = System.IO.Ports.SerialPort;

namespace SiameseCat.Transport;

/// <summary>
/// Raw serial port transport. Reads and writes are executed on the thread pool because
/// <see cref="SerialPort"/> has no cancellable asynchronous API.
/// </summary>
/// <remarks>
/// Cancelling a pending read or write closes the port: this is the only reliable way to abort a
/// blocking serial I/O call without leaving a partially transferred frame on the wire.
/// </remarks>
public sealed class SerialPortTransport : ITransport
{
    private readonly SerialPortOptions _options;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private SerialPortDevice? _port;
    private volatile bool _isOpen;
    private volatile bool _disposed;

    /// <inheritdoc/>
    public SerialPortTransport(SerialPortOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc/>
    public bool IsOpen => _isOpen;

    /// <inheritdoc/>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_isOpen)
            {
                return;
            }

            var port = new SerialPortDevice(
                _options.PortName,
                _options.BaudRate,
                _options.Parity,
                _options.DataBits,
                _options.StopBits)
            {
                Handshake = _options.Handshake,
                ReadTimeout = (int)_options.ReadTimeout.TotalMilliseconds,
                WriteTimeout = (int)_options.WriteTimeout.TotalMilliseconds,
                DtrEnable = _options.DtrEnable,
                RtsEnable = _options.RtsEnable,
                ReadBufferSize = _options.ReadBufferSize,
            };

            try
            {
                // SerialPort.Open is synchronous and not cancellable; cancellation is observed right after it returns.
                await Task.Run(port.Open, CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                SafeClose(port);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                SafeClose(port);
                throw new CommunicationException(
                    $"Unable to open serial port '{_options.PortName}': {ex.Message}", ex);
            }

            _port = port;
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
            var port = Interlocked.Exchange(ref _port, null);
            if (port is not null)
            {
                await Task.Run(() => SafeClose(port)).ConfigureAwait(false);
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.IsEmpty)
        {
            return 0;
        }

        var port = _port;
        if (!_isOpen || port is null)
        {
            throw new ConnectionClosedException("The serial port is not open.");
        }

        var temp = ArrayPool<byte>.Shared.Rent(buffer.Length);
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureSamePort(port);

            try
            {
                var read = await RunIoAsync(port, () => port.Read(temp, 0, buffer.Length), cancellationToken)
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
                MarkClosed(port);
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        var port = _port;
        if (!_isOpen || port is null)
        {
            throw new ConnectionClosedException("The serial port is not open.");
        }

        var temp = ArrayPool<byte>.Shared.Rent(buffer.Length);
        buffer.CopyTo(temp);
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureSamePort(port);

            try
            {
                await RunIoAsync(
                        port,
                        () =>
                        {
                            port.Write(temp, 0, buffer.Length);
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
                MarkClosed(port);
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
        var port = _port;
        if (!_isOpen || port is null)
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
            port.DiscardInBuffer();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            MarkClosed(port);
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
            var port = Interlocked.Exchange(ref _port, null);
            if (port is not null)
            {
                await Task.Run(() => SafeClose(port)).ConfigureAwait(false);
            }
        }
        finally
        {
            _stateGate.Release();
        }

        _stateGate.Dispose();
        _ioGate.Dispose();
    }

    private async Task<T> RunIoAsync<T>(SerialPortDevice port, Func<T> operation, CancellationToken cancellationToken)
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
            MarkClosed(port);
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

    private void EnsureSamePort(SerialPortDevice port)
    {
        if (!_isOpen || !ReferenceEquals(_port, port))
        {
            throw new ConnectionClosedException("The serial port was closed while the operation was pending.");
        }
    }

    private void MarkClosed(SerialPortDevice expected)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _port, null, expected), expected))
        {
            return;
        }

        _isOpen = false;
        SafeClose(expected);
    }

    private static void SafeClose(SerialPortDevice port)
    {
        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // The port is already unusable.
        }
        finally
        {
            port.Dispose();
        }
    }
}
