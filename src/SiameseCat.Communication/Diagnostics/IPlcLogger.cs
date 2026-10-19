namespace SiameseCat.Communication;

/// <summary>Log severity used by <see cref="IPlcLogger"/>.</summary>
public enum PlcLogLevel
{
    /// <summary>Very verbose diagnostics.</summary>
    Trace,

    /// <summary>Debug diagnostics.</summary>
    Debug,

    /// <summary>Normal operational events.</summary>
    Information,

    /// <summary>Recoverable problems.</summary>
    Warning,

    /// <summary>Failures.</summary>
    Error,
}

/// <summary>Minimal logging abstraction so the libraries stay dependency-free.</summary>
public interface IPlcLogger
{
    /// <summary>Writes a log entry.</summary>
    /// <param name="level">Severity.</param>
    /// <param name="message">Message.</param>
    /// <param name="exception">Optional exception.</param>
    void Log(PlcLogLevel level, string message, Exception? exception = null);
}
