using SiameseCat.Communication.Diagnostics;

namespace SiameseCat.Communication.Drivers;

/// <summary>
/// Template-method base class for device drivers. It owns the lifecycle state machine, the transaction
/// lock, timeouts, retries with reconnect, logging and metrics; derived drivers only implement the
/// protocol-specific hooks.
/// </summary>
public abstract partial class DriverBase : IDeviceDriver, IReadable, IWritable, IBatchReadable, IBatchWritable, ISubscribable, IBatchSubscribable, IHealthCheckable
{
    private readonly RequestLock _gate;
    private readonly bool _ownsGate;
    private readonly CancellationTokenSource _disposeCts = new();
    private DriverState _state = DriverState.Disconnected;
    private CancellationTokenSource? _keepAliveCts;
    private Task? _keepAliveTask;
    private volatile bool _disposed;

    /// <summary>Reused timeout source for requests that carry no caller token; guarded by the request gate.</summary>
    private CancellationTokenSource? _timeoutCts;

    /// <summary>Creates a driver.</summary>
    /// <param name="descriptor">Driver descriptor.</param>
    /// <param name="addressParser">Parser for protocol-native addresses.</param>
    /// <param name="configuration">Connection and retry settings.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="metrics">Optional metrics collector.</param>
    protected DriverBase(
        DriverDescriptor descriptor,
        IDeviceAddressParser addressParser,
        DriverConfiguration configuration,
        ICommunicationLogger? logger = null,
        CommunicationMetrics? metrics = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        AddressParser = addressParser ?? throw new ArgumentNullException(nameof(addressParser));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Configuration.Validate();
        Logger = logger ?? NullCommunicationLogger.Instance;
        Metrics = metrics ?? new CommunicationMetrics();
        _gate = configuration.RequestLock ?? new RequestLock();
        _ownsGate = configuration.RequestLock is null;

        if (Configuration.KeepAliveInterval > TimeSpan.Zero
            && (Descriptor.Capabilities & DriverCapabilities.Health) == 0)
        {
            throw new InvalidOperationException(
                $"Driver '{Descriptor.Id}' declares no health capability, so KeepAliveInterval cannot be enabled.");
        }
    }

    /// <inheritdoc/>
    public DriverDescriptor Descriptor { get; }

    /// <inheritdoc/>
    public IDeviceAddressParser AddressParser { get; }

    /// <inheritdoc/>
    public DriverState State => _state;

    /// <inheritdoc/>
    public event EventHandler<DriverStateChangedEventArgs>? StateChanged;

    /// <summary>Gets the configuration used by the driver.</summary>
    protected DriverConfiguration Configuration { get; }

    /// <summary>Gets the logger.</summary>
    protected ICommunicationLogger Logger { get; }

    /// <summary>Gets the metrics collector.</summary>
    public CommunicationMetrics Metrics { get; }

    /// <inheritdoc/>
    public Task<DeviceValue> ReadAsync(DeviceAddress address, CancellationToken cancellationToken = default) =>
        ReadCoreAsync(address, timeout: null, cancellationToken);

    /// <inheritdoc/>
    public Task<DeviceValue> ReadAsync(
        DeviceAddress address,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        ReadCoreAsync(address, ValidateTimeout(timeout), cancellationToken);

    private async Task<DeviceValue> ReadCoreAsync(
        DeviceAddress address,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        EnsureCapability(DriverCapabilities.Read);

        var value = await ExecuteAsync(
            token => DoReadAsync(address, token),
            isWrite: false,
            address,
            timeout,
            CommunicationOperation.Read,
            cancellationToken).ConfigureAwait(false);

        Metrics.RecordBytesRead(address.ByteSize);
        CommunicationDiagnostics.RecordBytesRead(Descriptor.Id, address.ByteSize);

        return Configuration.StampReadTimestamps
            ? value.WithTimestamp(DateTimeOffset.UtcNow)
            : value;
    }

    /// <inheritdoc/>
    public Task WriteAsync(
        DeviceAddress address,
        DeviceValue value,
        CancellationToken cancellationToken = default) =>
        WriteCoreAsync(address, value, timeout: null, cancellationToken);

    /// <inheritdoc/>
    public Task WriteAsync(
        DeviceAddress address,
        DeviceValue value,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        WriteCoreAsync(address, value, ValidateTimeout(timeout), cancellationToken);

    private async Task WriteCoreAsync(
        DeviceAddress address,
        DeviceValue value,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        EnsureCapability(DriverCapabilities.Write);
        ValidateAddress(address);
        await ExecuteAsync(
            async token =>
            {
                await DoWriteAsync(address, value, token).ConfigureAwait(false);
                return true;
            },
            isWrite: true,
            address,
            timeout,
            CommunicationOperation.Write,
            cancellationToken).ConfigureAwait(false);

        Metrics.RecordBytesWritten(address.ByteSize);
        CommunicationDiagnostics.RecordBytesWritten(Descriptor.Id, address.ByteSize);
    }

    /// <inheritdoc/>
    public Task<DeviceValue[]> ReadBatchAsync(
        IReadOnlyList<DeviceAddress> addresses,
        CancellationToken cancellationToken = default) =>
        ReadBatchCoreAsync(addresses, timeout: null, cancellationToken);

    /// <inheritdoc/>
    public Task<DeviceValue[]> ReadBatchAsync(
        IReadOnlyList<DeviceAddress> addresses,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        ReadBatchCoreAsync(addresses, ValidateTimeout(timeout), cancellationToken);

    private async Task<DeviceValue[]> ReadBatchCoreAsync(
        IReadOnlyList<DeviceAddress> addresses,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(addresses, nameof(addresses));
        if (addresses.Count == 0)
        {
            return Array.Empty<DeviceValue>();
        }

        EnsureCapability(DriverCapabilities.BatchRead);

        var values = await ExecuteAsync(
            token => DoReadBatchAsync(addresses, token),
            isWrite: false,
            address: null,
            timeout,
            CommunicationOperation.ReadBatch,
            cancellationToken).ConfigureAwait(false);

        foreach (var address in addresses)
        {
            Metrics.RecordBytesRead(address.ByteSize);
            CommunicationDiagnostics.RecordBytesRead(Descriptor.Id, address.ByteSize);
        }

        if (Configuration.StampReadTimestamps)
        {
            var sampledAt = DateTimeOffset.UtcNow;
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = values[i].WithTimestamp(sampledAt);
            }
        }

        return values;
    }

    /// <inheritdoc/>
    public Task WriteBatchAsync(
        IReadOnlyList<DeviceWriteRequest> requests,
        CancellationToken cancellationToken = default) =>
        WriteBatchCoreAsync(requests, timeout: null, cancellationToken);

    /// <inheritdoc/>
    public Task WriteBatchAsync(
        IReadOnlyList<DeviceWriteRequest> requests,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        WriteBatchCoreAsync(requests, ValidateTimeout(timeout), cancellationToken);

    private async Task WriteBatchCoreAsync(
        IReadOnlyList<DeviceWriteRequest> requests,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(requests, nameof(requests));
        if (requests.Count == 0)
        {
            return;
        }

        EnsureCapability(DriverCapabilities.BatchWrite);

        foreach (var request in requests)
        {
            ValidateAddress(request.Address);
        }

        await ExecuteAsync(
            async token =>
            {
                await DoWriteBatchAsync(requests, token).ConfigureAwait(false);
                return true;
            },
            isWrite: true,
            address: null,
            timeout,
            CommunicationOperation.WriteBatch,
            cancellationToken).ConfigureAwait(false);

        foreach (var request in requests)
        {
            Metrics.RecordBytesWritten(request.Address.ByteSize);
            CommunicationDiagnostics.RecordBytesWritten(Descriptor.Id, request.Address.ByteSize);
        }
    }

    /// <summary>Opens the protocol connection.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected abstract Task DoConnectAsync(CancellationToken cancellationToken);

    /// <summary>Closes the protocol connection.</summary>
    protected abstract Task DoDisconnectAsync();

    /// <summary>Reads one value.</summary>
    /// <param name="address">Address to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">
    /// The driver does not read. Override the method, or declare <see cref="DriverCapabilities.Read"/>
    /// in the descriptor and implement it.
    /// </exception>
    protected virtual Task<DeviceValue> DoReadAsync(DeviceAddress address, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"Driver '{Descriptor.Id}' does not support reads.");

    /// <summary>Writes one value.</summary>
    /// <param name="address">Address to write.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">
    /// The driver does not write. Override the method, or declare <see cref="DriverCapabilities.Write"/>
    /// in the descriptor and implement it.
    /// </exception>
    protected virtual Task DoWriteAsync(DeviceAddress address, DeviceValue value, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"Driver '{Descriptor.Id}' does not support writes.");

    /// <summary>
    /// Reads several values. The default implementation reads them one by one; protocols with native
    /// multi-read support should override it.
    /// </summary>
    /// <param name="addresses">Addresses to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected virtual async Task<DeviceValue[]> DoReadBatchAsync(
        IReadOnlyList<DeviceAddress> addresses,
        CancellationToken cancellationToken)
    {
        var values = new DeviceValue[addresses.Count];
        for (var i = 0; i < addresses.Count; i++)
        {
            values[i] = await DoReadAsync(addresses[i], cancellationToken).ConfigureAwait(false);
        }

        return values;
    }

    /// <summary>
    /// Writes several values. The default implementation writes them one by one; protocols with
    /// native multi-write support should override it.
    /// </summary>
    /// <param name="requests">Address/value pairs to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected virtual async Task DoWriteBatchAsync(
        IReadOnlyList<DeviceWriteRequest> requests,
        CancellationToken cancellationToken)
    {
        foreach (var request in requests)
        {
            await DoWriteAsync(request.Address, request.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs a protocol-specific operation with the driver transaction lock, timeout, retry policy and
    /// metrics. Use it for APIs that do not map onto the generic read/write capabilities.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="operation">Operation kind reported in metrics.</param>
    /// <param name="isWrite">Whether the operation is a write, which controls retry behavior.</param>
    /// <param name="timeout">Per-call timeout; <see langword="null"/> uses <see cref="CommunicationOptions.Timeout"/>.</param>
    /// <param name="body">Protocol-specific work.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected Task<T> ExecuteProtocolAsync<T>(
        CommunicationOperation operation,
        bool isWrite,
        TimeSpan? timeout,
        Func<CancellationToken, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(body, nameof(body));

        return ExecuteAsync(body, isWrite, address: null, timeout, operation, cancellationToken);
    }
}
