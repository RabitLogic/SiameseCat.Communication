using SiameseCat.Communication;
using SiameseCat.Communication.Diagnostics;

namespace SiameseCat.Communication.Simulation.Modbus;

/// <summary>Settings for <see cref="ModbusTcpSimulator"/>.</summary>
public sealed class ModbusSimulatorOptions
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

    /// <summary>Gets or sets the identifier returned by FC17 Report Server ID. Defaults to <c>SiameseCat</c>.</summary>
    public string ServerIdentifier { get; set; } = "SiameseCat";

    /// <summary>Gets or sets whether FC17 reports the run indicator as ON; <see langword="false"/> reports OFF.</summary>
    public bool RunIndicator { get; set; } = true;

    /// <summary>
    /// Gets the objects returned by Read Device Identification (function code 43 / MEI 14), keyed by
    /// object id: 0x00 vendor name, 0x01 product code, 0x02 revision, 0x03 vendor URL, 0x04 product name,
    /// 0x05 model name, 0x06 user application name, plus vendor specific ids from 0x80.
    /// </summary>
    public IDictionary<byte, string> DeviceIdentification { get; } =
        ModbusSimulatorDefaults.CreateDefaultDeviceIdentification();

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

        ValidateSize(HoldingRegisters, nameof(HoldingRegisters));
        ValidateSize(InputRegisters, nameof(InputRegisters));
        ValidateSize(Coils, nameof(Coils));
        ValidateSize(DiscreteInputs, nameof(DiscreteInputs));
        ModbusSimulatorDefaults.Validate(ServerIdentifier);
        ModbusSimulatorDefaults.ValidateDeviceIdentification(DeviceIdentification);
    }

    private static void ValidateSize(int size, string name) => SimulatorLimits.Validate(size, name);
}
