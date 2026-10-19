namespace SiameseCat.Core;

/// <summary>Convenience helpers for <see cref="IPlcLogger"/>.</summary>
public static class PlcLoggerExtensions
{
    /// <summary>Writes an informational entry.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="message">Message.</param>
    public static void Information(this IPlcLogger logger, string message) =>
        logger.Log(PlcLogLevel.Information, message);

    /// <summary>Writes a warning entry.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="message">Message.</param>
    /// <param name="exception">Optional exception.</param>
    public static void Warning(this IPlcLogger logger, string message, Exception? exception = null) =>
        logger.Log(PlcLogLevel.Warning, message, exception);

    /// <summary>Writes an error entry.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="message">Message.</param>
    /// <param name="exception">Optional exception.</param>
    public static void Error(this IPlcLogger logger, string message, Exception? exception = null) =>
        logger.Log(PlcLogLevel.Error, message, exception);
}
