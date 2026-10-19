namespace SiameseCat.Core;

/// <summary>Raised when a peer returns a malformed or unexpected protocol response.</summary>
public class ProtocolException : CommunicationException
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">Description of the protocol violation.</param>
    public ProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">Description of the protocol violation.</param>
    /// <param name="innerException">Underlying exception.</param>
    public ProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
