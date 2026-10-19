using SiameseCat.Communication;

namespace SiameseCat.Communication.S7;

/// <summary>Raised when the PLC returns an S7 protocol error.</summary>
public class S7Exception : ProtocolException
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">Description of the failure.</param>
    public S7Exception(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">Underlying exception.</param>
    public S7Exception(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception from the S7 error class and error code returned in the response header.</summary>
    /// <param name="errorClass">Error class byte.</param>
    /// <param name="errorCode">Error code byte.</param>
    public S7Exception(byte errorClass, byte errorCode)
        : base($"S7 error class 0x{errorClass:X2} ({GetErrorClassName(errorClass)}), code 0x{errorCode:X2}.")
    {
        ErrorClass = errorClass;
        ErrorCode = errorCode;
    }

    /// <summary>Gets the S7 error class, when the exception originates from a response header.</summary>
    public byte? ErrorClass { get; }

    /// <summary>Gets the S7 error code, when the exception originates from a response header.</summary>
    public byte? ErrorCode { get; }

    /// <summary>Gets the human-readable name of an S7 error class.</summary>
    /// <param name="errorClass">Error class byte.</param>
    public static string GetErrorClassName(byte errorClass) => errorClass switch
    {
        0x81 => "Application relationship",
        0x82 => "Object definition",
        0x83 => "No resources",
        0x84 => "Service processing",
        0x85 => "Supplies",
        0x87 => "Access",
        _ => "Unknown",
    };
}

/// <summary>Raised when one item in a read or write request is rejected by the PLC.</summary>
public sealed class S7ItemException : S7Exception
{
    /// <summary>Creates an exception from the per-item return code.</summary>
    /// <param name="returnCode">Return code of the rejected item.</param>
    public S7ItemException(byte returnCode)
        : base($"S7 item error 0x{returnCode:X2}: {GetReturnCodeName(returnCode)}.")
    {
        ReturnCode = returnCode;
    }

    /// <summary>Creates an exception from the per-item return code with address context.</summary>
    /// <param name="returnCode">Return code of the rejected item.</param>
    /// <param name="message">Message that explains which item failed.</param>
    public S7ItemException(byte returnCode, string message)
        : base(message)
    {
        ReturnCode = returnCode;
    }

    /// <summary>Gets the return code of the rejected item.</summary>
    public byte ReturnCode { get; }

    /// <summary>Gets the human-readable name of a per-item return code.</summary>
    /// <param name="returnCode">Return code of the rejected item.</param>
    public static string GetReturnCodeName(byte returnCode) => returnCode switch
    {
        0x01 => "Hardware fault",
        0x03 => "Access not allowed",
        0x05 => "Address out of range",
        0x06 => "Data type not supported",
        0x07 => "Data type inconsistent",
        0x0A => "Object does not exist",
        _ => "Unknown item error",
    };
}
