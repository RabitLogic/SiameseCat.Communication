using System.Diagnostics;

namespace SiameseCat.Core;

public abstract partial class DriverBase
{
    private async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        bool isWrite,
        PlcAddress? address,
        TimeSpan? timeout,
        PlcOperation operationKind,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureConnected();

            var attempts = isWrite && !Configuration.RetryWrites ? 1 : Configuration.RetryCount + 1;
            var effectiveTimeout = timeout ?? Configuration.Timeout;

            for (var attempt = 1; ; attempt++)
            {
                Metrics.RecordRequest(operationKind);
                PlcDiagnostics.RecordRequest(Descriptor.Id, operationKind);
                var started = Stopwatch.GetTimestamp();

                var ownedTimeoutSource = CreateTimeoutSource(effectiveTimeout, cancellationToken, out var token);
                try
                {
                    var result = await operation(token).ConfigureAwait(false);
                    var elapsed = GetElapsedTime(started);
                    Metrics.RecordLatency(elapsed);
                    PlcDiagnostics.RecordLatency(Descriptor.Id, operationKind, elapsed);
                    return result;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    var timeoutException = new CommunicationTimeoutException(
                        $"{Descriptor.Id} request{BuildAddressSuffix(address)} timed out after {effectiveTimeout}.");

                    Metrics.RecordFailure(operationKind, timeoutException);
                    PlcDiagnostics.RecordFailure(Descriptor.Id, operationKind, timeoutException);

                    if (attempt >= attempts)
                    {
                        SetState(DriverState.Faulted);
                        throw timeoutException;
                    }

                    Logger.Warning($"{timeoutException.Message} Reconnecting.");
                    await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                    await ReconnectAsync(address, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < attempts && RetryPolicy.IsTransient(ex))
                {
                    Metrics.RecordFailure(operationKind, ex);
                    Metrics.RecordRetry();
                    PlcDiagnostics.RecordFailure(Descriptor.Id, operationKind, ex);
                    PlcDiagnostics.RecordRetry(Descriptor.Id);
                    Logger.Warning(
                        $"{Descriptor.Id} request{BuildAddressSuffix(address)} failed, retrying ({attempt}/{attempts - 1}).",
                        ex);
                    await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                    await ReconnectAsync(address, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Metrics.RecordFailure(operationKind, ex);
                    PlcDiagnostics.RecordFailure(Descriptor.Id, operationKind, ex);
                    if (ex is ConnectionClosedException)
                    {
                        SetState(DriverState.Faulted);
                    }

                    throw;
                }
                finally
                {
                    ownedTimeoutSource?.Dispose();
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Creates the per-attempt token source. Requests are serialised by the gate, so the instance-level
    /// source is reused when the caller passes no token, which keeps the common path allocation free.
    /// </summary>
    private CancellationTokenSource? CreateTimeoutSource(
        TimeSpan timeout,
        CancellationToken cancellationToken,
        out CancellationToken token)
    {
        if (cancellationToken.CanBeCanceled)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout > TimeSpan.Zero)
            {
                linked.CancelAfter(timeout);
            }

            token = linked.Token;
            return linked;
        }

#if NET6_0_OR_GREATER
        var cached = _timeoutCts;
        if (cached is null || cached.IsCancellationRequested || !cached.TryReset())
        {
            cached?.Dispose();
            cached = new CancellationTokenSource();
            _timeoutCts = cached;
        }

        if (timeout > TimeSpan.Zero)
        {
            cached.CancelAfter(timeout);
        }

        token = cached.Token;
        return null;
#else
        var source = new CancellationTokenSource();
        if (timeout > TimeSpan.Zero)
        {
            source.CancelAfter(timeout);
        }

        token = source.Token;
        _timeoutCts = source;
        return source;
#endif
    }

    /// <summary>Formats the address suffix only when a failure message is actually built.</summary>
    private static string BuildAddressSuffix(PlcAddress? address) =>
        address is null ? string.Empty : $" for {address.Value}";

    /// <summary>Elapsed time since <paramref name="started"/> without allocating a <see cref="Stopwatch"/>.</summary>
    private static TimeSpan GetElapsedTime(long started)
    {
#if NET7_0_OR_GREATER
        return Stopwatch.GetElapsedTime(started);
#else
        var ticks = (long)((Stopwatch.GetTimestamp() - started) * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency));
        return TimeSpan.FromTicks(ticks);
#endif
    }

    private async Task ReconnectAsync(PlcAddress? address, CancellationToken cancellationToken)
    {
        if (address is null)
        {
            Logger.Information($"{Descriptor.Id} reconnecting.");
        }

        SetState(DriverState.Connecting);
        try
        {
            try
            {
                await DoDisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warning($"{Descriptor.Id} failed to close the broken connection.", ex);
            }

            await DoConnectAsync(cancellationToken).ConfigureAwait(false);
            SetState(DriverState.Connected);
            StartKeepAlive();
        }
        catch
        {
            SetState(DriverState.Faulted);
            throw;
        }
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be greater than zero.");
        }

        return timeout;
    }

    private async Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var delay = Configuration.GetRetryDelay(attempt);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateAddress(PlcAddress address)
    {
        if (string.IsNullOrWhiteSpace(address.Region))
        {
            throw new ArgumentException("The address has no region; it was probably not parsed.", nameof(address));
        }
    }
}
