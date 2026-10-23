using System.Collections.Concurrent;

using SiameseCat.Communication;
using SiameseCat.Communication.Diagnostics;

namespace SiameseCat.Tests.Fakes;

/// <summary>Logger fake that records every entry so tests can assert on diagnostics.</summary>
internal sealed class RecordingLogger : ICommunicationLogger
{
    private readonly ConcurrentQueue<(CommunicationLogLevel Level, string Message, Exception? Exception)> _entries = new();

    public IReadOnlyCollection<(CommunicationLogLevel Level, string Message, Exception? Exception)> Entries => _entries;

    public void Log(CommunicationLogLevel level, string message, Exception? exception = null) =>
        _entries.Enqueue((level, message, exception));

    public bool Contains(CommunicationLogLevel level, string text) =>
        _entries.Any(entry => entry.Level == level && entry.Message.Contains(text, StringComparison.Ordinal));
}
