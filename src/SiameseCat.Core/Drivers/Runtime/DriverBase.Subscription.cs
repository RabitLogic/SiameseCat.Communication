namespace SiameseCat.Core;

public abstract partial class DriverBase
{
    /// <inheritdoc/>
    public async Task<IAsyncDisposable> SubscribeAsync(
        PlcAddress address,
        TimeSpan interval,
        Func<PlcValue, Task> onChanged,
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
        PlcAddress address,
        TimeSpan interval,
        Func<PlcValue, Task> onChanged,
        CancellationToken cancellationToken) => Task.FromResult<IAsyncDisposable?>(null);

    private async Task PollAsync(
        PlcAddress address,
        TimeSpan interval,
        Func<PlcValue, Task> onChanged,
        CancellationTokenSource cts)
    {
        var token = cts.Token;
        var last = PlcValue.Null;
        var first = true;

        try
        {
            using var timer = new IntervalTimer(interval);
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    var value = await ReadAsync(address, token).ConfigureAwait(false);
                    if (first || value != last)
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
                    Logger.Warning($"{Descriptor.Id} subscription for {address} failed.", ex);
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
