namespace SiameseCat.Communication.Transport.Tcp;

/// <summary>How a custom TCP protocol delimits frames on the byte stream.</summary>
public enum TcpFrameMode
{
    /// <summary>
    /// No framing: every read from the transport is treated as one frame. Only use it for protocols
    /// where the peer sends exactly one message per read.
    /// </summary>
    Raw,

    /// <summary>A length field in front of the payload.</summary>
    LengthPrefix,

    /// <summary>A trailing delimiter, for example CR LF for line based text protocols.</summary>
    Delimiter,

    /// <summary>Every frame has the same fixed size.</summary>
    FixedLength,
}
