namespace SiameseCat.Communication;

/// <summary>Convenience helpers for <see cref="ICommunicationLogger"/>.</summary>
public static class CommunicationLoggerExtensions
{
    /// <summary>Writes an informational entry.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="message">Message.</param>
    public static void Information(this ICommunicationLogger logger, string message) =>
        logger.Log(CommunicationLogLevel.Information, message);

    /// <summary>Writes a warning entry.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="message">Message.</param>
    /// <param name="exception">Optional exception.</param>
    public static void Warning(this ICommunicationLogger logger, string message, Exception? exception = null) =>
        logger.Log(CommunicationLogLevel.Warning, message, exception);

    /// <summary>Writes an error entry.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="message">Message.</param>
    /// <param name="exception">Optional exception.</param>
    public static void Error(this ICommunicationLogger logger, string message, Exception? exception = null) =>
        logger.Log(CommunicationLogLevel.Error, message, exception);
}
