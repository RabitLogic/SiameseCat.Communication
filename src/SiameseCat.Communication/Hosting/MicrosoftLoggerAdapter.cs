using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiameseCat.Communication;

namespace SiameseCat.Communication.Hosting;

/// <summary>Adapts <see cref="IPlcLogger"/> onto <see cref="ILogger"/>.</summary>
/// <remarks>The message is pre-formatted by the driver, so it is forwarded as a single parameter.</remarks>
public sealed class MicrosoftLoggerAdapter : IPlcLogger
{
    private static readonly Action<ILogger, string, Exception?> TraceLog =
        LoggerMessage.Define<string>(LogLevel.Trace, default, "{Message}");

    private static readonly Action<ILogger, string, Exception?> DebugLog =
        LoggerMessage.Define<string>(LogLevel.Debug, default, "{Message}");

    private static readonly Action<ILogger, string, Exception?> InformationLog =
        LoggerMessage.Define<string>(LogLevel.Information, default, "{Message}");

    private static readonly Action<ILogger, string, Exception?> WarningLog =
        LoggerMessage.Define<string>(LogLevel.Warning, default, "{Message}");

    private static readonly Action<ILogger, string, Exception?> ErrorLog =
        LoggerMessage.Define<string>(LogLevel.Error, default, "{Message}");

    private readonly ILogger _logger;

    /// <summary>Creates the adapter.</summary>
    /// <param name="logger">Target Microsoft logger.</param>
    public MicrosoftLoggerAdapter(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public void Log(PlcLogLevel level, string message, Exception? exception = null) =>
        Resolve(level)(_logger, message, exception);

    /// <summary>Maps a <see cref="PlcLogLevel"/> onto <see cref="LogLevel"/>.</summary>
    /// <param name="level">Level to map.</param>
    public static LogLevel ToLogLevel(PlcLogLevel level) => level switch
    {
        PlcLogLevel.Trace => LogLevel.Trace,
        PlcLogLevel.Debug => LogLevel.Debug,
        PlcLogLevel.Information => LogLevel.Information,
        PlcLogLevel.Warning => LogLevel.Warning,
        PlcLogLevel.Error => LogLevel.Error,
        _ => LogLevel.None,
    };

    private static Action<ILogger, string, Exception?> Resolve(PlcLogLevel level) => level switch
    {
        PlcLogLevel.Trace => TraceLog,
        PlcLogLevel.Debug => DebugLog,
        PlcLogLevel.Information => InformationLog,
        PlcLogLevel.Warning => WarningLog,
        _ => ErrorLog,
    };
}
