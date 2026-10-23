using SiameseCat.Communication;
using SiameseCat.Communication.Transport;

namespace SiameseCat.Samples.Infrastructure;

/// <summary>
/// In-memory duplex byte transport that behaves like a serial cable or a socket.
/// </summary>
/// <remarks>
/// The pair demonstrates the <see cref="ITransport"/> injection point that every client exposes:
/// Modbus RTU can be exercised without a COM port, and a custom link (radio modem, RS-485 bridge,
/// mock in tests) plugs in the same way.
/// </remarks>
public sealed class LoopbackTransport : ITransport
{
    private readonly object _sync = new();
    private readonly Queue<byte> _incoming = new();
    private LoopbackTransport? _peer;
    private TaskCompletionSource<bool>? _signal;
    private volatile bool _open;

    private LoopbackTransport()
    {
    }

    /// <inheritdoc/>
    public bool IsOpen => _open;

    /// <summary>Creates two transports that are wired to each other.</summary>
    /// <returns>The two ends of the virtual cable; both start open.</returns>
    public static (LoopbackTransport Client, LoopbackTransport Server) CreatePair()
    {
        var client = new LoopbackTransport();
        var server = new LoopbackTransport();
        client._peer = server;
        server._peer = client;
        client._open = true;
        server._open = true;
        return (client, server);
    }

    /// <inheritdoc/>
    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        _open = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CloseAsync()
    {
        TaskCompletionSource<bool>? signal;
        lock (_sync)
        {
            _open = false;
            signal = _signal;
            _signal = null;
        }

        signal?.TrySetResult(true);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var peer = _peer ?? throw new InvalidOperationException("The transport is not connected to a peer.");

        TaskCompletionSource<bool>? signal;
        lock (peer._sync)
        {
            foreach (var value in buffer.Span)
            {
                peer._incoming.Enqueue(value);
            }

            signal = peer._signal;
            peer._signal = null;
        }

        signal?.TrySetResult(true);
        return default;
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task pending;
            lock (_sync)
            {
                if (!_open)
                {
                    return 0;
                }

                if (_incoming.Count > 0)
                {
                    var read = 0;
                    while (read < buffer.Length && _incoming.TryDequeue(out var value))
                    {
                        buffer.Span[read++] = value;
                    }

                    return read;
                }

                _signal ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending = _signal.Task;
            }

            await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public void DiscardInput()
    {
        lock (_sync)
        {
            _incoming.Clear();
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _open = false;
        return default;
    }
}
