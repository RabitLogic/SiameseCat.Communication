using SiameseCat.Communication.Diagnostics;

namespace SiameseCat.Communication.Drivers;

public abstract partial class DriverBase
{
#if !NET6_0_OR_GREATER
    /// <summary>Fallback jitter source for targets without <c>Random.Shared</c>.</summary>
    private static readonly Random SubscriptionRandom = new();
#endif

    /// <inheritdoc/>
    public async Task<IAsyncDisposable> SubscribeAsync(
        DeviceAddress address,
        TimeSpan interval,
        Func<DeviceValue, Task> onChanged,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(onChanged, nameof(onChanged));
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be greater than zero.");
        }

        ThrowIfDisposed();
        EnsureCapability(DriverCapabilities.Subscribe);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        var native = await DoSubscribeAsync(address, interval, onChanged, cts.Token).ConfigureAwait(false);
        if (native is not null)
        {
            return new Subscription(cts, native);
        }

        _ = Task.Run(() => PollAsync(address, interval, onChanged, cts), CancellationToken.None);
        return new Subscription(cts);
    }

    /// <inheritdoc/>
    public async Task<IAsyncDisposable> SubscribeBatchAsync(
        IReadOnlyList<DeviceAddress> addresses,
        TimeSpan interval,
        Func<IReadOnlyList<DeviceValue>, Task> onChanged,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(addresses, nameof(addresses));
        Guard.NotNull(onChanged, nameof(onChanged));
        if (addresses.Count == 0)
        {
            throw new ArgumentException("At least one address is required.", nameof(addresses));
        }

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be greater than zero.");
        }

        ThrowIfDisposed();
        EnsureCapability(DriverCapabilities.SubscribeBatch);

        // Copy so callers can reuse or clear their list while the subscription runs.
        var targets = new DeviceAddress[addresses.Count];
        for (var i = 0; i < targets.Length; i++)
        {
            targets[i] = addresses[i];
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        var native = await DoSubscribeBatchAsync(targets, interval, onChanged, cts.Token).ConfigureAwait(false);
        if (native is not null)
        {
            return new Subscription(cts, native);
        }

        // Polling fallback needs a single request for the whole set.
        if ((Descriptor.Capabilities & DriverCapabilities.BatchRead) == 0)
        {
            cts.Dispose();
            throw new NotSupportedException(
                $"Driver '{Descriptor.Id}' cannot batch-subscribe without the '{DriverCapabilities.BatchRead}' capability or a native batch subscription.");
        }

        _ = Task.Run(() => PollBatchAsync(targets, interval, onChanged, cts), CancellationToken.None);
        return new Subscription(cts);
    }

    /// <summary>
    /// Starts a protocol-native subscription. The default implementation returns <see langword="null"/>,
    /// which makes the driver fall back to polling <see cref="DoReadAsync"/> at the requested interval.
    /// </summary>
    /// <param name="address">Address to watch.</param>
    /// <param name="interval">Requested notification interval; protocols with their own sampling rate may ignore it.</param>
    /// <param name="onChanged">Callback invoked for every change.</param>
    /// <param name="cancellationToken">Cancelled when the caller disposes the subscription or the driver is disposed.</param>
    /// <returns>A handle that stops the subscription, or <see langword="null"/> to use polling.</returns>
    protected virtual Task<IAsyncDisposable?> DoSubscribeAsync(
        DeviceAddress address,
        TimeSpan interval,
        Func<DeviceValue, Task> onChanged,
        CancellationToken cancellationToken) => Task.FromResult<IAsyncDisposable?>(null);

    /// <summary>
    /// Starts a protocol-native subscription for several addresses. The default implementation returns
    /// <see langword="null"/>, which makes the driver poll <c>ReadBatchAsync</c>.
    /// </summary>
    /// <param name="addresses">Addresses to watch.</param>
    /// <param name="interval">Requested notification interval.</param>
    /// <param name="onChanged">Callback invoked with one value per address when any value changed.</param>
    /// <param name="cancellationToken">Cancelled when the caller disposes the subscription or the driver is disposed.</param>
    /// <returns>A handle that stops the subscription, or <see langword="null"/> to use polling.</returns>
    protected virtual Task<IAsyncDisposable?> DoSubscribeBatchAsync(
        IReadOnlyList<DeviceAddress> addresses,
        TimeSpan interval,
        Func<IReadOnlyList<DeviceValue>, Task> onChanged,
        CancellationToken cancellationToken) => Task.FromResult<IAsyncDisposable?>(null);

    private Task PollAsync(
        DeviceAddress address,
        TimeSpan interval,
        Func<DeviceValue, Task> onChanged,
        CancellationTokenSource cts) =>
        RunPollLoopAsync(
            interval,
            address.ToString(),
            token => ReadAsync(address, token),
            static (previous, current) => previous == current,
            onChanged,
            cts);

    private Task PollBatchAsync(
        DeviceAddress[] addresses,
        TimeSpan interval,
        Func<IReadOnlyList<DeviceValue>, Task> onChanged,
        CancellationTokenSource cts) =>
        RunPollLoopAsync(
            interval,
            $"{addresses.Length} addresses",
            token => ReadBatchAsync(addresses, token),
            static (previous, current) => ValuesEqual(previous, current),
            onChanged,
            cts);

    private static bool ValuesEqual(DeviceValue[] previous, DeviceValue[] current)
    {
        if (ReferenceEquals(previous, current))
        {
            return true;
        }

        if (previous.Length != current.Length)
        {
            return false;
        }

        for (var i = 0; i < previous.Length; i++)
        {
            if (previous[i] != current[i])
            {
                return false;
            }
        }

        return true;
    }

    private async Task RunPollLoopAsync<T>(
        TimeSpan interval,
        string target,
        Func<CancellationToken, Task<T>> poll,
        Func<T, T, bool> isSame,
        Func<T, Task> onChanged,
        CancellationTokenSource cts)
    {
        var token = cts.Token;
        var last = default(T);
        var first = true;
        var failures = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(GetPollDelay(interval, failures), token).ConfigureAwait(false);

                try
                {
                    var value = await poll(token).ConfigureAwait(false);
                    failures = 0;

                    if (first || !isSame(last!, value))
                    {
                        first = false;
                        last = value;
                        await onChanged(value).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    failures++;
                    Logger.Warning(
                        $"{Descriptor.Id} subscription for {target} failed ({failures} in a row).",
                        ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Subscription stopped.
        }
        finally
        {
            cts.Dispose();
        }
    }

    private TimeSpan GetPollDelay(TimeSpan interval, int failures)
    {
        if (failures > 0 && Configuration.SubscriptionMaxBackoff > TimeSpan.Zero)
        {
            var backoff = interval.TotalMilliseconds * Math.Pow(2, failures);
            interval = TimeSpan.FromMilliseconds(
                Math.Min(Math.Max(interval.TotalMilliseconds, backoff), Configuration.SubscriptionMaxBackoff.TotalMilliseconds));
        }

        if (Configuration.SubscriptionJitter)
        {
            interval = TimeSpan.FromMilliseconds(interval.TotalMilliseconds * (0.8 + (NextJitter() * 0.4)));
        }

        return interval;
    }

    private static double NextJitter()
    {
#if NET6_0_OR_GREATER
        return Random.Shared.NextDouble();
#else
        lock (SubscriptionRandom)
        {
            return SubscriptionRandom.NextDouble();
        }
#endif
    }

    private sealed class Subscription : IAsyncDisposable
    {
        private CancellationTokenSource? _cts;
        private IAsyncDisposable? _native;

        public Subscription(CancellationTokenSource cts, IAsyncDisposable? native = null)
        {
            _cts = cts;
            _native = native;
        }

        public async ValueTask DisposeAsync()
        {
            var cts = Interlocked.Exchange(ref _cts, null);
            if (cts is not null)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The polling loop already finished and disposed the source.
                }
            }

            var native = Interlocked.Exchange(ref _native, null);
            if (native is not null)
            {
                await native.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
