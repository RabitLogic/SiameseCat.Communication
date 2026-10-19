using SiameseCat.Communication;

namespace SiameseCat.Communication.Server.Modbus;

/// <summary>Settings for <see cref="ModbusRtuServer"/>.</summary>
public sealed class ModbusRtuServerOptions
{
    /// <summary>Gets or sets the station address the slave answers. Defaults to 1; range 1..247.</summary>
    public byte UnitId { get; set; } = 1;

    /// <summary>
    /// Gets or sets the serial settings. <see cref="SerialSettings.ReadTimeout"/> is used as the frame gap:
    /// a timeout ends the current frame. Defaults to 100 ms.
    /// </summary>
    public SerialSettings Serial { get; set; } = new() { ReadTimeout = TimeSpan.FromMilliseconds(100) };

    /// <summary>Gets or sets how many holding registers the slave exposes. Defaults to 256.</summary>
    public int HoldingRegisters { get; set; } = 256;

    /// <summary>Gets or sets how many input registers the slave exposes. Defaults to 256.</summary>
    public int InputRegisters { get; set; } = 256;

    /// <summary>Gets or sets how many coils the slave exposes. Defaults to 256.</summary>
    public int Coils { get; set; } = 256;

    /// <summary>Gets or sets how many discrete inputs the slave exposes. Defaults to 256.</summary>
    public int DiscreteInputs { get; set; } = 256;

    /// <summary>Gets or sets the identifier returned by FC17 Report Server ID. Defaults to <c>SiameseCat</c>.</summary>
    public string ServerIdentifier { get; set; } = "SiameseCat";

    /// <summary>Gets or sets whether FC17 reports the run indicator as ON; <see langword="false"/> reports OFF.</summary>
    public bool RunIndicator { get; set; } = true;

    /// <summary>Gets or sets the logger used for frame and protocol diagnostics. Defaults to no logging.</summary>
    public IPlcLogger? Logger { get; set; }

    /// <summary>Validates the options and throws when a value is out of range.</summary>
    public void Validate()
    {
        if (UnitId is < 1 or > 247)
        {
            throw new ArgumentOutOfRangeException(nameof(UnitId), UnitId, "The station address must be between 1 and 247.");
        }

        Guard.NotNull(Serial, nameof(Serial));
        Serial.Validate();
        ValidateSize(HoldingRegisters, nameof(HoldingRegisters));
        ValidateSize(InputRegisters, nameof(InputRegisters));
        ValidateSize(Coils, nameof(Coils));
        ValidateSize(DiscreteInputs, nameof(DiscreteInputs));
        ModbusServerIdentifier.Validate(ServerIdentifier);
    }

    private static void ValidateSize(int size, string name) => ServerBufferSizes.Validate(size, name);
}
