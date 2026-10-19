using System.Net.Sockets;

namespace SiameseCat.Core;

/// <summary>Executes an operation with the retry settings from <see cref="CommunicationOptions"/>.</summary>
public static class RetryPolicy
{
    /// <summary>
    /// Runs <paramref name="operation"/>, retrying transient communication failures.
    /// </summary>
    /// <param name="operation">Operation to execute. Receives the caller cancellation token.</param>
    /// <param name="options">Options that control the number of attempts and the delay between them.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <param name="isWrite">
    /// When <see langword="true"/>, retries are only performed if <see cref="CommunicationOptions.RetryWrites"/> is enabled.
    /// </param>
    /// <param name="reconnect">
    /// Optional hook invoked before a retry so a dropped connection can be re-established. The hook must be
    /// idempotent because it is also called when the connection is still open.
    /// </param>
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CommunicationOptions options,
        CancellationToken cancellationToken,
        bool isWrite = false,
        Func<CancellationToken, Task>? reconnect = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(options);

        var maxAttempts = isWrite && !options.RetryWrites ? 1 : options.RetryCount + 1;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts
                                       && !cancellationToken.IsCancellationRequested
                                       && IsTransient(ex))
            {
                if (reconnect is not null)
                {
                    await reconnect(cancellationToken).ConfigureAwait(false);
                }

                if (options.RetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(options.RetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>Returns whether an exception represents a failure that may succeed when retried.</summary>
    public static bool IsTransient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ProtocolException => false,
            CommunicationException => true,
            SocketException => true,
            IOException => true,
            _ => false,
        };
    }
}

