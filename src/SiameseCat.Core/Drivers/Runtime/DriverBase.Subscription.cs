namespace SiameseCat.Core;

public abstract partial class DriverBase
{
    /// <inheritdoc/>
    public Task<IAsyncDisposable> SubscribeAsync(
        PlcAddress address,
        TimeSpan interval,
        Func<PlcValue, Task> onChanged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be greater than zero.");
        }

        ThrowIfDisposed();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        _ = Task.Run(() => PollAsync(address, interval, onChanged, cts), CancellationToken.None);
        return Task.FromResult<IAsyncDisposable>(new Subscription(cts));
    }

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
            using var timer = new PeriodicTimer(interval);
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

        public Subscription(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public ValueTask DisposeAsync()
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

            return ValueTask.CompletedTask;
        }
    }
}
