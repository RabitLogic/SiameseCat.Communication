using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using SiameseCat.Core;

namespace SiameseCat.Tests.Fakes;

/// <summary>Scriptable in-memory transport used by the protocol tests.</summary>
internal sealed class MockTransport : ITransport
{
    private readonly Queue<byte[]> _responses = new();
    private byte[] _current = Array.Empty<byte>();
    private int _offset;

    public List<byte[]> Writes { get; } = new();

    public bool IsOpen { get; private set; }

    public bool BlockReads { get; set; }

    public int TimeoutFailuresRemaining { get; set; }

    public int OpenCount { get; private set; }

    public int CloseCount { get; private set; }

    public bool Disposed { get; private set; }

    public Func<byte[], byte[]>? ResponseFactory { get; set; }

    public void EnqueueResponse(params byte[] data) => _responses.Enqueue(data);

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        OpenCount++;
        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        CloseCount++;
        IsOpen = false;
        return Task.CompletedTask;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytes = buffer.ToArray();
        Writes.Add(bytes);

        if (ResponseFactory is not null)
        {
            EnqueueResponse(ResponseFactory(bytes));
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (BlockReads)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }

        if (TimeoutFailuresRemaining > 0)
        {
            TimeoutFailuresRemaining--;

            // Simulate a device that does not answer until the caller's timeout fires.
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }

        while (_offset >= _current.Length)
        {
            if (_responses.Count == 0)
            {
                throw new ConnectionClosedException("No scripted response is available.");
            }

            _current = _responses.Dequeue();
            _offset = 0;
        }

        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsSpan(_offset, count).CopyTo(buffer.Span);
        _offset += count;
        return count;
    }

    public void DiscardInput()
    {
    }

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
