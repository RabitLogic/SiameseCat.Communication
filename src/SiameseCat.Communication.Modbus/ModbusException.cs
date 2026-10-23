using SiameseCat.Communication;

namespace SiameseCat.Communication.Modbus;

/// <summary>Raised when a Modbus server returns an exception response.</summary>
public sealed class ModbusException : ProtocolException
{
    /// <summary>Creates an exception from a Modbus exception code.</summary>
    /// <param name="exceptionCode">Exception code returned by the server.</param>
    public ModbusException(byte exceptionCode)
        : base($"Modbus exception 0x{exceptionCode:X2}: {GetExceptionName(exceptionCode)}.")
    {
        ExceptionCode = exceptionCode;
    }

    /// <summary>Creates an exception from a Modbus exception code with request context.</summary>
    /// <param name="exceptionCode">Exception code returned by the server.</param>
    /// <param name="message">Message that explains which request failed.</param>
    public ModbusException(byte exceptionCode, string message)
        : base(message)
    {
        ExceptionCode = exceptionCode;
    }

    /// <summary>Gets the Modbus exception code.</summary>
    public byte ExceptionCode { get; }

    /// <summary>Gets the human-readable name of an exception code.</summary>
    /// <param name="exceptionCode">Exception code returned by the server.</param>
    public static string GetExceptionName(byte exceptionCode) => exceptionCode switch
    {
        0x01 => "Illegal function",
        0x02 => "Illegal data address",
        0x03 => "Illegal data value",
        0x04 => "Server device failure",
        0x05 => "Acknowledge",
        0x06 => "Server device busy",
        0x07 => "Negative acknowledge",
        0x08 => "Memory parity error",
        0x0A => "Gateway path unavailable",
        0x0B => "Gateway target device failed to respond",
        _ => "Unknown exception",
    };
}
