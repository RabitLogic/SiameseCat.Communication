namespace SiameseCat.Core;

public abstract partial class DriverBase
{
    /// <inheritdoc/>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureCapability(DriverCapabilities.Health);

        var probe = Configuration.ProbeAddress;
        if (string.IsNullOrWhiteSpace(probe))
        {
            throw new InvalidOperationException(
                $"Driver '{Descriptor.Id}' has no ProbeAddress configured; set DriverConfiguration.ProbeAddress to use PingAsync.");
        }

        try
        {
            var address = AddressParser.Parse(probe);
            await ExecuteAsync(
                token => DoReadAsync(address, token),
                isWrite: false,
                address,
                timeout: null,
                PlcOperation.Ping,
                cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning($"{Descriptor.Id} health probe '{probe}' failed.", ex);
            return false;
        }
    }

    private void StartKeepAlive()
    {
        if (Configuration.KeepAliveInterval <= TimeSpan.Zero || string.IsNullOrWhiteSpace(Configuration.ProbeAddress))
        {
            return;
        }

        StopKeepAlive();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        _keepAliveCts = cts;
        _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(cts.Token), CancellationToken.None);
    }

    private (CancellationTokenSource? Cts, Task? Task) StopKeepAlive()
    {
        var cts = _keepAliveCts;
        var task = _keepAliveTask;
        _keepAliveCts = null;
        _keepAliveTask = null;
        cts?.Cancel();
        return (cts, task);
    }

    private static async Task AwaitKeepAliveAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the loop is stopped.
        }
    }

    private async Task KeepAliveLoopAsync(CancellationToken token)
    {
        var failures = 0;

        try
        {
            using var timer = new PeriodicTimer(Configuration.KeepAliveInterval);
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                bool alive;
                try
                {
                    alive = await PingAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }

                if (alive)
                {
                    failures = 0;
                    continue;
                }

                failures++;
                Logger.Warning(
                    $"{Descriptor.Id} keep-alive failed ({failures}/{Configuration.KeepAliveFailureThreshold}).");

                if (failures < Configuration.KeepAliveFailureThreshold)
                {
                    continue;
                }

                failures = 0;
                try
                {
                    await _gate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await ReconnectAsync(null, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _gate.Release();
                    }

                    Logger.Information($"{Descriptor.Id} reconnected after consecutive keep-alive failures.");
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Error($"{Descriptor.Id} could not reconnect after keep-alive failures.", ex);
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Stopping.
        }
    }
}
