using SiameseCat.Communication.Diagnostics;
using SiameseCat.Communication.Exceptions;
using SiameseCat.Communication.Transport;

namespace SiameseCat.Communication.Drivers;

public abstract partial class DriverBase
{
    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_state == DriverState.Connected)
            {
                return;
            }

            SetState(DriverState.Connecting);
            var attempts = Configuration.RetryCount + 1;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await DoConnectAsync(cancellationToken).ConfigureAwait(false);
                    SetState(DriverState.Connected);
                    Logger.Information($"{Descriptor.Id} connected through {Configuration.Transport}.");
                    StartKeepAlive();
                    return;
                }
                catch (Exception ex) when (attempt < attempts && RetryPolicy.IsTransient(ex))
                {
                    Logger.Warning(
                        $"{Descriptor.Id} connect attempt {attempt}/{attempts} failed, retrying.",
                        ex);
                    await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    SetState(DriverState.Faulted);
                    Logger.Error($"{Descriptor.Id} failed to connect.", ex);
                    throw;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        if (_disposed)
        {
            return;
        }

        Task? keepAlive;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            keepAlive = StopKeepAlive().Task;

            if (_state != DriverState.Disconnected)
            {
                try
                {
                    await DoDisconnectAsync().ConfigureAwait(false);
                }
                finally
                {
                    SetState(DriverState.Disconnected);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        await AwaitKeepAliveAsync(keepAlive).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Dispose waits for an in-flight request to finish before the transport is closed, so a hanging
    /// transport makes dispose hang as well. Rely on the configured request timeout rather than on a
    /// bounded dispose.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        var keepAlive = StopKeepAlive().Task;

        var gateHeld = false;
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            gateHeld = true;
        }
        catch (ObjectDisposedException)
        {
            // A shared RequestLock disposed with its SharedTransportFactory; the driver still disconnects
            // so that disposing the factory first cannot throw from DisposeAsync.
            Logger.Warning(
                $"{Descriptor.Id} disconnects without its shared request lock because the lock was already disposed.");
        }

        try
        {
            try
            {
                await DoDisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warning($"{Descriptor.Id} failed to disconnect during dispose.", ex);
            }

            SetState(DriverState.Disconnected);
        }
        finally
        {
            if (gateHeld)
            {
                _gate.Release();
            }
        }

        await AwaitKeepAliveAsync(keepAlive).ConfigureAwait(false);
        await DisposeCoreAsync().ConfigureAwait(false);
        _timeoutCts?.Dispose();
        _timeoutCts = null;
        if (_ownsGate)
        {
            _gate.Dispose();
        }

        _disposeCts.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Sets the state and raises <see cref="StateChanged"/> when it changed.</summary>
    /// <param name="state">New state.</param>
    protected void SetState(DriverState state)
    {
        var previous = _state;
        if (previous == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, new DriverStateChangedEventArgs(previous, state));
    }

    /// <summary>Releases resources owned by the derived driver. Called by <see cref="DisposeAsync"/>.</summary>
    protected virtual ValueTask DisposeCoreAsync() => ValueTask.CompletedTask;

    /// <summary>Verifies that the driver declares a capability before the request is sent.</summary>
    /// <param name="capability">Required capability.</param>
    /// <exception cref="NotSupportedException">The descriptor does not declare the capability.</exception>
    protected void EnsureCapability(DriverCapabilities capability)
    {
        if ((Descriptor.Capabilities & capability) != capability)
        {
            throw new NotSupportedException(
                $"Driver '{Descriptor.Id}' does not declare the '{capability}' capability.");
        }
    }

    private void EnsureConnected()
    {
        if (_state != DriverState.Connected)
        {
            throw new CommunicationException(
                $"Driver '{Descriptor.Id}' is {_state}. Call ConnectAsync before sending requests.");
        }
    }

    private void ThrowIfDisposed()
    {
        Guard.NotDisposed(_disposed, this);
    }
}
