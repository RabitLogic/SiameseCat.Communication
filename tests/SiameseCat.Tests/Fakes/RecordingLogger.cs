using System.Collections.Concurrent;
using SiameseCat.Core;

namespace SiameseCat.Tests.Fakes;

/// <summary>Logger fake that records every entry so tests can assert on diagnostics.</summary>
internal sealed class RecordingLogger : IPlcLogger
{
    private readonly ConcurrentQueue<(PlcLogLevel Level, string Message, Exception? Exception)> _entries = new();

    public IReadOnlyCollection<(PlcLogLevel Level, string Message, Exception? Exception)> Entries => _entries;

    public void Log(PlcLogLevel level, string message, Exception? exception = null) =>
        _entries.Enqueue((level, message, exception));

    public bool Contains(PlcLogLevel level, string text) =>
        _entries.Any(entry => entry.Level == level && entry.Message.Contains(text, StringComparison.Ordinal));
}
