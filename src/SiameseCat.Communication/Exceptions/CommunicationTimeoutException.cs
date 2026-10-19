namespace SiameseCat.Communication;

/// <summary>Raised when an operation does not complete within the configured timeout.</summary>
public sealed class CommunicationTimeoutException : CommunicationException
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">Description of the timeout.</param>
    public CommunicationTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">Description of the timeout.</param>
    /// <param name="innerException">Underlying exception.</param>
    public CommunicationTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
