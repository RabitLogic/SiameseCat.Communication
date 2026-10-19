namespace SiameseCat.Server;

/// <summary>Settings for <see cref="ModbusTcpServer"/>.</summary>
public sealed class ModbusServerOptions
{
    /// <summary>Gets or sets the address to listen on. Defaults to loopback so the simulator is not exposed.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Gets or sets the TCP port. Defaults to 502; use 0 to let the operating system choose a free port.</summary>
    public int Port { get; set; } = 502;

    /// <summary>Gets or sets how many holding registers (FC03/FC06/FC16/FC23) the slave exposes. Defaults to 256.</summary>
    public int HoldingRegisters { get; set; } = 256;

    /// <summary>Gets or sets how many input registers (FC04) the slave exposes. Defaults to 256.</summary>
    public int InputRegisters { get; set; } = 256;

    /// <summary>Gets or sets how many coils (FC01/FC05/FC15) the slave exposes. Defaults to 256.</summary>
    public int Coils { get; set; } = 256;

    /// <summary>Gets or sets how many discrete inputs (FC02) the slave exposes. Defaults to 256.</summary>
    public int DiscreteInputs { get; set; } = 256;

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

        ValidateSize(HoldingRegisters, nameof(HoldingRegisters));
        ValidateSize(InputRegisters, nameof(InputRegisters));
        ValidateSize(Coils, nameof(Coils));
        ValidateSize(DiscreteInputs, nameof(DiscreteInputs));
    }

    private static void ValidateSize(int size, string name) => ModbusBufferSizes.Validate(size, name);
}
