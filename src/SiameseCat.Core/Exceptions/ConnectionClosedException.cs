namespace SiameseCat.Core;

/// <summary>Raised when the peer closes the connection unexpectedly.</summary>
public sealed class ConnectionClosedException : CommunicationException
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">Description of the failure.</param>
    public ConnectionClosedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">Underlying exception.</param>
    public ConnectionClosedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
