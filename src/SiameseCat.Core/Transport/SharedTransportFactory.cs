namespace SiameseCat.Core;

/// <summary>
/// Hands out one shared transport so several drivers (for example the stations of an RS-485 bus)
/// can use the same physical link.
/// </summary>
/// <remarks>
/// <para>
/// Assign both <see cref="Create"/>'s result through <see cref="DriverConfiguration.TransportFactory"/>
/// and <see cref="RequestLock"/> through <see cref="DriverConfiguration.RequestLock"/>; sharing only the
/// transport would let two drivers interleave their request/response frames.
/// </para>
/// <para>The transport is created on first use and disposed with this factory.</para>
/// </remarks>
public sealed class SharedTransportFactory : ITransportFactory, IAsyncDisposable
{
    private readonly Func<ITransport> _transportFactory;
    private readonly object _sync = new();
    private ITransport? _transport;
    private bool _disposed;

    /// <summary>Creates the factory.</summary>
    /// <param name="transportFactory">Creates the underlying transport on first use.</param>
    public SharedTransportFactory(Func<ITransport> transportFactory)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
    }

    /// <summary>Gets the lock that every driver sharing this link must use.</summary>
    public RequestLock RequestLock { get; } = new();

    /// <summary>Returns the shared transport, creating it on first use.</summary>
    /// <param name="configuration">Configuration of the driver that is being created.</param>
    public ITransport Create(DriverConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _transport ??= _transportFactory();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        ITransport? transport;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            transport = _transport;
            _transport = null;
        }

        if (transport is not null)
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }

        RequestLock.Dispose();
    }
}
