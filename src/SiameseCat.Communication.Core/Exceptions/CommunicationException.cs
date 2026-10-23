namespace SiameseCat.Communication.Exceptions;

/// <summary>Base exception for communication failures.</summary>
public class CommunicationException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">Description of the failure.</param>
    public CommunicationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">Underlying exception.</param>
    public CommunicationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
