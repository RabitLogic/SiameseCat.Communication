namespace SiameseCat.Core;

/// <summary>Logger that discards every entry.</summary>
public sealed class NullPlcLogger : IPlcLogger
{
    /// <summary>Gets the shared instance.</summary>
    public static NullPlcLogger Instance { get; } = new();

    private NullPlcLogger()
    {
    }

    /// <inheritdoc/>
    public void Log(PlcLogLevel level, string message, Exception? exception = null)
    {
    }
}
