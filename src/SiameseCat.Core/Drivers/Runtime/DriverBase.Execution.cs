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
            var where = address is null ? string.Empty : $" for {address.Value}";
            var effectiveTimeout = timeout ?? Configuration.Timeout;

            for (var attempt = 1; ; attempt++)
            {
                Metrics.RecordRequest(operationKind);
                PlcDiagnostics.RecordRequest(Descriptor.Id, operationKind);
                var stopwatch = Stopwatch.StartNew();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (effectiveTimeout > TimeSpan.Zero)
                {
                    timeoutCts.CancelAfter(effectiveTimeout);
                }

                try
                {
                    var result = await operation(timeoutCts.Token).ConfigureAwait(false);
                    Metrics.RecordLatency(stopwatch.Elapsed);
                    PlcDiagnostics.RecordLatency(Descriptor.Id, operationKind, stopwatch.Elapsed);
                    return result;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    var timeoutException = new CommunicationTimeoutException(
                        $"{Descriptor.Id} request{where} timed out after {effectiveTimeout}.");

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
                    Logger.Warning($"{Descriptor.Id} request{where} failed, retrying ({attempt}/{attempts - 1}).", ex);
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
            }
        }
        finally
        {
            _gate.Release();
        }
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
