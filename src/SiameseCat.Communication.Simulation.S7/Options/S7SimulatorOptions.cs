using SiameseCat.Communication;
using SiameseCat.Communication.Diagnostics;

namespace SiameseCat.Communication.Simulation.S7;

/// <summary>Settings for <see cref="S7Simulator"/>.</summary>
public sealed class S7SimulatorOptions
{
    /// <summary>Gets or sets the address to listen on. Defaults to loopback so the simulator is not exposed.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Gets or sets the TCP port. Defaults to 102 (the ISO-on-TCP port); use 0 to let the OS choose.</summary>
    public int Port { get; set; } = 102;

    /// <summary>Gets or sets the size of the M area in bytes. Defaults to 512.</summary>
    public int MemorySize { get; set; } = 512;

    /// <summary>Gets or sets the size of the I area in bytes. Defaults to 512.</summary>
    public int InputSize { get; set; } = 512;

    /// <summary>Gets or sets the size of the Q area in bytes. Defaults to 512.</summary>
    public int OutputSize { get; set; } = 512;

    /// <summary>Gets or sets the size of data blocks created on demand. Defaults to 1024 bytes.</summary>
    public int DefaultDataBlockSize { get; set; } = 1024;

    /// <summary>
    /// Gets or sets whether a data block referenced by the master is created on first use.
    /// When disabled, unknown data blocks answer with return code <c>0x0A</c>.
    /// </summary>
    public bool AutoCreateDataBlocks { get; set; } = true;

    /// <summary>Gets or sets the PDU size reported during Setup Communication. Defaults to 960.</summary>
    public int PduSize { get; set; } = 960;

    /// <summary>Gets or sets the logger used for connection and protocol diagnostics. Defaults to no logging.</summary>
    public ICommunicationLogger? Logger { get; set; }

    /// <summary>Validates the options and throws when a value is out of range.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("Host must be specified.", nameof(Host));
        }

        if (Port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between 0 and 65535.");
        }

        ValidateSize(MemorySize, nameof(MemorySize));
        ValidateSize(InputSize, nameof(InputSize));
        ValidateSize(OutputSize, nameof(OutputSize));
        ValidateSize(DefaultDataBlockSize, nameof(DefaultDataBlockSize));

        if (PduSize is < 240 or > 960)
        {
            throw new ArgumentOutOfRangeException(nameof(PduSize), PduSize, "The PDU size must be between 240 and 960.");
        }
    }

    private static void ValidateSize(int size, string name) => SimulatorLimits.Validate(size, name);
}
