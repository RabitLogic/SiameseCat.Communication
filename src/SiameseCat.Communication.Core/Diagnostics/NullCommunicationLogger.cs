namespace SiameseCat.Communication.Diagnostics;

/// <summary>Logger that discards every entry.</summary>
public sealed class NullCommunicationLogger : ICommunicationLogger
{
    /// <summary>Gets the shared instance.</summary>
    public static NullCommunicationLogger Instance { get; } = new();

    private NullCommunicationLogger()
    {
    }

    /// <inheritdoc/>
    public void Log(CommunicationLogLevel level, string message, Exception? exception = null)
    {
    }
}
