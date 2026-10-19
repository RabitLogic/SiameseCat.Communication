namespace SiameseCat.Core;

/// <summary>Common options for protocol clients.</summary>
public class CommunicationOptions
{
    /// <summary>Maximum time to wait for a single protocol request. Defaults to 3 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Number of additional attempts for transient failures. Defaults to 0.</summary>
    public int RetryCount { get; set; }

    /// <summary>Delay between retries. Defaults to 200 milliseconds.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Allows write operations to be retried. Defaults to <see langword="false"/> because a write
    /// may already have been executed by the device even when the response was lost.
    /// </summary>
    public bool RetryWrites { get; set; }

    /// <summary>
    /// Gets or sets whether every transport frame is logged at <see cref="PlcLogLevel.Trace"/>.
    /// The protocol clients wrap their transport in <see cref="LoggingTransport"/> when enabled.
    /// </summary>
    public bool LogFrames { get; set; }

    /// <summary>Gets or sets the logger that receives frame dumps when <see cref="LogFrames"/> is enabled.</summary>
    public IPlcLogger? Logger { get; set; }

    /// <summary>Validates the option values and throws when a value is out of range.</summary>
    public virtual void Validate()
    {
        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout), Timeout, "Timeout must be greater than zero.");
        }

        if (RetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryCount), RetryCount, "RetryCount cannot be negative.");
        }

        if (RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryDelay), RetryDelay, "RetryDelay cannot be negative.");
        }
    }
}
