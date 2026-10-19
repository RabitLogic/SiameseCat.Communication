namespace SiameseCat.Core;

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
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        var keepAlive = StopKeepAlive().Task;

        await _gate.WaitAsync().ConfigureAwait(false);
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
            _gate.Release();
        }

        await AwaitKeepAliveAsync(keepAlive).ConfigureAwait(false);
        await DisposeCoreAsync().ConfigureAwait(false);
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
